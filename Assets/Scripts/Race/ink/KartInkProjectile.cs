using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Lightweight, swept, homing arcade ink projectile. Detects target hits
    /// directly instead of relying on high-speed OnTriggerEnter messages.
    /// Does not modify CarController, camera or other item prefabs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KartInkProjectile : MonoBehaviour
    {
        private CarController owner;
        private CarController target;
        private float speed;
        private float expiresAt;
        private float blindDuration;
        private float aiSteeringWobble;
        private float aiThrottleMultiplier;
        private Vector3 startPoint;
        private Vector3 lastKnownTargetPoint;
        private float launchTime;
        private float flightDuration;
        private float arcApexHeight;
        private Material visualMaterial;
        private bool configured;

        private const float HitRadius = 2.0f;

        public static bool Launch(CarController source, CarController victim,
            float shotSpeed, float lifetime, float duration, float aiWobble, float aiThrottle,
            GameObject customPrefab = null, Transform muzzlePoint = null)
        {
            if (source == null || source.TargetBody == null ||
                victim == null || victim.TargetBody == null)
                return false;

            if (customPrefab == null)
            {
                customPrefab = Resources.Load<GameObject>("CannonBall_Ink");
            }

            Vector3 origin;
            Quaternion initialRotation;

            if (muzzlePoint != null)
            {
                origin = muzzlePoint.position;
                Vector3 toVictim = (victim.TargetBody.worldCenterOfMass - origin).normalized;
                initialRotation = toVictim.sqrMagnitude > 0.001f ? Quaternion.LookRotation(toVictim) : muzzlePoint.rotation;
            }
            else
            {
                origin = source.TargetBody.worldCenterOfMass +
                    source.transform.forward * 1.6f + Vector3.up * 1.1f;
                initialRotation = source.transform.rotation;
            }

            GameObject shot;
            KartInkProjectile projectile;

            if (customPrefab != null)
            {
                shot = Instantiate(customPrefab, origin, initialRotation);
                shot.name = "Ink_CannonBall";
                projectile = shot.GetComponent<KartInkProjectile>();
                if (projectile == null)
                    projectile = shot.AddComponent<KartInkProjectile>();
                projectile.Configure(source, victim, shotSpeed, lifetime, duration, aiWobble, aiThrottle, hasCustomVisual: true);
            }
            else
            {
                shot = new GameObject("Ink_CannonBall");
                shot.transform.position = origin;
                shot.transform.rotation = initialRotation;
                projectile = shot.AddComponent<KartInkProjectile>();
                projectile.Configure(source, victim, shotSpeed, lifetime, duration, aiWobble, aiThrottle, hasCustomVisual: false);
            }

            if (PoseKartNetworkItemBridge.IsOnlineRace && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                var netObj = shot.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj != null && !netObj.IsSpawned)
                {
                    netObj.Spawn(true);
                }
            }

            // Caster Feedback: Ink wave burst from muzzlePoint (or rear exhaust fallback) + juicy "SPLAT!" sound
            Vector3 burstPos = muzzlePoint != null ? muzzlePoint.position : origin;
            KartParticleEffect.Burst(burstPos, new Color(0.14f, 0.04f, 0.22f, 0.95f), 32);
            KartParticleEffect.Burst(burstPos + Vector3.up * 0.2f, new Color(0.03f, 0.02f, 0.05f, 0.95f), 24);
            PlaySplatSound(burstPos);

            Debug.Log($"[Ink] LAUNCH ARC {source.name} -> {victim.name} from {(muzzlePoint != null ? muzzlePoint.name : "CenterOfMass")}", source);
            return true;
        }
        private void Start()
        {
            ParticleSystem[] systems =
                GetComponentsInChildren<ParticleSystem>(true);

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];

                if (ps == null ||
                    !ps.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!ps.isPlaying)
                {
                    ps.Play(true);
                }
            }

            TrailRenderer[] trails =
                GetComponentsInChildren<TrailRenderer>(true);

            if (trails.Length == 0)
            {
                // Client clone không chạy Configure(),
                // nên tự tạo fallback trail ở đây.
                AddDarkTrail();
            }
            else
            {
                for (int i = 0; i < trails.Length; i++)
                {
                    if (trails[i] == null)
                        continue;

                    trails[i].Clear();
                    trails[i].emitting = true;
                }
            }
        }
        private void Awake()
        {
            PlayPrefabVisualEffects();
        }

        private void PlayPrefabVisualEffects()
        {
            ParticleSystem[] particles =
                GetComponentsInChildren<ParticleSystem>(true);

            foreach (ParticleSystem ps in particles)
            {
                if (ps == null)
                    continue;

                if (!ps.gameObject.activeSelf)
                    ps.gameObject.SetActive(true);

                ps.Play(true);
            }

            TrailRenderer[] trails =
                GetComponentsInChildren<TrailRenderer>(true);

            if (trails.Length == 0)
            {
                // Client clone không chạy Configure(),
                // nên tạo trail cosmetic ngay từ Awake.
                AddDarkTrail();
                return;
            }

            foreach (TrailRenderer trail in trails)
            {
                if (trail == null)
                    continue;

                trail.Clear();
                trail.emitting = true;
            }
        }
        private void Configure(CarController source, CarController victim,
            float shotSpeed, float lifetime, float duration, float aiWobble, float aiThrottle,
            bool hasCustomVisual)
        {
            owner = source;
            target = victim;
            speed = Mathf.Max(1f, shotSpeed);
            blindDuration = Mathf.Max(0.25f, duration);
            aiSteeringWobble = aiWobble;
            aiThrottleMultiplier = aiThrottle;

            launchTime = Time.time;
            startPoint = transform.position;
            lastKnownTargetPoint = GetAimPoint(victim);

            float initialDistance = Vector3.Distance(startPoint, lastKnownTargetPoint);
            // Parabolic / circular arc travel time between 1.1s and 2.4s for an authentic lob
            flightDuration = Mathf.Clamp(initialDistance / speed * 1.35f, 1.1f, 2.4f);
            expiresAt = launchTime + flightDuration + 0.75f;

            // Arc apex height tossed up into the sky
            arcApexHeight = Mathf.Clamp(initialDistance * 0.35f + 5.5f, 6.5f, 24f);

            // Initial look direction pointing up into the sky
            Vector3 launchHeading = (lastKnownTargetPoint - startPoint);
            launchHeading.y = 0f;
            Vector3 upwardDir = (launchHeading.normalized + Vector3.up * 1.75f).normalized;
            transform.rotation = Quaternion.LookRotation(upwardDir);

            if (!hasCustomVisual)
            {
                BuildVisual();
            }
            else
            {
                if (GetComponentInChildren<TrailRenderer>() == null)
                {
                    AddDarkTrail();
                }
            }

            // Ensure colliders are triggers
            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                if (c != null) c.isTrigger = true;
            }

            configured = true;
        }

        private void BuildVisual()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                visualMaterial = new Material(shader);
                visualMaterial.name = "CannonBall_Black_Runtime_Material";
                Color ink = new Color(0.05f, 0.055f, 0.065f, 1f);
                if (visualMaterial.HasProperty("_BaseColor")) visualMaterial.SetColor("_BaseColor", ink);
                if (visualMaterial.HasProperty("_Color")) visualMaterial.SetColor("_Color", ink);
                if (visualMaterial.HasProperty("_Metallic")) visualMaterial.SetFloat("_Metallic", 0.8f);
                if (visualMaterial.HasProperty("_Smoothness")) visualMaterial.SetFloat("_Smoothness", 0.6f);
            }

            GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blob.name = "CannonBall_Mesh";
            blob.transform.SetParent(transform, false);
            blob.transform.localScale = Vector3.one * 0.72f;
            Collider collider = blob.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }
            MeshRenderer renderer = blob.GetComponent<MeshRenderer>();
            if (renderer != null && visualMaterial != null)
                renderer.sharedMaterial = visualMaterial;

            AddDarkTrail();
        }

        private void AddDarkTrail()
        {
            TrailRenderer trail =
                GetComponent<TrailRenderer>();

            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
                return;
            }

            trail =
                gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.55f;
            trail.minVertexDistance = 0.1f;
            trail.startWidth = 0.42f;
            trail.endWidth = 0.02f;
            trail.startColor = new Color(0.04f, 0.04f, 0.06f, 0.95f);
            trail.endColor = new Color(0.02f, 0.02f, 0.03f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                Material trailMat = new Material(shader);
                Color ink = new Color(0.03f, 0.03f, 0.05f, 0.9f);
                if (trailMat.HasProperty("_BaseColor")) trailMat.SetColor("_BaseColor", ink);
                if (trailMat.HasProperty("_Color")) trailMat.SetColor("_Color", ink);
                trail.sharedMaterial = trailMat;
            }
        }
        private void OnEnable()
        {
            ParticleSystem[] particles =
                GetComponentsInChildren<ParticleSystem>(true);

            foreach (ParticleSystem ps in particles)
            {
                if (ps == null)
                    continue;

                if (!ps.gameObject.activeSelf)
                    ps.gameObject.SetActive(true);

                ps.Play(true);
            }

            TrailRenderer[] trails =
                GetComponentsInChildren<TrailRenderer>(true);

            foreach (TrailRenderer trail in trails)
            {
                if (trail == null)
                    continue;

                trail.Clear();
                trail.emitting = true;
            }
        }
        private static Vector3 GetAimPoint(CarController car)
        {
            return car.TargetBody != null
                ? car.TargetBody.worldCenterOfMass + Vector3.up * 0.25f
                : car.transform.position + Vector3.up;
        }

        private void FixedUpdate()
        {
            if (!configured) return;

            if (PoseKartNetworkItemBridge.IsOnlineRace && Unity.Netcode.NetworkManager.Singleton != null && !Unity.Netcode.NetworkManager.Singleton.IsServer)
                return;
            if (Time.time >= expiresAt || owner == null)
            {
                Destroy(gameObject);
                return;
            }

            bool isTargetValid = target != null && target.TargetBody != null &&
                (PoseKartNetworkItemBridge.IsOnlineRace ? target.gameObject.activeInHierarchy : target.isActiveAndEnabled);
            if (isTargetValid)
            {
                RaceParticipant racer = target.GetComponent<RaceParticipant>();
                if (racer == null || !racer.Finished)
                {
                    lastKnownTargetPoint = GetAimPoint(target);
                }
            }

            float elapsed = Time.time - launchTime;
            float progress = Mathf.Clamp01(elapsed / flightDuration);

            // Circular/parabolic arc trajectory:
            // 1. Base linear interpolation towards target aim position
            Vector3 currentBase = Vector3.Lerp(startPoint, lastKnownTargetPoint, progress);

            // 2. Smooth vertical circular arc: Sin(progress * PI) is 0 at start, 1 at midpoint, 0 at target
            float arcFactor = Mathf.Sin(progress * Mathf.PI);
            Vector3 arcOffset = Vector3.up * (arcFactor * arcApexHeight);
            Vector3 currentPos = currentBase + arcOffset;

            // 3. Orient towards tangent along the curve
            float nextProgress = Mathf.Clamp01(progress + 0.025f);
            Vector3 nextBase = Vector3.Lerp(startPoint, lastKnownTargetPoint, nextProgress);
            Vector3 nextArcOffset = Vector3.up * (Mathf.Sin(nextProgress * Mathf.PI) * arcApexHeight);
            Vector3 tangent = (nextBase + nextArcOffset) - currentPos;
            if (tangent.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(tangent.normalized);
            }

            // 4. Hit detection: When progress reaches >= 0.98 or within proximity
            bool reachedTarget = progress >= 0.98f;
            bool hitProximity = (currentPos - lastKnownTargetPoint).sqrMagnitude <= (HitRadius * HitRadius);

            if (reachedTarget || hitProximity)
            {
                Vector3 impactPoint = lastKnownTargetPoint;
                transform.position = impactPoint;

                if (PoseKartNetworkItemBridge.IsOnlineRace)
                {
                    if (isTargetValid)
                    {
                        var bridge = target.GetComponent<PoseKartNetworkItemBridge>();
                        if (bridge != null)
                        {
                            bridge.ServerApplyInkHit(
                                impactPoint,
                                blindDuration,
                                aiSteeringWobble,
                                aiThrottleMultiplier
                            );
                        }
                    }

                    if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
                    {
                        var netObj = GetComponent<Unity.Netcode.NetworkObject>();
                        if (netObj != null && netObj.IsSpawned)
                        {
                            netObj.Despawn(true);
                            return;
                        }
                    }

                    Destroy(gameObject);
                    return;
                }

                bool applied = false;
                if (target != null && target.isActiveAndEnabled)
                {
                    applied = KartInkStatus.TryApply(target, blindDuration,
                        aiSteeringWobble, aiThrottleMultiplier, impactPoint);
                    Debug.Log($"[Ink] ARC IMPACT {target.name} | applied={applied}", target);
                }

                PoseKart.Audio.AudioManager.PlayMissileImpact(impactPoint);
                KartParticleEffect.Burst(impactPoint,
                    applied ? new Color(0.05f, 0.06f, 0.1f, 1f)
                            : new Color(0.22f, 0.86f, 1f, 1f),
                    applied ? 32 : 18);

                Destroy(gameObject);
                return;
            }

            transform.position = currentPos;
        }

        private static AudioClip splatClip;
        private static void PlaySplatSound(Vector3 position)
        {
            if (splatClip == null)
            {
                int sampleRate = 44100;
                float duration = 0.16f;
                int count = Mathf.CeilToInt(sampleRate * duration);
                float[] samples = new float[count];

                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / sampleRate;
                    float progress = t / duration;
                    // Downward frequency sweep from 360Hz down to 65Hz (gooey splat)
                    float freq = Mathf.Lerp(360f, 65f, progress);
                    float noise = (Random.value * 2f - 1f) * 0.22f;
                    float envelope = Mathf.Sin(progress * Mathf.PI) * Mathf.Clamp01(1f - progress * 0.75f);
                    samples[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) + noise) * envelope * 0.85f;
                }

                splatClip = AudioClip.Create("Ink_Splat_Procedural", count, 1, sampleRate, false);
                splatClip.SetData(samples, 0);
            }

            AudioSource.PlayClipAtPoint(splatClip, position, 0.9f);
        }

        private void OnDestroy()
        {
            if (visualMaterial != null)
                Destroy(visualMaterial);
        }
    }
}
