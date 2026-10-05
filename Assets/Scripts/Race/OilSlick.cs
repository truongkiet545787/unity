using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    public sealed class OilSlick : MonoBehaviour
    {
        private CarController owner;private float safeUntil;
        public void Configure(CarController source){owner=source;safeUntil=Time.time+.5f;}
        private void OnTriggerEnter(Collider other)
        {
            CarController car=other.attachedRigidbody!=null?other.attachedRigidbody.GetComponent<CarController>():null;
            if(car!=null&&car!=owner&&Time.time>=safeUntil){car.ApplyOilSlip();Destroy(gameObject);}
        }
    }
}
