using UnityEngine;

namespace PoseKart.Vehicle
{
    internal static class VehicleGroundProbe
    {
        private static readonly RaycastHit[] Hits=new RaycastHit[32];
        // Ignore the entire vehicle, triggers and other racers, even when a ray starts inside the chassis.
        public static bool Cast(Rigidbody body, Vector3 origin, float distance, out RaycastHit support)
        {
            support=default;
            float nearest=float.PositiveInfinity;
            int count=Physics.RaycastNonAlloc(origin,Vector3.down,Hits,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                RaycastHit hit=Hits[i];
                if(hit.collider.attachedRigidbody!=null||hit.collider.transform.IsChildOf(body.transform)||hit.normal.y<.5f)continue;
                if(hit.distance<nearest){nearest=hit.distance;support=hit;}
            }
            return nearest<float.PositiveInfinity;
        }
    }
}
