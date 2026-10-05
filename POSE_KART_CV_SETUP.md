# PoseKart CV — circular hand steering

See Tools/PoseServer/README.md for full instructions.

- Python camera window: wheel on the LEFT, DRIFT / NITRO / SKILL / R in the middle RIGHT.
- Front wheel yaw follows the car's continuous smoothed steering command (up to 30 degrees), independently of tire rolling. Rear wheels do not steer.
- Steering uses the palm moving around a circle; two hands may rotate the line between grips.
- Finger folding does not drive steering. Index fingertip is used only for action buttons.
- Pose wrist fallback supports a temporarily lost closed grip when visible to the pose model.
- C in Python recalibrates pivot/neutral. No fixed green-dot hold.
- Restart Start_Pose_Server.bat after updates. Unity: Connect Python hand controls,
  then START HAND DRIVING. No camera is drawn over the game.
- Stop Python to release webcam; stopping Play only disconnects Unity.

The in-place road repair menu now clips terrain triangles out of the road footprint:
PoseKart > Reference > Repair Road Surface (Keep Scene), then Ctrl+S in Edit Mode.
It has not been executed by the assistant; no runtime tests were run.
