using UnityEngine;

namespace PoseKart.Camera
{
    [DisallowMultipleComponent]
    public sealed class ChaseCameraController : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(0.1f)] private float distance = 7.5f;
        [SerializeField, Min(0f)] private float height = 4f;
        [SerializeField, Min(0f)] private float lookHeight = 1f;
        [SerializeField, Min(0f)] private float lookAhead = 2.5f;
        [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.18f;
        [SerializeField, Min(0.01f)] private float rotationSharpness = 10f;

        private Vector3 followVelocity;
        private Vector3 stableForward=Vector3.forward;
        private PoseKart.Vehicle.CarController car;
        private UnityEngine.Camera view;
        private float baseFov;

        private void Start()
        {
            view=GetComponent<UnityEngine.Camera>();if(view!=null)baseFov=view.fieldOfView;
            if(target!=null)SetTarget(target);
            if (target == null)
            {
                Debug.LogError($"{nameof(ChaseCameraController)} requires a follow target.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if(target==null)return;
            if(view!=null)view.fieldOfView=Mathf.Lerp(view.fieldOfView,baseFov+(car!=null&&car.IsBoosting?8f:0f),1f-Mathf.Exp(-5f*Time.deltaTime));
            Vector3 flatForward = GetFlatForward();
            Vector3 desiredPosition = target.position - flatForward * distance + Vector3.up * height;
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref followVelocity,
                positionSmoothTime);

            Vector3 lookPosition = target.position + Vector3.up * lookHeight + flatForward * lookAhead;
            Vector3 lookDirection = lookPosition - transform.position;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            float rotationBlend = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationBlend);
        }

        public void SetTarget(Transform followTarget)
        {
            target = followTarget;
            car=target!=null?target.GetComponent<PoseKart.Vehicle.CarController>():null;
            if(target!=null){Vector3 heading=Vector3.ProjectOnPlane(target.forward,Vector3.up);if(heading.sqrMagnitude>.01f)stableForward=heading.normalized;}
            enabled = target != null;
        }

        public void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            followVelocity = Vector3.zero;
            Vector3 flatForward = GetFlatForward();
            transform.position = target.position - flatForward * distance + Vector3.up * height;
            transform.LookAt(target.position + Vector3.up * lookHeight + flatForward * lookAhead);
        }

        private Vector3 GetFlatForward()
        {
            if(car==null)car=target.GetComponent<PoseKart.Vehicle.CarController>();
            // Keep the takeoff heading throughout flips and missile knock-ups.
            if(car!=null&&car.IsAirAction)return stableForward;
            Vector3 flatForward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if(flatForward.sqrMagnitude>.01f)stableForward=Vector3.Slerp(stableForward,flatForward.normalized,1f-Mathf.Exp(-5f*Time.deltaTime)).normalized;
            return stableForward;
        }
        private void OnDisable(){if(view!=null&&baseFov>0f)view.fieldOfView=baseFov;}
    }
}
