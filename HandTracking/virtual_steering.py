import math
import numpy as np
import cv2

class VirtualSteeringWheel:
    def __init__(self, calibration_manager):
        self.calib = calibration_manager
        self.current_steering = 0.0      # Smoothed output [-1.0 .. +1.0]
        self.raw_steering = 0.0          # Unfiltered output [-1.0 .. +1.0]
        self.current_wheel_angle_deg = 0.0 # Current angle relative to neutral
        self.raw_angle_deg = 0.0         # Absolute angle between palms
        self.palm_left = None            # (x, y) normalized [0..1]
        self.palm_right = None           # (x, y) normalized [0..1]
        self.hand_distance = 0.0

    @staticmethod
    def calculate_palm_center(landmarks):
        """
        Tính trọng tâm lòng bàn tay từ các điểm mốc:
        0 (cổ tay), 5 (gốc ngón trỏ), 9 (gốc ngón giữa), 13 (gốc ngón áp út), 17 (gốc ngón út).
        """
        indices = [0, 5, 9, 13, 17]
        cx = sum(landmarks[i].x for i in indices) / len(indices)
        cy = sum(landmarks[i].y for i in indices) / len(indices)
        return (cx, cy)

    def update(self, hands_landmarks, image_width, image_height):
        """
        Cập nhật góc lái từ 2 bàn tay phát hiện được.
        hands_landmarks: danh sách landmarks của các bàn tay.
        """
        if len(hands_landmarks) < 2:
            # Mất dấu một hoặc cả hai tay -> từ từ trả lái về 0
            self.current_steering = float(np.clip(
                self.current_steering * 0.85 if abs(self.current_steering) > 0.01 else 0.0,
                -1.0, 1.0
            ))
            self.raw_steering = 0.0
            self.current_wheel_angle_deg = 0.0
            self.palm_left = None
            self.palm_right = None
            return self.current_steering, 0.0

        # Tính tọa độ tâm 2 lòng bàn tay
        palms = [self.calculate_palm_center(lm) for lm in hands_landmarks[:2]]

        # Trong ảnh webcam đã được lật gương (mirrored):
        # Bàn tay có x nhỏ hơn nằm bên trái màn hình (tay trái của người lái)
        # Bàn tay có x lớn hơn nằm bên phải màn hình (tay phải của người lái)
        if palms[0][0] < palms[1][0]:
            p_left, p_right = palms[0], palms[1]
        else:
            p_left, p_right = palms[1], palms[0]

        self.palm_left = p_left
        self.palm_right = p_right

        # Vector từ tay trái sang tay phải (hệ tọa độ pixel thực tế)
        dx = (p_right[0] - p_left[0]) * image_width
        dy = (p_right[1] - p_left[1]) * image_height

        self.hand_distance = math.sqrt((p_right[0] - p_left[0])**2 + (p_right[1] - p_left[1])**2)

        # Tính góc nghiêng (độ):
        # Trong ảnh máy tính (trục y hướng xuống):
        # - Quay vô lăng sang TRÁI: tay trái hạ xuống (y_L tăng), tay phải nâng lên (y_R giảm) -> dy < 0 -> angle < 0
        # - Quay vô lăng sang PHẢI: tay trái nâng lên (y_L giảm), tay phải hạ xuống (y_R tăng) -> dy > 0 -> angle > 0
        self.raw_angle_deg = math.degrees(math.atan2(dy, dx))

        # Nếu đang trong quá trình hiệu chuẩn thì gửi mẫu vào CalibrationManager
        if self.calib.is_calibrating:
            self.calib.add_sample(self.raw_angle_deg, self.hand_distance)

        # Trừ đi góc trung hòa đã hiệu chuẩn
        neutral = self.calib.data.get("neutral_angle_deg", 0.0)
        delta_angle = self.raw_angle_deg - neutral

        # Chuẩn hóa về khoảng [-180, 180]
        while delta_angle > 180.0:
            delta_angle -= 360.0
        while delta_angle < -180.0:
            delta_angle += 360.0

        self.current_wheel_angle_deg = delta_angle

        # Áp dụng Dead zone & Góc lái tối đa
        dead_zone = self.calib.data.get("dead_zone_deg", 5.0)
        max_angle = self.calib.data.get("max_steering_angle_deg", 35.0)

        abs_delta = abs(delta_angle)
        if abs_delta <= dead_zone:
            raw_val = 0.0
        else:
            effective = (abs_delta - dead_zone) / max(0.1, (max_angle - dead_zone))
            raw_val = math.copysign(min(1.0, effective), delta_angle)

        # Tùy chọn đảo chiều lái (Invert Steering)
        if self.calib.data.get("invert_steering", False):
            raw_val = -raw_val

        self.raw_steering = raw_val

        # Lọc mượt (Exponential Moving Average smoothing)
        alpha = self.calib.data.get("steering_smoothing", 0.35)
        self.current_steering = (alpha * self.raw_steering) + ((1.0 - alpha) * self.current_steering)
        self.current_steering = float(np.clip(self.current_steering, -1.0, 1.0))

        return self.current_steering, self.current_wheel_angle_deg

    def draw_visualization(self, image):
        """
        Vẽ giao diện vô lăng ảo trực tiếp lên khung hình OpenCV.
        """
        h, w = image.shape[:2]
        if self.palm_left is None or self.palm_right is None:
            # Vẽ thông báo hướng dẫn đưa cả 2 tay lên
            cv2.putText(image, "DUA CA 2 TAY LEN TRUOC CAMERA DE LAI", 
                        (w // 2 - 240, h // 2), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 165, 255), 2)
            return

        pt_l = (int(self.palm_left[0] * w), int(self.palm_left[1] * h))
        pt_r = (int(self.palm_right[0] * w), int(self.palm_right[1] * h))
        center_x = (pt_l[0] + pt_r[0]) // 2
        center_y = (pt_l[1] + pt_r[1]) // 2
        wheel_radius = int(math.hypot(pt_r[0] - pt_l[0], pt_r[1] - pt_l[1]) / 2)

        # Màu sắc vô lăng thay đổi theo góc bẻ lái: Xanh lá (thẳng), Vàng (vừa), Đỏ (gắt)
        steer_abs = abs(self.current_steering)
        if steer_abs < 0.2:
            color = (0, 255, 128) # Xanh lá
        elif steer_abs < 0.65:
            color = (0, 220, 255) # Vàng cam
        else:
            color = (0, 90, 255)  # Cam đỏ

        # 1. Vẽ vòng tròn vô lăng ảo nối 2 tay
        if wheel_radius > 20:
            cv2.circle(image, (center_x, center_y), wheel_radius, color, 3, cv2.LINE_AA)
            cv2.circle(image, (center_x, center_y), 6, (255, 255, 255), -1, cv2.LINE_AA)

        # 2. Vẽ thanh nối 2 tâm bàn tay
        cv2.line(image, pt_l, pt_r, color, 4, cv2.LINE_AA)

        # 3. Vẽ biểu tượng tay trái và tay phải
        cv2.circle(image, pt_l, 14, (255, 120, 0), -1, cv2.LINE_AA) # Xanh dương
        cv2.circle(image, pt_r, 14, (0, 200, 255), -1, cv2.LINE_AA) # Vàng
        cv2.putText(image, "L", (pt_l[0] - 6, pt_l[1] + 5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 2)
        cv2.putText(image, "R", (pt_r[0] - 6, pt_r[1] + 5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 0), 2)

        # 4. Hiển thị góc quay độ và giá trị lái
        direction_text = "THANG"
        if self.current_steering < -0.05:
            direction_text = f"TRAI ({abs(self.current_steering)*100:.0f}%)"
        elif self.current_steering > 0.05:
            direction_text = f"PHAI ({self.current_steering*100:.0f}%)"

        info_str = f"Goc: {self.current_wheel_angle_deg:+.1f} deg | Huong: {direction_text}"
        cv2.putText(image, info_str, (center_x - 130, center_y - wheel_radius - 15),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2, cv2.LINE_AA)
