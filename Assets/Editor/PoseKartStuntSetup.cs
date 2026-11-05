using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using PoseKart.Stunts;
using PoseKart.Vehicle;
using PoseKart.Race;
using PoseKart.AI;
using PoseKart.UI;
using PoseKart.Input;

namespace PoseKart.Editor
{
    public static class PoseKartStuntSetup
    {
        [MenuItem("PoseKart/Stunts/Setup All Stunt Features in Scene")]
        public static void SetupAllStuntFeatures()
        {
            var hud = SetupStuntHUD();
            SetupVehicles(hud);
            SetupRamps();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("<color=#00E5FF><b>[PoseKart Stunt System]</b> All stunt features (HUD, Vehicles, Ramps) successfully configured!</color>");
        }

        [MenuItem("PoseKart/Stunts/Convert Selected Object To Barrel Roll Ramp")]
        public static void ConvertSelectedObjectToBarrelRollRamp()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[PoseKart Stunt Setup] Select a ramp GameObject first.");
                return;
            }

            Transform existing = selected.transform.Find("StuntTrigger_BarrelRoll");
            GameObject triggerObject = existing != null ? existing.gameObject : null;
            if (triggerObject == null)
            {
                triggerObject = new GameObject("StuntTrigger_BarrelRoll");
                Undo.RegisterCreatedObjectUndo(triggerObject, "Create Barrel Roll Trigger");
                triggerObject.transform.SetParent(selected.transform, false);
            }

            triggerObject.transform.localPosition = Vector3.zero;
            triggerObject.transform.localRotation = Quaternion.identity;
            triggerObject.transform.localScale = Vector3.one;

            BoxCollider trigger = triggerObject.GetComponent<BoxCollider>();
            if (trigger == null)
                trigger = Undo.AddComponent<BoxCollider>(triggerObject);
            trigger.isTrigger = true;

            // Keep the trigger in world-sized dimensions. Imported ramp prefabs in
            // this scene use a parent scale of 100, so assigning mesh-local bounds
            // directly would create a trigger tens of metres wide.
            Vector3 desiredWorldSize = new Vector3(4f, 3f, 2f);
            Vector3 lossyScale = trigger.transform.lossyScale;
            trigger.center = Vector3.zero;
            trigger.size = new Vector3(
                desiredWorldSize.x / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x)),
                desiredWorldSize.y / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.y)),
                desiredWorldSize.z / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z)));

            PoseKartStuntRamp stuntRamp = triggerObject.GetComponent<PoseKartStuntRamp>();
            if (stuntRamp == null)
                stuntRamp = Undo.AddComponent<PoseKartStuntRamp>(triggerObject);

            SerializedObject serializedRamp = new SerializedObject(stuntRamp);
            serializedRamp.FindProperty("rampType").enumValueIndex = (int)RampType.BarrelRollRamp;
            serializedRamp.FindProperty("minimumForwardSpeed").floatValue = 8f;
            serializedRamp.FindProperty("armWindow").floatValue = 1.4f;
            serializedRamp.FindProperty("rotationDuration").floatValue = 0.82f;
            serializedRamp.FindProperty("clockwise").boolValue = false;
            serializedRamp.FindProperty("applyLaunchImpulse").boolValue = true;
            serializedRamp.FindProperty("upwardVelocityChange").floatValue = 8f;
            serializedRamp.FindProperty("forwardVelocityChange").floatValue = 5f;
            serializedRamp.FindProperty("rewardMultiplier").floatValue = 1.15f;
            serializedRamp.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(triggerObject);
            EditorUtility.SetDirty(stuntRamp);
            if (PrefabUtility.IsPartOfPrefabInstance(selected))
                PrefabUtility.RecordPrefabInstancePropertyModifications(selected);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = triggerObject;
            Debug.Log($"[PoseKart Stunt Setup] '{selected.name}' converted to Barrel Roll ramp with trigger '{triggerObject.name}'.");
        }

        [MenuItem("PoseKart/Stunts/Repair Selected Barrel Roll Exit Trigger")]
        public static void RepairSelectedBarrelRollExitTrigger()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[PoseKart Stunt Setup] Select BarrelRollExitTrigger first.");
                return;
            }

            Transform rampRoot = selected.transform.parent;
            if (rampRoot == null)
            {
                Debug.LogWarning("[PoseKart Stunt Setup] The selected trigger must be a child of the ramp mesh.");
                return;
            }

            Undo.RecordObject(selected.transform, "Repair Barrel Roll Exit Trigger Transform");

            Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);
            bool hasBounds = false;
            Renderer[] renderers = rampRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || renderer.transform == selected.transform || renderer.transform.IsChildOf(selected.transform))
                    continue;

                Bounds worldBounds = renderer.bounds;
                Vector3 min = worldBounds.min;
                Vector3 max = worldBounds.max;
                Vector3[] corners =
                {
                    new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                    new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z)
                };

                foreach (Vector3 corner in corners)
                {
                    Vector3 localCorner = rampRoot.InverseTransformPoint(corner);
                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }

            if (hasBounds)
            {
                float forwardSign = selected.transform.localPosition.z >= localBounds.center.z ? 1f : -1f;
                Vector3 localPosition = selected.transform.localPosition;
                localPosition.z = forwardSign > 0f
                    ? localBounds.max.z + 0.5f
                    : localBounds.min.z - 0.5f;
                localPosition.x = Mathf.Clamp(localPosition.x, localBounds.min.x, localBounds.max.x);
                selected.transform.localPosition = localPosition;
            }

            BoxCollider trigger = selected.GetComponent<BoxCollider>();
            if (trigger == null)
                trigger = Undo.AddComponent<BoxCollider>(selected);
            Undo.RecordObject(trigger, "Resize Barrel Roll Exit Trigger Collider");
            trigger.isTrigger = true;
            trigger.center = Vector3.zero;
            float rampWidth = hasBounds ? localBounds.size.x : 4f;
            float rampHeight = hasBounds ? localBounds.size.y : 3f;
            trigger.size = new Vector3(
                Mathf.Clamp(rampWidth * 0.8f, 3f, 8f),
                Mathf.Clamp(rampHeight + 1f, 2.5f, 8f),
                2f);

            PoseKartStuntRamp stuntRamp = selected.GetComponent<PoseKartStuntRamp>();
            if (stuntRamp == null)
                stuntRamp = Undo.AddComponent<PoseKartStuntRamp>(selected);

            SerializedObject serializedRamp = new SerializedObject(stuntRamp);
            serializedRamp.FindProperty("rampType").enumValueIndex = (int)RampType.BarrelRollRamp;
            serializedRamp.FindProperty("minimumForwardSpeed").floatValue = 8f;
            serializedRamp.FindProperty("armWindow").floatValue = 1.2f;
            serializedRamp.FindProperty("rotationDuration").floatValue = 0.82f;
            serializedRamp.FindProperty("clockwise").boolValue = false;
            serializedRamp.FindProperty("applyLaunchImpulse").boolValue = true;
            serializedRamp.FindProperty("upwardVelocityChange").floatValue = 8f;
            serializedRamp.FindProperty("forwardVelocityChange").floatValue = 5f;
            serializedRamp.FindProperty("rewardMultiplier").floatValue = 1.15f;
            serializedRamp.ApplyModifiedPropertiesWithoutUndo();

            PoseKartBarrelRollRamp legacyRamp = selected.GetComponent<PoseKartBarrelRollRamp>();
            if (legacyRamp != null)
            {
                Undo.DestroyObjectImmediate(legacyRamp);
            }

            EditorUtility.SetDirty(selected);
            EditorUtility.SetDirty(trigger);
            EditorUtility.SetDirty(stuntRamp);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = selected;

            Debug.Log($"[PoseKart Stunt Setup] Repaired '{selected.name}': unified Barrel Roll trigger at local Z {selected.transform.localPosition.z:0.00}, collider {trigger.size}.");
        }

        [MenuItem("PoseKart/Stunts/Fix Selected Stunt Trigger Collider Scale")]
        public static void FixSelectedStuntTriggerColliderScale()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[PoseKart Stunt Setup] Select a stunt trigger first.");
                return;
            }

            BoxCollider trigger = selected.GetComponent<BoxCollider>();
            if (trigger == null)
            {
                Debug.LogWarning($"[PoseKart Stunt Setup] '{selected.name}' has no BoxCollider.");
                return;
            }

            // Work in world units so imported ramps with a large parent scale do not
            // accidentally create a trigger covering half the track.
            Vector3 desiredWorldSize = new Vector3(4f, 3f, 2f);
            Vector3 lossyScale = trigger.transform.lossyScale;
            Vector3 localSize = new Vector3(
                desiredWorldSize.x / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x)),
                desiredWorldSize.y / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.y)),
                desiredWorldSize.z / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z)));

            Undo.RecordObject(trigger, "Fix Stunt Trigger Collider Scale");
            trigger.center = Vector3.zero;
            trigger.size = localSize;
            trigger.isTrigger = true;

            EditorUtility.SetDirty(trigger);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[PoseKart Stunt Setup] Normalized '{selected.name}' collider to world size {desiredWorldSize} (local size {localSize}).");
        }

        [MenuItem("PoseKart/Stunts/Fix All Barrel Roll Trigger Collider Scales")]
        public static void FixAllBarrelRollTriggerColliderScales()
        {
            const float targetWidth = 4f;
            const float targetHeight = 3f;
            const float targetDepth = 2f;
            int fixedCount = 0;

            PoseKartStuntRamp[] ramps = Object.FindObjectsByType<PoseKartStuntRamp>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (PoseKartStuntRamp ramp in ramps)
            {
                if (ramp == null || ramp.Type != RampType.BarrelRollRamp)
                    continue;

                BoxCollider trigger = ramp.GetComponent<BoxCollider>();
                if (trigger == null)
                    continue;

                Vector3 lossyScale = trigger.transform.lossyScale;
                Undo.RecordObject(trigger, "Fix All Barrel Roll Trigger Collider Scales");
                trigger.center = Vector3.zero;
                trigger.size = new Vector3(
                    targetWidth / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x)),
                    targetHeight / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.y)),
                    targetDepth / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z)));
                trigger.isTrigger = true;
                EditorUtility.SetDirty(trigger);
                fixedCount++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[PoseKart Stunt Setup] Normalized {fixedCount} Barrel Roll trigger collider(s) to world size 4 x 3 x 2 m.");
        }

        [MenuItem("PoseKart/Stunts/Setup Stunt HUD in Canvas")]
        public static PoseKartStuntHUD SetupStuntHUD()
        {
            Canvas canvas = null;
            var arcadeHud = Object.FindAnyObjectByType<PoseKartArcadeHUD>();
            if (arcadeHud != null)
            {
                canvas = arcadeHud.GetComponent<Canvas>();
                if (canvas == null) canvas = arcadeHud.GetComponentInParent<Canvas>();
            }

            if (canvas == null)
            {
                Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (var c in canvases)
                {
                    if (c.name.Contains("RaceHUD") || c.name.Contains("ArcadeHUD") || c.isRootCanvas)
                    {
                        canvas = c;
                        break;
                    }
                }
            }

            if (canvas == null)
            {
                Debug.LogWarning("[PoseKart Stunt Setup] No Canvas found in scene! Creating Canvas (ArcadeHUD_Root).");
                GameObject canvasGo = new GameObject("Canvas (ArcadeHUD_Root)");
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");
            }

            Transform hudParent = canvas.transform;

            Transform existingHud = hudParent.Find("StuntHUD");
            GameObject hudGo;
            if (existingHud != null)
            {
                hudGo = existingHud.gameObject;
            }
            else
            {
                hudGo = new GameObject("StuntHUD", typeof(RectTransform), typeof(CanvasGroup), typeof(PoseKartStuntHUD));
                hudGo.transform.SetParent(hudParent, false);
                Undo.RegisterCreatedObjectUndo(hudGo, "Create StuntHUD");
            }

            RectTransform hudRect = hudGo.GetComponent<RectTransform>();
            hudRect.anchorMin = new Vector2(0.5f, 0.72f);
            hudRect.anchorMax = new Vector2(0.5f, 0.72f);
            hudRect.pivot = new Vector2(0.5f, 0.5f);
            hudRect.anchoredPosition = Vector2.zero;
            hudRect.sizeDelta = new Vector2(700f, 150f);

            CanvasGroup group = hudGo.GetComponent<CanvasGroup>();
            if (group == null) group = hudGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            PoseKartStuntHUD stuntHud = hudGo.GetComponent<PoseKartStuntHUD>();
            if (stuntHud == null) stuntHud = hudGo.AddComponent<PoseKartStuntHUD>();

            // Punch transform container
            Transform punchTr = hudGo.transform.Find("PunchContent");
            GameObject punchGo;
            if (punchTr != null)
            {
                punchGo = punchTr.gameObject;
            }
            else
            {
                punchGo = new GameObject("PunchContent", typeof(RectTransform));
                punchGo.transform.SetParent(hudGo.transform, false);
                Undo.RegisterCreatedObjectUndo(punchGo, "Create PunchContent");
            }
            RectTransform punchRect = punchGo.GetComponent<RectTransform>();
            punchRect.anchorMin = Vector2.zero;
            punchRect.anchorMax = Vector2.one;
            punchRect.offsetMin = Vector2.zero;
            punchRect.offsetMax = Vector2.zero;

            // Stunt Name Text
            Transform nameTr = punchGo.transform.Find("StuntNameText");
            GameObject nameGo = nameTr != null ? nameTr.gameObject : new GameObject("StuntNameText", typeof(RectTransform), typeof(TextMeshProUGUI));
            if (nameTr == null)
            {
                nameGo.transform.SetParent(punchGo.transform, false);
                Undo.RegisterCreatedObjectUndo(nameGo, "Create StuntNameText");
            }
            RectTransform nameRect = nameGo.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.45f);
            nameRect.anchorMax = new Vector2(0.7f, 1f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            TextMeshProUGUI nameTmp = nameGo.GetComponent<TextMeshProUGUI>();
            nameTmp.text = "FLAT SPIN";
            nameTmp.fontSize = 42f;
            nameTmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
            nameTmp.alignment = TextAlignmentOptions.Right;
            nameTmp.color = new Color(0f, 0.95f, 1f, 1f);

            // Revolution Count Text
            Transform revTr = punchGo.transform.Find("RevolutionCountText");
            GameObject revGo = revTr != null ? revTr.gameObject : new GameObject("RevolutionCountText", typeof(RectTransform), typeof(TextMeshProUGUI));
            if (revTr == null)
            {
                revGo.transform.SetParent(punchGo.transform, false);
                Undo.RegisterCreatedObjectUndo(revGo, "Create RevolutionCountText");
            }
            RectTransform revRect = revGo.GetComponent<RectTransform>();
            revRect.anchorMin = new Vector2(0.72f, 0.45f);
            revRect.anchorMax = new Vector2(1f, 1f);
            revRect.offsetMin = Vector2.zero;
            revRect.offsetMax = Vector2.zero;
            TextMeshProUGUI revTmp = revGo.GetComponent<TextMeshProUGUI>();
            revTmp.text = "x1";
            revTmp.fontSize = 44f;
            revTmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
            revTmp.alignment = TextAlignmentOptions.Left;
            revTmp.color = Color.white;

            // Nitro Reward Text
            Transform rewardTr = punchGo.transform.Find("NitroRewardText");
            GameObject rewardGo = rewardTr != null ? rewardTr.gameObject : new GameObject("NitroRewardText", typeof(RectTransform), typeof(TextMeshProUGUI));
            if (rewardTr == null)
            {
                rewardGo.transform.SetParent(punchGo.transform, false);
                Undo.RegisterCreatedObjectUndo(rewardGo, "Create NitroRewardText");
            }
            RectTransform rewardRect = rewardGo.GetComponent<RectTransform>();
            rewardRect.anchorMin = new Vector2(0f, 0f);
            rewardRect.anchorMax = new Vector2(1f, 0.45f);
            rewardRect.offsetMin = Vector2.zero;
            rewardRect.offsetMax = Vector2.zero;
            TextMeshProUGUI rewardTmp = rewardGo.GetComponent<TextMeshProUGUI>();
            rewardTmp.text = "CLEAN LANDING! +15% NITRO";
            rewardTmp.fontSize = 24f;
            rewardTmp.fontStyle = FontStyles.Bold;
            rewardTmp.alignment = TextAlignmentOptions.Center;
            rewardTmp.color = new Color(0.1f, 1f, 0.5f, 1f);

            // Combo Text
            Transform comboTr = punchGo.transform.Find("ComboText");
            GameObject comboGo = comboTr != null ? comboTr.gameObject : new GameObject("ComboText", typeof(RectTransform), typeof(TextMeshProUGUI));
            if (comboTr == null)
            {
                comboGo.transform.SetParent(punchGo.transform, false);
                Undo.RegisterCreatedObjectUndo(comboGo, "Create ComboText");
            }
            RectTransform comboRect = comboGo.GetComponent<RectTransform>();
            comboRect.anchorMin = new Vector2(0f, 0.95f);
            comboRect.anchorMax = new Vector2(1f, 1.25f);
            comboRect.offsetMin = Vector2.zero;
            comboRect.offsetMax = Vector2.zero;
            TextMeshProUGUI comboTmp = comboGo.GetComponent<TextMeshProUGUI>();
            comboTmp.text = "";
            comboTmp.fontSize = 20f;
            comboTmp.fontStyle = FontStyles.Bold;
            comboTmp.alignment = TextAlignmentOptions.Center;
            comboTmp.color = new Color(1f, 0.8f, 0f, 1f);

            // Configure HUD component
            stuntHud.ConfigureUI(nameTmp, revTmp, rewardTmp, comboTmp, group, punchRect);

            EditorUtility.SetDirty(stuntHud);
            EditorUtility.SetDirty(hudGo);
            Debug.Log("[PoseKart Stunt Setup] Configured StuntHUD under " + canvas.name);
            return stuntHud;
        }

        [MenuItem("PoseKart/Stunts/Setup Stunt Controllers on Vehicles")]
        public static void SetupVehiclesMenu()
        {
            var hud = Object.FindAnyObjectByType<PoseKartStuntHUD>();
            if (hud == null) hud = SetupStuntHUD();
            SetupVehicles(hud);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        public static void SetupVehicles(PoseKartStuntHUD hud)
        {
            CarController[] cars = Object.FindObjectsByType<CarController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            PoseKartStuntController playerStunt = null;

            foreach (var car in cars)
            {
                Undo.RecordObject(car.gameObject, "Setup Stunt Controller");
                var stunt = car.GetComponent<PoseKartStuntController>();
                if (stunt == null)
                {
                    stunt = car.gameObject.AddComponent<PoseKartStuntController>();
                }

                SerializedObject so = new SerializedObject(stunt);

                // Find or assign pivot
                Transform pivot = car.transform.Find("StuntPivot");
                if (pivot == null) pivot = car.transform.Find("Visual");
                if (pivot == null) pivot = car.transform.Find("VisualPivot");

                if (pivot != null)
                {
                    so.FindProperty("visualPivot").objectReferenceValue = pivot;
                }

                so.FindProperty("car").objectReferenceValue = car;
                so.FindProperty("body").objectReferenceValue = car.GetComponent<Rigidbody>();

                var cam = Object.FindAnyObjectByType<PoseKartCinematicCamera>();
                if (cam != null)
                {
                    so.FindProperty("cinematicCamera").objectReferenceValue = cam;
                }

                var participant = car.GetComponent<RaceParticipant>();
                bool isPlayer = (participant != null && participant.IsPlayer) || (car.InputProviderSource is KeyboardInputProvider);
                if (isPlayer)
                {
                    playerStunt = stunt;
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(stunt);
                Debug.Log($"[PoseKart Stunt Setup] Vehicle '{car.name}' configured with StuntController (VisualPivot: {(pivot != null ? pivot.name : "null")}, isPlayer: {isPlayer})");
            }

            if (hud != null && playerStunt != null)
            {
                SerializedObject hudSo = new SerializedObject(hud);
                hudSo.FindProperty("playerStuntController").objectReferenceValue = playerStunt;
                hudSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(hud);
            }
        }

        [MenuItem("PoseKart/Stunts/Configure Track Ramps")]
        public static void SetupRamps()
        {
            int configuredCount = 0;
            // 1. Look for existing BarrelRoll ramps / triggers
            var barrelTriggers = Object.FindObjectsByType<PoseKartBarrelRollRamp>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var rampTrigger in barrelTriggers)
            {
                Undo.RecordObject(rampTrigger.gameObject, "Configure Stunt Ramp");
                var stuntRamp = rampTrigger.GetComponent<PoseKartStuntRamp>();
                if (stuntRamp == null) stuntRamp = rampTrigger.gameObject.AddComponent<PoseKartStuntRamp>();

                SerializedObject so = new SerializedObject(stuntRamp);
                so.FindProperty("rampType").enumValueIndex = (int)RampType.BarrelRollRamp;
                so.FindProperty("upwardVelocityChange").floatValue = 8f;
                so.FindProperty("forwardVelocityChange").floatValue = 6f;
                so.FindProperty("applyLaunchImpulse").boolValue = true;
                so.FindProperty("rotationDuration").floatValue = 0.85f;
                so.FindProperty("clockwise").boolValue = false; // counter-clockwise corkscrew
                so.ApplyModifiedProperties();

                EditorUtility.SetDirty(stuntRamp);
                configuredCount++;
                Debug.Log($"[PoseKart Stunt Setup] BarrelRoll ramp trigger configured on '{rampTrigger.name}'");
            }

            // 2. Look for stunt takeoff triggers or boost pads configured as stunt ramps
            var boostPads = Object.FindObjectsByType<KartBoostPad>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var pad in boostPads)
            {
                if (pad.name.IndexOf("Stunt", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pad.name.IndexOf("Ramp", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pad.name.IndexOf("Jump", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Undo.RecordObject(pad.gameObject, "Configure Stunt Ramp");
                    var stuntRamp = pad.GetComponent<PoseKartStuntRamp>();
                    if (stuntRamp == null) stuntRamp = pad.gameObject.AddComponent<PoseKartStuntRamp>();

                    SerializedObject so = new SerializedObject(stuntRamp);
                    so.FindProperty("rampType").enumValueIndex = (int)RampType.NormalRamp;
                    so.FindProperty("upwardVelocityChange").floatValue = 9f;
                    so.FindProperty("forwardVelocityChange").floatValue = 6f;
                    so.FindProperty("applyLaunchImpulse").boolValue = true;
                    so.FindProperty("rotationDuration").floatValue = 0.75f;
                    so.ApplyModifiedProperties();

                    EditorUtility.SetDirty(stuntRamp);
                    configuredCount++;
                    Debug.Log($"[PoseKart Stunt Setup] Jump ramp trigger configured on '{pad.name}'");
                }
            }

            Debug.Log($"[PoseKart Stunt Setup] Configured {configuredCount} stunt ramps on the track.");
        }
    }
}
