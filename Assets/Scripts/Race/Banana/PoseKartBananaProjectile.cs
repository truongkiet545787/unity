using PoseKart.Audio;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Banana projectile launched in an arcade ballistic trajectory from the Banana Triple Launcher.
    /// In flight, it flies through the air with realistic gravity, vehicle velocity inheritance, and tumbling.
    /// On collision with a valid road surface, it spawns and configures the active KartBananaTrap.
    /// If it hits an invalid surface (steep wall, ceiling) or expires in the void, it despawns without spawning a trap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseKartBananaProjectile : MonoBehaviour
    {
        [Header("Ballistic Arc")]
        [Tooltip("Multiplies standard Physics.gravity for a snappy, responsive arcade trajectory.")]
        [SerializeField, Min(0.1f)] private float gravityMultiplier = 2.4f;
        [Tooltip("Maximum flight time in seconds before auto-despawning if no ground is hit.")]
        [SerializeField, Min(0.5f)] private float maxLifetime = 3.5f;

        [Header("Ground Detection")]
        [Tooltip("Layers recognized as valid driving surfaces for banana trap placement.")]
        [SerializeField] private LayerMask roadLayerMask = ~0;
        [Tooltip("Radius of the collision sphere cast along the flight path.")]
        [SerializeField, Min(0.05f)] private float projectileRadius = 0.28f;
        [Tooltip("Minimum surface normal Dot(up) required to count as road (0.45 ≈ up to 63° slope).")]
        [SerializeField, Range(0.1f, 0.9f)] private float minRoadNormalUpDot = 0.42f;

        [Header("Prefabs & Effects")]
        [Tooltip("Active ground trap prefab instantiated upon valid road contact.")]
        [SerializeField] private GameObject bananaTrapPrefab;
        [Tooltip("Optional visual landing particle/poof effect.")]
        [SerializeField] private GameObject landingVfxPrefab;

        [Header("Visual Tumble")]
        [Tooltip("Degrees per second rotation along local axes to simulate cartoon banana toss.")]
        [SerializeField] private Vector3 tumbleAngularVelocity = new Vector3(420f, 210f, 150f);

        private CarController owner;
        private Vector3 currentVelocity;
        private float trapLifetime = 12f;
        private float trapSlipDuration = 1.0f;
        private float trapOwnerImmunity = 0.65f;
        private float spawnTime;
        private bool hasLanded;
        private Collider[] ownerColliders;

        public void Initialize(
            CarController sourceOwner,
            Vector3 initialVelocity,
            GameObject trapPrefab,
            float lifetime,
            float slipDuration,
            float immunity,
            LayerMask groundLayers,
            GameObject landingVfx = null)
        {
            owner = sourceOwner;
            currentVelocity = initialVelocity;
            if (trapPrefab != null)
                bananaTrapPrefab = trapPrefab;
            trapLifetime = lifetime;
            trapSlipDuration = slipDuration;
            trapOwnerImmunity = immunity;
            roadLayerMask = groundLayers;
            if (landingVfx != null)
                landingVfxPrefab = landingVfx;

            spawnTime = Time.time;
            hasLanded = false;

            if (owner != null)
            {
                ownerColliders = owner.GetComponentsInChildren<Collider>(true);
            }

            EnsureVisualMesh();
        }

        private void Awake()
        {
            spawnTime = Time.time;
        }

        private void Update()
        {
            if (hasLanded) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Check max flight duration
            if (Time.time - spawnTime >= maxLifetime)
            {
                Destroy(gameObject);
                return;
            }

            // Apply gravity
            Vector3 gravityAcc = Physics.gravity * gravityMultiplier;
            currentVelocity += gravityAcc * dt;

            Vector3 step = currentVelocity * dt;
            float stepDistance = step.magnitude;

            if (stepDistance > 0.0001f)
            {
                Vector3 stepDir = step / stepDistance;

                // Sphere cast along flight direction to detect road / obstacles
                if (Physics.SphereCast(transform.position, projectileRadius, stepDir, out RaycastHit hit, stepDistance, roadLayerMask, QueryTriggerInteraction.Ignore))
                {
                    if (!IsOwnerCollider(hit.collider))
                    {
                        HandleImpact(hit);
                        return;
                    }
                }
            }

            // Advance position and tumble rotation
            transform.position += step;
            transform.Rotate(tumbleAngularVelocity * dt, Space.Self);
        }

        private bool IsOwnerCollider(Collider col)
        {
            if (col == null || owner == null) return false;
            if (ownerColliders != null)
            {
                for (int i = 0; i < ownerColliders.Length; i++)
                {
                    if (ownerColliders[i] == col) return true;
                }
            }
            return col.transform.IsChildOf(owner.transform);
        }

        private void HandleImpact(RaycastHit hit)
        {
            hasLanded = true;

            float upDot = Vector3.Dot(hit.normal, Vector3.up);
            if (upDot >= minRoadNormalUpDot)
            {
                // Valid road surface hit
                SpawnActiveTrap(hit.point, hit.normal);
            }
            else
            {
                // Invalid hit (cliff wall, barrier, ceiling) - despawn without placing a trap
                PlayLandingPoof(hit.point, hit.normal);
            }

            Destroy(gameObject);
        }

        private void SpawnActiveTrap(Vector3 point, Vector3 normal)
        {
            GameObject prefabToUse = bananaTrapPrefab != null
                ? bananaTrapPrefab
                : KartItemInventory.SharedBananaPrefab;

            Vector3 forwardOnSurface = Vector3.ProjectOnPlane(currentVelocity.sqrMagnitude > 0.1f ? currentVelocity.normalized : Vector3.forward, normal).normalized;
            if (forwardOnSurface.sqrMagnitude < 0.01f)
                forwardOnSurface = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;

            Quaternion surfaceRot = Quaternion.LookRotation(forwardOnSurface, normal);
            Quaternion prefabRot = prefabToUse != null ? prefabToUse.transform.rotation : Quaternion.identity;
            Quaternion spawnRot = surfaceRot * prefabRot;
            Vector3 spawnPos = point + normal * 0.035f;

            GameObject trapObj = prefabToUse != null
                ? Instantiate(prefabToUse, spawnPos, spawnRot)
                : KartBananaTrap.CreateFallbackVisual();

            if (trapObj != null)
            {
                trapObj.transform.SetPositionAndRotation(spawnPos, spawnRot);
                if (!trapObj.activeSelf)
                    trapObj.SetActive(true);

                KartBananaTrap trap = trapObj.GetComponent<KartBananaTrap>();
                if (trap == null)
                    trap = trapObj.AddComponent<KartBananaTrap>();

                trap.enabled = true;
                trap.Configure(owner, trapLifetime, trapSlipDuration, trapOwnerImmunity, point, normal);
            }

            AudioManager.PlayBananaDrop(point);
            PlayLandingPoof(point, normal);
        }

        private void PlayLandingPoof(Vector3 point, Vector3 normal)
        {
            if (landingVfxPrefab != null)
            {
                GameObject vfx = Instantiate(landingVfxPrefab, point + normal * 0.05f, Quaternion.LookRotation(normal));
                Destroy(vfx, 2.5f);
            }
        }

        private void EnsureVisualMesh()
        {
            // If already has renderer, keep it
            if (GetComponentInChildren<Renderer>() != null) return;

            // Otherwise, clone visual from bananaTrapPrefab
            GameObject prefabToCopy = bananaTrapPrefab != null ? bananaTrapPrefab : KartItemInventory.SharedBananaPrefab;
            if (prefabToCopy != null)
            {
                GameObject visualChild = Instantiate(prefabToCopy, transform);
                visualChild.name = "BananaVisual";
                visualChild.transform.localPosition = Vector3.zero;
                visualChild.transform.localRotation = prefabToCopy.transform.rotation;
                visualChild.transform.localScale = prefabToCopy.transform.localScale;

                // Disable any colliders or trap logic on the flying visual
                var comps = visualChild.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] != this)
                        comps[i].enabled = false;
                }
                var colliders = visualChild.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    colliders[i].enabled = false;
                }
            }
        }
    }
}
