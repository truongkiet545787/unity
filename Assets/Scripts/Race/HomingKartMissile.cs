using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    public sealed class HomingKartMissile : MonoBehaviour
    {
        private CarController owner,target;
        private float born;
        private bool spent;
        private ParticleSystem exhaust;
        public void Configure(CarController source,CarController destination)
        {
            owner=source;target=destination;born=Time.time;
            exhaust=KartParticleEffect.Create("Missile flame",transform,new Color(1f,.4f,.08f,1f),.35f,.3f);
        }
        private void FixedUpdate()
        {
            if(spent)return;
            if(target==null||Time.time-born>6f){Destroy(gameObject);return;}
            Vector3 destination=target.TargetBody.worldCenterOfMass;
            Vector3 from=transform.position,delta=destination-from;
            float travel=Mathf.Min(delta.magnitude,Mathf.Max(62f,target.TargetBody.linearVelocity.magnitude+22f)*Time.fixedDeltaTime);
            // Sweep the complete movement segment, including at low frame rates.
            RaycastHit[] hits=Physics.SphereCastAll(from,.4f,delta.normalized,travel,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(RaycastHit hit in hits)
            {
                CarController car=hit.collider.attachedRigidbody!=null?hit.collider.attachedRigidbody.GetComponent<CarController>():null;
                if(car==owner)continue;
                Detonate(car,hit.point);return;
            }
            transform.position=from+delta.normalized*travel;
            if(delta.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(delta);
            exhaust.Emit(new ParticleSystem.EmitParams{position=transform.position,velocity=-transform.forward*5f},3);
            if(delta.magnitude<=travel+.4f)Detonate(target,destination);
        }
        private void OnTriggerEnter(Collider other)
        {
            CarController car=other.attachedRigidbody!=null?other.attachedRigidbody.GetComponent<CarController>():null;
            if(car!=null&&car!=owner)Detonate(car,transform.position);
        }
        private void Detonate(CarController car,Vector3 point)
        {
            if(spent)return;spent=true;
            if(car!=null)car.ApplyHomingHit();
            KartParticleEffect.Burst(point,new Color(1f,.35f,.05f,1f),52);
            Destroy(gameObject);
        }
    }
}
