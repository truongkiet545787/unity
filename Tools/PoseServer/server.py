"""Local webcam + MediaPipe Hands window. Unity receives controls only, never video."""
import argparse
import asyncio
import json
import logging
import threading
import time
from contextlib import ExitStack
from pathlib import Path

import cv2
import mediapipe as mp
from websockets.asyncio.server import serve
from websockets.exceptions import ConnectionClosed
from hand_controls import HandControls
from hand_display import WINDOW, render

LOG = logging.getLogger("posekart")


class LatestCamera:
    """Drain camera continuously; inference consumes only the most recent frame."""
    def __init__(self, index, backend, stop):
        self.index, self.backend, self.stop = index, backend, stop
        self.lock = threading.Lock()
        self.frame = None
        self.error = None
        self.thread = threading.Thread(target=self.run, name="webcam", daemon=True)

    def run(self):
        camera = None
        try:
            api = cv2.CAP_DSHOW if self.backend == "dshow" else cv2.CAP_MSMF
            camera = cv2.VideoCapture(self.index, api)
            if not camera.isOpened():
                raise RuntimeError(f"Cannot open camera {self.index}. Close other camera apps or change --camera/--backend.")
            camera.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
            camera.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
            camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
            camera.set(cv2.CAP_PROP_FPS, 30)
            camera.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            LOG.info("Camera opened (%s, index %s)", self.backend, self.index)
            while not self.stop.is_set():
                ok, image = camera.read()
                if not ok:
                    raise RuntimeError("Camera stopped returning frames. Check Windows camera permissions.")
                captured = time.monotonic()
                image = cv2.resize(image, (640, 480), interpolation=cv2.INTER_AREA)
                with self.lock:
                    self.frame = (captured, image)
        except Exception as exc:
            self.error = str(exc)
            LOG.error("%s", exc)
        finally:
            if camera is not None:
                camera.release()
            LOG.info("Camera released")

    def latest(self):
        with self.lock:
            return self.frame



class HandSession:
    def __init__(self, args):
        self.args = args
        self.stop = threading.Event()
        self.recenter = threading.Event()
        self.lock = threading.Lock()
        self.packet = None
        self.display = None
        self.error = None
        self.camera = LatestCamera(args.camera, args.backend, self.stop)
        self.thread = threading.Thread(target=self.run, name="mediapipe-hands", daemon=True)

    def run(self):
        try:
            cv2.setNumThreads(1)
            options = mp.tasks.vision.HandLandmarkerOptions(
                base_options=mp.tasks.BaseOptions(model_asset_path=str(self.args.model)),
                running_mode=mp.tasks.vision.RunningMode.VIDEO,
                num_hands=2,
                min_hand_detection_confidence=0.5,
                min_hand_presence_confidence=0.5,
                min_tracking_confidence=0.5,
            )
            controls = HandControls()
            with ExitStack() as stack:
                model=stack.enter_context(mp.tasks.vision.HandLandmarker.create_from_options(options))
                pose=None
                wrist_model=Path(__file__).parent / "models" / "pose_landmarker_lite.task"
                if wrist_model.is_file():
                    pose=stack.enter_context(mp.tasks.vision.PoseLandmarker.create_from_options(
                        mp.tasks.vision.PoseLandmarkerOptions(
                            base_options=mp.tasks.BaseOptions(model_asset_path=str(wrist_model)),
                            running_mode=mp.tasks.vision.RunningMode.VIDEO,num_poses=1,
                            output_segmentation_masks=False)))
                else:LOG.warning("Wrist fallback model missing; run setup.ps1 to enable closed-grip fallback.")
                last_pose=-100.0;wrists=[]
                self.camera.thread.start()
                previous_capture = previous_timestamp = -1
                seq = 0
                previous_result = None
                fps = 0.0
                opened = time.monotonic()
                while not self.stop.is_set():
                    if self.camera.error:
                        raise RuntimeError(self.camera.error)
                    latest = self.camera.latest()
                    if latest is None or latest[0] == previous_capture:
                        last = opened if latest is None else latest[0]
                        if time.monotonic()-last > 8:
                            raise RuntimeError("Camera has not returned a fresh frame for 8 seconds.")
                        self.stop.wait(0.005)
                        continue
                    captured, bgr = latest
                    previous_capture = captured
                    timestamp = max(previous_timestamp + 1, int(captured * 1000))
                    previous_timestamp = timestamp
                    # Mirror ONCE before inference, so controls and Python preview use the same coordinates.
                    mirrored = cv2.flip(bgr, 1)
                    rgb = cv2.cvtColor(mirrored, cv2.COLOR_BGR2RGB)
                    began = time.monotonic()
                    result = model.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), timestamp)
                    has_left_grip=any(sum(h[i].x for i in (0,5,9,13,17))/5<.64 for h in result.hand_landmarks)
                    # A closed fist may hide fingers. Pose wrist fallback runs only when
                    # the right-side grip is missing, capped at 8 Hz to limit CPU cost.
                    if pose is not None and not has_left_grip and began-last_pose>=.125:
                        pose_result=pose.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB,data=rgb),timestamp)
                        last_pose=began;wrists=[]
                        if pose_result.pose_landmarks:
                            for i in (15,16):
                                p=pose_result.pose_landmarks[0][i]
                                if min(p.visibility or 0,p.presence or 0)>.55:wrists.append((p.x,p.y))
                    inference_ms = (time.monotonic() - began) * 1000
                    if self.recenter.is_set():
                        controls.recenter_requested = True
                        self.recenter.clear()
                    now = time.monotonic()
                    packet, visual = controls.update(result, now,wrists if began-last_pose<.18 else ())
                    if previous_result is not None:
                        instant = 1 / max(0.001, now - previous_result)
                        fps = instant if fps == 0 else fps * 0.85 + instant * 0.15
                    previous_result = now
                    seq += 1
                    packet.update(protocol=2, seq=seq, inference_ms=round(inference_ms,2), hand_fps=round(fps,1))
                    with self.lock:
                        self.packet = (captured, packet)
                        self.display = (mirrored, visual, packet)
                    self.stop.wait(max(0, 1 / self.args.fps - (time.monotonic() - began)))
        except Exception as exc:
            self.error = str(exc)
            LOG.exception("Hand tracking stopped")
        finally:
            self.stop.set()
            if self.camera.thread.ident is not None:
                self.camera.thread.join(timeout=3)

    async def next_packet(self, last_seq):
        deadline = time.monotonic() + 12
        while time.monotonic() < deadline:
            if self.error:
                raise RuntimeError(self.error)
            with self.lock:
                snapshot = self.packet
            if snapshot is not None and snapshot[1]["seq"] > last_seq:
                captured, packet = snapshot
                return dict(packet, age_ms=round((time.monotonic() - captured) * 1000, 2))
            await asyncio.sleep(0.005)
        raise RuntimeError("No fresh hand-tracking frame for 12 seconds.")

    def close(self):
        self.stop.set()
        self.thread.join(timeout=5)


async def main(args):
    session = HandSession(args)
    connected = False

    async def handle(websocket):
        nonlocal connected
        if connected:
            await websocket.close(1013, "Another Unity client is connected.")
            return
        connected = True
        LOG.info("Unity connected; sending hand controls (no video)")
        last_seq = 0
        try:
            while True:
                command = await asyncio.wait_for(websocket.recv(), timeout=20)
                if command != "next":
                    await websocket.close(1008, "Expected next")
                    break
                packet = await session.next_packet(last_seq)
                last_seq = packet["seq"]
                await websocket.send(json.dumps(packet, separators=(",", ":"), allow_nan=False))
        except ConnectionClosed:
            pass
        except Exception as exc:
            LOG.error("Connection stopped: %s", exc)
            try:
                await websocket.send(json.dumps({"protocol": 2, "error": True, "status": str(exc)}))
            except ConnectionClosed:
                pass
        finally:
            connected = False
            LOG.info("Unity disconnected; Python hand preview remains open until Esc/Ctrl+C")

    # Native loopback client only; camera never leaves this process.
    async with serve(handle, "127.0.0.1", 8765, origins=[None], compression=None,
                     max_size=1024, max_queue=1, close_timeout=2):
        session.thread.start()
        LOG.info("Ready: ws://127.0.0.1:8765. Opening webcam + Python hand preview. Esc/Ctrl+C to stop.")
        shown = False
        last_displayed = -1
        try:
            while not session.stop.is_set():
                with session.lock:
                    display = session.display
                if display is not None and display[2]["seq"] != last_displayed:
                    if not shown:
                        cv2.namedWindow(WINDOW, cv2.WINDOW_NORMAL)
                        cv2.resizeWindow(WINDOW, 800, 600)
                        shown = True
                    cv2.imshow(WINDOW, render(*display))
                    last_displayed = display[2]["seq"]
                key = cv2.waitKey(1) & 0xff if shown else -1
                if key in (27, ord("q")):
                    break
                if key in (ord("c"),ord("C")):
                    session.recenter.set()
                if shown and cv2.getWindowProperty(WINDOW, cv2.WND_PROP_VISIBLE) < 1:
                    break
                await asyncio.sleep(.008)
            if session.error:
                raise RuntimeError(session.error)
        finally:
            await asyncio.to_thread(session.close)
            cv2.destroyAllWindows()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--backend", choices=("dshow", "msmf"), default="dshow")
    parser.add_argument("--fps", type=int, choices=range(5, 61), default=30)
    parser.add_argument("--model", type=Path, default=Path(__file__).parent / "models" / "hand_landmarker.task")
    args = parser.parse_args()
    if not args.model.is_file():
        parser.error("Hand model missing. Run setup.ps1 first.")
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    try:
        asyncio.run(main(args))
    except KeyboardInterrupt:
        LOG.info("Server stopped")
    except (OSError,RuntimeError) as exc:
        LOG.error("Server stopped: %s", exc)
        raise SystemExit(1)
