#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PoseKart.Race;
using PoseKart.Vehicle;
using PoseKart.AI;

namespace PoseKart.EditorTools
{
    public static class PoseKartPhase3Setup
    {
        private static readonly Color NavyBg = new Color(0.015f, 0.05f, 0.12f, 0.92f);
        private static readonly Color DangerRed = new Color(1f, 0.22f, 0.22f, 1f);
        private static readonly Color GoldAccent = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color CyanAccent = new Color(0.18f, 0.92f, 1f, 1f);
        private static readonly Color WhiteText = new Color(0.96f, 0.98f, 1f, 1f);

        [MenuItem("PoseKart/Phase 3/Setup All Features in Scene (Rocket Start, Wrong Way, Slipstream)")]
        public static void SetupAllPhase3Features()
        {
            // 1. Tìm hoặc xác nhận Player Kart
            CarController playerCar = FindPlayerCar();
            if (playerCar == null)
            {
                Debug.LogError("[Phase 3 Setup] Không tìm thấy Player Car (Buggy_Player) trong Scene!");
                return;
            }

            GameObject playerGo = playerCar.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(playerGo, "Setup Phase 3 Components");

            // 2. Tìm hoặc xác nhận HUD Root
            RectTransform hudRoot = FindHudRoot();
            if (hudRoot == null)
            {
                Debug.LogError("[Phase 3 Setup] Không tìm thấy ArcadeHUD_Root trong Scene!");
                return;
            }

            // 3. Cài đặt Feature 1: Rocket Start
            SetupRocketStart(playerGo, hudRoot);

            // 4. Cài đặt Feature 2: Wrong Way Alert
            SetupWrongWayAlert(playerGo, hudRoot);

            // 5. Cài đặt Feature 3: Player Slipstream
            SetupPlayerSlipstream(playerGo, hudRoot);

            // 6. Đảm bảo RaceManager cho phép đếm ngược
            RaceManager raceManager = UnityEngine.Object.FindFirstObjectByType<RaceManager>();
            if (raceManager != null)
            {
                SerializedObject rmSo = new SerializedObject(raceManager);
                SerializedProperty skipProp = rmSo.FindProperty("skipCountdown");
                if (skipProp != null && skipProp.boolValue)
                {
                    skipProp.boolValue = false;
                    rmSo.ApplyModifiedProperties();
                    Debug.Log("[Phase 3 Setup] Đã tắt skipCountdown trên RaceManager để hỗ trợ đếm ngược 3-2-1-GO!");
                }
            }

            EditorSceneManager.MarkSceneDirty(playerGo.scene);
            Debug.Log("<color=#00FFAA><b>[PoseKart Phase 3]</b> Cài đặt thành công toàn bộ Rocket Start, Wrong Way Alert và Slipstream vào Scene!</color>");
        }

        private static CarController FindPlayerCar()
        {
            RaceParticipant[] participants = UnityEngine.Object.FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None);
            for (int i = 0; i < participants.Length; i++)
            {
                if (participants[i].IsPlayer && participants[i].CarController != null)
                {
                    return participants[i].CarController;
                }
            }

            GameObject buggy = GameObject.Find("Buggy_Player");
            if (buggy != null)
            {
                return buggy.GetComponent<CarController>();
            }

            return UnityEngine.Object.FindFirstObjectByType<CarController>();
        }

        private static RectTransform FindHudRoot()
        {
            GameObject hudGo = GameObject.Find("ArcadeHUD_Root");
            if (hudGo != null)
            {
                return hudGo.GetComponent<RectTransform>();
            }

            GameObject raceHud = GameObject.Find("RaceHUD");
            if (raceHud != null)
            {
                Transform child = raceHud.transform.Find("ArcadeHUD_Root");
                if (child != null) return child.GetComponent<RectTransform>();
                return raceHud.GetComponent<RectTransform>();
            }

            Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            return canvas != null ? canvas.GetComponent<RectTransform>() : null;
        }

        private static void SetupRocketStart(GameObject playerGo, RectTransform hudRoot)
        {
            PoseKartRocketStart rocketStart = playerGo.GetComponent<PoseKartRocketStart>();
            if (rocketStart == null)
            {
                rocketStart = playerGo.AddComponent<PoseKartRocketStart>();
            }

            // Tạo hoặc tái sử dụng UI Feedback
            Transform existingFeedback = hudRoot.Find("RocketStart_Feedback");
            GameObject feedbackRoot;
            CanvasGroup group;
            TMP_Text text;

            if (existingFeedback == null)
            {
                feedbackRoot = new GameObject("RocketStart_Feedback", typeof(RectTransform), typeof(CanvasGroup));
                feedbackRoot.transform.SetParent(hudRoot, false);
                RectTransform rect = feedbackRoot.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 130f);
                rect.sizeDelta = new Vector2(600f, 90f);

                group = feedbackRoot.GetComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;

                GameObject textGo = new GameObject("FeedbackText", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(feedbackRoot.transform, false);
                RectTransform textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;

                text = textGo.GetComponent<TextMeshProUGUI>();
                text.text = "PERFECT START!";
                text.fontSize = 46;
                text.fontStyle = FontStyles.Bold | FontStyles.Italic;
                text.alignment = TextAlignmentOptions.Center;
                text.color = GoldAccent;
            }
            else
            {
                feedbackRoot = existingFeedback.gameObject;
                group = feedbackRoot.GetComponent<CanvasGroup>();
                text = feedbackRoot.GetComponentInChildren<TMP_Text>();
            }

            rocketStart.ResolveReferences();
            rocketStart.ConfigureUI(feedbackRoot, text, group);
            feedbackRoot.SetActive(false);
        }

        private static void SetupWrongWayAlert(GameObject playerGo, RectTransform hudRoot)
        {
            PoseKartWrongWayDetector detector = playerGo.GetComponent<PoseKartWrongWayDetector>();
            if (detector == null)
            {
                detector = playerGo.AddComponent<PoseKartWrongWayDetector>();
            }

            Transform existingAlert = hudRoot.Find("WrongWayAlert");
            GameObject alertRoot;
            CanvasGroup group;
            TMP_Text text;
            RectTransform arrow;
            Image icon;

            if (existingAlert == null)
            {
                alertRoot = new GameObject("WrongWayAlert", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
                alertRoot.transform.SetParent(hudRoot, false);
                RectTransform rect = alertRoot.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 210f);
                rect.sizeDelta = new Vector2(440f, 80f);

                group = alertRoot.GetComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;

                Image bg = alertRoot.GetComponent<Image>();
                bg.color = new Color(0.12f, 0.01f, 0.02f, 0.94f);

                Outline outline = alertRoot.GetComponent<Outline>();
                outline.effectColor = DangerRed;
                outline.effectDistance = new Vector2(3f, -3f);

                // Warning Icon
                GameObject iconGo = new GameObject("WarningIcon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(alertRoot.transform, false);
                RectTransform iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = new Vector2(45f, 0f);
                iconRect.sizeDelta = new Vector2(44f, 44f);
                icon = iconGo.GetComponent<Image>();
                icon.color = GoldAccent;

                // Wrong Way Text
                GameObject textGo = new GameObject("WrongWayText", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(alertRoot.transform, false);
                RectTransform textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(1f, 1f);
                textRect.offsetMin = new Vector2(80f, 0f);
                textRect.offsetMax = new Vector2(-70f, 0f);

                text = textGo.GetComponent<TextMeshProUGUI>();
                text.text = "WRONG WAY!";
                text.fontSize = 36;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.color = DangerRed;

                // Direction Arrow
                GameObject arrowGo = new GameObject("DirectionArrow", typeof(RectTransform), typeof(TextMeshProUGUI));
                arrowGo.transform.SetParent(alertRoot.transform, false);
                arrow = arrowGo.GetComponent<RectTransform>();
                arrow.anchorMin = new Vector2(1f, 0.5f);
                arrow.anchorMax = new Vector2(1f, 0.5f);
                arrow.pivot = new Vector2(0.5f, 0.5f);
                arrow.anchoredPosition = new Vector2(-40f, 0f);
                arrow.sizeDelta = new Vector2(40f, 40f);

                TMP_Text arrowText = arrowGo.GetComponent<TextMeshProUGUI>();
                arrowText.text = "➔";
                arrowText.fontSize = 32;
                arrowText.fontStyle = FontStyles.Bold;
                arrowText.alignment = TextAlignmentOptions.Center;
                arrowText.color = GoldAccent;
            }
            else
            {
                alertRoot = existingAlert.gameObject;
                group = alertRoot.GetComponent<CanvasGroup>();
                text = alertRoot.transform.Find("WrongWayText")?.GetComponent<TMP_Text>();
                arrow = alertRoot.transform.Find("DirectionArrow")?.GetComponent<RectTransform>();
                icon = alertRoot.transform.Find("WarningIcon")?.GetComponent<Image>();
            }

            detector.ResolveReferences();
            detector.ConfigureUI(alertRoot, group, text, arrow, icon);
            alertRoot.SetActive(false);
        }

        private static void SetupPlayerSlipstream(GameObject playerGo, RectTransform hudRoot)
        {
            PoseKartPlayerSlipstream slipstream = playerGo.GetComponent<PoseKartPlayerSlipstream>();
            if (slipstream == null)
            {
                slipstream = playerGo.AddComponent<PoseKartPlayerSlipstream>();
            }

            // 1. Setup HUD Indicator
            Transform existingHud = hudRoot.Find("Slipstream_HUD");
            GameObject hudRootGo;
            CanvasGroup hudGroup;
            TMP_Text label;
            Image chargeBar;

            if (existingHud == null)
            {
                hudRootGo = new GameObject("Slipstream_HUD", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
                hudRootGo.transform.SetParent(hudRoot, false);
                RectTransform rect = hudRootGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 55f);
                rect.sizeDelta = new Vector2(280f, 48f);

                hudGroup = hudRootGo.GetComponent<CanvasGroup>();
                hudGroup.alpha = 0f;
                hudGroup.blocksRaycasts = false;
                hudGroup.interactable = false;

                Image bg = hudRootGo.GetComponent<Image>();
                bg.color = NavyBg;

                Outline outline = hudRootGo.GetComponent<Outline>();
                outline.effectColor = CyanAccent;
                outline.effectDistance = new Vector2(2f, 2f);

                // Label Text
                GameObject labelGo = new GameObject("SlipstreamLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(hudRootGo.transform, false);
                RectTransform labelRect = labelGo.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 0.35f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = new Vector2(10f, 0f);
                labelRect.offsetMax = new Vector2(-10f, 0f);

                label = labelGo.GetComponent<TextMeshProUGUI>();
                label.text = "SLIPSTREAM!";
                label.fontSize = 20;
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.color = CyanAccent;

                // Charge Track & Bar
                GameObject trackGo = new GameObject("ChargeTrack", typeof(RectTransform), typeof(Image));
                trackGo.transform.SetParent(hudRootGo.transform, false);
                RectTransform trackRect = trackGo.GetComponent<RectTransform>();
                trackRect.anchorMin = new Vector2(0.08f, 0.12f);
                trackRect.anchorMax = new Vector2(0.92f, 0.32f);
                trackRect.offsetMin = Vector2.zero;
                trackRect.offsetMax = Vector2.zero;

                Image trackImg = trackGo.GetComponent<Image>();
                trackImg.color = new Color(0.05f, 0.1f, 0.2f, 0.9f);

                GameObject barGo = new GameObject("ChargeFill", typeof(RectTransform), typeof(Image));
                barGo.transform.SetParent(trackGo.transform, false);
                RectTransform barRect = barGo.GetComponent<RectTransform>();
                barRect.anchorMin = Vector2.zero;
                barRect.anchorMax = Vector2.one;
                barRect.offsetMin = Vector2.zero;
                barRect.offsetMax = Vector2.zero;

                chargeBar = barGo.GetComponent<Image>();
                chargeBar.type = Image.Type.Filled;
                chargeBar.fillMethod = Image.FillMethod.Horizontal;
                chargeBar.fillOrigin = (int)Image.OriginHorizontal.Left;
                chargeBar.fillAmount = 0f;
                chargeBar.color = CyanAccent;
            }
            else
            {
                hudRootGo = existingHud.gameObject;
                hudGroup = hudRootGo.GetComponent<CanvasGroup>();
                label = hudRootGo.transform.Find("SlipstreamLabel")?.GetComponent<TMP_Text>();
                chargeBar = hudRootGo.transform.Find("ChargeTrack/ChargeFill")?.GetComponent<Image>();
            }

            // 2. Setup VFX GameObjects on Player Kart
            Transform vfxRoot = playerGo.transform.Find("Slipstream_VFX");
            ParticleSystem leftTrail = null;
            ParticleSystem rightTrail = null;
            ParticleSystem burst = null;

            if (vfxRoot == null)
            {
                GameObject vfxGo = new GameObject("Slipstream_VFX");
                vfxGo.transform.SetParent(playerGo.transform, false);
                vfxGo.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                vfxRoot = vfxGo.transform;

                leftTrail = CreateWindTrailParticle("LeftWindTrail", vfxRoot, new Vector3(-0.85f, 0.15f, -0.6f));
                rightTrail = CreateWindTrailParticle("RightWindTrail", vfxRoot, new Vector3(0.85f, 0.15f, -0.6f));
                burst = CreateAirflowBurstParticle("AirflowBurst", vfxRoot, new Vector3(0f, 0.35f, 0.5f));
            }
            else
            {
                leftTrail = vfxRoot.Find("LeftWindTrail")?.GetComponent<ParticleSystem>();
                rightTrail = vfxRoot.Find("RightWindTrail")?.GetComponent<ParticleSystem>();
                burst = vfxRoot.Find("AirflowBurst")?.GetComponent<ParticleSystem>();
            }

            slipstream.ResolveReferences();
            slipstream.ConfigureUI(hudRootGo, hudGroup, label, chargeBar);
            slipstream.ConfigureVFX(leftTrail, rightTrail, burst);
            hudRootGo.SetActive(false);
        }

        private static ParticleSystem CreateWindTrailParticle(string name, Transform parent, Vector3 localPos)
        {
            GameObject psGo = new GameObject(name, typeof(ParticleSystem));
            psGo.transform.SetParent(parent, false);
            psGo.transform.localPosition = localPos;
            psGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // Stream backwards

            ParticleSystem ps = psGo.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.35f;
            main.startSpeed = 8f;
            main.startSize = 0.18f;
            main.startColor = new Color(0.7f, 0.95f, 1f, 0.45f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 18f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 5f;
            shape.radius = 0.08f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(CyanAccent, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            colorOverLifetime.color = grad;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private static ParticleSystem CreateAirflowBurstParticle(string name, Transform parent, Vector3 localPos)
        {
            GameObject psGo = new GameObject(name, typeof(ParticleSystem));
            psGo.transform.SetParent(parent, false);
            psGo.transform.localPosition = localPos;
            psGo.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);

            ParticleSystem ps = psGo.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.4f;
            main.startSpeed = 12f;
            main.startSize = 0.35f;
            main.startColor = new Color(0.2f, 1f, 0.7f, 0.7f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.enabled = false;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 16) });

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }
    }
}
#endif
