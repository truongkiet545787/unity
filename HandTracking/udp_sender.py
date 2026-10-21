import socket
import json

class UdpSender:
    def __init__(self, host="127.0.0.1", port=5052):
        self.host = host
        self.port = port
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sequence = 0
        self.is_closed = False

    def send(self, tracking=False, confidence=0.0, steering=0.0, throttle=0.0,
             brake=0.0, wheel_angle_deg=0.0, nitro_press_id=0, item_press_id=0, drift=False):
        """
        Đóng gói và gửi gói tin điều khiển UDP JSON tới Unity (localhost:5052).
        """
        if self.is_closed:
            return

        self.sequence += 1

        packet = {
            "sequence": self.sequence,
            "tracking": bool(tracking),
            "confidence": float(round(confidence, 3)),
            "steering": float(round(steering, 4)),
            "throttle": float(round(throttle, 2)),
            "brake": float(round(brake, 2)),
            "wheelAngleDeg": float(round(wheel_angle_deg, 2)),
            "nitroPressId": int(nitro_press_id),
            "itemPressId": int(item_press_id),
            "drift": bool(drift)
        }

        try:
            payload = json.dumps(packet).encode("utf-8")
            self.sock.sendto(payload, (self.host, self.port))
        except Exception as e:
            # Không làm gián đoạn vòng lặp xử lý hình ảnh nếu có sự cố mạng cục bộ
            pass

    def close(self):
        if not self.is_closed:
            self.is_closed = True
            try:
                self.sock.close()
            except Exception:
                pass
