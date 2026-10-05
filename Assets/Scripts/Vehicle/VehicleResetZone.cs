using UnityEngine;

namespace PoseKart.Vehicle
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VehicleResetZone : MonoBehaviour
    {
        [SerializeField] private Rigidbody playerBody;
        [SerializeField] private CarResetController resetController;

        public bool IsConfigured => playerBody != null && resetController != null;

        public void Configure(Rigidbody body, CarResetController controller)
        {
            playerBody = body;
            resetController = controller;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsConfigured && other.attachedRigidbody == playerBody)
            {
                resetController.ResetVehicle();
            }
        }

        private void OnValidate()
        {
            BoxCollider trigger = GetComponent<BoxCollider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }
    }
}
