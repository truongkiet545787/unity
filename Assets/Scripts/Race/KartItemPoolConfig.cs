using UnityEngine;

namespace PoseKart.Race
{
    [DisallowMultipleComponent]
    public sealed class KartItemPoolConfig : MonoBehaviour
    {
        [Header("Random Gift Box Item Chances")]
        [SerializeField, Min(0f)] private float boostWeight = 1f;
        [SerializeField, Min(0f)] private float homingMissileWeight = 1f;
        [SerializeField, Min(0f)] private float bananaTrapWeight = 1f;
        [SerializeField, Min(0f)] private float shieldWeight = 1f;

        private static KartItemPoolConfig instance;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
        }

        public static KartItemPickup.ItemType RollItem()
        {
            KartItemPoolConfig config = instance != null
                ? instance
                : FindFirstObjectByType<KartItemPoolConfig>();
            if (config == null)
            {
                return (KartItemPickup.ItemType)Random.Range(0, 4);
            }

            float total = config.boostWeight + config.homingMissileWeight
                + config.bananaTrapWeight + config.shieldWeight;
            if (total <= 0.001f)
            {
                return (KartItemPickup.ItemType)Random.Range(0, 4);
            }

            float roll = Random.value * total;
            if ((roll -= config.boostWeight) < 0f) return KartItemPickup.ItemType.Boost;
            if ((roll -= config.homingMissileWeight) < 0f) return KartItemPickup.ItemType.HomingMissile;
            if ((roll -= config.bananaTrapWeight) < 0f) return KartItemPickup.ItemType.BananaTrap;
            return KartItemPickup.ItemType.Shield;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
