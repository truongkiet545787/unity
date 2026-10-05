using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseKart.Vehicle
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarResetController : MonoBehaviour
    {
        [SerializeField] private Rigidbody targetBody;
        [SerializeField] private bool autoResetWhenFallen = true;
        [SerializeField] private float fallenHeight = -8f;

        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private float nextSnapshotTime;
        private Snapshot lastSafe;
        private PoseKart.AI.RacingLine racingLine;
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
            if(controller!=null&&!controller.IsGrounded)return;
            if(targetBody.linearVelocity.sqrMagnitude<1f||Vector3.Dot(transform.up,Vector3.up)<.65f)return;
            if(VehicleGroundProbe.Cast(targetBody,targetBody.worldCenterOfMass+Vector3.up*.25f,3f,out RaycastHit hit))
            {
                // Terrain beside the course must never replace the last on-track recovery point.
                string surface=hit.collider.name;
                if(surface!="Continuous sand and dirt road"&&!surface.Contains("alley")&&!surface.Contains("ridge cut")&&!surface.Contains("sand trail"))return;
                var line=racingLine;
                Quaternion heading=targetBody.rotation;
                if(line!=null&&line.IsConfigured)
                {
                    int next=line.FindStartingWaypoint(hit.point);
                    Vector3 forward=line.GetPosition(next)-line.GetPosition((next-1+line.Count)%line.Count);
                    heading=Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,hit.normal).normalized,hit.normal);
                }
                // Require a margin on both sides so recovering does not leave a wheel off the edge.
                foreach(float side in new[]{-1.5f,1.5f})
                    if(!hit.collider.Raycast(new Ray(hit.point+heading*Vector3.right*side+Vector3.up*2f,Vector3.down),out _,4f))return;
                lastSafe=new Snapshot{position=targetBody.position,rotation=heading,time=Time.time};
            }
        }

        public void Configure(Rigidbody body)
        {
            targetBody = body;
        }

        public void ResetVehicle()
        {
            GetComponent<CarController>()?.CancelAirAction();
            Snapshot rewind=lastSafe;
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
