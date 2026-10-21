import time
import math

class HandPoseState:
    UNKNOWN = "UNKNOWN"
    OPEN_PALM = "OPEN_PALM"
    FIST = "FIST"
    GRIP = "GRIP"
    V_SIGN = "V_SIGN"

class GestureDetector:
    def __init__(self, calibration_manager):
        self.calib = calibration_manager
        
        # Sequence IDs for discrete events (sent to Unity)
        self.nitro_press_id = 0
        self.item_press_id = 0

        # State machines & release-to-rearm protection
        self.nitro_armed = True
        self.item_armed = True
        
        # Timing trackers
        self.nitro_gesture_start_time = 0.0
        self.item_gesture_start_time = 0.0
        self.last_nitro_trigger_time = 0.0
        self.last_item_trigger_time = 0.0

        # Active status
        self.current_gesture_name = "STEERING (LÁI XE)"
        self.is_braking = False
        self.is_drifting = False
        self.is_auto_throttle = True

    @staticmethod
    def _distance(p1, p2):
        return math.hypot(p1.x - p2.x, p1.y - p2.y)

    def analyze_hand_shape(self, landmarks, palm_center):
        """
        Phân tích trạng thái bàn tay: 
        - Mở (OPEN_PALM)
        - Nắm đấm (FIST)
        - Chữ V (V_SIGN - Trỏ + Giữa duỗi thẳng, Áp út + Út gập lại để Drift)
        - Cầm lái tự nhiên (GRIP)
        """
        wrist = landmarks[0]

        # Kiểm tra 4 ngón: Trỏ (8), Giữa (12), Áp út (16), Út (20)
        finger_tips = [8, 12, 16, 20]
        finger_pips = [6, 10, 14, 18]
        finger_mcps = [5, 9, 13, 17]

        extended_count = 0
        curled_count = 0

        dist_tip_wrist_list = []
        dist_pip_wrist_list = []

        for tip_idx, pip_idx, mcp_idx in zip(finger_tips, finger_pips, finger_mcps):
            tip = landmarks[tip_idx]
            pip = landmarks[pip_idx]
            mcp = landmarks[mcp_idx]

            dist_tip_wrist = self._distance(tip, wrist)
            dist_pip_wrist = self._distance(pip, wrist)
            dist_tip_mcp = self._distance(tip, mcp)

            dist_tip_wrist_list.append(dist_tip_wrist)
            dist_pip_wrist_list.append(dist_pip_wrist)

            # Ngón tay duỗi thẳng: đầu ngón xa cổ tay hơn khớp PIP
            if dist_tip_wrist > dist_pip_wrist * 1.15:
                extended_count += 1
            # Ngón tay gập/cuộn: đầu ngón gần cổ tay hoặc khớp MCP
            elif dist_tip_wrist < dist_pip_wrist * 1.02 or dist_tip_mcp < 0.06:
                curled_count += 1

        # Kiểm tra cử chỉ chữ V (V-Sign / Peace Sign):
        # Ngón trỏ (index 0 trong list) và Giữa (index 1) duỗi thẳng
        # Ngón áp út (index 2) và Út (index 3) gập lại
        index_ext = dist_tip_wrist_list[0] > dist_pip_wrist_list[0] * 1.15
        middle_ext = dist_tip_wrist_list[1] > dist_pip_wrist_list[1] * 1.15
        ring_curl = dist_tip_wrist_list[2] < dist_pip_wrist_list[2] * 1.05
        pinky_curl = dist_tip_wrist_list[3] < dist_pip_wrist_list[3] * 1.05

        if index_ext and middle_ext and ring_curl and pinky_curl:
            return HandPoseState.V_SIGN

        if extended_count >= 4:
            return HandPoseState.OPEN_PALM
        elif curled_count >= 4:
            return HandPoseState.FIST
        else:
            return HandPoseState.GRIP

    def process(self, hands_landmarks, current_steering):
        """
        Nhận diện cử chỉ kết hợp hai bàn tay theo thứ tự ưu tiên nghiêm ngặt:
        1. PHANH (Brake): CẢ HAI TAY XÒE MỞ -> Ưu tiên cao nhất.
        2. DRIFT (Chữ V): TAY TRÁI (hoặc TAY PHẢI) TẠO CHỮ V (2 ngón trỏ + giữa giơ lên).
        3. SỬ DỤNG VẬT PHẨM (Use Item): TAY PHẢI NẮM ĐẤM.
        4. NITRO: TAY PHẢI XÒE MỞ (tay trái cầm vô lăng).
        5. LÁI XE BÌNH THƯỜNG: Ga tự động (Auto Throttle = 1.0, Brake = 0.0).
        """
        now = time.time()
        min_hold = self.calib.data.get("min_gesture_hold_sec", 0.08)
        cooldown = self.calib.data.get("gesture_cooldown_sec", 0.25)

        throttle = 1.0 if self.calib.data.get("auto_throttle", True) else 0.0
        brake = 0.0
        drift = False

        if len(hands_landmarks) < 2:
            self.current_gesture_name = "MẤT DẤU TAY (TRACKING LOST)"
            return 0.0, 0.0, self.nitro_press_id, self.item_press_id, False

        # Xác định tay trái và tay phải theo tọa độ x (sau khi đã lật gương)
        if hands_landmarks[0][0].x < hands_landmarks[1][0].x:
            left_lm, right_lm = hands_landmarks[0], hands_landmarks[1]
        else:
            left_lm, right_lm = hands_landmarks[1], hands_landmarks[0]

        left_shape = self.analyze_hand_shape(left_lm, (left_lm[0].x, left_lm[0].y))
        right_shape = self.analyze_hand_shape(right_lm, (right_lm[0].x, right_lm[0].y))

        # -------------------------------------------------------------
        # QUY TẮC ƯU TIÊN 1: PHANH (CẢ HAI TAY ĐỀU XÒE MỞ)
        # -------------------------------------------------------------
        if left_shape == HandPoseState.OPEN_PALM and right_shape == HandPoseState.OPEN_PALM:
            self.is_braking = True
            self.is_drifting = False
            throttle = 0.0
            brake = 1.0
            self.current_gesture_name = "PHANH XE (BRAKE - CẢ 2 TAY MỞ)"
            self.nitro_gesture_start_time = 0.0
            self.item_gesture_start_time = 0.0
            return throttle, brake, self.nitro_press_id, self.item_press_id, False

        self.is_braking = False

        # -------------------------------------------------------------
        # QUY TẮC ƯU TIÊN 2: DRIFT (CỬ CHỈ CHỮ V Ở TAY TRÁI HOẶC TAY PHẢI)
        # -------------------------------------------------------------
        if left_shape == HandPoseState.V_SIGN or right_shape == HandPoseState.V_SIGN:
            drift = True
            self.is_drifting = True
            self.current_gesture_name = "DRIFT! (CHỮ V - 2 NGÓN TAY)"
        else:
            self.is_drifting = False

        # -------------------------------------------------------------
        # QUY TẮC ƯU TIÊN 3: SỬ DỤNG VẬT PHẨM (TAY PHẢI NẮM ĐẤM)
        # -------------------------------------------------------------
        if right_shape == HandPoseState.FIST:
            if self.item_gesture_start_time == 0.0:
                self.item_gesture_start_time = now

            hold_time = now - self.item_gesture_start_time
            if hold_time >= min_hold and self.item_armed and (now - self.last_item_trigger_time >= cooldown):
                self.item_press_id += 1
                self.item_armed = False
                self.last_item_trigger_time = now
                self.current_gesture_name = f"DÙNG ITEM! (FIST - Sự kiện #{self.item_press_id})"
            elif not self.item_armed:
                self.current_gesture_name = f"GIỮ ITEM (ĐÃ DÙNG #{self.item_press_id})"
            else:
                self.current_gesture_name = "CHUẨN BỊ ITEM (ĐANG NẮM ĐẤM...)"
        else:
            self.item_armed = True
            self.item_gesture_start_time = 0.0

        # -------------------------------------------------------------
        # QUY TẮC ƯU TIÊN 4: NITRO (CHỈ TAY PHẢI XÒE MỞ, TAY TRÁI KHÔNG MỞ)
        # -------------------------------------------------------------
        if right_shape == HandPoseState.OPEN_PALM and left_shape != HandPoseState.OPEN_PALM:
            if self.nitro_gesture_start_time == 0.0:
                self.nitro_gesture_start_time = now

            hold_time = now - self.nitro_gesture_start_time
            if hold_time >= min_hold and self.nitro_armed and (now - self.last_nitro_trigger_time >= cooldown):
                self.nitro_press_id += 1
                self.nitro_armed = False
                self.last_nitro_trigger_time = now
                self.current_gesture_name = f"KÍCH HOẠT NITRO! (XÒE PHẢI - Sự kiện #{self.nitro_press_id})"
            elif not self.nitro_armed:
                self.current_gesture_name = f"GIỮ NITRO (ĐÃ KÍCH HOẠT #{self.nitro_press_id})"
            else:
                self.current_gesture_name = "CHUẨN BỊ NITRO (ĐANG XÒE TAY...)"
        else:
            self.nitro_armed = True
            self.nitro_gesture_start_time = 0.0

        # Nếu đang lái xe bình thường (không có cử chỉ đặc biệt)
        if not drift and right_shape != HandPoseState.FIST and right_shape != HandPoseState.OPEN_PALM:
            self.current_gesture_name = "ĐANG LÁI XE (STEERING - CẦM VÔ LĂNG)"

        return throttle, brake, self.nitro_press_id, self.item_press_id, drift
