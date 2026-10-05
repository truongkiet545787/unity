using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Input
{
    /// <summary>Python owns adaptive hand tracking and UI. Unity applies the control packets.</summary>
    public sealed partial class VirtualWheelInput : MonoBehaviour,ICarInputProvider,IKartGestureInput
    {
        private CarController car;
        private MonoBehaviour keyboard;
        private LocalPoseTracker tracker;
        private bool cameraMode,started,paused,previousRunInBackground;
        private float steer,recovery,savedTimeScale=1;
        private int lastNitro,lastSkill,lastReset;
        public float Steering=>steer;
        public float Throttle=>IsInputValid?1:0;
        public float Brake=>0;
        public bool Boost=>false;
        public bool Drift { get; private set; }
        public bool IsInputValid=>isActiveAndEnabled&&cameraMode&&started&&tracker!=null&&tracker.TrackingValid&&!paused;

        private void Start()
        {
            car=GetComponent<CarController>();keyboard=car.InputProviderSource;
            tracker=gameObject.AddComponent<LocalPoseTracker>();
        }
        private void SetPaused(bool value)
        {
            if(value==paused)return;
            if(value){savedTimeScale=Time.timeScale;Time.timeScale=0;}else Time.timeScale=savedTimeScale;
            paused=value;
        }
        private void EnableCamera()
        {
            if(!cameraMode){previousRunInBackground=Application.runInBackground;Application.runInBackground=true;}
            cameraMode=true;started=false;steer=0;recovery=0;Drift=false;
            SetPaused(true);tracker.OpenCamera();car.SetInputProvider(this);
        }
        private void KeyboardMode()
        {
            bool wasCamera=cameraMode;
            cameraMode=false;started=false;Drift=false;steer=0;
            if(tracker!=null)tracker.CloseCamera();
            if(car!=null&&keyboard!=null)car.SetInputProvider(keyboard);
            SetPaused(false);
            if(wasCamera)Application.runInBackground=previousRunInBackground;
        }
        private void Update()
        {
            if(!cameraMode||tracker==null)return;
            // Consume counters even while paused, so old gestures never fire on resume.
            bool nitro=tracker.NitroSequence>lastNitro,skill=tracker.SkillSequence>lastSkill;
            bool reset=tracker.ResetSequence>lastReset;
            lastNitro=tracker.NitroSequence;lastSkill=tracker.SkillSequence;lastReset=tracker.ResetSequence;
            if(!started)return;
            // Manual R works even during tracking-loss pause. Never replay stale packets.
            if(reset&&tracker.Fresh){GetComponent<CarResetController>()?.ResetVehicle();steer=0;Drift=false;}
            float dt=Time.unscaledDeltaTime;
            if(!tracker.TrackingValid)
            {
                Drift=false;recovery=0;steer=Mathf.MoveTowards(steer,0,dt*3);
                SetPaused(true);return;
            }
            // Short occlusions are handled smoothly by Python. Sustained loss pauses safely.
            recovery+=dt;
            if(paused&&recovery<.2f)return;
            bool wasPaused=paused;
            SetPaused(false);
            // Python already filters landmarks and wheel angle; avoid another long input lag.
            steer=Mathf.Lerp(steer,tracker.Steering,1-Mathf.Exp(-32*dt));
            Drift=tracker.Drift;
            if(car.ControlEnabled&&!wasPaused)
            {
                if(nitro)car.UseStoredNitro();
                if(skill)GetComponent<KartItemInventory>()?.UseItem();
            }
        }
        private void OnDisable()=>KeyboardMode();
    }
}
