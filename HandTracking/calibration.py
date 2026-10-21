import os
import json

DEFAULT_CALIBRATION_FILE = os.path.join(os.path.dirname(__file__), "calibration.json")

DEFAULT_SETTINGS = {
    "neutral_angle_deg": 0.0,
    "neutral_distance": 0.35,
    "dead_zone_deg": 5.0,
    "max_steering_angle_deg": 35.0,
    "invert_steering": False,
    "auto_throttle": True,
    "steering_smoothing": 0.35,
    "min_gesture_hold_sec": 0.08,
    "gesture_cooldown_sec": 0.25
}

class CalibrationManager:
    def __init__(self, filepath=DEFAULT_CALIBRATION_FILE):
        self.filepath = filepath
        self.data = dict(DEFAULT_SETTINGS)
        self.load()
        
        # Calibration sampling state
        self.is_calibrating = False
        self.sample_count = 0
        self.max_samples = 30
        self.accumulated_angle = 0.0
        self.accumulated_distance = 0.0
        self.calibration_message = "Chưa hiệu chuẩn (dùng mặc định)"

    def load(self):
        if os.path.exists(self.filepath):
            try:
                with open(self.filepath, "r", encoding="utf-8") as f:
                    saved = json.load(f)
                    self.data.update(saved)
                self.calibration_message = f"Đã nạp hiệu chuẩn (Góc lệch: {self.data['neutral_angle_deg']:.1f}°)"
            except Exception as e:
                print(f"[Calibration] Lỗi khi nạp file hiệu chuẩn: {e}")

    def save(self):
        try:
            with open(self.filepath, "w", encoding="utf-8") as f:
                json.dump(self.data, f, indent=4, ensure_ascii=False)
            self.calibration_message = f"Đã lưu hiệu chuẩn: {self.data['neutral_angle_deg']:.1f}°"
            print(f"[Calibration] {self.calibration_message}")
        except Exception as e:
            print(f"[Calibration] Lỗi khi lưu file hiệu chuẩn: {e}")

    def reset_to_defaults(self):
        self.data = dict(DEFAULT_SETTINGS)
        self.save()
        self.calibration_message = "Đã đặt lại hiệu chuẩn mặc định (0.0°)"

    def start_calibration(self):
        self.is_calibrating = True
        self.sample_count = 0
        self.accumulated_angle = 0.0
        self.accumulated_distance = 0.0
        self.calibration_message = "ĐANG HIỆU CHUẨN: Giữ yên hai tay ở tư thế tự nhiên..."

    def add_sample(self, raw_angle_deg, distance):
        if not self.is_calibrating:
            return False

        self.accumulated_angle += raw_angle_deg
        self.accumulated_distance += distance
        self.sample_count += 1

        if self.sample_count >= self.max_samples:
            avg_angle = self.accumulated_angle / self.sample_count
            avg_dist = self.accumulated_distance / self.sample_count
            self.data["neutral_angle_deg"] = float(round(avg_angle, 2))
            self.data["neutral_distance"] = float(round(avg_dist, 3))
            self.save()
            self.is_calibrating = False
            self.calibration_message = f"HIỆU CHUẨN HOÀN TẤT: Góc trung hòa = {self.data['neutral_angle_deg']:.1f}°"
            return True
        else:
            pct = int((self.sample_count / self.max_samples) * 100)
            self.calibration_message = f"ĐANG LẤY MẪU: {pct}% - Giữ yên tay..."
            return False

    def toggle_invert_steering(self):
        self.data["invert_steering"] = not self.data.get("invert_steering", False)
        self.save()
        return self.data["invert_steering"]
