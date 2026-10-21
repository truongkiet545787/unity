using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PoseKart.Race;

namespace PoseKart.Vehicle
{
    public enum SlipstreamPhase
    {
        Inactive = 0,
        Charging = 1,
        Active = 2,
        Slingshot = 3
    }

    /// <summary>
    /// Player Aerodynamic Drafting & Slipstream system.
    /// Allows the player kart to draft behind opponents (AI or rivals) at high speed,
    /// build up aerodynamic suction, burst past in an overtake slingshot, and smoothly transition back.
    /// Safely integrates with existing CarController speed limits and Nitro without overwriting physics.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CarController))]
    public sealed class PoseKartPlayerSlipstream : MonoBehaviour
    {
        [Header("Vehicle & Race References")]
        [SerializeField] private CarController carController;
        [SerializeField] private Rigidbody targetBody;
        [SerializeField] private RaceManager raceManager;

        [Header("Drafting Detection (Inspector Editable)")]
        [Tooltip("Khoảng cách tối đa để bắt đầu núp gió (mét).")]
        [SerializeField, Range(8f, 25f)] private float maxDraftDistance = 16f;
        [Tooltip("Khoảng cách tối thiểu để tránh va chạm trực tiếp (mét).")]
        [SerializeField, Range(1f, 5f)] private float minDraftDistance = 2.5f;
        [Tooltip("Độ lệch sang hai bên tối đa so với đuôi xe phía trước (mét).")]
        [SerializeField, Range(1f, 4f)] private float maxLateralOffset = 2.6f;
        [Tooltip("Độ thẳng hàng tối thiểu giữa 2 xe (Dot product hướng đầu xe).")]
        [SerializeField, Range(0.5f, 0.95f)] private float minAlignmentDot = 0.70f;
        [Tooltip("Tốc độ tối thiểu (km/h) để có thể bắt đầu kích hoạt hiệu ứng khí động học núp gió.")]
        [SerializeField, Range(20f, 70f)] private float minDraftSpeedKmh = 45f;

        [Header("Timing & Tuning")]
        [Tooltip("Thời gian bám đuôi liên tục để sạc đầy Slipstream (giây).")]
        [SerializeField, Range(0.2f, 1.5f)] private float chargeDuration = 0.5f;
        [Tooltip("Thời gian duy trì đà quán tính (Slingshot) sau khi thoát khỏi đuôi xe để vượt lên (giây).")]
        [SerializeField, Range(0.5f, 2.0f)] private float slingshotDuration = 1.0f;
        [Tooltip("Tốc độ tối đa cộng thêm khi núp gió (km/h). Bị giới hạn an toàn dưới mốc Nitro.")]
        [SerializeField, Range(5f, 20f)] private float speedBonusKmh = 14f;
        [Tooltip("Lực hút gia tốc khí động học kéo xe về phía trước (m/s^2).")]
        [SerializeField, Range(2f, 12f)] private float suctionAcceleration = 5.5f;

        [Header("Visual Effects (Editable VFX References)")]
        [SerializeField] private ParticleSystem leftWindTrail;
        [SerializeField] private ParticleSystem rightWindTrail;
        [SerializeField] private ParticleSystem airflowBurst;

        [Header("UI Feedback (Editable Hierarchy)")]
        [SerializeField] private GameObject hudIndicatorRoot;
        [SerializeField] private CanvasGroup hudGroup;
        [SerializeField] private TMP_Text hudLabel;
        [SerializeField] private Image hudChargeBar;

        [Header("Audio")]
        [SerializeField] private AudioClip slipstreamActivateSound;
        [SerializeField, Range(0f, 1f)] private float audioVolume = 0.8f;

        [Header("Runtime State (Read Only)")]
        [SerializeField] private SlipstreamPhase currentPhase = SlipstreamPhase.Inactive;
        [SerializeField] private float draftCharge = 0f;
        [SerializeField] private float slingshotTimer = 0f;
        [SerializeField] private CarController currentLeadCar = null;
        [SerializeField] private float currentDistanceToLead = 0f;

        private readonly List<CarController> cachedOpponents = new List<CarController>();
        private float nextOpponentRefreshTime = 0f;
        private bool wasActiveSoundPlayed = false;

        public SlipstreamPhase CurrentPhase => currentPhase;
        public float DraftCharge01 => Mathf.Clamp01(draftCharge / Mathf.Max(0.01f, chargeDuration));
        public bool IsActive => currentPhase == SlipstreamPhase.Active || currentPhase == SlipstreamPhase.Slingshot;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ResetState();
        }

        private void OnDisable()
        {
            if (carController != null)
            {
                carController.SetSlipstreamBonus(0f);
            }
            StopVFX();
            ResetState();
        }

        public void ResolveReferences()
        {
            if (carController == null) carController = GetComponent<CarController>();
            if (targetBody == null && carController != null) targetBody = carController.TargetBody;
            if (targetBody == null) targetBody = GetComponent<Rigidbody>();
            if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();

            if (hudIndicatorRoot == null)
            {
                GameObject hud = GameObject.Find("ArcadeHUD_Root");
                Transform found = hud != null ? hud.transform.Find("Slipstream_HUD") : null;
                if (found != null)
                {
                    hudIndicatorRoot = found.gameObject;
                    hudGroup = found.GetComponent<CanvasGroup>();
                    hudLabel = found.Find("SlipstreamLabel")?.GetComponent<TMP_Text>();
                    hudChargeBar = found.Find("ChargeTrack/ChargeFill")?.GetComponent<Image>();
                }
            }

            if (leftWindTrail == null || rightWindTrail == null)
            {
                Transform vfx = transform.Find("Slipstream_VFX");
                if (vfx != null)
                {
                    if (leftWindTrail == null) leftWindTrail = vfx.Find("LeftWindTrail")?.GetComponent<ParticleSystem>();
                    if (rightWindTrail == null) rightWindTrail = vfx.Find("RightWindTrail")?.GetComponent<ParticleSystem>();
                    if (airflowBurst == null) airflowBurst = vfx.Find("AirflowBurst")?.GetComponent<ParticleSystem>();
                }
            }
        }

        public void ConfigureUI(GameObject root, CanvasGroup group, TMP_Text label, Image chargeBar)
        {
            hudIndicatorRoot = root;
            hudGroup = group;
            hudLabel = label;
            hudChargeBar = chargeBar;
        }

        public void ConfigureVFX(ParticleSystem leftTrail, ParticleSystem rightTrail, ParticleSystem burst)
        {
            leftWindTrail = leftTrail;
            rightWindTrail = rightTrail;
            airflowBurst = burst;
        }

        private void ResetState()
        {
            currentPhase = SlipstreamPhase.Inactive;
            draftCharge = 0f;
            slingshotTimer = 0f;
            currentLeadCar = null;
            currentDistanceToLead = 0f;
            wasActiveSoundPlayed = false;
            UpdateHUD();
        }

        private void RefreshOpponentsCache()
        {
            cachedOpponents.Clear();
            if (raceManager != null && raceManager.Racers != null)
            {
                foreach (var racer in raceManager.Racers)
                {
                    if (racer != null && racer.CarController != null && racer.CarController != carController)
                    {
                        cachedOpponents.Add(racer.CarController);
                    }
                }
            }

            if (cachedOpponents.Count == 0)
            {
                CarController[] allCars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
                for (int i = 0; i < allCars.Length; i++)
                {
                    if (allCars[i] != carController)
                    {
                        cachedOpponents.Add(allCars[i]);
                    }
                }
            }

            nextOpponentRefreshTime = Time.time + 3.0f;
        }

        private void FixedUpdate()
        {
            if (carController == null || targetBody == null) return;

            // Áp dụng lực hút khí động học khi đang ở pha Active hoặc Slingshot
            if (currentPhase == SlipstreamPhase.Active)
            {
                carController.SetSlipstreamBonus(speedBonusKmh);
                if (carController.IsGrounded)
                {
                    targetBody.AddForce(transform.forward * suctionAcceleration, ForceMode.Acceleration);
                }
            }
            else if (currentPhase == SlipstreamPhase.Slingshot)
            {
                float decayFraction = Mathf.Clamp01(slingshotTimer / Mathf.Max(0.01f, slingshotDuration));
                carController.SetSlipstreamBonus(speedBonusKmh * decayFraction);
                if (carController.IsGrounded)
                {
                    targetBody.AddForce(transform.forward * (suctionAcceleration * decayFraction), ForceMode.Acceleration);
                }
            }
            else
            {
                carController.SetSlipstreamBonus(0f);
            }
        }

        private void Update()
        {
            if (carController == null || targetBody == null) return;

            // 1. Chỉ hoạt động khi cuộc đua đang chạy
            if (raceManager != null && (raceManager.State != RaceState.Racing || raceManager.PlayerFinished))
            {
                if (currentPhase != SlipstreamPhase.Inactive)
                {
                    ResetState();
                    StopVFX();
                }
                return;
            }

            // 2. Làm mới danh sách đối thủ định kỳ
            if (Time.time >= nextOpponentRefreshTime || cachedOpponents.Count == 0)
            {
                RefreshOpponentsCache();
            }

            // 3. Tìm xe đối thủ ở phía trước đang thỏa mãn nón núp gió
            CarController lead = FindTargetOpponentInDraftCone();
            currentLeadCar = lead;

            float currentSpeedKmh = carController.ForwardSpeed * 3.6f;
            bool canDraft = lead != null && currentSpeedKmh >= minDraftSpeedKmh;

            // 4. Máy trạng thái Slipstream
            if (canDraft)
            {
                slingshotTimer = slingshotDuration; // Sạc sẵn bộ nhớ slingshot
                draftCharge = Mathf.Min(draftCharge + Time.deltaTime, chargeDuration);

                if (draftCharge >= chargeDuration)
                {
                    if (currentPhase != SlipstreamPhase.Active)
                    {
                        currentPhase = SlipstreamPhase.Active;
                        PlayActivationFeedback();
                    }
                }
                else
                {
                    currentPhase = SlipstreamPhase.Charging;
                    UpdateWindStreaks(DraftCharge01 * 0.4f);
                }
            }
            else
            {
                if (currentPhase == SlipstreamPhase.Active || currentPhase == SlipstreamPhase.Slingshot)
                {
                    // Chuyển sang pha Slingshot (Đà vượt)
                    slingshotTimer -= Time.deltaTime;
                    if (slingshotTimer > 0f)
                    {
                        currentPhase = SlipstreamPhase.Slingshot;
                        draftCharge = Mathf.MoveTowards(draftCharge, 0f, Time.deltaTime);
                        UpdateWindStreaks(Mathf.Clamp01(slingshotTimer / slingshotDuration));
                    }
                    else
                    {
                        currentPhase = SlipstreamPhase.Inactive;
                        draftCharge = 0f;
                        wasActiveSoundPlayed = false;
                        StopVFX();
                    }
                }
                else
                {
                    draftCharge = Mathf.MoveTowards(draftCharge, 0f, Time.deltaTime * 2f);
                    currentPhase = SlipstreamPhase.Inactive;
                    wasActiveSoundPlayed = false;
                    StopVFX();
                }
            }

            UpdateHUD();
        }

        private CarController FindTargetOpponentInDraftCone()
        {
            Vector3 myPos = targetBody.worldCenterOfMass;
            Vector3 myForward = transform.forward;
            CarController bestLead = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < cachedOpponents.Count; i++)
            {
                CarController opponent = cachedOpponents[i];
                if (opponent == null || !opponent.isActiveAndEnabled || opponent.TargetBody == null) continue;

                Vector3 oppPos = opponent.TargetBody.worldCenterOfMass;
                Vector3 toOpponent = oppPos - myPos;
                Vector3 localLead = transform.InverseTransformPoint(oppPos);

                // Đối thủ phải ở phía trước (localLead.z > minDraftDistance)
                if (localLead.z < minDraftDistance || localLead.z > maxDraftDistance) continue;

                // Độ lệch sang 2 bên phải nằm trong nón khí động
                if (Mathf.Abs(localLead.x) > maxLateralOffset) continue;

                // Độ cao không quá chênh lệch (không núp gió xe đang bay trên đỉnh đầu)
                if (Mathf.Abs(localLead.y) > 2.0f) continue;

                // Hướng đầu xe của đối thủ phải tương đồng với xe ta (cùng chạy xuôi chiều)
                float alignDot = Vector3.Dot(myForward, opponent.transform.forward);
                if (alignDot < minAlignmentDot) continue;

                if (localLead.z < closestDist)
                {
                    closestDist = localLead.z;
                    bestLead = opponent;
                }
            }

            currentDistanceToLead = closestDist < float.MaxValue ? closestDist : 0f;
            return bestLead;
        }

        private void PlayActivationFeedback()
        {
            if (!wasActiveSoundPlayed)
            {
                wasActiveSoundPlayed = true;
                if (slipstreamActivateSound != null)
                {
                    PoseKart.Audio.AudioManager.Instance.PlaySFX(slipstreamActivateSound, audioVolume);
                }
                else
                {
                    PoseKart.Audio.AudioManager.PlayBoostActivate(transform.position);
                }

                if (airflowBurst != null)
                {
                    airflowBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    airflowBurst.Play(true);
                }
            }

            UpdateWindStreaks(1f);
        }

        private void UpdateWindStreaks(float intensity)
        {
            if (leftWindTrail != null)
            {
                if (!leftWindTrail.isPlaying) leftWindTrail.Play();
                var emission = leftWindTrail.emission;
                emission.rateOverTime = Mathf.Lerp(6f, 32f, intensity);
            }
            if (rightWindTrail != null)
            {
                if (!rightWindTrail.isPlaying) rightWindTrail.Play();
                var emission = rightWindTrail.emission;
                emission.rateOverTime = Mathf.Lerp(6f, 32f, intensity);
            }
        }

        private void StopVFX()
        {
            if (leftWindTrail != null && leftWindTrail.isPlaying)
            {
                leftWindTrail.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            if (rightWindTrail != null && rightWindTrail.isPlaying)
            {
                rightWindTrail.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            if (airflowBurst != null && airflowBurst.isPlaying)
            {
                airflowBurst.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void UpdateHUD()
        {
            if (hudIndicatorRoot == null) return;

            bool showHud = currentPhase != SlipstreamPhase.Inactive;
            if (hudIndicatorRoot.activeSelf != showHud)
            {
                hudIndicatorRoot.SetActive(showHud);
            }

            if (!showHud)
            {
                if (hudGroup != null) hudGroup.alpha = 0f;
                return;
            }

            if (hudGroup != null)
            {
                hudGroup.alpha = currentPhase == SlipstreamPhase.Active ? 1f : 0.85f;
            }

            if (hudChargeBar != null)
            {
                if (currentPhase == SlipstreamPhase.Charging)
                {
                    hudChargeBar.fillAmount = DraftCharge01;
                    hudChargeBar.color = new Color(0.2f, 0.8f, 1f, 0.8f);
                }
                else if (currentPhase == SlipstreamPhase.Active)
                {
                    hudChargeBar.fillAmount = 1f;
                    hudChargeBar.color = new Color(0.1f, 1f, 0.5f, 1f);
                }
                else if (currentPhase == SlipstreamPhase.Slingshot)
                {
                    hudChargeBar.fillAmount = Mathf.Clamp01(slingshotTimer / Mathf.Max(0.01f, slingshotDuration));
                    hudChargeBar.color = new Color(1f, 0.85f, 0.2f, 1f);
                }
            }

            if (hudLabel != null)
            {
                if (currentPhase == SlipstreamPhase.Active)
                {
                    hudLabel.text = "<color=#00FFAA>SLIPSTREAM!</color>";
                }
                else if (currentPhase == SlipstreamPhase.Slingshot)
                {
                    hudLabel.text = "<color=#FFD200>SLINGSHOT!</color>";
                }
                else
                {
                    hudLabel.text = "<color=#38D2FF>DRAFTING...</color>";
                }
            }
        }
    }
}
