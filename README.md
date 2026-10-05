# PoseKart — đua xe kart điều khiển bằng bàn tay

Prototype game đua xe arcade xây dựng bằng Unity, có thể điều khiển bằng bàn phím hoặc vô lăng ảo từ webcam. Python/MediaPipe nhận diện bàn tay và gửi lệnh điều khiển tới Unity qua WebSocket local.

## Tình trạng

Đã có xe và camera bám theo, hệ thống đua (checkpoint, vòng đua, boost và vật phẩm), scene đường đua đảo, lựa chọn xe, HUD, đầu vào bàn phím, cùng server Python cho điều khiển tay. Phần kết nối webcam → Python → Unity và các scene cần được chạy kiểm tra trên máy đích; README này không khẳng định đã nghiệm thu end-to-end.

## Chạy game

1. Mở project bằng Unity **6000.3.21f1**.
2. Mở scene `PoseKart_VehicleSelect` hoặc `PoseKart_FinalIsland` trong `Assets/Scenes/` rồi nhấn Play.
3. Để lái bằng webcam, cài Python 3.11 theo [`Tools/PoseServer/README.md`](Tools/PoseServer/README.md), chạy `Start_Pose_Server.bat`, sau đó trong Unity chọn **Connect Python hand controls** và **START HAND DRIVING**.
4. Trong cửa sổ Python, nhấn `C` để hiệu chỉnh vô lăng; nhấn `Esc` để dừng server và nhả webcam.

## Thành phần

- `Assets/Scripts/Vehicle/`, `Race/`, `AI/`, `Camera/`: xe, hệ thống đua, đối thủ và camera.
- `Assets/Scripts/Input/`: bàn phím, vô lăng ảo và kết nối điều khiển.
- `Assets/Scenes/`: các scene PoseKart và scene milestone đua xe.
- `Assets/Editor/PoseKart*.cs`: công cụ tạo/chỉnh scene đường đua.
- `Tools/PoseServer/`: Python webcam/MediaPipe server, cài đặt và hướng dẫn.
- `POSE_KART_CODEX_HANDOFF/`: kế hoạch, giao thức và tài liệu tham chiếu thiết kế.

Project dùng Unity Input System và Universal Render Pipeline. Server hiện kết nối local với Unity Windows Editor/standalone; chưa hỗ trợ WebGL. Hướng dẫn cấu hình và giới hạn tracking được ghi trong `Tools/PoseServer/README.md`.
