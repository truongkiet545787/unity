using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseKart.Vehicle
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarResetController : MonoBehaviour
    {
        [SerializeField] private Rigidbody targetBody;
        [Header("Automatic Recovery")]
        [SerializeField] private bool autoResetWhenFallen = true;
        [Tooltip("A vehicle that is airborne, upside down, or off the valid road for this long is returned to its last safe road position.")]
        [SerializeField, Range(5f, 10f)] private float offTrackResetDelay = 6f;
        [Tooltip("Optional hard lower bound. It still uses the same recovery delay instead of teleporting immediately.")]
        [SerializeField] private float fallenHeight = -8f;
        [SerializeField] private float groundProbeDistance = 3f;

        [Header("Runtime Debug (Read Only)")]
        [InspectorName("Off Track Timer")]
        [SerializeField] private float debugOffTrackSeconds;
        [InspectorName("Last Safe Snapshot Age")]
        [SerializeField] private float debugLastSafeSnapshotAge;
        [InspectorName("Automatic Reset Armed")]
        [SerializeField] private bool debugAutomaticResetArmed;

        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private float nextSnapshotTime;
        private Snapshot lastSafe;
        private PoseKart.AI.RacingLine racingLine;
        private float offTrackSince = -1f;
        private struct Snapshot { public Vector3 position; public Quaternion rotation; public float time; }

        private void Reset()
        {
            targetBody = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            if (targetBody == null)
            {
                targetBody = GetComponent<Rigidbody>();
            }

            spawnPosition = targetBody.position;
            racingLine=FindFirstObjectByType<PoseKart.AI.RacingLine>();
            spawnRotation = targetBody.rotation;
            lastSafe=new Snapshot{position=spawnPosition,rotation=spawnRotation,time=Time.time};
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            var racer=GetComponent<PoseKart.Race.RaceParticipant>();
            bool resetPressed = (racer==null||racer.IsPlayer)&&keyboard != null && keyboard.rKey.wasPressedThisFrame;
            if (resetPressed)
            {
                ResetVehicle();
            }
        }

        private void FixedUpdate()
        {
            if(targetBody==null||Time.time<nextSnapshotTime)return;
            nextSnapshotTime=Time.time+.1f;
            CarController controller=GetComponent<CarController>();
            bool grounded = controller == null || controller.IsGrounded;
            bool upright = Vector3.Dot(transform.up,Vector3.up) >= .65f;
            RaycastHit hit = default;
            bool onTrack = grounded && upright && TryGetRoadHit(out hit);
            bool requiresRecovery = !onTrack || targetBody.position.y < fallenHeight;

            if (onTrack)
            {
                // Terrain beside the course must never replace the last on-track recovery point.
                if (targetBody.linearVelocity.sqrMagnitude >= 1f)
                {
                    var line=racingLine;
                    Quaternion heading=targetBody.rotation;
                    if(line!=null&&line.IsConfigured)
                    {
                        int next=line.FindStartingWaypoint(hit.point);
                        Vector3 forward=line.GetPosition(next)-line.GetPosition((next-1+line.Count)%line.Count);
                        Vector3 projectedForward=Vector3.ProjectOnPlane(forward,hit.normal);
                        if (projectedForward.sqrMagnitude > 0.001f)
                            heading=Quaternion.LookRotation(projectedForward.normalized,hit.normal);
                    }
                    lastSafe=new Snapshot{position=targetBody.position,rotation=heading,time=Time.time};
                }

                offTrackSince = -1f;
            }
            else if (autoResetWhenFallen && requiresRecovery)
            {
                if (offTrackSince < 0f)
                    offTrackSince = Time.time;

                float delay = Mathf.Clamp(offTrackResetDelay, 5f, 10f);
                if (Time.time - offTrackSince >= delay)
                    ResetVehicle();
            }

            debugOffTrackSeconds = offTrackSince < 0f ? 0f : Mathf.Max(0f, Time.time - offTrackSince);
            debugLastSafeSnapshotAge = Mathf.Max(0f, Time.time - lastSafe.time);
            debugAutomaticResetArmed = autoResetWhenFallen && offTrackSince >= 0f;
        }

        private bool TryGetRoadHit(out RaycastHit hit)
        {
            hit = default;
            if (!VehicleGroundProbe.Cast(
                    targetBody,
                    targetBody.worldCenterOfMass + Vector3.up * .25f,
                    Mathf.Max(0.5f, groundProbeDistance),
                    out hit))
            {
                return false;
            }

            string surface = hit.collider.name.ToLowerInvariant();
            bool validSurface = surface.Contains("road") ||
                surface.Contains("qms") ||
                surface == "continuous sand and dirt road" ||
                surface == "smoothroadphysics" ||
                surface.Contains("alley") ||
                surface.Contains("ridge cut") ||
                surface.Contains("sand trail");
            if (!validSurface)
                return false;

            // Require a margin on both sides so recovery does not leave a wheel off the edge.
            Quaternion heading = targetBody.rotation;
            if (racingLine != null && racingLine.IsConfigured)
            {
                int next = racingLine.FindStartingWaypoint(hit.point);
                Vector3 forward = racingLine.GetPosition(next) -
                    racingLine.GetPosition((next - 1 + racingLine.Count) % racingLine.Count);
                Vector3 projectedForward = Vector3.ProjectOnPlane(forward, hit.normal);
                if (projectedForward.sqrMagnitude > 0.001f)
                    heading = Quaternion.LookRotation(projectedForward.normalized, hit.normal);
            }

            if (!HasRoadMargin(hit, heading, -1.5f) || !HasRoadMargin(hit, heading, 1.5f))
                return false;

            return true;
        }

        private bool HasRoadMargin(RaycastHit hit, Quaternion heading, float side)
        {
            Vector3 sideOffset = heading * Vector3.right * side;
            if (!hit.collider.Raycast(
                    new Ray(hit.point + sideOffset + Vector3.up * 2f, Vector3.down),
                    out _,
                    4f))
            {
                return false;
            }

            return true;
        }

        public void Configure(Rigidbody body)
        {
            targetBody = body;
        }

        public void SetStartingSnapshot(Vector3 position, Quaternion rotation)
        {
            spawnPosition = position;
            spawnRotation = rotation;
            lastSafe = new Snapshot
            {
                position = position,
                rotation = rotation,
                time = Time.time
            };
            offTrackSince = -1f;
            debugOffTrackSeconds = 0f;
            debugAutomaticResetArmed = false;
            nextSnapshotTime = Time.time + 0.3f;
        }

        public void ResetVehicle()
        {
            GetComponent<CarController>()?.CancelAirAction();
            Snapshot rewind=lastSafe;
            offTrackSince = -1f;
            debugOffTrackSeconds = 0f;
            debugAutomaticResetArmed = false;
            targetBody.linearVelocity = Vector3.zero;
            targetBody.angularVelocity = Vector3.zero;
            targetBody.position = rewind.position+Vector3.up*.06f;
            targetBody.rotation = rewind.rotation;
            lastSafe=rewind;nextSnapshotTime=Time.time+.3f;
            targetBody.Sleep();
            targetBody.WakeUp();
        }
    }
}
