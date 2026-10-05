using System;
using System.Linq;
using UnityEngine;

namespace PoseKart.Vehicle
{
    public sealed class VehicleWheelAnimator : MonoBehaviour
    {
        [SerializeField] private Transform[] wheels;
        [SerializeField] private float wheelRadius=.42f;
        private Quaternion[] baseRotations;
        private Vector3[] centers,positions,axes;
        private float[] radii;
        private bool[] frontWheels;
        private Vector3[] steeringAxes;
        private CarController car;
        [SerializeField] private float maximumWheelSteerAngle=30f;
        private Rigidbody body;
        private float spinDegrees;
        private Vector3 lastPosition;
        private void Start()
        {
            wheels=GetComponentsInChildren<Transform>(true).Where(value=>value.name.IndexOf("wheel",StringComparison.OrdinalIgnoreCase)>=0&&value.GetComponent<Renderer>()!=null).ToArray();
            baseRotations=wheels.Select(value=>value.localRotation).ToArray();
            positions=wheels.Select(value=>value.localPosition).ToArray();
            centers=wheels.Select(value=>value.GetComponent<Renderer>().localBounds.center).ToArray();
            axes=wheels.Select(value=>value.InverseTransformDirection(transform.right).normalized).ToArray();
            radii=wheels.Select(value=>Mathf.Max(.1f,value.GetComponent<Renderer>().bounds.extents.y)).ToArray();
            car=GetComponent<CarController>();
            var longitudinal=wheels.Select(value=>transform.InverseTransformPoint(value.TransformPoint(value.GetComponent<Renderer>().localBounds.center)).z).ToArray();
            float axleMidpoint=longitudinal.Length>0?(longitudinal.Min()+longitudinal.Max())*.5f:0f;
            frontWheels=longitudinal.Select(z=>z>axleMidpoint+.05f).ToArray();
            steeringAxes=wheels.Select(value=>value.parent!=null?value.parent.InverseTransformDirection(transform.up).normalized:transform.up).ToArray();
            body=GetComponent<Rigidbody>();
            lastPosition=transform.position;
        }
        private void LateUpdate()
        {
            float distance=Vector3.Dot(transform.position-lastPosition,transform.forward);lastPosition=transform.position;
            if(Mathf.Abs(distance)>3f)return;
            spinDegrees+=distance/Mathf.Max(.05f,wheelRadius)*Mathf.Rad2Deg;
            for(int i=0;i<wheels.Length;i++)if(wheels[i]!=null)
            {
                // Use the same continuous, smoothed command as vehicle steering, not key state.
                float steerAngle=frontWheels[i]&&car!=null?car.SteeringInput*maximumWheelSteerAngle:0f;
                Quaternion rotation=Quaternion.AngleAxis(steerAngle,steeringAxes[i])*baseRotations[i]*Quaternion.AngleAxis(spinDegrees,axes[i]);
                Vector3 center=Vector3.Scale(centers[i],wheels[i].localScale);
                wheels[i].localRotation=rotation;
                wheels[i].localPosition=positions[i]+baseRotations[i]*center-rotation*center;
                // Small visual suspension travel keeps each tire on the surface on slopes.
                Vector3 worldCenter=wheels[i].TransformPoint(centers[i]);
                if(body!=null&&VehicleGroundProbe.Cast(body,worldCenter+Vector3.up*.5f,radii[i]+.85f,out RaycastHit hit))
                {
                    float offset=hit.point.y+radii[i]-worldCenter.y;
                    wheels[i].position+=Vector3.up*Mathf.Clamp(offset,-.25f,.25f);
                }
            }
        }
    }
}
