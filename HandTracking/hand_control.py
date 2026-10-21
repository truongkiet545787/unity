import cv2
import time
import os
import sys
import numpy as np

# Thêm thư mục hiện tại vào sys.path để import dễ dàng
CURRENT_DIR = os.path.dirname(os.path.abspath(__file__))
if CURRENT_DIR not in sys.path:
    sys.path.insert(0, CURRENT_DIR)

from calibration import CalibrationManager
from virtual_steering import VirtualSteeringWheel
from gesture_detector import GestureDetector
from udp_sender import UdpSender

# ============================================================
# CẤU HÌNH MEDIAPIPE (Tương thích Tasks API mới & Legacy API)
# ============================================================
MODEL_PATH = os.path.join(CURRENT_DIR, "hand_landmarker.task")

class MediaPipeHandTracker:
    def __init__(self, model_path=MODEL_PATH):
        self.use_tasks_api = False
        self.detector = None
        self.legacy_hands = None
        self.last_timestamp_ms = 0

        # Thử khởi tạo MediaPipe Tasks API (MediaPipe >= 0.10.x & 1.0.x)
        if os.path.exists(model_path):
            try:
                import mediapipe as mp
                from mediapipe.tasks import python as mp_python
                from mediapipe.tasks.python import vision as mp_vision

                base_options = mp_python.BaseOptions(model_asset_path=model_path)
                options = mp_vision.HandLandmarkerOptions(
                    base_options=base_options,
                    num_hands=2,
                    min_hand_detection_confidence=0.5,
                    min_hand_presence_confidence=0.5,
                    min_tracking_confidence=0.5,
                    running_mode=mp_vision.RunningMode.VIDEO
                )
                self.detector = mp_vision.HandLandmarker.create_from_options(options)
                self.use_tasks_api = True
                print(f"[MediaPipe] Khoi tao thanh cong voi Tasks API (Model: {model_path})")
            except Exception as e:
                print(f"[MediaPipe] Khong khoi tao duoc Tasks API ({e}), thu Legacy API...")

        # Nếu không có Tasks API, thử fallback sang mp.solutions.hands
        if not self.use_tasks_api:
            try:
                import mediapipe as mp
                if hasattr(mp, "solutions") and hasattr(mp.solutions, "hands"):
                    self.legacy_hands = mp.solutions.hands.Hands(
                        static_image_mode=False,
                        max_num_hands=2,
                        min_detection_confidence=0.5,
                        min_tracking_confidence=0.5
                    )
                    print("[MediaPipe] Khoi tao thanh cong voi Legacy solutions.hands API")
                else:
                    raise RuntimeError("MediaPipe khong co solutions.hands va thieu model task.")
            except Exception as e:
                print(f"[MediaPipe] LỖI KHỞI TẠO: {e}")

    def detect(self, rgb_frame, timestamp_ms):
        """
        Nhận diện bàn tay trên khung hình RGB.
        Trả về danh sách các bàn tay, mỗi bàn tay là danh sách 21 điểm mốc (có thuộc tính .x, .y, .z).
        """
        if self.use_tasks_api and self.detector is not None:
            import mediapipe as mp
            # Đảm bảo timestamp tăng dần
            if timestamp_ms <= self.last_timestamp_ms:
                timestamp_ms = self.last_timestamp_ms + 1
            self.last_timestamp_ms = timestamp_ms

            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
            result = self.detector.detect_for_video(mp_image, timestamp_ms)
            if result and result.hand_landmarks:
                return result.hand_landmarks
            return []

        elif self.legacy_hands is not None:
            results = self.legacy_hands.process(rgb_frame)
            if results and results.multi_hand_landmarks:
                return [hand_lm.landmark for hand_lm in results.multi_hand_landmarks]
            return []

        return []

    def close(self):
        if self.use_tasks_api and self.detector is not None:
            try:
                self.detector.close()
            except Exception:
                pass
        elif self.legacy_hands is not None:
            try:
                self.legacy_hands.close()
            except Exception:
                pass


# ============================================================
# CHƯƠNG TRÌNH CHÍNH (MAIN APPLICATION)
# ============================================================
def main():
    print("=" * 65)
    print("   POSEKART - DIEU KHIEN VO LANG AO VOI MEDIAPIPE HANDS")
    print("   Du an tot nghiep: He thong nhan dien cu chi ban tay cho Unity")
    print("=" * 65)

    # Khởi tạo các module
    calib = CalibrationManager()
    steering = VirtualSteeringWheel(calib)
    gesture = GestureDetector(calib)
    udp = UdpSender(host="127.0.0.1", port=5052)
    tracker = MediaPipeHandTracker()

    # Mở Webcam
    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("[Webcam] Không thể mở webcam camera 0. Vui lòng kiểm tra kết nối camera.")
        return

    # Cấu hình độ phân giải camera mượt mà
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    cap.set(cv2.CAP_PROP_FPS, 30)

    print("\n[San sang] Da mo webcam. Dang truyen tin hieu UDP toi 127.0.0.1:5052...")
    print("Phim tat:")
    print("  [C] : Bat dau hieu chuan vi tri trung hoa cua vo lang")
    print("  [R] : Dat lai hieu chuan ve mac dinh (0 do)")
    print("  [I] : Bat/Tat dao chieu danh lai (Invert Steering)")
    print("  [Q] hoac [ESC] : Thoat chuong trinh\n")

    fps_counter = 0
    fps_start_time = time.time()
    current_fps = 0.0

    try:
        while True:
            ret, frame = cap.read()
            if not ret or frame is None:
                # Xử lý khi camera tạm ngắt kết nối
                time.sleep(0.05)
                # Gửi gói tin mất dấu tới Unity
                udp.send(tracking=False)
                continue

            # Tính FPS
            fps_counter += 1
            if time.time() - fps_start_time >= 1.0:
                current_fps = fps_counter / (time.time() - fps_start_time)
                fps_counter = 0
                fps_start_time = time.time()

            # 1. Lật gương hình ảnh (Mirrored view) để trải nghiệm lái xe tự nhiên như nhìn gương
            frame = cv2.flip(frame, 1)
            h, w = frame.shape[:2]

            # 2. Chuyển đổi màu sang RGB để MediaPipe xử lý
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            current_timestamp_ms = int(time.time() * 1000)

            # 3. Nhận diện các điểm mốc bàn tay
            hands_landmarks = tracker.detect(rgb_frame, current_timestamp_ms)
            tracking_valid = len(hands_landmarks) >= 2

            # 4. Tính toán góc quay vô lăng ảo
            steer_val, wheel_angle = steering.update(hands_landmarks, w, h)

            # 5. Nhận diện cử chỉ (Phanh, Drift Chữ V, Nitro, Dùng Vật Phẩm, Ga tự động)
            throttle_val, brake_val, nitro_id, item_id, drift_val = gesture.process(hands_landmarks, steer_val)

            # 6. Gửi gói tin UDP tới Unity
            udp.send(
                tracking=tracking_valid,
                confidence=0.95 if tracking_valid else 0.0,
                steering=steer_val,
                throttle=throttle_val,
                brake=brake_val,
                wheel_angle_deg=wheel_angle,
                nitro_press_id=nitro_id,
                item_press_id=item_id,
                drift=drift_val
            )

            # ---------------------------------------------------------
            # 7. VẼ GIAO DIỆN TRỰC QUAN (OPENCV HUD & VISUALIZATION)
            # ---------------------------------------------------------
            # Vẽ các khớp nối bàn tay (Skeleton)
            for lm in hands_landmarks:
                pts = [(int(pt.x * w), int(pt.y * h)) for pt in lm]
                for p in pts:
                    cv2.circle(frame, p, 3, (0, 255, 0), -1)
                # Nối một số xương bàn tay chính
                connections = [
                    (0, 1), (1, 2), (2, 3), (3, 4),        # Ngón cái
                    (0, 5), (5, 6), (6, 7), (7, 8),        # Ngón trỏ
                    (5, 9), (9, 10), (10, 11), (11, 12),   # Ngón giữa
                    (9, 13), (13, 14), (14, 15), (15, 16), # Ngón áp út
                    (13, 17), (17, 18), (18, 19), (19, 20),# Ngón út
                    (0, 17)                                # Cạnh bàn tay
                ]
                for start_idx, end_idx in connections:
                    cv2.line(frame, pts[start_idx], pts[end_idx], (200, 255, 200), 1, cv2.LINE_AA)

            # Vẽ vô lăng ảo
            steering.draw_visualization(frame)

            # Bảng trạng thái HUD phía trên (Header)
            overlay = frame.copy()
            cv2.rectangle(overlay, (0, 0), (w, 85), (20, 20, 20), -1)
            cv2.addWeighted(overlay, 0.75, frame, 0.25, 0, frame)

            # Trạng thái theo dõi
            track_color = (0, 255, 0) if tracking_valid else (0, 0, 255)
            track_text = f"TRACKING: {'2 TAY (OK)' if tracking_valid else f'{len(hands_landmarks)} TAY (CHUA DU)'}"
            cv2.putText(frame, track_text, (15, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.6, track_color, 2)
            cv2.putText(frame, f"FPS: {current_fps:.1f}", (w - 110, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1)

            # Trạng thái hành động / Cử chỉ hiện tại
            gesture_color = (0, 255, 255)
            if "PHANH" in gesture.current_gesture_name:
                gesture_color = (0, 0, 255)
            elif "DRIFT" in gesture.current_gesture_name:
                gesture_color = (0, 140, 255) # Màu cam khi Drift
            elif "NITRO" in gesture.current_gesture_name:
                gesture_color = (255, 0, 255)
            elif "ITEM" in gesture.current_gesture_name:
                gesture_color = (0, 165, 255)

            cv2.putText(frame, f"CU CHI: {gesture.current_gesture_name}", (15, 52), cv2.FONT_HERSHEY_SIMPLEX, 0.6, gesture_color, 2)

            # Hiển thị số đếm sự kiện Nitro & Item & Drift
            drift_tag = " | DRIFT: DANG TRUOT BANH!" if drift_val else ""
            cv2.putText(frame, f"Nitro ID: {nitro_id} | Item ID: {item_id}{drift_tag}", (15, 75), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)

            # Bảng trạng thái hiệu chuẩn phía dưới (Footer)
            footer_overlay = frame.copy()
            cv2.rectangle(footer_overlay, (0, h - 35), (w, h), (10, 10, 10), -1)
            cv2.addWeighted(footer_overlay, 0.75, frame, 0.25, 0, frame)

            calib_msg = calib.calibration_message
            invert_flag = " [DAO LAI: BAT]" if calib.data.get("invert_steering", False) else ""
            cv2.putText(frame, f"{calib_msg}{invert_flag} | [C] Calib | [R] Reset | [I] Invert | [Q] Thoat",
                        (10, h - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (220, 220, 220), 1)

            # Hiển thị cửa sổ
            cv2.imshow("PoseKart - Virtual Steering & Hand Gestures (UDP 5052)", frame)

            # Xử lý phím bấm
            key = cv2.waitKey(1) & 0xFF
            if key == ord('q') or key == 27: # 'q' hoặc ESC
                print("[PoseKart] Da nhan lenh thoat tu nguoi dung.")
                break
            elif key == ord('c') or key == ord('C'):
                calib.start_calibration()
                print("[Calibration] Bat dau hieu chuan goc trung hoa...")
            elif key == ord('r') or key == ord('R'):
                calib.reset_to_defaults()
                print("[Calibration] Da dat lai goc trung hoa ve 0 do.")
            elif key == ord('i') or key == ord('I'):
                inv = calib.toggle_invert_steering()
                print(f"[Steering] Dao chieu danh lai: {'BAT' if inv else 'TAT'}")

    except KeyboardInterrupt:
        print("\n[PoseKart] Ngat boi nguoi dung.")
    finally:
        # Dọn dẹp tài nguyên
        udp.send(tracking=False, throttle=0.0, brake=0.0, steering=0.0)
        udp.close()
        tracker.close()
        cap.release()
        cv2.destroyAllWindows()
        print("[PoseKart] Da giai phong webcam va socket UDP. Chuong trinh ket thuc.")

if __name__ == "__main__":
    main()
