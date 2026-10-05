using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Checkpoint : MonoBehaviour
    {
        [SerializeField, Min(0)] private int checkpointIndex;
        [SerializeField] private CheckpointManager checkpointManager;

        public int CheckpointIndex => checkpointIndex;

        public void Configure(int index, CheckpointManager manager)
        {
            checkpointIndex = Mathf.Max(0, index);
            checkpointManager = manager;
        }

        private void OnTriggerEnter(Collider other)
        {
            checkpointManager?.TryPassCheckpoint(this, other);
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
