
using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class MissileVictimSmokeSpawner : MonoBehaviour
    {
        [Header("Custom Smoke Prefab")]
        [Tooltip("Drag your own smoke VFX prefab here.")]
        [SerializeField] private GameObject smokePrefab;

        [Header("Attachment Settings")]
        [SerializeField]
        private Vector3 localOffset = new Vector3(0f, 0.4f, -0.6f);

        [SerializeField]
        private Vector3 localRotation;

        [Header("Duration")]
        [SerializeField, Min(0.1f)]
        private float smokeDuration = 1.8f;

        public void Spawn(Transform victim)
        {
            if (smokePrefab == null)
            {
                Debug.LogWarning(
                    "[PoseKart] Smoke Prefab has not been assigned.",
                    this
                );
                return;
            }

            if (victim == null)
                return;

            GameObject instance = Instantiate(smokePrefab, victim);

            instance.transform.localPosition = localOffset;
            instance.transform.localRotation =
                Quaternion.Euler(localRotation);

            MissileVictimSmoke controller =
                instance.GetComponent<MissileVictimSmoke>();

            if (controller == null)
                controller = instance.AddComponent<MissileVictimSmoke>();

            controller.AttachToVictim(victim, smokeDuration);
        }
    }
}
