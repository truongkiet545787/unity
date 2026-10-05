# Computer Vision + Virtual Steering Protocol

## Recommended architecture

Webcam → Python/OpenCV + MediaPipe Pose/Hands → gesture/steering interpreter → UDP localhost → Unity `PoseUdpReceiver` → `PoseInputProvider` → `ICarInputProvider` → `CarController`.

## Coordinate and mirror convention

Choose one convention and keep it consistent. Recommended: Python outputs display-mirrored normalized coordinates so player motion feels mirror-like on the preview. Hand semantic identity must still remain correct.

Mandatory test: player rotates hands left → virtual wheel rotates left → car steers left.

## Required landmarks

- left/right shoulder
- left/right elbow
- left/right wrist
- left/right hip
- optional hand landmarks for more robust grip detection

## Calibration

Capture 2–3 seconds of stable pose. Save:

- shoulder center
- shoulder width
- wheel center
- wheel radius
- neutral wrist-line angle
- baseline confidence

After calibration, freeze wheel center/scale for the race session.

## Grip detection

The virtual wheel has left and right grip points near 9 and 3 o'clock. Compute normalized wrist-to-grip distance. Steering is fully valid only when both grips are held.

## Steering

```text
V = rightWrist - leftWrist
angle = atan2(-(Ry - Ly), Rx - Lx)
rawAngle = shortestAngle(angle - neutralAngle)
steering = clamp(rawAngle / maxPoseAngle, -1, +1)
```

Recommended defaults:

- `maxPoseAngle = 55 deg`
- `deadZone = 4 deg`
- EMA alpha = `0.25`
- grip hysteresis: separate enter/exit threshold to prevent flicker

## Tracking loss

- confidence low for >0.15 s: show warning
- no valid tracking for >0.5 s: smooth steering toward 0, throttle toward 0.25, disable boost
- no valid tracking for >1.5 s: auto-brake or pause (choose one after playtest)

## UDP packet

Default port: `5052`.

```json
{
  "timestamp": 1730000000.123,
  "trackingValid": true,
  "confidence": 0.94,
  "leftGrip": true,
  "rightGrip": true,
  "wheelAngleDeg": -18.4,
  "steering": -0.33,
  "throttle": 1.0,
  "brake": 0.0,
  "boost": false,
  "poseFps": 30.0
}
```

## Unity receiver rules

- UDP listener must not block Unity main thread
- keep only the latest packet snapshot
- timeout after 0.5 s
- do not spam logs per packet
- expose last-packet age for debug UI

## Debug overlay

Show: tracking state, left/right grip, wheel angle, steering value, pose FPS, network packet age/latency estimate. Toggle with F3.
