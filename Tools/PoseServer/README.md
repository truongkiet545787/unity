# PoseKart — circular palm steering / Python camera

## Bản hiện tại

- Vô lăng ở vùng **bên trái** của ảnh camera; các nút **DRIFT, NITRO, SKILL,
  R / RECOVER** xếp dọc ở **giữa bên phải**.
- Marker cầm lái là **tâm lòng bàn tay**, không phải đầu ngón trỏ.
- Một tay: di chuyển bàn tay theo cung tròn quanh tâm vô lăng để cua.
  Chỉ bẻ ngón tay hoặc nghiêng cổ tay tại chỗ không còn là thao tác lái chính.
- Hai tay: xoay đường nối hai lòng bàn tay. Khi một tay sang vùng nút,
  tay còn lại tiếp tục điều khiển theo cung tròn.
- Tâm được lấy gần vị trí cầm ban đầu và giới hạn trong vùng bên trái.
  Tâm không chạy theo từng góc xoay, vì như vậy sẽ triệt tiêu chuyển động vòng tròn.
  Tâm giữ cố định sau hiệu chỉnh; bấm C nếu đổi tư thế ngồi/cầm lái.
- Vùng đi thẳng chống rung: bắt đầu cua từ 8°, trở lại thẳng trong 5°.
  Góc nhỏ cua nhẹ theo đường cong tăng dần; đạt hết lái ở 55°.
  Góc lái đo so với tư thế ban đầu, không cộng dồn từng khung hình.
- Bấm **C trong cửa sổ Python**, tay ở tư thế đi thẳng, để đặt lại tâm/hướng.
- Không cần giữ chấm xanh 2 giây; tay thao tác có thể hạ xuống khi chưa cần.

## Chạy

1. Dừng Play và tắt server cũ (Ctrl+C).
2. Nhấp đúp Start_Pose_Server.bat.
3. Python mở camera, tracking và vô lăng. Không hiển thị camera trong game.
4. Unity → Play → Connect Python hand controls → START HAND DRIVING.
5. Giữ Python chạy. Esc/Q/đóng cửa sổ Python hoặc Ctrl+C sẽ dừng webcam/server.
   Dừng Unity chỉ ngắt game; Python vẫn giữ camera để xem tracking.

Các model Hands và Pose Lite dự phòng đã có trên máy hiện tại. Trên máy khác,
chạy setup.ps1 với Python 3.11 trước. Không cần rebuild map để cập nhật CV.

## Các nút

Giữ tay lái trong vùng trái. Đưa **ngón trỏ duỗi** của tay thao tác vào ô bên phải.
Ngón trỏ chỉ dùng cho nút; tay nắm để lái không cần duỗi ngón trỏ.

- DRIFT: giữ tay trong ô và tiếp tục cua.
- NITRO/SKILL: giữ khoảng 0,3 giây; rời ô 0,18 giây trước lần tiếp.
- R / RECOVER: giữ 0,45 giây; gọi đúng chức năng R của xe:
  trở lại vị trí an toàn gần nhất trên đường, không tự reset về xuất phát.
  R vẫn dùng được khi game đang pause do mất tay lái.
- Nitro cần tích đủ và xe chạm đất. Boost nhặt từ hộp quà dùng bằng SKILL.

## Tracking / giới hạn

Hands dùng chung ngưỡng cho hai phía. ID ghép theo vị trí, không dựa vào nhãn trái/phải.
Palm points được làm mượt thích ứng; góc được unwrap, lọc và giới hạn tốc độ.
Khoảng xoay ~40 độ cho lệnh cua tối đa, vùng chết ~2,5 độ.

Khi nắm tay làm Hands mất dấu vùng phải, **Pose Lite dự phòng cổ tay**
chạy tối đa 8 Hz trong lúc cần. Marker dự phòng có chữ wrist; không vẽ ngón tay
giả, không cho điểm dự phòng kích hoạt nút. Chế độ bình thường chỉ cần Hands.
Có thể cần vai/cánh tay lọt khung hình để dự phòng hoạt động. Không bảo đảm
nhận diện được tay bị che hoàn toàn, ra ngoài khung hoặc thiếu sáng.

Mất tay ngắn ~0,25 giây: giữ hướng; sau đó trả lái dần. Quá ~0,8 giây sẽ pause.
Mất gói/camera đứng hình ~0,4 giây cũng pause. Khôi phục tracking sẽ resume.
Smoothing không chứng minh độ chính xác thực tế; người dùng cần kiểm tra.

## Lỗi thường gặp

- Server cũ: đóng và mở lại BAT, rồi Reconnect trong Unity.
- Camera busy: tắt app khác dùng webcam.
- Đổi thiết bị: start.ps1 -CameraIndex 1.
- Đổi backend: start.ps1 -Backend msmf.
- Camera vẫn bật sau khi dừng Play: tắt cửa sổ Python để nhả camera.

## Giao thức / riêng tư

WebSocket local 127.0.0.1:8765, protocol 2, chỉ gửi controls:
tracking_valid, hand_count, steering, drift, nitro_seq, skill_seq, reset_seq,
seq, age_ms, hand_fps, inference_ms, mode, status.
Không gửi JPEG/video sang Unity; không lưu ảnh hay upload ra ngoài máy.
Một native client; browser Origin bị từ chối. Không phải dịch vụ public có xác thực.
Unity Windows Editor/standalone; chưa hỗ trợ WebGL.

Nguồn model:
https://ai.google.dev/edge/mediapipe/solutions/vision/hand_landmarker/python
https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker/python

## Sửa đường (độc lập CV)

Dừng Play → PoseKart > Reference > Repair Road Surface (Keep Scene) → Ctrl+S.
Bản sửa mới hạ terrain và cắt phần tam giác terrain giao footprint mặt đường,
thay vì chỉ hạ đỉnh, đồng thời bỏ riêng vách cắt đường. Giữ địa hình sâu dưới cầu,
UI và cây cối. Lưu terrain vào RoadFittedTerrain.asset, giữ mesh gốc.
Full map builder cũng gọi bước này; không cần full rebuild để dùng menu sửa tại chỗ.

Chưa chạy webcam/inference/Play Mode hay chạy menu sửa scene trong lần sửa này.
