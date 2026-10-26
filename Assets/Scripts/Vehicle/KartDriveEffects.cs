using UnityEngine;

namespace PoseKart.Vehicle
{
    public sealed class KartDriveEffects : MonoBehaviour
    {
        private CarController car;
        private ParticleSystem dust,nitro;
        private float nextDust,nextFlame;
        private bool wasGrounded;
        private void Start()
        {
            car=GetComponent<CarController>();
            if(GetComponent<NitroJetEffect>()==null)gameObject.AddComponent<NitroJetEffect>();
            dust=KartParticleEffect.Create("Tire dust",transform,new Color(.69f,.52f,.29f,.6f),.65f,.35f);
            nitro=KartParticleEffect.Create("Nitro exhaust",transform,new Color(.12f,.8f,1f,1f),.45f,.65f);
        }
        private void Update()
        {
            if(car==null||car.TargetBody==null)return;
            float speed=car.TargetBody.linearVelocity.magnitude;
            bool drifting=car.IsDrifting;
            if(car.IsGrounded&&speed>3f&&Time.time>=nextDust)
            {
                nextDust=Time.time+.08f;
                if (!drifting)
                {
                    for(int side=-1;side<=1;side+=2)
                    {
                        Vector3 point=transform.position-transform.forward*.8f+transform.right*(side*.65f);
                        if(VehicleGroundProbe.Cast(car.TargetBody,point+Vector3.up,3f,out RaycastHit hit))
                        {
                            var emission=new ParticleSystem.EmitParams{position=hit.point+hit.normal*.07f,velocity=-transform.forward*2f+Vector3.up*1.2f,startSize=.3f};
                            dust.Emit(emission,1);
                        }
                    }
                }
            }
            if (car.IsVisualNitroBoosting && Time.time >= nextFlame)
            {
                nextFlame = Time.time + .018f;
                var emission = new ParticleSystem.EmitParams { position = transform.position - transform.forward * 1.25f + transform.up * .35f, velocity = -transform.forward * 14f + car.TargetBody.linearVelocity * .15f };
                nitro.Emit(emission, 3);
            }
            if(car.IsGrounded&&!wasGrounded&&speed>8f)KartParticleEffect.Burst(transform.position,new Color(.75f,.62f,.38f,.7f),18);
            wasGrounded=car.IsGrounded;
        }
    }
}
