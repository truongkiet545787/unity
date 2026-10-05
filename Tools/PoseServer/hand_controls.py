"""Palm-circle steering on the left; fingertip actions in the middle-right panel."""
import math
import time
import numpy as np

BUTTONS = [("DRIFT", (.715,.24,.25,.12)), ("NITRO", (.715,.39,.25,.12)),
           ("SKILL", (.715,.54,.25,.12)), ("R / RECOVER", (.715,.69,.25,.12))]
SCALE = np.array([640,480])


def wrap(angle):
    return (angle+180)%360-180


def palm(points):
    return points[[0,5,9,13,17]].mean(axis=0)


def inside(point, rect, margin=0):
    x,y,w,h = rect
    return x-margin <= point[0] <= x+w+margin and y-margin <= point[1] <= y+h+margin


class HandControls:
    def __init__(self):
        self.tracks = {}
        self.next_id = 0
        self.primary = self.action_id = None
        self.mode_key = None
        self.pair_since = None
        self.last_raw = None
        self.neutral_angles = {}
        self.turning = False
        self.target_angle = self.angle = self.steering = 0.0
        self.last_seen = -100.0
        self.previous = time.monotonic()
        self.hover = -1
        self.dwell = self.release = 0.0
        self.latched = True
        self.nitro_seq = self.skill_seq = self.reset_seq = 0
        self.recenter_requested = False
        self.wheel_center = None
        self.wheel_radius = 100.0
        self.radial_drift = 0.0

    def associate(self, detections, now):
        self.tracks = {k:v for k,v in self.tracks.items() if now-v["seen"]<.9}
        unused = set(range(len(detections)));available = set(self.tracks);matches=[]
        pairs = sorted((float(np.linalg.norm((d["center"]-t["center"])*SCALE)),k,i)
                       for i,d in enumerate(detections) for k,t in self.tracks.items())
        for distance,k,i in pairs:
            if distance<140 and i in unused and k in available:
                matches.append((k,i));unused.remove(i);available.remove(k)
        for i in sorted(unused):
            self.next_id+=1;matches.append((self.next_id,i))
        hands=[]
        for k,i in matches:
            hand=dict(detections[i]);previous=self.tracks.get(k)
            if previous is not None:
                elapsed=max(.001,now-previous["seen"])
                speed=float(np.linalg.norm(hand["center"]-previous["center"]))/elapsed
                alpha=1-math.exp(-min(40,14+speed*22)*min(elapsed,.15))
                hand["center"]=previous["center"]+(hand["center"]-previous["center"])*alpha
                if hand["points"] is not None and previous["points"] is not None:
                    hand["points"]=previous["points"]+(hand["points"]-previous["points"])*alpha
            hand.update(id=k,seen=now)
            self.tracks[k]=hand;hands.append(hand)
        return hands

    def update(self,result,now,wrists=()):
        dt=max(.001,min(.12,now-self.previous));self.previous=now
        detections=[]
        for landmarks in result.hand_landmarks:
            points=np.array([[p.x,p.y] for p in landmarks],dtype=float)
            if points.shape==(21,2) and np.isfinite(points).all():
                detections.append({"points":points,"center":palm(points),"wrist":points[0],"grip_offset":palm(points)-points[0],"fallback":False})
        # Genuine pose wrist observations, not invented finger landmarks.
        # Fallback can steer only; it can never click a button.
        for wrist in wrists:
            wrist=np.asarray(wrist,dtype=float)
            if not (.03<wrist[0]<.6 and .1<wrist[1]<.85):continue
            if any(np.linalg.norm((wrist-h["wrist"])*SCALE)<85 for h in detections):continue
            previous=min(self.tracks.values(),key=lambda h:np.linalg.norm((wrist-h["wrist"])*SCALE),default=None)
            offset=np.zeros(2)
            if previous is not None and np.linalg.norm((wrist-previous["wrist"])*SCALE)<100:
                offset=previous["grip_offset"]
            detections.append({"points":None,"center":wrist+offset,"wrist":wrist,"grip_offset":offset,"fallback":True})
        hands=self.associate(detections,now)

        def pointing(hand):
            p=hand["points"]
            return p is not None and np.linalg.norm(p[8]-p[0])>np.linalg.norm(p[6]-p[0])*1.10

        action=next((h for h in hands if h["id"]==self.action_id and h["points"] is not None
                     and h["points"][8,0]>.64 and .16<h["points"][8,1]<.87),None)
        if action is None:
            candidates=[h for h in hands if pointing(h) and h["points"][8,0]>.69
                        and .2<h["points"][8,1]<.84]
            if candidates:action=max(candidates,key=lambda h:h["points"][8,0])
        self.action_id=action["id"] if action is not None else None
        drivers=[h for h in hands if (action is None or h["id"]!=action["id"])
                 and .12<h["center"][1]<.87]
        # Left-side driving zone. An optional second grip may be to its right.
        left=[h for h in drivers if h["center"][0]<.64]
        primary=next((h for h in left if h["id"]==self.primary),None)
        if primary is None and left:primary=min(left,key=lambda h:h["center"][0])
        if primary is None:
            drivers=[]
        else:
            self.primary=primary["id"]
            others=[h for h in drivers if h["id"]!=self.primary and not h["fallback"]
                    and np.linalg.norm((h["center"]-primary["center"])*SCALE)>90]
            drivers=[primary]+others[:1]
        if len(drivers)==2:
            if self.pair_since is None:self.pair_since=now
            if now-self.pair_since<.15:drivers=drivers[:1]
        else:self.pair_since=None

        if drivers:
            acquired=now-self.last_seen>.8
            self.last_seen=now
            p=drivers[0]["center"]*SCALE
            reset=self.recenter_requested
            if reset or acquired:
                self.neutral_angles.clear()
                self.turning=False
            if self.wheel_center is None or reset:
                # Set pivot from the current grip, but do not chase each angular motion.
                self.wheel_center=np.clip(p+[0,100],[125,210],[275,320]).astype(float)
                self.wheel_radius=100.0
                self.last_raw=None
            key=tuple(sorted(h["id"] for h in drivers))
            if len(drivers)==2:
                a,b=sorted([h["center"]*SCALE for h in drivers],key=lambda point:point[0])
                delta=b-a
                raw=math.degrees(math.atan2(delta[1],delta[0]))
                # Do not move the pivot here: the one-hand neutral must remain valid
                # after the second hand leaves to press an action button.
                radius=max(85,min(115,float(np.linalg.norm(delta))*.5))
                self.wheel_radius+=(radius-self.wheel_radius)*(1-math.exp(-6*dt))
            else:
                delta=p-self.wheel_center;distance=float(np.linalg.norm(delta))
                # Keep the calibrated pivot fixed: chasing radial hand jitter changes
                # the neutral direction and leaves the car turning after the hand returns.
                # C explicitly relocates the pivot when the player changes posture.
                raw=math.degrees(math.atan2(delta[1],delta[0]))
                if np.linalg.norm(delta)<30:raw=self.last_raw if self.last_raw is not None else raw
            if key not in self.neutral_angles:
                # New grip starts neutral; remember each one/two-hand configuration
                # so returning from the button panel restores its original neutral.
                self.neutral_angles[key]=raw
            if key!=self.mode_key:
                self.turning=False
                self.mode_key=key
            self.target_angle=max(-90,min(90,wrap(raw-self.neutral_angles[key])))
            self.last_raw=raw
            self.recenter_requested=False
            movement=(self.target_angle-self.angle)*(1-math.exp(-18*dt))
            self.angle+=max(-180*dt,min(180*dt,movement))
            mode="2 HANDS" if len(drivers)==2 else "WRIST BACKUP" if drivers[0]["fallback"] else "PALM CIRCLE"
            status=mode+" - move grip around wheel / C: re-center"
        else:
            lost=now-self.last_seen
            if lost>.25:self.angle*=math.exp(-5*dt);self.target_angle=self.angle
            mode="BRIEF LOSS" if lost<.8 else "NO GRIP"
            status="Place steering hand in LEFT zone" if lost>=.8 else "Keeping steering through brief occlusion"
        valid=now-self.last_seen<.8
        # Hysteresis prevents hand tremor repeatedly engaging steering near neutral.
        magnitude=abs(self.angle)
        if magnitude<=5:self.turning=False
        elif magnitude>=8:self.turning=True
        # Gentle small corrections, deliberate full lock at 55 degrees.
        strength=min(1,max(0,magnitude-5)/50)**1.6 if self.turning else 0
        target=math.copysign(strength,self.angle)
        self.steering+=(target-self.steering)*(1-math.exp(-(24 if target==0 else 16)*dt))
        if target==0 and abs(self.steering)<.01:self.steering=0.0

        hover=-1
        if action is not None and pointing(action):
            for i,(_,rect) in enumerate(BUTTONS):
                if inside(action["points"][8],rect,.018 if i==self.hover else 0):hover=i;break
        if hover!=self.hover:self.dwell=0
        self.hover=hover;self.dwell+=dt
        if hover<0:
            self.release+=dt
            if self.release>=.18:self.latched=False
        else:
            self.release=0
            dwell=.45 if hover==3 else .3
            if not self.latched and self.dwell>=dwell:
                if hover==1 and drivers:self.nitro_seq+=1;self.latched=True
                if hover==2 and drivers:self.skill_seq+=1;self.latched=True
                if hover==3:self.reset_seq+=1;self.latched=True
        return {"tracking_valid":valid,"hand_count":len(hands),"steering":round(self.steering,5),
                "drift":bool(drivers and valid and hover==0 and self.dwell>=.12),
                "nitro_seq":self.nitro_seq,"skill_seq":self.skill_seq,"reset_seq":self.reset_seq,
                "mode":mode,"status":status}, {
                    "hands":hands,"action_id":self.action_id,"driver_ids":[h["id"] for h in drivers],
                    "center":None if self.wheel_center is None else self.wheel_center.copy(),
                    "radius":self.wheel_radius,"angle":self.angle,"valid":valid,"hover":hover,
                    "progress":min(1,self.dwell/(.45 if hover==3 else .3)) if hover>=0 else 0}
