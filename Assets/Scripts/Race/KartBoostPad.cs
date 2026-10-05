using System.Collections.Generic;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    public sealed class KartBoostPad : MonoBehaviour
    {
        [SerializeField] private bool jump;
        [SerializeField] private Vector3 landingPoint;
        private readonly Dictionary<CarController,float> cooldown=new Dictionary<CarController,float>();
        public void Configure(bool launch,Vector3 destination){jump=launch;landingPoint=destination;}
        private void OnTriggerEnter(Collider other)
        {
            CarController car=other.attachedRigidbody!=null?other.attachedRigidbody.GetComponent<CarController>():null;
            if(car==null||!car.ControlEnabled||!car.IsGrounded)return;
            if(Vector3.Dot(car.TargetBody.linearVelocity,transform.forward)<2f)return;
            if(cooldown.TryGetValue(car,out float ready)&&Time.time<ready)return;
            cooldown[car]=Time.time+3f;
            car.ApplySpeedBoost(jump?2.5f:3f,12f);
            if(jump)car.LaunchStunt(landingPoint);
            KartParticleEffect.Burst(car.transform.position,new Color(.1f,.9f,1f,1f),24);
        }
    }
}
