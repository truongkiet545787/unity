using Cinemachine;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Kinematic arcade homing missile. Uses a swept physics query for impacts and
    /// Rigidbody movement for consistent FixedUpdate motion.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class HomingKartMissile : MonoBehaviour
    {
        private const float CollisionRadius = 0.4f;
        private const float ArrivalRadius = 1.15f;

        private static readonly System.Collections.Generic.List<HomingKartMissile> activeMissiles = new System.Collections.Generic.List<HomingKartMissile>();
        public static System.Collections.Generic.IReadOnlyList<HomingKartMissile> ActiveMissiles => activeMissiles;

        private CarController owner;
        private CarController target;
        public CarController Target => target;
        public CarController Owner => owner;
        public bool IsSpent => spent;
        private Rigidbody body;
        private ParticleSystem exhaust;
        private float born;
        private float missileSpeed = 62f;
        private float trackingStrength = 8f;
        private float targetPredictionStrength = 0.75f;
        private float maximumPredictionTime = 1.25f;
        private float lifetime = 6f;
        private GameObject impactExplosionPrefab;
        private float impactExplosionLifetime = 4f;
        private float explosionRadius = 6f;
        private float maximumHorizontalKnockback = 5f;
        private float maximumUpwardKnockback = 2.5f;
        private float impactTorqueStrength = 12f;
        private float minimumEffectiveDistance = .5f;
        private AnimationCurve explosionFalloff;
        private float knockbackRecoveryDuration = .7f;
        private bool explosionAffectsNearbyRacers = true;
        private bool explosionLineOfSightCheck = true;
        private GameObject groundFirePrefab;
        private float groundFireDuration = 5f;
        private float groundFireRadius = 3f;
        private bool groundFireAffectsRacers = true;
        private bool configured;
        private bool spent;

        // Avoid allocating RaycastHit arrays every physics tick.
        private readonly RaycastHit[] hitBuffer = new RaycastHit[32];
        private readonly Collider[] blastOverlapBuffer = new Collider[64];
        private readonly CarController[] affectedCars = new CarController[32];

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            if (!activeMissiles.Contains(this))
                activeMissiles.Add(this);
        }

        private void OnDisable()
        {
            activeMissiles.Remove(this);
        }

        private void OnDestroy()
        {
            activeMissiles.Remove(this);
        }

        public void Configure(
            CarController source,
            CarController destination,
            float speed = 62f,
            float tracking = 8f,
            float maxLifetime = 6f,
            GameObject explosionPrefab = null,
            float explosionLifetime = 4f,
            float blastRadius = 6f,
            float maxHorizontalKnockback = 5f,
            float maxUpwardKnockback = 2.5f,
            float maxImpactTorque = 12f,
            float minEffectiveDistance = .5f,
            AnimationCurve falloff = null,
            float recoveryDuration = .7f,
            bool affectNearbyRacers = true,
            bool requireLineOfSight = true,
            float prediction = .75f,
            float maxPredictionTime = 1.25f,
            GameObject firePrefab = null,
            float fireDuration = 5f,
            float fireRadius = 3f,
            bool damageRacersOnFire = true)
        {
            owner = source;
            target = destination;
            born = Time.time;
            spent = false;
            configured = true;
            missileSpeed = Mathf.Max(1f, speed);
            trackingStrength = Mathf.Max(0.1f, tracking);
            targetPredictionStrength = Mathf.Clamp01(prediction);
            maximumPredictionTime = Mathf.Clamp(maxPredictionTime, 0f, 3f);
            lifetime = Mathf.Max(0.5f, maxLifetime);
            impactExplosionPrefab = explosionPrefab;
            impactExplosionLifetime = Mathf.Max(0.25f, explosionLifetime);
            explosionRadius = Mathf.Max(.5f, blastRadius);
            maximumHorizontalKnockback = Mathf.Max(0f, maxHorizontalKnockback);
            maximumUpwardKnockback = Mathf.Max(0f, maxUpwardKnockback);
            impactTorqueStrength = Mathf.Max(0f, maxImpactTorque);
            minimumEffectiveDistance = Mathf.Clamp(minEffectiveDistance, 0f, explosionRadius);
            explosionFalloff = falloff;
            knockbackRecoveryDuration = Mathf.Max(.05f, recoveryDuration);
            explosionAffectsNearbyRacers = affectNearbyRacers;
            explosionLineOfSightCheck = requireLineOfSight;
            groundFirePrefab = firePrefab;
            groundFireDuration = Mathf.Max(1f, fireDuration);
            groundFireRadius = Mathf.Max(0.5f, fireRadius);
            groundFireAffectsRacers = damageRacersOnFire;

            if (body == null)
                body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();

            body.useGravity = false;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            if (exhaust == null)
            {
                exhaust = KartParticleEffect.Create(
                    "Missile flame", transform,
                    new Color(1f, 0.4f, 0.08f, 1f), 0.35f, 0.3f);
            }
        }

        private void FixedUpdate()
        {
            // Bỏ qua nếu chưa được cấu hình hoặc đã nổ
            if (!configured || spent)
                return;

            // Chỉ huỷ nếu không có người bắn (chấp nhận target bị null)
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }

            // Nổ khi hết thời gian sống
            if (Time.time - born >= lifetime)
            {
                Explode(body.position, null);
                return;
            }

            Vector3 from = body.position;
            Vector3 currentDirection = body.rotation * Vector3.forward;

            // MẶC ĐỊNH: Bay thẳng tắp theo hướng đang quay (dành cho bắn các hướng tuỳ ý)
            Vector3 desiredDirection = currentDirection;

            // NẾU CÓ MỤC TIÊU: Kích hoạt chế độ bám đuổi (Homing)
            if (target != null && target.TargetBody != null && target.isActiveAndEnabled)
            {
                RaceParticipant participant = target.GetComponent<RaceParticipant>();
                if (participant != null && participant.Finished)
                {
                    target = null; // Bỏ bám đuổi nếu xe đó đã về đích
                }
                else
                {
                    Vector3 targetPosition = target.TargetBody.worldCenterOfMass;
                    Vector3 delta = targetPosition - from;

                    if (delta.sqrMagnitude <= ArrivalRadius * ArrivalRadius)
                    {
                        Explode(targetPosition, target);
                        return;
                    }

                    Vector3 destination = PredictTargetPosition(from, targetPosition);
                    desiredDirection = delta.normalized;
                    Vector3 predictedDelta = destination - from;
                    if (predictedDelta.sqrMagnitude > 0.0001f)
                        desiredDirection = predictedDelta.normalized;
                }
            }

            if (currentDirection.sqrMagnitude < 0.0001f)
                currentDirection = desiredDirection;

            // Xoay mượt mà (sẽ không bị lệch nếu desiredDirection = currentDirection)
            float turnBlend = 1f - Mathf.Exp(-trackingStrength * Time.fixedDeltaTime);
            Vector3 flightDirection = Vector3.Slerp(
                currentDirection, desiredDirection, turnBlend).normalized;

            if (flightDirection.sqrMagnitude < 0.0001f)
                flightDirection = desiredDirection;

            float travel = missileSpeed * Time.fixedDeltaTime;

            // Quét va chạm
            int hitCount = Physics.SphereCastNonAlloc(
                from, CollisionRadius, flightDirection, hitBuffer, travel,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            // Sắp xếp va chạm gần nhất
            for (int i = 1; i < hitCount; i++)
            {
                RaycastHit hit = hitBuffer[i];
                int j = i - 1;
                while (j >= 0 && hitBuffer[j].distance > hit.distance)
                {
                    hitBuffer[j + 1] = hitBuffer[j];
                    j--;
                }
                hitBuffer[j + 1] = hit;
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = hitBuffer[i].collider;
                if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
                    continue;

                CarController hitCar = ResolveCar(hitCollider);
                // Bỏ qua va chạm với người bắn
                if (hitCar == owner ||
                    (owner != null && owner.TargetBody != null && hitCollider.attachedRigidbody == owner.TargetBody))
                    continue;

                Explode(hitBuffer[i].point, hitCar);
                return;
            }

            // Di chuyển tên lửa với quỹ đạo lượn sóng Zigzag nhẹ (stylized arcade heatseeker)
            float weaveAge = Time.time - born;
            Vector3 rightAxis = Vector3.Cross(flightDirection, Vector3.up);
            if (rightAxis.sqrMagnitude < 0.001f)
                rightAxis = Vector3.right;
            else
                rightAxis.Normalize();

            // Tự động triệt tiêu độ lượn khi tên lửa lao sát mục tiêu (<15m) để đảm bảo độ chuẩn xác
            float weaveDampen = (target != null && target.TargetBody != null)
                ? Mathf.Clamp01((target.TargetBody.worldCenterOfMass - from).magnitude / 15f)
                : 1f;
            Vector3 weaveOffset = rightAxis * (Mathf.Sin(weaveAge * 14f) * 1.8f * weaveDampen * Time.fixedDeltaTime);

            Vector3 newPosition = from + flightDirection * travel + weaveOffset;
            Vector3 actualFlightDir = (newPosition - from);
            if (actualFlightDir.sqrMagnitude > 0.0001f)
            {
                body.MoveRotation(Quaternion.LookRotation(actualFlightDir.normalized, Vector3.up));
            }
            body.MovePosition(newPosition);

            if (exhaust != null)
                exhaust.Emit(4);

            // Quét nổ backup khi ở gần mục tiêu (chỉ quét nếu có target)
            if (target != null)
            {
                if ((target.TargetBody.worldCenterOfMass - newPosition).sqrMagnitude <= ArrivalRadius * ArrivalRadius)
                    Explode(target.TargetBody.worldCenterOfMass, target);
            }
        }

        private Vector3 PredictTargetPosition(Vector3 missilePosition, Vector3 targetPosition)
        {
            if (targetPredictionStrength <= 0f || maximumPredictionTime <= 0f ||
                target == null || target.TargetBody == null)
                return targetPosition;

            Vector3 relative = targetPosition - missilePosition;
            Vector3 targetVelocity = target.TargetBody.linearVelocity;
            float distance = relative.magnitude;
            if (distance <= 0.01f || targetVelocity.sqrMagnitude <= 0.0001f)
                return targetPosition;

            // Solve the constant-velocity intercept equation:
            // |relative + targetVelocity * t| = missileSpeed * t.
            // This gives a better lead than simply aiming one fixed distance ahead,
            // while the cap keeps sharp turns and lagging targets controllable.
            float a = Vector3.Dot(targetVelocity, targetVelocity) - missileSpeed * missileSpeed;
            float b = 2f * Vector3.Dot(relative, targetVelocity);
            float c = Vector3.Dot(relative, relative);
            float interceptTime = distance / Mathf.Max(1f, missileSpeed);

            if (Mathf.Abs(a) < 0.0001f)
            {
                if (Mathf.Abs(b) > 0.0001f)
                    interceptTime = -c / b;
            }
            else
            {
                float discriminant = b * b - 4f * a * c;
                if (discriminant >= 0f)
                {
                    float root = Mathf.Sqrt(discriminant);
                    float t0 = (-b - root) / (2f * a);
                    float t1 = (-b + root) / (2f * a);
                    interceptTime = SelectSmallestPositive(t0, t1, interceptTime);
                }
            }

            if (float.IsNaN(interceptTime) || float.IsInfinity(interceptTime) || interceptTime < 0f)
                interceptTime = distance / Mathf.Max(1f, missileSpeed);

            interceptTime = Mathf.Clamp(interceptTime * targetPredictionStrength, 0f, maximumPredictionTime);
            return targetPosition + targetVelocity * interceptTime;
        }

        private static float SelectSmallestPositive(float first, float second, float fallback)
        {
            bool firstValid = first > 0f && !float.IsNaN(first) && !float.IsInfinity(first);
            bool secondValid = second > 0f && !float.IsNaN(second) && !float.IsInfinity(second);
            if (firstValid && secondValid) return Mathf.Min(first, second);
            if (firstValid) return first;
            if (secondValid) return second;
            return fallback;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!configured || spent || other == null || other.transform.IsChildOf(transform))
                return;

            CarController hitCar = ResolveCar(other);
            if (hitCar == owner)
                return;

            // Trigger-based scenery and custom missile prefabs still use the same
            // single explosion path; a null racer simply means the blast has no
            // direct victim and may affect nearby racers through the area query.
            Explode(transform.position, hitCar);
        }

        private CarController ResolveCar(Collider hitCollider)
        {
            if (hitCollider == null)
                return null;

            // Most cars have the physics colliders on children of CarController.
            CarController car = hitCollider.GetComponentInParent<CarController>();
            if (car != null)
                return car;

            Rigidbody attachedBody = hitCollider.attachedRigidbody;
            if (attachedBody == null)
                return null;

            car = attachedBody.GetComponent<CarController>();
            if (car == null)
                car = attachedBody.GetComponentInParent<CarController>();
            if (car != null)
                return car;

            // Also support vehicle setups with the Rigidbody on a sibling/child.
            if (owner != null && owner.TargetBody == attachedBody)
                return owner;
            if (target != null && target.TargetBody == attachedBody)
                return target;

            return null;
        }

        private void Explode(Vector3 point, CarController directHitTarget)
        {
            if (spent)
                return;

            spent = true;
            activeMissiles.Remove(this);
            PoseKart.Audio.AudioManager.PlayMissileImpact(point);
            SpawnImpactExplosion(point);
            SpawnRadialShockwave(point);
            TriggerMissileCameraShake(directHitTarget);
            CheckAndSpawnGroundFire(point);
            ApplyAreaKnockback(point, directHitTarget);

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.detectCollisions = false;
            }

            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                    collider.enabled = false;
            }

            Destroy(gameObject);
        }

        private void ApplyAreaKnockback(Vector3 point, CarController directHitTarget)
        {
            int affectedCount = 0;
            if (explosionAffectsNearbyRacers)
            {
                int overlapCount = Physics.OverlapSphereNonAlloc(
                    point,
                    explosionRadius,
                    blastOverlapBuffer,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);

                for (int index = 0; index < overlapCount; index++)
                {
                    CarController car = ResolveCar(blastOverlapBuffer[index]);
                    if (!IsEligibleRacer(car) || ContainsCar(car, affectedCount))
                        continue;

                    if (affectedCount < affectedCars.Length)
                        affectedCars[affectedCount++] = car;
                }
            }

            // The direct hit is authoritative even if the overlap query missed a
            // compound collider or the impact point is just outside the body COM.
            if (IsEligibleRacer(directHitTarget) && !ContainsCar(directHitTarget, affectedCount)
                && affectedCount < affectedCars.Length)
            {
                affectedCars[affectedCount++] = directHitTarget;
            }

            for (int index = 0; index < affectedCount; index++)
            {
                CarController car = affectedCars[index];
                affectedCars[index] = null;
                ApplyRacerKnockback(car, point, car == directHitTarget);
            }
        }

        private void ApplyRacerKnockback(CarController car, Vector3 point, bool directHit)
        {
            if (!IsEligibleRacer(car) || car.TargetBody == null)
                return;

            Vector3 bodyPoint = car.TargetBody.worldCenterOfMass;
            float distance = Vector3.Distance(point, bodyPoint);
            float factor;
            if (directHit)
            {
                factor = 1f;
            }
            else
            {
                if (distance > explosionRadius)
                    return;
                if (explosionLineOfSightCheck && !HasLineOfSight(point, bodyPoint, car))
                    return;

                float normalizedDistance = Mathf.InverseLerp(
                    minimumEffectiveDistance,
                    explosionRadius,
                    distance);
                float centerWeight = 1f - normalizedDistance;
                factor = explosionFalloff != null
                    ? Mathf.Clamp01(explosionFalloff.Evaluate(centerWeight))
                    : Mathf.SmoothStep(0f, 1f, centerWeight);
            }

            if (factor <= .001f)
                return;

            KartItemDefense defense = car.GetComponent<KartItemDefense>();
            if (defense != null && defense.TryBlockMissileExplosion(point))
                return;

            Vector3 horizontal = Vector3.ProjectOnPlane(bodyPoint - point, Vector3.up);
            if (horizontal.sqrMagnitude < .0001f)
                horizontal = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up);
            if (horizontal.sqrMagnitude < .0001f)
                horizontal = Vector3.right;
            horizontal.Normalize();

            Vector3 deltaVelocity = horizontal * (maximumHorizontalKnockback * factor)
                                  + Vector3.up * (maximumUpwardKnockback * factor);
            car.ApplyMissileExplosionImpact(
                deltaVelocity,
                knockbackRecoveryDuration,
                point,
                impactTorqueStrength * 1.35f);

            // Stunt / Flip Tumble physics: hất tung xe và lộn nhào ngoạn mục
            if (car.TargetBody != null && !car.TargetBody.isKinematic)
            {
                car.TargetBody.AddForce(Vector3.up * (maximumUpwardKnockback * 1.4f), ForceMode.VelocityChange);
                Vector3 tumbleAxis = (Vector3.Cross(horizontal, Vector3.up) + Random.insideUnitSphere * 0.4f).normalized;
                car.TargetBody.AddTorque(tumbleAxis * (impactTorqueStrength * 1.5f), ForceMode.Impulse);
            }

            // Khói đen bốc lên bám theo xe nạn nhân 1.8 giây
            SpawnVictimBurnSmoke(car, 1.8f);

            // Rung màn hình Cinemachine Impulse cực mạnh cho cả 2 bên
            TriggerMissileCameraShake(car);
        }

        private bool HasLineOfSight(Vector3 origin, Vector3 destination, CarController car)
        {
            Vector3 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance <= .01f)
                return true;

            if (!Physics.Raycast(origin, delta / distance, out RaycastHit hit, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;

            return hit.collider != null && ResolveCar(hit.collider) == car;
        }

        private bool IsEligibleRacer(CarController car)
        {
            if (car == null || car == owner || !car.isActiveAndEnabled || car.TargetBody == null)
                return false;

            RaceParticipant participant = car.GetComponent<RaceParticipant>();
            return participant == null || !participant.Finished;
        }

        private bool ContainsCar(CarController car, int count)
        {
            for (int index = 0; index < count; index++)
            {
                if (affectedCars[index] == car)
                    return true;
            }

            return false;
        }

        private void SpawnImpactExplosion(Vector3 point)
        {
            if (impactExplosionPrefab == null)
            {
                KartParticleEffect.Burst(point, new Color(1f, 0.35f, 0.05f, 1f), 52);
                return;
            }

            GameObject effect = Instantiate(impactExplosionPrefab, point, transform.rotation);
            if (effect == null)
                return;

            ParticleSystem[] systems = effect.GetComponentsInChildren<ParticleSystem>(true);
            float cleanupDelay = impactExplosionLifetime;
            foreach (ParticleSystem system in systems)
            {
                if (system == null)
                    continue;

                if (!system.isPlaying)
                    system.Play(true);

                ParticleSystem.MainModule main = system.main;
                float duration = main.duration + main.startLifetime.constantMax;
                if (!main.loop)
                    cleanupDelay = Mathf.Max(cleanupDelay, duration + .25f);
            }

            // The explosion is world-space and independent from the missile. A
            // bounded cleanup protects the scene even if a supplied prefab loops.
            Destroy(effect, Mathf.Max(.25f, cleanupDelay));
        }

        private void CheckAndSpawnGroundFire(Vector3 impactPoint)
        {
            // Probe downwards to check if the explosion touched or is near the ground/terrain/track
            if (Physics.Raycast(impactPoint + Vector3.up * 0.75f, Vector3.down, out RaycastHit groundHit, 3.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // Only ignite if hitting static scenery/ground, not directly on an enemy kart chassis
                if (groundHit.collider != null && ResolveCar(groundHit.collider) == null)
                {
                    GameObject fireHolder = new GameObject("Missile_GroundFire_Zone");
                    fireHolder.transform.SetPositionAndRotation(groundHit.point + groundHit.normal * 0.05f, Quaternion.FromToRotation(Vector3.up, groundHit.normal));
                    KartGroundFireZone fireZone = fireHolder.AddComponent<KartGroundFireZone>();
                    fireZone.Initialize(owner, groundFireDuration, groundFireRadius, groundFireAffectsRacers, groundFirePrefab);
                }
            }
        }

        private void TriggerMissileCameraShake(CarController victimCar)
        {
            bool isVictimPlayer = victimCar != null && IsPlayer(victimCar);
            bool isOwnerPlayer = owner != null && IsPlayer(owner);

            if (isVictimPlayer || isOwnerPlayer)
            {
                var impulse = FindFirstObjectByType<CinemachineImpulseSource>();
                if (impulse != null)
                {
                    float force = isVictimPlayer ? 2.0f : 1.25f;
                    impulse.GenerateImpulse(force);
                }
            }
        }

        private static bool IsPlayer(CarController c)
        {
            if (c == null) return false;
            RaceParticipant p = c.GetComponent<RaceParticipant>();
            return p != null ? p.IsPlayer : c.name.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SpawnRadialShockwave(Vector3 point)
        {
            GameObject waveObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            waveObj.name = "Missile_Radial_Shockwave";
            waveObj.transform.position = point + Vector3.up * 0.12f;
            waveObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            waveObj.transform.localScale = Vector3.one * 0.5f;

            Collider col = waveObj.GetComponent<Collider>();
            if (col != null) Destroy(col);

            MeshRenderer mr = waveObj.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null && mr != null)
            {
                Material mat = new Material(shader);
                mat.color = new Color(1f, 0.45f, 0.1f, 0.85f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 0.45f, 0.1f, 0.85f));
                mr.sharedMaterial = mat;
            }

            ShockwaveRingAnimator anim = waveObj.AddComponent<ShockwaveRingAnimator>();
            anim.Initialize(7.5f, 0.38f);
        }

        private static void SpawnVictimBurnSmoke(CarController victimCar, float duration)
        {
            if (victimCar == null) return;

            // 1. Tải Prefab Missile_Victim_Smoke từ Resources hoặc Assets/Prefabs/VFX/
            GameObject prefab = Resources.Load<GameObject>("Missile_Victim_Smoke");
#if UNITY_EDITOR
            if (prefab == null)
            {
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/Missile_Victim_Smoke.prefab");
            }
#endif

            if (prefab != null)
            {
                GameObject smokeGo = Instantiate(prefab);
                smokeGo.name = "Missile_Victim_Smoke";
                MissileVictimSmoke smokeCtrl = smokeGo.GetComponent<MissileVictimSmoke>();
                if (smokeCtrl != null)
                {
                    smokeCtrl.AttachToVictim(victimCar.transform, duration);
                }
                else
                {
                    smokeGo.transform.SetParent(victimCar.transform, false);
                    smokeGo.transform.localPosition = new Vector3(0f, 0.4f, -0.6f);
                    ParticleSystem ps = smokeGo.GetComponent<ParticleSystem>();
                    if (ps != null && !ps.isPlaying) ps.Play();
                    victimCar.StartCoroutine(StopSmokeAfterDelay(ps, duration));
                    Destroy(smokeGo, duration + 1.5f);
                }
                return;
            }

            // 2. Fallback dự phòng nếu chưa tìm thấy prefab: Tạo hạt và gán Material URP chuẩn (không bao giờ bị ô vuông tím)
            GameObject fallbackObj = new GameObject("Missile_Victim_Smoke");
            fallbackObj.transform.SetParent(victimCar.transform, false);
            fallbackObj.transform.localPosition = new Vector3(0f, 0.4f, -0.6f);

            ParticleSystem fallbackPs = fallbackObj.AddComponent<ParticleSystem>();
            var main = fallbackPs.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.20f, 0.20f, 0.22f, 0.85f), new Color(0.05f, 0.05f, 0.06f, 0.95f));
            main.gravityModifier = -0.15f;
            main.loop = true;

            var emission = fallbackPs.emission;
            emission.rateOverTime = 25f;

            var shape = fallbackPs.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;

            ParticleSystemRenderer psRenderer = fallbackObj.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null)
            {
#if UNITY_EDITOR
                Material smokeMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/PoseKart/Materials/M_Missile_Victim_Smoke.mat")
                    ?? UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/PoseKart/VFX/Drift/Materials/DriftSmoke.mat");
                if (smokeMat != null) psRenderer.material = smokeMat;
#endif
            }

            fallbackPs.Play();

            Destroy(fallbackObj, duration + 1.5f);
            victimCar.StartCoroutine(StopSmokeAfterDelay(fallbackPs, duration));
        }

        private static System.Collections.IEnumerator StopSmokeAfterDelay(ParticleSystem ps, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (ps != null)
            {
                var emission = ps.emission;
                emission.enabled = false;
            }
        }
    }

    internal sealed class ShockwaveRingAnimator : MonoBehaviour
    {
        private float maxScale;
        private float duration;
        private float spawnTime;
        private Material mat;
        private Color baseColor;

        public void Initialize(float targetScale, float time)
        {
            maxScale = targetScale;
            duration = Mathf.Max(0.1f, time);
            spawnTime = Time.time;
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mat = mr.material;
                baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
            }
            Destroy(gameObject, duration + 0.05f);
        }

        private void Update()
        {
            float elapsed = Time.time - spawnTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float currentScale = Mathf.Lerp(0.5f, maxScale, Mathf.Sqrt(t));
            transform.localScale = new Vector3(currentScale, currentScale, 1f);

            if (mat != null)
            {
                Color c = new Color(baseColor.r, baseColor.g, baseColor.b, (1f - t) * baseColor.a);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                else mat.color = c;
            }
        }

        private void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
