using System;
using PoseKart.Input;
using UnityEngine;

namespace PoseKart.Vehicle
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody targetBody;
        [SerializeField] private MonoBehaviour inputProviderSource;

        [Header("Speed and acceleration")]
        [SerializeField, Min(0.1f)] private float maxForwardSpeed = 22f;
        [SerializeField, Min(0.1f)] private float maxReverseSpeed = 10f;
        [SerializeField, Min(0f)] private float acceleration = 18f;
        [SerializeField, Min(0f)] private float reverseAcceleration = 15f;
        [SerializeField, Min(0f)] private float brakeDeceleration = 28f;
        [SerializeField, Min(0f)] private float coastingResistance = 1.5f;
        [Tooltip("Rigidbody drag. Keep this low because drive and braking values are accelerations in m/s squared.")]
        [SerializeField, Min(0f)] private float linearDamping = 0.03f;
        [SerializeField, Min(0f)] private float brakeToReverseSpeed = 0.75f;
        [SerializeField, Range(0.05f, 1f)] private float accelerationAtTopSpeed = 0.2f;
        [SerializeField, Min(0.25f)] private float accelerationFalloffPower = 2f;

        [Header("Steering")]
        [SerializeField, Min(0f)] private float steeringDegreesPerSecond = 125f;
        [SerializeField, Range(0.1f, 1f)] private float steeringAtMaxSpeedMultiplier = 0.6f;
        [SerializeField, Min(0.01f)] private float steeringResponse = 7f;
        [SerializeField, Min(0f)] private float uphillAssist = 8f;
        [SerializeField, Min(0f)] private float minimumSteeringSpeed = 0.5f;
        [SerializeField, Range(0f, 1f)] private float cornerPenaltySteeringThreshold = 0.2f;
        [SerializeField, Min(0f)] private float cornerPenaltyStartSpeed = 5f;
        [SerializeField, Min(0f)] private float maximumCornerDeceleration = 8f;
        [SerializeField, Min(0.25f)] private float cornerPenaltyPower = 1.5f;

        [Header("Arcade stability")]
        [SerializeField, Min(0f)] private float lateralGrip = 7f;
        [SerializeField, Min(0f)] private float maximumLateralSpeed = 5f;
        [SerializeField, Min(0f)] private float baseDownforce;
        [SerializeField, Min(0f)] private float speedDownforce = 0.35f;
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 0.9f;
        [SerializeField, Min(0f)] private float maximumGroundedUpwardSpeed = 0.75f;
        [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.45f, 0f);
        [SerializeField] private bool lockPitchAndRoll = true;

        [Header("Control Permission")]
        [SerializeField] private bool controlEnabled = true;
        [SerializeField] private bool brakeAllowedWhenControlDisabled;

        [Header("Arcade Collision Speed Loss")]
        [SerializeField, Min(0f)] private float minimumImpactSpeed = 3f;
        [SerializeField, Min(0f)] private float collisionSpeedLossMultiplier = 0.45f;
        [SerializeField, Min(0f)] private float maximumCollisionSpeedLoss = 8f;
        [SerializeField, Min(0f)] private float collisionLossCooldown = 0.2f;
        [SerializeField, Range(0f, 1f)] private float sideImpactLossMultiplier = 0.15f;

        [Header("Runtime Debug (Read Only)")]
        [InspectorName("Input Provider Valid")]
        [SerializeField] private bool inputProviderValid;
        [InspectorName("Current Steering")]
        [SerializeField] private float debugSteering;
        [InspectorName("Current Throttle")]
        [SerializeField] private float debugThrottle;
        [InspectorName("Current Brake")]
        [SerializeField] private float debugBrake;
        [InspectorName("Rigidbody Velocity")]
        [SerializeField] private Vector3 debugRigidbodyVelocity;
        [InspectorName("Current Speed")]
        [SerializeField] private float debugSpeed;
        [InspectorName("Forward Speed")]
        [SerializeField] private float debugForwardSpeed;
        [InspectorName("Speed Normalized")]
        [SerializeField] private float debugSpeedNormalized;
        [InspectorName("Acceleration Factor")]
        [SerializeField] private float debugAccelerationFactor = 1f;
        [InspectorName("Steering Speed Penalty")]
        [SerializeField] private float debugSteeringSpeedPenalty;
        [InspectorName("Last Collision Impact")]
        [SerializeField] private float debugLastCollisionImpact;
        [InspectorName("Last Collision Speed Loss")]
        [SerializeField] private float debugLastCollisionSpeedLoss;
        [InspectorName("Last Contact Collider")]
        [SerializeField] private string debugLastContactCollider;
        [InspectorName("Last Contact Impulse")]
        [SerializeField] private float debugLastContactImpulse;
        [InspectorName("Last Contact Up Dot")]
        [SerializeField] private float debugLastContactUpDot;
        [InspectorName("Observed Speed Drop")]
        [SerializeField] private float debugObservedSpeedDrop;
        [InspectorName("Observed Vertical Delta")]
        [SerializeField] private float debugObservedVerticalDelta;

        private ICarInputProvider inputProvider;
        private float smoothedSteering;
        private float lastCollisionLossTime = float.NegativeInfinity;
        private readonly RaycastHit[] groundProbeHits = new RaycastHit[8];
        private Vector3 previousObservedVelocity;
        private bool hasPreviousObservedVelocity;
        private float previousSteering;
        private float previousThrottle;
        private float previousBrake;
        private string recentContactCollider = "None";
        private float recentContactImpulse;
        private float recentContactUpDot = 1f;
        private float nextAnomalyLogTime;
        private float boostUntil;
        private float oilSlipUntil;
        private float launchedUntil;
        private bool grounded;
        private Vector3 groundNormal=Vector3.up;
        private Vector3 driveForward=Vector3.forward;
        private float yawRate;
        private float stuntStart, stuntDuration;
        private Quaternion stuntHeading;
        private Vector3 stuntRight;
        private float airLateralSpeed;
        private float airLateralOffset;
        private float driftCharge;
        private float storedNitro;
        public float StoredNitro=>storedNitro;
        public bool NitroReady=>storedNitro>=.7f;
        private void Update()
        {
            if(inputProviderSource is KeyboardInputProvider&&UnityEngine.InputSystem.Keyboard.current!=null&&UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame)
                UseStoredNitro();
        }
        public void UseStoredNitro()
        {
            if(!controlEnabled||!grounded||!NitroReady)return;
            ApplySpeedBoost(storedNitro>=1.8f?2.2f:1.2f,storedNitro>=1.8f?12f:8f);
            storedNitro=0f;
        }
        public bool IsDrifting { get; private set; }
        public float DriftCharge=>driftCharge;
        public bool IsAirAction=>!grounded||stuntDuration>0f||Time.time<launchedUntil;
        public bool IsBoosting=>Time.time<boostUntil;
        private float SpeedLimit=>maxForwardSpeed+(IsBoosting?14f:grounded?Mathf.Clamp(-driveForward.y*22f,0f,8f):0f);
        public bool IsGrounded=>grounded;
        public float SteeringInput=>smoothedSteering;

        public float ForwardSpeed { get; private set; }
        public Rigidbody TargetBody => targetBody;
        public MonoBehaviour InputProviderSource => inputProviderSource;
        public bool ControlEnabled => controlEnabled;
        public float MaxForwardSpeed => maxForwardSpeed;

        public void ApplySpeedBoost(float duration=2f,float impulse=8f)
        {
            boostUntil=Mathf.Max(boostUntil,Time.time+duration);
            targetBody.AddForce(transform.forward*impulse,ForceMode.VelocityChange);
        }

        public void ApplyHomingHit()
        {
            stuntDuration=0f;
            launchedUntil=Time.time+1.2f;
            grounded=false;
            Vector3 impactVelocity=targetBody.linearVelocity*.55f-transform.forward*3f;
            impactVelocity.y=Mathf.Max(10f,impactVelocity.y);
            targetBody.linearVelocity=impactVelocity;
            targetBody.AddTorque(Vector3.up*7f,ForceMode.VelocityChange);
        }

        public void LaunchStunt(Vector3 landingPoint)
        {
            if(!grounded||!controlEnabled)return;
            Vector3 delta=landingPoint-targetBody.position;
            // Bounded launch height: never derive an arbitrarily large upward impulse from distance.
            float gravity=Mathf.Max(.1f,-Physics.gravity.y),upSpeed=4.5f;
            float flight=(upSpeed+Mathf.Sqrt(Mathf.Max(0f,upSpeed*upSpeed-2f*gravity*delta.y)))/gravity;
            flight=Mathf.Clamp(flight,.8f,3f);
            targetBody.useGravity=true;
            targetBody.linearVelocity=new Vector3(delta.x/flight,upSpeed,delta.z/flight);
            targetBody.angularVelocity=Vector3.zero;
            stuntHeading=Quaternion.LookRotation(Vector3.ProjectOnPlane(delta,Vector3.up).normalized);
            stuntRight=stuntHeading*Vector3.right;airLateralSpeed=0f;airLateralOffset=0f;
            stuntStart=Time.time;stuntDuration=flight;
            launchedUntil=Time.time+flight*.8f;grounded=false;
            boostUntil=Mathf.Max(boostUntil,Time.time+flight+.8f);
        }

        public void CancelAirAction()
        {
            stuntDuration=0f;launchedUntil=0f;boostUntil=0f;yawRate=0f;IsDrifting=false;driftCharge=0f;
        }

        public void ApplyOilSlip(float duration=2f)
        {
            oilSlipUntil=Mathf.Max(oilSlipUntil,Time.time+duration);
            targetBody.AddForce(transform.right*(UnityEngine.Random.value<.5f?-4f:4f),ForceMode.VelocityChange);
        }

        private void Reset()
        {
            targetBody = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            SeamlessRoadSurface.EnsureBuilt();

            if (targetBody == null)
            {
                targetBody = GetComponent<Rigidbody>();
            }

            ApplyRigidbodySetup();
            ResolveInputProvider();
            if(inputProviderSource is KeyboardInputProvider&&GetComponent<PoseKart.Input.VirtualWheelInput>()==null)gameObject.AddComponent<PoseKart.Input.VirtualWheelInput>();
            if(GetComponent<KartDriveEffects>()==null)gameObject.AddComponent<KartDriveEffects>();
            if(GetComponent<PoseKart.Race.KartItemInventory>()==null)gameObject.AddComponent<PoseKart.Race.KartItemInventory>();
        }

        private void FixedUpdate()
        {
            RaycastHit support=default;
            grounded=Time.time>=launchedUntil&&VehicleGroundProbe.Cast(targetBody,targetBody.worldCenterOfMass+Vector3.up*.3f,1.7f,out support);
            if(grounded)groundNormal=support.normal;else groundNormal=Vector3.up;
            if(stuntDuration>0f)
            {
                float progress=Mathf.Clamp01((Time.time-stuntStart)/(stuntDuration*.78f));
                targetBody.angularVelocity=Vector3.zero;
                targetBody.MoveRotation(stuntHeading*Quaternion.Euler(-360f*progress,0,0));
                if(grounded||Time.time-stuntStart>stuntDuration+1f)stuntDuration=0f;
            }
            driveForward=Vector3.ProjectOnPlane(transform.forward,groundNormal).normalized;
            // Editor-generated scenes can enter Play Mode without a full scene/domain reload.
            // Rehydrate the non-serialized interface cache from its serialized component reference.
            if (inputProvider == null && inputProviderSource != null)
            {
                inputProvider = inputProviderSource as ICarInputProvider;
            }

            Vector3 localVelocity = transform.InverseTransformDirection(targetBody.linearVelocity);
            ForwardSpeed = localVelocity.z;
            debugRigidbodyVelocity = targetBody.linearVelocity;
            debugSpeed = targetBody.linearVelocity.magnitude;
            debugForwardSpeed = ForwardSpeed;
            debugSpeedNormalized = Mathf.Clamp01(Mathf.Max(0f, ForwardSpeed) / maxForwardSpeed);
            inputProviderValid = inputProvider != null && inputProvider.IsInputValid;

            if (!inputProviderValid)
            {
                debugSteering = 0f;
                debugThrottle = 0f;
                debugBrake = 0f;
                RecordMotionSample(localVelocity, 0f, 0f, 0f);
                smoothedSteering = Mathf.MoveTowards(
                    smoothedSteering,
                    0f,
                    steeringResponse * Time.fixedDeltaTime);
                ApplyStability(localVelocity);
                LimitGroundContactImpulses();
                ClampPlanarSpeed();
                return;
            }

            float steering = controlEnabled
                ? Mathf.Clamp(inputProvider.Steering, -1f, 1f)
                : 0f;
            if(Time.time<oilSlipUntil)steering=Mathf.Clamp(steering+Mathf.Sin(Time.time*13f)*.75f,-1f,1f);
            float throttle = controlEnabled
                ? Mathf.Clamp01(inputProvider.Throttle)
                : 0f;
            float brake = controlEnabled || brakeAllowedWhenControlDisabled
                ? Mathf.Clamp01(inputProvider.Brake)
                : 0f;
            debugSteering = steering;
            debugThrottle = throttle;
            debugBrake = brake;
            RecordMotionSample(localVelocity, steering, throttle, brake);

            smoothedSteering = Mathf.MoveTowards(
                smoothedSteering,
                steering,
                7f * Time.fixedDeltaTime);

            AssistLowStep(support, throttle, brake);

            ApplyDrive(throttle, brake, ForwardSpeed, controlEnabled);
            bool driftHeld=inputProviderSource is IKartGestureInput gestures&&gestures.Drift;
            bool nextDrift=controlEnabled&&grounded&&ForwardSpeed>6f&&driftHeld&&Mathf.Abs(smoothedSteering)>.15f&&brake<=0f;
            IsDrifting=nextDrift;
            driftCharge=IsDrifting?Mathf.Min(2.5f,driftCharge+Time.fixedDeltaTime):0f;
            if(IsDrifting)storedNitro=Mathf.Min(2.5f,storedNitro+Time.fixedDeltaTime);
            ApplyCornerSpeedLoss(smoothedSteering, ForwardSpeed);
            ApplySteering(ForwardSpeed);
            ApplyStability(localVelocity);
            LimitGroundContactImpulses();
            ClampPlanarSpeed();
        }

        public void Configure(Rigidbody body, MonoBehaviour provider)
        {
            targetBody = body;
            SetInputProvider(provider);
            ApplyRigidbodySetup();
        }

        public void SetInputProvider(MonoBehaviour provider)
        {
            if (provider != null && !(provider is ICarInputProvider))
            {
                Debug.LogError($"{provider.GetType().Name} does not implement {nameof(ICarInputProvider)}.", this);
                return;
            }

            inputProviderSource = provider;
            inputProvider = provider as ICarInputProvider;
            enabled = inputProvider != null;
        }

        public void SetControlPermission(bool allowDriving, bool allowBrakingWhenDisabled = false)
        {
            controlEnabled = allowDriving;
            brakeAllowedWhenControlDisabled = !allowDriving && allowBrakingWhenDisabled;
        }

        private void ApplyDrive(float throttle, float brake, float forwardSpeed, bool allowReverse)
        {
            debugAccelerationFactor = 0f;
            if(!grounded)return;
            // Assist uphill travel, but retain gravity acceleration on downhill sections.
            if((throttle>0f&&driveForward.y>0f)||(brake>0f&&driveForward.y<0f))targetBody.AddForce(-Vector3.ProjectOnPlane(Physics.gravity,groundNormal)*.9f,ForceMode.Acceleration);
            if (brake > 0f)
            {
                if (forwardSpeed > brakeToReverseSpeed)
                {
                    targetBody.AddForce(-driveForward * (brake * brakeDeceleration), ForceMode.Acceleration);
                }
                else if (forwardSpeed < -brakeToReverseSpeed && !allowReverse)
                {
                    targetBody.AddForce(driveForward * (brake * brakeDeceleration), ForceMode.Acceleration);
                }
                else if (allowReverse && forwardSpeed > -maxReverseSpeed)
                {
                    targetBody.AddForce(-driveForward * (brake * reverseAcceleration), ForceMode.Acceleration);
                }

                return;
            }

            if (throttle > 0f && forwardSpeed < SpeedLimit)
            {
                float normalizedForwardSpeed = Mathf.Clamp01(Mathf.Max(0f, forwardSpeed) / SpeedLimit);
                float falloff = Mathf.Pow(normalizedForwardSpeed, accelerationFalloffPower);
                debugAccelerationFactor = Mathf.Lerp(1f, accelerationAtTopSpeed, falloff);
                targetBody.AddForce(driveForward*(throttle*acceleration*debugAccelerationFactor*(IsBoosting?1.7f:1f)),ForceMode.Acceleration);
            }
            else if (throttle<=0f&&Mathf.Abs(forwardSpeed) > 0.05f)
            {
                targetBody.AddForce(-transform.forward * (forwardSpeed * coastingResistance), ForceMode.Acceleration);
            }
        }

        // Small arcade tire-climb allowance, not a jump or an obstacle teleport.
        private void AssistLowStep(RaycastHit support, float throttle, float brake)
        {
            if(!grounded||!controlEnabled||stuntDuration>0f||Time.time<launchedUntil)return;
            float direction=brake>0f&&ForwardSpeed<.75f?-1f:throttle>0f?1f:0f;
            if(direction==0f)return;
            Vector3 travel=Vector3.ProjectOnPlane(transform.forward*direction,Vector3.up).normalized;
            // Sweep the chassis to detect the actual leading edge, including in reverse.
            float reach=Mathf.Clamp(Mathf.Abs(ForwardSpeed)*Time.fixedDeltaTime+.18f,.18f,.8f);
            if(!targetBody.SweepTest(travel,out RaycastHit obstacle,reach,QueryTriggerInteraction.Ignore))return;
            if(obstacle.collider.attachedRigidbody!=null||Mathf.Abs(obstacle.normal.y)>.45f)return;
            if(obstacle.collider.GetComponentInParent<TrackReturnBoundary>()!=null)return;
            Vector3 sample=obstacle.point+travel*.18f;
            sample.y=support.point.y+.5f;
            if(!VehicleGroundProbe.Cast(targetBody,sample,.55f,out RaycastHit top)||top.normal.y<.75f)return;
            float rise=top.point.y-support.point.y;
            if(rise<.025f||rise>.35f)return;
            // Require support beyond the lip; never climb a tall wall or launch ramp.
            if(!VehicleGroundProbe.Cast(targetBody,sample+travel*.25f,.55f,out RaycastHit beyond)
                ||Mathf.Abs(beyond.point.y-top.point.y)>.12f)return;
            float lift=rise+.025f;
            if(targetBody.SweepTest(Vector3.up,out _,lift,QueryTriggerInteraction.Ignore))return;
            targetBody.position+=Vector3.up*lift;
        }

        private void ApplyCornerSpeedLoss(float steering, float forwardSpeed)
        {
            if(!grounded)return;
            float steeringFactor = Mathf.InverseLerp(
                cornerPenaltySteeringThreshold,
                1f,
                Mathf.Abs(steering));
            float speedFactor = Mathf.InverseLerp(
                cornerPenaltyStartSpeed,
                maxForwardSpeed,
                Mathf.Abs(forwardSpeed));
            debugSteeringSpeedPenalty = Mathf.Min(maximumCornerDeceleration,2.5f)
                                        * Mathf.Pow(steeringFactor, cornerPenaltyPower)
                                        * speedFactor;

            if (debugSteeringSpeedPenalty <= 0f || Mathf.Abs(forwardSpeed) <= 0.05f)
            {
                return;
            }

            float travelDirection = Mathf.Sign(forwardSpeed);
            targetBody.AddForce(
                -transform.forward * (travelDirection * debugSteeringSpeedPenalty),
                ForceMode.Acceleration);
        }

        private void ApplySteering(float forwardSpeed)
        {
            if(!grounded)
            {
                yawRate=0f;
                if(stuntDuration>0f&&controlEnabled)
                {
                    float desired=smoothedSteering*7f;
                    float next=Mathf.MoveTowards(airLateralSpeed,desired,20f*Time.fixedDeltaTime);
                    next=Mathf.Clamp(next,(-4.5f-airLateralOffset)/Time.fixedDeltaTime,(4.5f-airLateralOffset)/Time.fixedDeltaTime);
                    targetBody.linearVelocity+=stuntRight*(next-airLateralSpeed);
                    airLateralOffset+=next*Time.fixedDeltaTime;
                    airLateralSpeed=next;
                }
                return;
            }
            float absoluteSpeed = Mathf.Abs(forwardSpeed);
            float normalizedSpeed = Mathf.Clamp01(absoluteSpeed / maxForwardSpeed);
            // Linear response: the old bicycle formula saturated even at small inputs.
            float desiredRate=smoothedSteering*(IsDrifting?112f:82f)*Mathf.Deg2Rad*Mathf.Clamp01(absoluteSpeed/4f)*Mathf.Sign(forwardSpeed);
            yawRate=Mathf.MoveTowards(yawRate,desiredRate,7f*Time.fixedDeltaTime);
            // Smooth terrain alignment only: smoothing the complete rotation also attenuates
            // every frame's steering increment, removing most of the requested turn rate.
            Vector3 up=Vector3.Slerp(transform.up,groundNormal,1f-Mathf.Exp(-14f*Time.fixedDeltaTime)).normalized;
            Vector3 heading=Vector3.ProjectOnPlane(transform.forward,up).normalized;
            heading=Quaternion.AngleAxis(yawRate*Mathf.Rad2Deg*Time.fixedDeltaTime,up)*heading;
            targetBody.MoveRotation(Quaternion.LookRotation(heading,up));
        }

        private void ApplyStability(Vector3 localVelocity)
        {
            if(!grounded)return;
            Vector3 right=Vector3.Cross(groundNormal,driveForward).normalized;
            float sideSpeed=Vector3.Dot(targetBody.linearVelocity,right);
            if(Time.time>=oilSlipUntil)
            {
                Vector3 planar=Vector3.ProjectOnPlane(targetBody.linearVelocity,groundNormal);
                float slipAngle=IsDrifting?-smoothedSteering*10f:0f;
                Vector3 travel=Quaternion.AngleAxis(slipAngle,groundNormal)*driveForward*Mathf.Sign(ForwardSpeed);
                Vector3 desired=travel*planar.magnitude;
                Vector3 blended=Vector3.Slerp(planar,desired,1f-Mathf.Exp(-(IsDrifting?14f:18f)*Time.fixedDeltaTime));
                targetBody.linearVelocity=blended+groundNormal*Vector3.Dot(targetBody.linearVelocity,groundNormal);
            }
            else if (Mathf.Abs(localVelocity.x) > 0.01f)
            {
                targetBody.AddForce(
                    -right * (sideSpeed * lateralGrip*.12f),
                    ForceMode.Acceleration);
            }

            float planarSpeed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
            float downforceAcceleration = baseDownforce + planarSpeed * speedDownforce;
            if (downforceAcceleration > 0f)
            {
                targetBody.AddForce(
                    -groundNormal * downforceAcceleration,
                    ForceMode.Acceleration);
            }
        }

        private void ClampPlanarSpeed()
        {
            if(!grounded)return;
            Vector3 velocity = targetBody.linearVelocity;
            Vector3 right=Vector3.Cross(groundNormal,driveForward).normalized;
            float forwardLimit=SpeedLimit;
            float forward=Mathf.Clamp(Vector3.Dot(velocity,driveForward),-maxReverseSpeed,forwardLimit);
            float lateralLimit=IsDrifting?18f:Mathf.Max(8f,maximumLateralSpeed);
            float lateral=Mathf.Clamp(Vector3.Dot(velocity,right),-lateralLimit,lateralLimit);
            targetBody.linearVelocity=driveForward*forward+right*lateral+groundNormal*Vector3.Dot(velocity,groundNormal);
        }

        private void LimitGroundContactImpulses()
        {
            if(Time.time<launchedUntil||!grounded)return;
            Vector3 velocity = targetBody.linearVelocity;
            float separatingSpeed=Vector3.Dot(velocity,groundNormal);
            if (separatingSpeed <= maximumGroundedUpwardSpeed)
            {
                return;
            }

            // Thin, overlapping road pieces can briefly generate a large separation impulse
            // at a seam. Preserve normal bumps, but prevent that contact impulse from launching
            // an arcade kart that is still directly above a support surface.
            velocity-=groundNormal*(separatingSpeed-maximumGroundedUpwardSpeed);
            targetBody.linearVelocity = velocity;
        }

        private bool IsNearSupportSurface()
        {
            int hitCount = Physics.RaycastNonAlloc(
                targetBody.worldCenterOfMass + Vector3.up * 0.05f,
                Vector3.down,
                groundProbeHits,
                groundProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = groundProbeHits[index];
                if (hit.collider == null || hit.collider.attachedRigidbody == targetBody)
                {
                    continue;
                }

                if (Vector3.Dot(hit.normal, Vector3.up) >= 0.6f)
                {
                    return true;
                }
            }

            return false;
        }

        private void RecordMotionSample(Vector3 localVelocity, float steering, float throttle, float brake)
        {
            Vector3 observedVelocity = targetBody.linearVelocity;
            if (hasPreviousObservedVelocity)
            {
                Vector3 previousPlanar = Vector3.ProjectOnPlane(previousObservedVelocity, Vector3.up);
                Vector3 currentPlanar = Vector3.ProjectOnPlane(observedVelocity, Vector3.up);
                debugObservedSpeedDrop = Mathf.Max(0f, previousPlanar.magnitude - currentPlanar.magnitude);
                debugObservedVerticalDelta = observedVelocity.y - previousObservedVelocity.y;
                debugLastContactCollider = recentContactCollider;
                debugLastContactImpulse = recentContactImpulse;
                debugLastContactUpDot = recentContactUpDot;

            }

            previousObservedVelocity = observedVelocity;
            hasPreviousObservedVelocity = true;
            previousSteering = steering;
            previousThrottle = throttle;
            previousBrake = brake;
            recentContactCollider = "None";
            recentContactImpulse = 0f;
            recentContactUpDot = 1f;
        }

        private void CaptureCollisionSnapshot(Collision collision)
        {
            float impulse = collision.impulse.magnitude;
            if (impulse < recentContactImpulse)
            {
                return;
            }

            float minimumUpDot = 1f;
            for (int index = 0; index < collision.contactCount; index++)
            {
                minimumUpDot = Mathf.Min(
                    minimumUpDot,
                    Vector3.Dot(collision.GetContact(index).normal, Vector3.up));
            }

            recentContactCollider = collision.collider != null ? collision.collider.name : "Unknown";
            recentContactImpulse = impulse;
            recentContactUpDot = minimumUpDot;
        }

        private void OnCollisionEnter(Collision collision)
        {
            CaptureCollisionSnapshot(collision);

            // Support contacts must never be interpreted as an arcade crash.  Road pieces
            // are identified by hierarchy name because the generated ML5 track has no tag
            // or layer dependency, while the normal test also protects other flat ground.
            if (IsDrivableSurface(collision.collider) || IsGroundContact(collision))
            {
                return;
            }

            Vector3 planarRelativeVelocity = Vector3.ProjectOnPlane(collision.relativeVelocity, transform.up);
            float planarImpactSpeed = planarRelativeVelocity.magnitude;
            if (planarImpactSpeed <= 0.01f)
            {
                return;
            }

            float frontalAlignment = Mathf.Abs(
                Vector3.Dot(planarRelativeVelocity / planarImpactSpeed, transform.forward));
            float impactWeight = Mathf.Lerp(
                sideImpactLossMultiplier,
                1f,
                frontalAlignment * frontalAlignment);
            float weightedImpactSpeed = planarImpactSpeed * impactWeight;
            debugLastCollisionImpact = weightedImpactSpeed;

            if (weightedImpactSpeed < minimumImpactSpeed ||
                Time.time < lastCollisionLossTime + collisionLossCooldown)
            {
                return;
            }

            float speedLoss = Mathf.Min(
                maximumCollisionSpeedLoss,
                (weightedImpactSpeed - minimumImpactSpeed) * collisionSpeedLossMultiplier);
            if (speedLoss <= 0f)
            {
                return;
            }

            Vector3 velocity = targetBody.linearVelocity;
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);
            localVelocity.z = Mathf.MoveTowards(localVelocity.z, 0f, speedLoss);
            localVelocity.x = Mathf.MoveTowards(localVelocity.x, 0f, speedLoss * 0.25f);
            Vector3 reducedPlanarVelocity = transform.TransformDirection(
                new Vector3(localVelocity.x, 0f, localVelocity.z));
            targetBody.linearVelocity = new Vector3(
                reducedPlanarVelocity.x,
                velocity.y,
                reducedPlanarVelocity.z);

            debugLastCollisionSpeedLoss = speedLoss;
            lastCollisionLossTime = Time.time;
        }

        private void OnCollisionStay(Collision collision)
        {
            CaptureCollisionSnapshot(collision);
        }

        private static bool IsDrivableSurface(Collider col)
        {
            if (col == null)
            {
                return false;
            }

            Transform current = col.transform;
            while (current != null)
            {
                string currentName = current.name;
                if (currentName.StartsWith("Road", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("Track", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("Ground", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("Sand", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("Ramp", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("SmallJumpRamp", StringComparison.OrdinalIgnoreCase) ||
                    currentName.StartsWith("BoostPad", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private bool IsGroundContact(Collision collision)
        {
            int contactCount = collision.contactCount;
            if (contactCount == 0)
            {
                return false;
            }

            for (int i = 0; i < contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                if (Vector3.Dot(contact.normal, transform.up) <= 0.6f)
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyRigidbodySetup()
        {
            if (targetBody == null)
            {
                return;
            }

            targetBody.centerOfMass = centerOfMassOffset;
            targetBody.linearDamping = linearDamping;
            ApplyLowFrictionColliderSetup();
            if (lockPitchAndRoll)
            {
                targetBody.constraints &= ~(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ);
            }
        }

        private void ApplyLowFrictionColliderSetup()
        {
            foreach (Collider vehicleCollider in GetComponentsInChildren<Collider>(true))
            {
                if (vehicleCollider.isTrigger)
                {
                    continue;
                }

                PhysicsMaterial material = vehicleCollider.material;
                material.staticFriction = 0f;
                material.dynamicFriction = 0f;
                material.frictionCombine = PhysicsMaterialCombine.Minimum;
            }
        }

        private void ResolveInputProvider()
        {
            inputProvider = inputProviderSource as ICarInputProvider;
            if (inputProvider == null)
            {
                Debug.LogError(
                    $"{nameof(CarController)} on {name} requires a component that implements {nameof(ICarInputProvider)}.",
                    this);
                enabled = false;
            }
        }

        private void OnValidate()
        {
            if (inputProviderSource != null && !(inputProviderSource is ICarInputProvider))
            {
                Debug.LogError(
                    $"Assigned input provider must implement {nameof(ICarInputProvider)}.",
                    this);
            }
        }
    }
}
