using System.Collections;
using System.Collections.Generic;
using PoseKart.Audio;
using PoseKart.UI;
using PoseKart.Vehicle;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Upgraded Missile Hit, Wreck, Physical Debris, and Checkpoint Respawn Controller.
    /// Handles single-hit processing, control disabling, physics toss/flip with strict caps,
    /// physical breakaway debris from pre-separated vehicle parts, and track-aligned respawn.
    /// Works seamlessly across Offline single-player and Netcode for GameObjects multiplayer.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CarController))]
    public sealed class PoseKartWreckController : MonoBehaviour
    {
        [Header("Wreck & Debris Assets")]
        [Tooltip("Optional dedicated wreck/debris prefab. If unassigned, the active vehicle's pre-separated parts (wheels, body, suspension) are dynamically detached and scattered.")]
        [SerializeField] private GameObject wreckPrefab;
        [Tooltip("Optional explosion VFX prefab. If unassigned, falls back to the missile impact explosion or procedural particle burst.")]
        [SerializeField] private GameObject explosionVfxPrefab;
        [Tooltip("Optional explosion audio clip. If unassigned, uses AudioManager.PlayMissileImpact.")]
        [SerializeField] private AudioClip explosionAudio;

        [Header("Intact Visual Root")]
        [Tooltip("Root GameObject containing the intact vehicle mesh. If unassigned, auto-resolved from PoseKartVehiclePaint or children.")]
        [SerializeField] private GameObject intactVisualRoot;

        [Header("Explosion Physics Impulse & Limits")]
        [Tooltip("Upward impulse applied to the kart Rigidbody.")]
        [SerializeField, Min(0f)] private float throwUpwardForce = 5.5f;
        [Tooltip("Horizontal impulse applied to the kart Rigidbody away from blast.")]
        [SerializeField, Min(0f)] private float throwHorizontalForce = 9.0f;
        [Tooltip("Torque impulse causing pitch and roll flips.")]
        [SerializeField, Min(0f)] private float flipTorqueForce = 20f;
        [Tooltip("Hard ceiling for kart linear velocity after impact.")]
        [SerializeField, Min(1f)] private float maxLinearVelocity = 18f;
        [Tooltip("Hard ceiling for kart angular velocity after impact.")]
        [SerializeField, Min(0.1f)] private float maxAngularVelocity = 12f;

        [Header("Debris Configuration")]
        [Tooltip("Maximum number of debris pieces spawned to maintain high online performance.")]
        [SerializeField, Range(1, 12)] private int maxDebrisPieces = 6;
        [Tooltip("Seconds before spawned debris pieces are destroyed.")]
        [SerializeField, Min(1f)] private float debrisLifetime = 3.5f;
        [Tooltip("Shorter visual debris duration for a Shockwave contact. This does not change kart physics or respawn.")]
        [SerializeField, Min(0.25f)] private float shockwaveDebrisLifetime = 1.35f;
        [Tooltip("Explosion force applied to debris pieces.")]
        [SerializeField, Min(1f)] private float debrisExplosionForce = 15f;
        [Tooltip("Upward modifier for debris explosion force.")]
        [SerializeField, Min(0f)] private float debrisUpwardModifier = 1.2f;

        [Header("Respawn Configuration")]
        [Tooltip("Delay in seconds from missile impact until checkpoint respawn.")]
        [SerializeField, Min(0.5f)] private float respawnDelay = 2.2f;
        [Tooltip("Optional VFX played around the kart while it is recovering after respawn.")]
        [SerializeField] private GameObject recoveryVfxPrefab;
        [Tooltip("One-second recovery window after respawning. During this time the kart cannot be affected and other karts pass through it.")]
        [SerializeField, Min(0f)] private float postRespawnInvulnerability = 1f;

        private CarController carController;
        private Rigidbody kartBody;
        private PoseKartVehiclePaint vehiclePaint;
        private LapTracker lapTracker;
        private CarResetController resetController;

        private bool isWrecked;
        private bool recoveryProtectionActive;
        private float invulnerableUntil = float.NegativeInfinity;
        private Coroutine respawnRoutine;
        private Coroutine recoveryRoutine;
        private Coroutine shockwaveVisualRoutine;
        private GameObject recoveryVfxInstance;
        private readonly List<GameObject> activeDebrisList = new List<GameObject>();

        private sealed class IgnoredCollisionPair
        {
            public Collider first;
            public Collider second;
        }

        private readonly List<IgnoredCollisionPair> ignoredCollisionPairs =
            new List<IgnoredCollisionPair>();

        public bool IsWrecked => isWrecked;
        public float RespawnDelay => respawnDelay;
        public bool IsInvulnerable => Time.time < invulnerableUntil;
        public bool IsGameplayProtected => recoveryProtectionActive;
        public float ShockwaveDebrisLifetime => shockwaveDebrisLifetime;

        private void Awake()
        {
            carController = GetComponent<CarController>();
            kartBody = carController != null && carController.TargetBody != null
                ? carController.TargetBody
                : GetComponent<Rigidbody>();
            vehiclePaint = GetComponent<PoseKartVehiclePaint>();
            if (vehiclePaint == null)
                vehiclePaint = GetComponentInChildren<PoseKartVehiclePaint>(true);
            lapTracker = GetComponent<LapTracker>();
            resetController = GetComponent<CarResetController>();
        }

        private void OnDestroy()
        {
            StopRecoveryProtection();
            CleanupActiveDebris();
        }

        /// <summary>
        /// Offline / Single-player entry point.
        /// </summary>
        public void TriggerMissileWreck(Vector3 blastPoint, Vector3 deltaVelocity, float torqueStrength)
        {
            if (IsGameplayProtected || IsInvulnerable || respawnRoutine != null)
                return;

            // 1. Play visual and debris locally
            ExecuteWreckVisualAndDebris(blastPoint, respawnDelay);

            // 2. Execute local physics toss and schedule respawn
            ExecuteOwnerWreckPhysics(blastPoint, deltaVelocity, torqueStrength, respawnDelay);
        }

        /// <summary>
        /// Replicated visual and debris presentation (runs on all clients and host).
        /// </summary>
        public void ExecuteWreckVisualAndDebris(Vector3 blastPoint, float delay)
        {
            if (shockwaveVisualRoutine != null)
            {
                StopCoroutine(shockwaveVisualRoutine);
                shockwaveVisualRoutine = null;
            }

            isWrecked = true;
            recoveryProtectionActive = true;
            ApplyVehicleCollisionImmunity();

            // 1. Audio and VFX (wrapped to keep gameplay strictly independent)
            PlayExplosionEffects(blastPoint);

            // 2. Screen shake for local player
            TriggerLocalCameraShake();

            // 3. Copy the intact mesh parts before hiding them. Otherwise the
            // dynamic debris path would see disabled renderers and fall back
            // to generic cubes even when the kart has separated visual parts.
            SpawnWreckAndDebris(blastPoint);

            // 4. Switch intact visual to wreck / debris setup
            SetIntactVisualVisible(false);

            // 5. Spawn burning smoke on victim chassis
            MissileVictimSmokeSpawner.SpawnForVictim(transform, delay + 1.2f);
        }

        /// <summary>
        /// Shockwave-only visual breakaway. It deliberately does not change
        /// Rigidbody, controls, Nitro, NetworkTransform or respawn state.
        /// The existing authoritative Shockwave collision remains in charge of
        /// gameplay physics; this method is presentation only.
        /// </summary>
        public void TriggerShockwaveDebrisVisual(
            Vector3 blastPoint,
            float lifetime)
        {
            if (isWrecked || IsInvulnerable)
                return;

            isWrecked = true;
            SpawnWreckAndDebris(blastPoint);
            SetIntactVisualVisible(false);

            if (shockwaveVisualRoutine != null)
                StopCoroutine(shockwaveVisualRoutine);

            shockwaveVisualRoutine = StartCoroutine(
                RestoreShockwaveVisualAfter(
                    Mathf.Max(0.25f, lifetime)));
        }

        private IEnumerator RestoreShockwaveVisualAfter(float lifetime)
        {
            yield return new WaitForSeconds(lifetime);

            CleanupActiveDebris();
            SetIntactVisualVisible(true);
            isWrecked = false;
            shockwaveVisualRoutine = null;
        }

        /// <summary>
        /// Physics authority execution (runs on Owner for human network player, or Server for AI/Host).
        /// </summary>
        public void ExecuteOwnerWreckPhysics(Vector3 blastPoint, Vector3 deltaVelocity, float torqueStrength, float delay)
        {
            isWrecked = true;
            recoveryProtectionActive = true;
            ApplyVehicleCollisionImmunity();

            // The missile hit is authoritative. Clear the held item on the
            // authority copy as well as on the local owner copy below.
            GetComponent<KartItemInventory>()?.ClearHeldItem();

            // 1. Briefly disable controls and cancel ongoing nitro / air actions
            if (carController != null)
            {
                carController.SetControlPermission(false);
                carController.StopNitro();
                carController.CancelAirAction();
            }

            // 2. Apply natural capped impulse and torque to throw/flip the kart
            ApplyExplosionForces(blastPoint, deltaVelocity, torqueStrength);

            // 3. Schedule checkpoint respawn after delay
            if (respawnRoutine != null)
                StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(RespawnRoutine(delay));
        }

        private void ApplyExplosionForces(Vector3 blastPoint, Vector3 deltaVelocity, float torqueStrength)
        {
            if (kartBody == null)
                kartBody = carController != null && carController.TargetBody != null ? carController.TargetBody : GetComponent<Rigidbody>();

            if (kartBody == null || kartBody.isKinematic)
                return;

            Vector3 blastVector = kartBody.worldCenterOfMass - blastPoint;
            Vector3 horizontalDir = Vector3.ProjectOnPlane(blastVector, Vector3.up);
            if (horizontalDir.sqrMagnitude < 0.001f)
            {
                horizontalDir = carController != null ? -carController.transform.forward : -transform.forward;
                horizontalDir = Vector3.ProjectOnPlane(horizontalDir, Vector3.up);
            }
            if (horizontalDir.sqrMagnitude < 0.001f)
                horizontalDir = Vector3.right;
            horizontalDir.Normalize();

            // Calculate impulse with balanced vertical lift and horizontal ejection
            float horizontalForce = Mathf.Max(throwHorizontalForce, deltaVelocity.magnitude * 0.5f);
            float upwardForce = Mathf.Max(throwUpwardForce, deltaVelocity.y * 0.7f);
            Vector3 impulse = (horizontalDir * horizontalForce + Vector3.up * upwardForce) * Mathf.Max(0.01f, kartBody.mass);

            kartBody.AddForce(impulse, ForceMode.Impulse);

            // Calculate flip axis perpendicular to blast direction
            Vector3 flipAxis = Vector3.Cross(Vector3.up, horizontalDir).normalized;
            if (flipAxis.sqrMagnitude < 0.001f)
                flipAxis = transform.right;

            float appliedTorque = Mathf.Max(flipTorqueForce, torqueStrength * 1.5f);
            kartBody.AddTorque(flipAxis * appliedTorque * Mathf.Max(0.01f, kartBody.mass) * 0.2f, ForceMode.Impulse);

            // Enforce strict sensible limits so the kart does not fly too far or spin wildly
            kartBody.linearVelocity = Vector3.ClampMagnitude(kartBody.linearVelocity, maxLinearVelocity);
            kartBody.angularVelocity = Vector3.ClampMagnitude(kartBody.angularVelocity, maxAngularVelocity);
        }

        private IEnumerator RespawnRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);

            RespawnAtLatestCheckpoint();
        }

        /// <summary>
        /// Respawns the kart at the latest valid checkpoint, resetting velocity and control.
        /// </summary>
        public void RespawnAtLatestCheckpoint()
        {
            Vector3 respawnPos;
            Quaternion respawnRot;

            if (!TryGetRespawnTransform(out respawnPos, out respawnRot))
            {
                respawnPos = transform.position + Vector3.up * 0.5f;
                respawnRot = transform.rotation;
            }

            // 1. Reset Rigidbody velocity and position
            if (kartBody == null)
                kartBody = carController != null && carController.TargetBody != null ? carController.TargetBody : GetComponent<Rigidbody>();

            if (kartBody != null)
            {
                kartBody.linearVelocity = Vector3.zero;
                kartBody.angularVelocity = Vector3.zero;
                kartBody.position = respawnPos;
                kartBody.rotation = respawnRot;
                kartBody.Sleep();
                kartBody.WakeUp();
            }

            transform.position = respawnPos;
            transform.rotation = respawnRot;

            // 2. Netcode NetworkTransform Teleport if networked
            NetworkTransform netTransform = GetComponent<NetworkTransform>();
            if (netTransform != null && netTransform.IsSpawned)
            {
                netTransform.Teleport(respawnPos, respawnRot, transform.localScale);
            }

            // 3. Restore visual and remove any remaining local debris.
            CleanupActiveDebris();
            SetIntactVisualVisible(true);

            // 4. Restore controls, cancel stunt/stun states
            if (carController != null)
            {
                carController.CancelAirAction();
                carController.SetControlPermission(true);
            }

            // 5. If networked, notify peers to restore intact visual
            PoseKartNetworkItemBridge bridge = GetComponent<PoseKartNetworkItemBridge>();
            if (bridge != null && bridge.IsSpawned)
            {
                if (bridge.IsServer)
                {
                    bridge.ServerBroadcastWreckRestored();
                }
                else if (bridge.IsOwner)
                {
                    bridge.OwnerReportWreckRestoredServerRpc();
                }
            }

            isWrecked = false;
            BeginRecoveryProtection();
            respawnRoutine = null;
        }

        /// <summary>
        /// Restores visual on remote peers when notified across network.
        /// </summary>
        public void RestoreIntactVisualOnly()
        {
            bool alreadyRecovering = recoveryProtectionActive && !isWrecked;

            if (shockwaveVisualRoutine != null)
            {
                StopCoroutine(shockwaveVisualRoutine);
                shockwaveVisualRoutine = null;
            }

            CleanupActiveDebris();
            SetIntactVisualVisible(true);
            isWrecked = false;
            if (!alreadyRecovering)
                BeginRecoveryProtection();
        }

        private void BeginRecoveryProtection()
        {
            if (recoveryRoutine != null)
                StopCoroutine(recoveryRoutine);

            recoveryProtectionActive = true;
            invulnerableUntil = Time.time + Mathf.Max(0f, postRespawnInvulnerability);
            ApplyVehicleCollisionImmunity();
            SpawnRecoveryVfx();

            if (postRespawnInvulnerability > 0f)
            {
                recoveryRoutine = StartCoroutine(
                    RecoveryProtectionRoutine(postRespawnInvulnerability));
            }
            else
            {
                recoveryProtectionActive = false;
                RestoreVehicleCollisions();
            }
        }

        private IEnumerator RecoveryProtectionRoutine(float duration)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, duration));
            recoveryProtectionActive = false;
            invulnerableUntil = float.NegativeInfinity;
            RestoreVehicleCollisions();

            if (recoveryVfxInstance != null)
                Destroy(recoveryVfxInstance);
            recoveryVfxInstance = null;
            recoveryRoutine = null;
        }

        private void ApplyVehicleCollisionImmunity()
        {
            RestoreVehicleCollisions();

            Collider[] ownColliders = GetComponentsInChildren<Collider>(true);
            if (ownColliders.Length == 0)
                return;

            CarController[] allKarts = FindObjectsByType<CarController>(FindObjectsSortMode.None);
            foreach (CarController otherKart in allKarts)
            {
                if (otherKart == null || otherKart == carController)
                    continue;

                Collider[] otherColliders = otherKart.GetComponentsInChildren<Collider>(true);
                foreach (Collider ownCollider in ownColliders)
                {
                    if (ownCollider == null)
                        continue;
                    foreach (Collider otherCollider in otherColliders)
                    {
                        if (otherCollider == null || ownCollider == otherCollider)
                            continue;

                        try
                        {
                            Physics.IgnoreCollision(ownCollider, otherCollider, true);
                            ignoredCollisionPairs.Add(new IgnoredCollisionPair
                            {
                                first = ownCollider,
                                second = otherCollider
                            });
                        }
                        catch (System.Exception)
                        {
                            // A collider can be destroyed during a despawn.
                        }
                    }
                }
            }
        }

        private void RestoreVehicleCollisions()
        {
            for (int i = ignoredCollisionPairs.Count - 1; i >= 0; i--)
            {
                IgnoredCollisionPair pair = ignoredCollisionPairs[i];
                if (pair != null && pair.first != null && pair.second != null)
                {
                    try
                    {
                        Physics.IgnoreCollision(pair.first, pair.second, false);
                    }
                    catch (System.Exception)
                    {
                        // Despawned colliders no longer need restoring.
                    }
                }
            }
            ignoredCollisionPairs.Clear();
        }

        private void StopRecoveryProtection()
        {
            if (recoveryRoutine != null)
            {
                StopCoroutine(recoveryRoutine);
                recoveryRoutine = null;
            }

            recoveryProtectionActive = false;
            invulnerableUntil = float.NegativeInfinity;
            RestoreVehicleCollisions();

            if (recoveryVfxInstance != null)
                Destroy(recoveryVfxInstance);
            recoveryVfxInstance = null;
        }

        private void SpawnRecoveryVfx()
        {
            if (recoveryVfxPrefab == null)
                return;

            if (recoveryVfxInstance != null)
                Destroy(recoveryVfxInstance);

            recoveryVfxInstance = Instantiate(
                recoveryVfxPrefab,
                transform.position,
                transform.rotation,
                transform);
            if (!recoveryVfxInstance.activeSelf)
                recoveryVfxInstance.SetActive(true);
        }

        private bool TryGetRespawnTransform(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (lapTracker == null)
                lapTracker = GetComponent<LapTracker>();

            int checkpointIndex = lapTracker != null ? lapTracker.LastValidCheckpoint : -1;

            CheckpointManager cpManager = FindFirstObjectByType<CheckpointManager>();
            if (cpManager != null && cpManager.CheckpointCount > 0)
            {
                int safeIndex = checkpointIndex >= 0 && checkpointIndex < cpManager.CheckpointCount
                    ? checkpointIndex
                    : 0;

                Checkpoint cp = cpManager.GetCheckpoint(safeIndex);
                if (cp != null)
                {
                    position = cp.transform.position + Vector3.up * 0.5f;

                    // Align cleanly with road surface
                    if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit roadHit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        position = roadHit.point + Vector3.up * 0.35f;
                    }

                    // Align heading towards next checkpoint
                    int nextIndex = (safeIndex + 1) % cpManager.CheckpointCount;
                    Checkpoint nextCp = cpManager.GetCheckpoint(nextIndex);
                    if (nextCp != null)
                    {
                        Vector3 tangent = nextCp.transform.position - cp.transform.position;
                        tangent.y = 0f;
                        if (tangent.sqrMagnitude > 0.01f)
                        {
                            rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                            return true;
                        }
                    }

                    rotation = cp.transform.rotation;
                    return true;
                }
            }

            // Fallback: CarResetController lastSafe road point
            if (resetController == null)
                resetController = GetComponent<CarResetController>();

            if (resetController != null)
            {
                position = resetController.LastSafePosition + Vector3.up * 0.2f;
                rotation = resetController.LastSafeRotation;
                return true;
            }

            return false;
        }

        private void SpawnWreckAndDebris(Vector3 blastPoint)
        {
            CleanupActiveDebris();

            // Case A: Dedicated wreck prefab assigned by artist
            if (wreckPrefab != null)
            {
                GameObject spawnedWreck = Instantiate(wreckPrefab, transform.position, transform.rotation);
                if (spawnedWreck != null)
                {
                    Rigidbody[] rbs = spawnedWreck.GetComponentsInChildren<Rigidbody>();
                    foreach (Rigidbody rb in rbs)
                    {
                        if (rb != null)
                        {
                            rb.AddExplosionForce(debrisExplosionForce, blastPoint, 12f, debrisUpwardModifier, ForceMode.Impulse);
                        }
                    }
                    activeDebrisList.Add(spawnedWreck);
                    Destroy(spawnedWreck, debrisLifetime);
                }
                return;
            }

            // Case B: Dynamic breakaway debris using the active vehicle's pre-separated parts
            GameObject visualRoot = GetActiveVisualRoot();
            if (visualRoot == null)
                return;

            MeshRenderer[] renderers = visualRoot.GetComponentsInChildren<MeshRenderer>(false);
            if (renderers == null || renderers.Length == 0)
            {
                // Fallback for single combined mesh karts without separated parts
                SpawnFallbackScrapDebris(blastPoint);
                return;
            }

            int spawnedCount = 0;
            foreach (MeshRenderer r in renderers)
            {
                if (r == null || !r.gameObject.activeInHierarchy)
                    continue;

                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;

                string partName = r.gameObject.name.ToLowerInvariant();
                // Skip UI, light markers, or shield bubbles
                if (partName.Contains("shield") || partName.Contains("marker") || partName.Contains("light"))
                    continue;

                GameObject debris = new GameObject($"Debris_{r.gameObject.name}");
                debris.transform.position = r.transform.position;
                debris.transform.rotation = r.transform.rotation;
                debris.transform.localScale = r.transform.lossyScale;

                MeshFilter debrisMf = debris.AddComponent<MeshFilter>();
                debrisMf.sharedMesh = mf.sharedMesh;

                MeshRenderer debrisMr = debris.AddComponent<MeshRenderer>();
                debrisMr.sharedMaterials = r.sharedMaterials;

                // Box collider bounded by part geometry
                BoxCollider box = debris.AddComponent<BoxCollider>();
                box.size = mf.sharedMesh.bounds.size;
                box.center = mf.sharedMesh.bounds.center;

                // Physics Rigidbody
                Rigidbody rb = debris.AddComponent<Rigidbody>();
                rb.mass = Mathf.Clamp(box.size.x * box.size.y * box.size.z * 60f, 4f, 20f);
                rb.linearDamping = 0.35f;
                rb.angularDamping = 0.6f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                // Scatter outwards from blast point
                rb.AddExplosionForce(debrisExplosionForce, blastPoint, 12f, debrisUpwardModifier, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * flipTorqueForce * 0.6f, ForceMode.Impulse);

                activeDebrisList.Add(debris);
                Destroy(debris, debrisLifetime);

                spawnedCount++;
                if (spawnedCount >= maxDebrisPieces)
                    break;
            }

            // A single combined body mesh is not a believable break-apart
            // effect. Remove that whole-body copy and use capped fallback
            // scrap pieces instead.
            if (spawnedCount <= 1)
            {
                CleanupActiveDebris();
                SpawnFallbackScrapDebris(blastPoint);
            }
        }

        private void SpawnFallbackScrapDebris(Vector3 blastPoint)
        {
            // Fallback for vehicles with only 1 combined mesh: spawns capped physical scrap fragments
            Color scrapColor = vehiclePaint != null ? vehiclePaint.CurrentColor : new Color(0.8f, 0.2f, 0.1f);
            for (int i = 0; i < Mathf.Min(4, maxDebrisPieces); i++)
            {
                Vector3 scrapPos = transform.position + Random.insideUnitSphere * 0.6f + Vector3.up * 0.4f;
                GameObject scrap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                scrap.name = $"FallbackScrap_{i}";
                scrap.transform.position = scrapPos;
                scrap.transform.localScale = new Vector3(0.35f, 0.2f, 0.4f);

                Renderer scrapRenderer = scrap.GetComponent<Renderer>();
                if (scrapRenderer != null)
                {
                    scrapRenderer.material.color = scrapColor;
                }

                Rigidbody rb = scrap.GetComponent<Rigidbody>();
                if (rb == null)
                    rb = scrap.AddComponent<Rigidbody>();

                rb.mass = 8f;
                rb.linearDamping = 0.4f;
                rb.angularDamping = 0.8f;
                rb.AddExplosionForce(debrisExplosionForce, blastPoint, 10f, debrisUpwardModifier, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * flipTorqueForce, ForceMode.Impulse);

                activeDebrisList.Add(scrap);
                Destroy(scrap, debrisLifetime);
            }
        }

        private void SetIntactVisualVisible(bool visible)
        {
            GameObject visualRoot = GetActiveVisualRoot();
            if (visualRoot != null)
            {
                MeshRenderer[] renderers = visualRoot.GetComponentsInChildren<MeshRenderer>(true);
                foreach (MeshRenderer r in renderers)
                {
                    if (r != null)
                        r.enabled = visible;
                }
            }
        }

        private GameObject GetActiveVisualRoot()
        {
            if (intactVisualRoot != null)
                return intactVisualRoot;

            if (vehiclePaint == null)
                vehiclePaint = GetComponent<PoseKartVehiclePaint>();
            if (vehiclePaint == null)
                vehiclePaint = GetComponentInChildren<PoseKartVehiclePaint>(true);

            if (vehiclePaint != null && vehiclePaint.ActiveVehicleModel != null)
                return vehiclePaint.ActiveVehicleModel;

            Transform visualChild = transform.Find("VehicleVisualSet");
            if (visualChild != null)
                return visualChild.gameObject;

            return gameObject;
        }

        private void PlayExplosionEffects(Vector3 blastPoint)
        {
            try
            {
                // Audio
                if (explosionAudio != null)
                {
                    AudioSource.PlayClipAtPoint(explosionAudio, blastPoint, 1f);
                }
                else
                {
                    AudioManager.PlayMissileImpact(blastPoint);
                }

                // VFX
                if (explosionVfxPrefab != null)
                {
                    GameObject vfx = Instantiate(explosionVfxPrefab, blastPoint, Quaternion.identity);
                    Destroy(vfx, 4.5f);
                }
                else
                {
                    KartItemInventory inventory = GetComponent<KartItemInventory>();
                    GameObject prefab = inventory != null ? inventory.MissileImpactExplosionPrefab : null;
                    if (prefab != null)
                    {
                        GameObject vfx = Instantiate(prefab, blastPoint, Quaternion.identity);
                        Destroy(vfx, 4.5f);
                    }
                    else
                    {
                        KartParticleEffect.Burst(blastPoint, new Color(1f, 0.4f, 0.05f, 1f), 64);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PoseKartWreckController] Handled VFX/Audio exception: {ex.Message}");
            }
        }

        private void TriggerLocalCameraShake()
        {
            try
            {
                RaceParticipant participant = GetComponent<RaceParticipant>();
                bool isLocalPlayer = participant != null && participant.IsPlayer;

                NetworkObject netObj = GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    isLocalPlayer = netObj.IsOwner && netObj.IsPlayerObject;
                }

                if (isLocalPlayer)
                {
                    var impulse = FindFirstObjectByType<Cinemachine.CinemachineImpulseSource>();
                    if (impulse != null)
                    {
                        impulse.GenerateImpulse(3.5f);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PoseKartWreckController] Camera shake exception: {ex.Message}");
            }
        }

        private void CleanupActiveDebris()
        {
            for (int i = activeDebrisList.Count - 1; i >= 0; i--)
            {
                if (activeDebrisList[i] != null)
                {
                    Destroy(activeDebrisList[i]);
                }
            }
            activeDebrisList.Clear();
        }
    }
}

