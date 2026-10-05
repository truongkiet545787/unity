"""OpenCV preview belongs to Python, never sent to the game's viewport."""
import math
import cv2
from hand_controls import BUTTONS

WINDOW = "PoseKart - MediaPipe Hands (C: center, Esc: stop)"
EDGES = [(0,1),(1,2),(2,3),(3,4),(0,5),(5,6),(6,7),(7,8),
         (5,9),(9,10),(10,11),(11,12),(9,13),(13,14),(14,15),(15,16),
         (13,17),(0,17),(17,18),(18,19),(19,20)]


def render(image, visual, packet):
    frame = image.copy()
    for hand in visual["hands"]:
        grip=tuple(int(v*s) for v,s in zip(hand["center"],[640,480]))
        if hand["fallback"]:
            cv2.circle(frame,grip,9,(150,200,255),2,cv2.LINE_AA)
            cv2.putText(frame,"wrist",(grip[0]+10,grip[1]),cv2.FONT_HERSHEY_SIMPLEX,.4,(150,200,255),1)
            continue
        points = [(int(p[0]*640),int(p[1]*480)) for p in hand["points"]]
        color = (0,215,255) if hand["id"]==visual["action_id"] else (80,235,100)
        for a,b in EDGES:cv2.line(frame,points[a],points[b],color,2,cv2.LINE_AA)
        for p in points:cv2.circle(frame,p,3,(255,220,70),-1,cv2.LINE_AA)
        # Only the action hand uses its fingertip. Steering marker is the palm/grip.
        cv2.circle(frame,points[8] if hand["id"]==visual["action_id"] else grip,8,color,2,cv2.LINE_AA)
    if visual["center"] is not None:
        x,y = map(int,visual["center"]);radius = int(visual["radius"])
        color = (50,235,75) if visual["valid"] else (110,110,110)
        theta = math.radians(visual["angle"])
        dx,dy = int(math.cos(theta)*radius),int(math.sin(theta)*radius)
        cv2.circle(frame,(x,y),radius,color,3,cv2.LINE_AA)
        cv2.line(frame,(x-dx,y-dy),(x+dx,y+dy),color,3,cv2.LINE_AA)
        cv2.line(frame,(x,y),(x-dy,y+dx),color,3,cv2.LINE_AA)
    for i,(label,rect) in enumerate(BUTTONS):
        x,y,w,h = [int(v*s) for v,s in zip(rect,[640,480,640,480])]
        color = (0,215,255) if visual["hover"]==i else (65,70,85)
        cv2.rectangle(frame,(x,y),(x+w,y+h),color,2)
        cv2.putText(frame,label,(x+12,y+30),cv2.FONT_HERSHEY_SIMPLEX,.65,color,2,cv2.LINE_AA)
        if visual["hover"]==i:
            cv2.rectangle(frame,(x,y+h-7),(x+int(w*visual["progress"]),y+h),color,-1)
    # Solid captions stay readable over a moving camera image.
    cv2.rectangle(frame,(0,0),(640,27),(25,25,25),-1)
    cv2.putText(frame,"LEFT: circular palm steering / RIGHT: buttons + R",(7,19),cv2.FONT_HERSHEY_SIMPLEX,.48,(245,245,245),1,cv2.LINE_AA)
    cv2.rectangle(frame,(0,426),(640,480),(25,25,25),-1)
    cv2.putText(frame,packet["status"],(7,446),cv2.FONT_HERSHEY_SIMPLEX,.5,(240,240,240),1,cv2.LINE_AA)
    cv2.putText(frame,f'Hands: {packet["hand_count"]}  FPS: {packet["hand_fps"]:.1f}  Steer: {packet["steering"]:+.2f}  C: center',
                (7,470),cv2.FONT_HERSHEY_SIMPLEX,.48,(100,235,235),1,cv2.LINE_AA)
    return frame
