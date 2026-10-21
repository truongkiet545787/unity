# PoseKart - Hệ Thống Điều Khiển Bằng Cử Chỉ Bàn Tay (MediaPipe Hand Tracking)

Thư mục này chứa toàn bộ mã nguồn module thị giác máy tính (Computer Vision) xử lý cử chỉ bàn tay từ webcam, tính toán góc xoay vô lăng ảo và truyền lệnh điều khiển tới Unity thông qua giao thức UDP localhost:5052.

---

## 1. Yêu cầu hệ thống
- Hệ điều hành: Windows 10 / 11
- Webcam tiêu chuẩn (tích hợp hoặc USB)
- Python 3.10 hoặc 3.11 (đã cài đặt và thêm vào biến môi trường PATH)
- Các thư viện Python: OpenCV, MediaPipe, NumPy

---

## 2. Cách cài đặt và khởi động
Có 2 cách để khởi chạy:

### Cách 1: Chạy file Batch tự động (Khuyến nghị)
Nhấp đúp chuột vào file:
```text
run_hand_control.bat
```
File này sẽ tự động kiểm tra Python, cài đặt thư viện thiếu và mở ứng dụng.

### Cách 2: Chạy thủ công bằng dòng lệnh
Mở Command Prompt / Terminal tại thư mục `HandTracking`:
```bash
pip install -r requirements.txt
python hand_control.py
```

---

## 3. Cấu trúc các file mã nguồn
- `hand_control.py`: Điểm khởi chạy chính. Nhận diện bàn tay qua MediaPipe, vẽ giao diện vô lăng ảo và điều phối luồng gửi UDP.
- `virtual_steering.py`: Tính toán tâm lòng bàn tay, góc nghiêng giữa 2 tay, vùng chết (deadzone) và bộ lọc mượt (smoothing).
- `gesture_detector.py`: Nhận diện các cử chỉ Phanh (Brake), Nitro, Dùng vật phẩm (Use Item) với cơ chế chống kích hoạt liên tục (release-to-rearm) và ưu tiên cử chỉ.
- `calibration.py`: Quản lý hiệu chuẩn góc trung hòa (neutral angle) của vô lăng, lưu vào file `calibration.json`.
- `udp_sender.py`: Đóng gói dữ liệu điều khiển theo định dạng JSON chuẩn và gửi qua UDP tới `127.0.0.1:5052`.
- `hand_landmarker.task`: File mô hình nhận diện mốc bàn tay của MediaPipe Tasks API.

---

## 4. Hướng dẫn cử chỉ điều khiển trong game

| Hành động trong game | Cử chỉ bàn tay tương ứng | Mô tả chi tiết |
| :--- | :--- | :--- |
| **Đánh lái (Steering)** | **Xoay vô lăng ảo bằng 2 tay** | Giơ cả 2 tay trước camera như đang cầm vô lăng. Nghiêng tay trái xuống để rẽ trái, nghiêng tay phải xuống để rẽ phải. |
| **Tăng tốc (Auto Throttle)** | **Duy trì nhận diện 2 tay** | Tự động đạp ga khi đang theo dõi hợp lệ 2 tay và không phanh. |
| **Phanh xe (Brake)** | **Xòe cả 2 lòng bàn tay** | Xòe tất cả các ngón ở cả 2 bàn tay hướng về camera. Cử chỉ này có ưu tiên cao nhất, lập tức ngắt ga và đạp phanh. |
| **Kích hoạt Nitro (Nitro)** | **Xòe bàn tay phải** | Giữ tay trái cầm lái, xòe các ngón tay phải ra. Mỗi lần xòe kích hoạt đúng 1 lần nhấn Nitro (tương đương phím E). Cần khép tay lại rồi xòe tiếp để lên cấp Nitro kế tiếp hoặc canh Perfect Nitro. |
| **Dùng vật phẩm (Use Item)** | **Nắm đấm bàn tay phải** | Nắm chặt bàn tay phải lại thành nắm đấm. Kích hoạt đúng 1 lần dùng item (tương đương phím Space). Cần thả lỏng tay rồi nắm lại để dùng item tiếp theo. |
| **Drift (Trượt bánh)** | **Giơ 2 ngón tay chữ V (Peace Sign)** hoặc **Cua gắt tự động (Drift Assist)** | Giơ 2 ngón tay (ngón trỏ và ngón giữa) tạo thành hình chữ V ở tay trái (hoặc tay phải) khi đang ôm cua để chủ động vào Drift. Hoặc đánh lái gắt (> 70%) ở tốc độ cao (> 65 km/h) để xe tự động Drift. |
| **Đặt lại xe (Reset)** | **Phím R trên bàn phím** | Phím R trên bàn phím luôn hoạt động dự phòng để hồi phục xe. |

---

## 5. Phím tắt trên cửa sổ Webcam
- **[C]**: Bắt đầu hiệu chuẩn góc trung hòa (người chơi giữ yên 2 tay ở tư thế thoải mái nhất trong 1-2 giây).
- **[R]**: Đặt lại hiệu chuẩn về góc 0 độ mặc định.
- **[I]**: Đảo chiều đánh lái (Invert Steering) nếu cần.
- **[Q]** hoặc **[ESC]**: Đóng chương trình.

---

## 6. Xử lý sự cố thường gặp (Troubleshooting)
1. **Camera không hiển thị / Báo lỗi không mở được camera:**
   - Đảm bảo không có ứng dụng nào khác (Zoom, Teams, Camera app) đang chiếm quyền sử dụng webcam.
   - Thử đổi chỉ số camera trong `hand_control.py` từ `cv2.VideoCapture(0)` thành `cv2.VideoCapture(1)`.
2. **Unity không nhận tín hiệu điều khiển:**
   - Đảm bảo cửa sổ Python đang chạy và hiển thị trạng thái `TRACKING: 2 TAY (OK)`.
   - Kiểm tra firewall/antivirus không chặn cổng UDP nội bộ `5052`.
   - Trong Unity, đảm bảo chế độ điều khiển đã được chuyển sang **Hand Control** (hoặc nhấn phím `F2` trong khi chơi để chuyển đổi).
