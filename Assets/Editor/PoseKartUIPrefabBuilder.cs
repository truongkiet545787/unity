#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using PoseKart.Race;

namespace PoseKart.EditorTools
{
    public static class PoseKartUIPrefabBuilder
    {
        [MenuItem("PoseKart/Build Dedicated UI Prefabs")]
        public static void BuildAllUIPrefabs()
        {
            string dir = "Assets/Prefabs/UI";
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
            }

            BuildShieldBlockedUIPrefab(dir + "/PoseKart_ShieldBlocked_UI.prefab");
            BuildMissileWarningUIPrefab(dir + "/PoseKart_MissileWarning_UI.prefab");
            BuildRaceFinishUIPrefab(dir + "/PoseKart_RaceFinish_UI.prefab");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=#00FF88><b>[PoseKart]</b> Created all 3 dedicated UI Prefabs in Assets/Prefabs/UI/!</color>");
        }

        private static void BuildShieldBlockedUIPrefab(string path)
        {
            GameObject root = new GameObject("PoseKart_ShieldBlocked_UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PoseKart.Race.PoseKartShieldBlockedUI));
            
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2250;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject banner = new GameObject("Blocked_Banner", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
            banner.transform.SetParent(root.transform, false);
            RectTransform bannerRect = banner.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
            bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.sizeDelta = new Vector2(360f, 68f);
            bannerRect.anchoredPosition = new Vector2(0f, 140f);

            CanvasGroup group = banner.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = false;
            group.interactable = false;

            Image bg = banner.GetComponent<Image>();
            bg.color = new Color(0.02f, 0.05f, 0.12f, 0.92f);
            bg.raycastTarget = false;

            Outline outline = banner.GetComponent<Outline>();
            outline.effectColor = new Color(0.18f, 0.92f, 1f, 0.95f);
            outline.effectDistance = new Vector2(2.5f, 2.5f);

            GameObject textObj = new GameObject("Blocked_Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(banner.transform, false);
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.text = "BLOCKED!";
            tmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
            tmp.fontSize = 34f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1f, 0.92f, 0.25f, 1f);
            tmp.raycastTarget = false;

            SerializedObject so = new SerializedObject(root.GetComponent<PoseKart.Race.PoseKartShieldBlockedUI>());
            so.FindProperty("bannerGroup").objectReferenceValue = group;
            so.FindProperty("bannerRect").objectReferenceValue = bannerRect;
            so.FindProperty("bannerText").objectReferenceValue = tmp;
            so.FindProperty("bannerBg").objectReferenceValue = bg;
            so.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static void BuildMissileWarningUIPrefab(string path)
        {
            GameObject root = new GameObject("PoseKart_MissileWarning_UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PoseKart.Race.PoseKartMissileWarningSystem));
            
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2200;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject banner = new GameObject("Warning_Banner", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
            banner.transform.SetParent(root.transform, false);
            RectTransform bannerRect = banner.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 0.82f);
            bannerRect.anchorMax = new Vector2(0.5f, 0.82f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.sizeDelta = new Vector2(460f, 68f);
            bannerRect.anchoredPosition = Vector2.zero;

            CanvasGroup group = banner.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = false;
            group.interactable = false;

            Image border = banner.GetComponent<Image>();
            border.color = new Color(0.65f, 0.04f, 0.04f, 0.88f);
            border.raycastTarget = false;

            Outline outline = banner.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 0.18f, 0.18f, 0.95f);
            outline.effectDistance = new Vector2(3f, 3f);

            GameObject titleObj = new GameObject("Warning_Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleObj.transform.SetParent(banner.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.04f, 0.1f);
            titleRect.anchorMax = new Vector2(0.72f, 0.9f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            TextMeshProUGUI titleTMP = titleObj.GetComponent<TextMeshProUGUI>();
            titleTMP.text = "MISSILE LOCK-ON!";
            titleTMP.fontStyle = FontStyles.Bold;
            titleTMP.fontSize = 26f;
            titleTMP.alignment = TextAlignmentOptions.MidlineLeft;
            titleTMP.color = Color.white;
            titleTMP.raycastTarget = false;

            GameObject distObj = new GameObject("Distance_Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            distObj.transform.SetParent(banner.transform, false);
            RectTransform distRect = distObj.GetComponent<RectTransform>();
            distRect.anchorMin = new Vector2(0.72f, 0.1f);
            distRect.anchorMax = new Vector2(0.96f, 0.9f);
            distRect.offsetMin = Vector2.zero;
            distRect.offsetMax = Vector2.zero;

            TextMeshProUGUI distTMP = distObj.GetComponent<TextMeshProUGUI>();
            distTMP.text = "35m";
            distTMP.fontStyle = FontStyles.Bold;
            distTMP.fontSize = 28f;
            distTMP.alignment = TextAlignmentOptions.MidlineRight;
            distTMP.color = new Color(1f, 0.92f, 0.2f, 1f);
            distTMP.raycastTarget = false;

            SerializedObject so = new SerializedObject(root.GetComponent<PoseKart.Race.PoseKartMissileWarningSystem>());
            so.FindProperty("warningGroup").objectReferenceValue = group;
            so.FindProperty("warningBox").objectReferenceValue = bannerRect;
            so.FindProperty("warningLabel").objectReferenceValue = titleTMP;
            so.FindProperty("distanceLabel").objectReferenceValue = distTMP;
            so.FindProperty("dangerBorder").objectReferenceValue = border;
            so.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static void BuildRaceFinishUIPrefab(string path)
        {
            if (System.IO.File.Exists(path))
            {
                Debug.Log($"<color=#00FF88><b>[PoseKart]</b> Preserving existing custom Finish UI prefab at {path}!</color>");
                return;
            }

            GameObject root = new GameObject("PoseKart_RaceFinish_UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PoseKart.UI.PoseKartRaceFinishUI));
            
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2500;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject container = new GameObject("RaceFinish_Container", typeof(RectTransform), typeof(Image));
            container.transform.SetParent(root.transform, false);
            RectTransform contRect = container.GetComponent<RectTransform>();
            contRect.anchorMin = Vector2.zero;
            contRect.anchorMax = Vector2.one;
            contRect.offsetMin = Vector2.zero;
            contRect.offsetMax = Vector2.zero;

            Image overlay = container.GetComponent<Image>();
            overlay.color = new Color(0.005f, 0.018f, 0.05f, 0.78f);
            overlay.raycastTarget = true;

            // 1. Finish Animation Layer (chữ FINISH to)
            GameObject animLayer = new GameObject("FinishAnimation", typeof(RectTransform), typeof(CanvasGroup));
            animLayer.transform.SetParent(container.transform, false);
            RectTransform animRect = animLayer.GetComponent<RectTransform>();
            animRect.anchorMin = new Vector2(0.5f, 0.5f);
            animRect.anchorMax = new Vector2(0.5f, 0.5f);
            animRect.pivot = new Vector2(0.5f, 0.5f);
            animRect.sizeDelta = new Vector2(1100f, 260f);
            animRect.anchoredPosition = new Vector2(0f, 55f);

            CanvasGroup animGroup = animLayer.GetComponent<CanvasGroup>();
            animGroup.alpha = 1f;

            GameObject finishTextObj = new GameObject("FinishText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Outline));
            finishTextObj.transform.SetParent(animLayer.transform, false);
            RectTransform ftRect = finishTextObj.GetComponent<RectTransform>();
            ftRect.anchorMin = Vector2.zero;
            ftRect.anchorMax = Vector2.one;
            ftRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI ftTMP = finishTextObj.GetComponent<TextMeshProUGUI>();
            ftTMP.text = "FINISH!";
            ftTMP.fontStyle = FontStyles.Bold | FontStyles.Italic;
            ftTMP.fontSize = 116f;
            ftTMP.alignment = TextAlignmentOptions.Center;
            ftTMP.color = new Color(1f, 0.82f, 0.22f, 1f);
            ftTMP.raycastTarget = false;

            Outline ftOutline = finishTextObj.GetComponent<Outline>();
            ftOutline.effectColor = new Color(0.015f, 0.055f, 0.12f, 0.96f);
            ftOutline.effectDistance = new Vector2(4f, -4f);

            // 2. Results Panel Layer
            GameObject resLayer = new GameObject("ResultsPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
            resLayer.transform.SetParent(container.transform, false);
            RectTransform resRect = resLayer.GetComponent<RectTransform>();
            resRect.anchorMin = new Vector2(0.5f, 0.5f);
            resRect.anchorMax = new Vector2(0.5f, 0.5f);
            resRect.pivot = new Vector2(0.5f, 0.5f);
            resRect.sizeDelta = new Vector2(760f, 680f);
            resRect.anchoredPosition = Vector2.zero;

            CanvasGroup resGroup = resLayer.GetComponent<CanvasGroup>();
            resGroup.alpha = 1f;

            Image resBg = resLayer.GetComponent<Image>();
            resBg.color = new Color(0.015f, 0.055f, 0.12f, 0.94f);
            resBg.raycastTarget = true;

            Outline resOutline = resLayer.GetComponent<Outline>();
            resOutline.effectColor = new Color(0.12f, 0.92f, 0.92f, 0.85f);
            resOutline.effectDistance = new Vector2(2f, 2f);

            // Heading
            GameObject headingObj = new GameObject("Heading", typeof(RectTransform), typeof(TextMeshProUGUI));
            headingObj.transform.SetParent(resLayer.transform, false);
            RectTransform hRect = headingObj.GetComponent<RectTransform>();
            hRect.anchorMin = new Vector2(0.5f, 1f);
            hRect.anchorMax = new Vector2(0.5f, 1f);
            hRect.pivot = new Vector2(0.5f, 1f);
            hRect.anchoredPosition = new Vector2(0f, -40f);
            hRect.sizeDelta = new Vector2(680f, 64f);

            TextMeshProUGUI hTMP = headingObj.GetComponent<TextMeshProUGUI>();
            hTMP.text = "RACE COMPLETE";
            hTMP.fontStyle = FontStyles.Bold;
            hTMP.fontSize = 34f;
            hTMP.alignment = TextAlignmentOptions.Center;
            hTMP.color = Color.white;
            hTMP.raycastTarget = false;

            // Caption
            GameObject capObj = new GameObject("PositionCaption", typeof(RectTransform), typeof(TextMeshProUGUI));
            capObj.transform.SetParent(resLayer.transform, false);
            RectTransform capRect = capObj.GetComponent<RectTransform>();
            capRect.anchorMin = new Vector2(0.5f, 1f);
            capRect.anchorMax = new Vector2(0.5f, 1f);
            capRect.pivot = new Vector2(0.5f, 1f);
            capRect.anchoredPosition = new Vector2(0f, -115f);
            capRect.sizeDelta = new Vector2(680f, 36f);

            TextMeshProUGUI capTMP = capObj.GetComponent<TextMeshProUGUI>();
            capTMP.text = "FINISH POSITION";
            capTMP.fontStyle = FontStyles.Bold;
            capTMP.fontSize = 20f;
            capTMP.alignment = TextAlignmentOptions.Center;
            capTMP.color = new Color(0.12f, 0.92f, 0.92f, 1f);
            capTMP.raycastTarget = false;

            // Position Value
            GameObject posValObj = new GameObject("PositionValue", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Outline));
            posValObj.transform.SetParent(resLayer.transform, false);
            RectTransform pvRect = posValObj.GetComponent<RectTransform>();
            pvRect.anchorMin = new Vector2(0.5f, 1f);
            pvRect.anchorMax = new Vector2(0.5f, 1f);
            pvRect.pivot = new Vector2(0.5f, 1f);
            pvRect.anchoredPosition = new Vector2(0f, -160f);
            pvRect.sizeDelta = new Vector2(680f, 150f);

            TextMeshProUGUI pvTMP = posValObj.GetComponent<TextMeshProUGUI>();
            pvTMP.text = "1ST";
            pvTMP.fontStyle = FontStyles.Bold | FontStyles.Italic;
            pvTMP.fontSize = 112f;
            pvTMP.alignment = TextAlignmentOptions.Center;
            pvTMP.color = new Color(1f, 0.82f, 0.22f, 1f);
            pvTMP.raycastTarget = false;

            Outline pvOutline = posValObj.GetComponent<Outline>();
            pvOutline.effectColor = new Color(0.015f, 0.055f, 0.12f, 0.96f);
            pvOutline.effectDistance = new Vector2(4f, -4f);

            // Total Time
            GameObject timeObj = new GameObject("TotalTime", typeof(RectTransform), typeof(TextMeshProUGUI));
            timeObj.transform.SetParent(resLayer.transform, false);
            RectTransform timeRect = timeObj.GetComponent<RectTransform>();
            timeRect.anchorMin = new Vector2(0.5f, 1f);
            timeRect.anchorMax = new Vector2(0.5f, 1f);
            timeRect.pivot = new Vector2(0.5f, 1f);
            timeRect.anchoredPosition = new Vector2(-150f, -340f);
            timeRect.sizeDelta = new Vector2(300f, 75f);

            TextMeshProUGUI timeTMP = timeObj.GetComponent<TextMeshProUGUI>();
            timeTMP.text = "TOTAL TIME\n00:00.000";
            timeTMP.fontStyle = FontStyles.Bold;
            timeTMP.fontSize = 24f;
            timeTMP.alignment = TextAlignmentOptions.Center;
            timeTMP.color = Color.white;
            timeTMP.raycastTarget = false;

            // Laps
            GameObject lapsObj = new GameObject("Laps", typeof(RectTransform), typeof(TextMeshProUGUI));
            lapsObj.transform.SetParent(resLayer.transform, false);
            RectTransform lapsRect = lapsObj.GetComponent<RectTransform>();
            lapsRect.anchorMin = new Vector2(0.5f, 1f);
            lapsRect.anchorMax = new Vector2(0.5f, 1f);
            lapsRect.pivot = new Vector2(0.5f, 1f);
            lapsRect.anchoredPosition = new Vector2(150f, -340f);
            lapsRect.sizeDelta = new Vector2(300f, 75f);

            TextMeshProUGUI lapsTMP = lapsObj.GetComponent<TextMeshProUGUI>();
            lapsTMP.text = "LAPS\n1 / 1";
            lapsTMP.fontStyle = FontStyles.Bold;
            lapsTMP.fontSize = 24f;
            lapsTMP.alignment = TextAlignmentOptions.Center;
            lapsTMP.color = Color.white;
            lapsTMP.raycastTarget = false;

            // Result Hint
            GameObject hintObj = new GameObject("ResultHint", typeof(RectTransform), typeof(TextMeshProUGUI));
            hintObj.transform.SetParent(resLayer.transform, false);
            RectTransform hintRect = hintObj.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 1f);
            hintRect.anchorMax = new Vector2(0.5f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.anchoredPosition = new Vector2(0f, -440f);
            hintRect.sizeDelta = new Vector2(680f, 32f);

            TextMeshProUGUI hintTMP = hintObj.GetComponent<TextMeshProUGUI>();
            hintTMP.text = "OFFICIAL RESULT";
            hintTMP.fontStyle = FontStyles.Normal;
            hintTMP.fontSize = 16f;
            hintTMP.alignment = TextAlignmentOptions.Center;
            hintTMP.color = new Color(0.12f, 0.92f, 0.92f, 1f);
            hintTMP.raycastTarget = false;

            // Buttons
            Button playAgainBtn = CreateButton("PlayAgain_Button", "PLAY AGAIN", resLayer.transform, new Vector2(-150f, -540f));
            Button selectVehicleBtn = CreateButton("SelectVehicle_Button", "SELECT VEHICLE", resLayer.transform, new Vector2(150f, -540f));

            SerializedObject so = new SerializedObject(root.GetComponent<PoseKart.UI.PoseKartRaceFinishUI>());
            so.FindProperty("useCustomUI").boolValue = true;
            so.FindProperty("customFinishRoot").objectReferenceValue = container;
            so.FindProperty("customFinishGroup").objectReferenceValue = animGroup;
            so.FindProperty("customFinishRect").objectReferenceValue = animRect;
            so.FindProperty("customFinishText").objectReferenceValue = ftTMP;
            so.FindProperty("customResultsGroup").objectReferenceValue = resGroup;
            so.FindProperty("customResultsRect").objectReferenceValue = resRect;
            so.FindProperty("customResultHeading").objectReferenceValue = hTMP;
            so.FindProperty("customPositionText").objectReferenceValue = pvTMP;
            so.FindProperty("customPositionCaption").objectReferenceValue = capTMP;
            so.FindProperty("customTimeText").objectReferenceValue = timeTMP;
            so.FindProperty("customLapsText").objectReferenceValue = lapsTMP;
            so.FindProperty("customResultHint").objectReferenceValue = hintTMP;
            so.FindProperty("customPlayAgainButton").objectReferenceValue = playAgainBtn;
            so.FindProperty("customSelectVehicleButton").objectReferenceValue = selectVehicleBtn;
            so.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static Button CreateButton(string name, string label, Transform parent, Vector2 pos)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            obj.transform.SetParent(parent, false);

            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(260f, 70f);
            rect.anchoredPosition = pos;

            Image img = obj.GetComponent<Image>();
            img.color = new Color(0.12f, 0.92f, 0.92f, 0.22f);

            Outline outline = obj.GetComponent<Outline>();
            outline.effectColor = new Color(0.12f, 0.92f, 0.92f, 0.9f);
            outline.effectDistance = new Vector2(2f, 2f);

            Button btn = obj.GetComponent<Button>();
            btn.targetGraphic = img;

            GameObject textObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(obj.transform, false);
            RectTransform tRect = textObj.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontStyle = FontStyles.Bold;
            tmp.fontSize = 20f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            return btn;
        }

        [MenuItem("PoseKart/Install UI Directly Into RaceHUD")]
        public static void InstallUIIntoCurrentAndAllScenes()
        {
            BuildAllUIPrefabs();

            string[] targetScenes = new string[]
            {
                "Assets/Scenes/PoseKart_FinalIsland.unity",
                "Assets/Scenes/PoseKart_ReferenceIsland.unity",
                "Assets/Scenes/CHAOTIC_POSEKART.unity"
            };

            foreach (string scenePath in targetScenes)
            {
                if (System.IO.File.Exists(scenePath))
                {
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
                    GameObject hud = GameObject.Find("RaceHUD");
                    if (hud != null)
                    {
                        InstallDirectlyIntoRaceHUD(hud);
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                        Debug.Log($"<color=#00FF88><b>[PoseKart]</b> Successfully installed UI hierarchy directly under RaceHUD in {scenePath}!</color>");
                    }
                }
            }
        }

        public static void InstallDirectlyIntoRaceHUD(GameObject raceHud)
        {
            if (raceHud == null) return;

            string dir = "Assets/Prefabs/UI";
            string shieldPrefabPath = dir + "/PoseKart_ShieldBlocked_UI.prefab";
            string missilePrefabPath = dir + "/PoseKart_MissileWarning_UI.prefab";
            string finishPrefabPath = dir + "/PoseKart_RaceFinish_UI.prefab";

            GameObject shieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(shieldPrefabPath);
            GameObject missilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(missilePrefabPath);
            GameObject finishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(finishPrefabPath);

            // 1. Missile Warning UI
            Transform existingMissile = raceHud.transform.Find("PoseKart_MissileWarning_UI");
            if (existingMissile == null) existingMissile = raceHud.transform.Find("MissileWarning_UI");
            if (existingMissile != null) Object.DestroyImmediate(existingMissile.gameObject);

            if (missilePrefab != null)
            {
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(missilePrefab, raceHud.transform);
                inst.name = "MissileWarning_UI";
                RectTransform rt = inst.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
            }

            // 2. Shield Blocked UI
            Transform existingShield = raceHud.transform.Find("PoseKart_ShieldBlocked_UI");
            if (existingShield == null) existingShield = raceHud.transform.Find("ShieldBlocked_UI");
            if (existingShield != null) Object.DestroyImmediate(existingShield.gameObject);

            if (shieldPrefab != null)
            {
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(shieldPrefab, raceHud.transform);
                inst.name = "ShieldBlocked_UI";
                RectTransform rt = inst.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
            }

            // 3. Race Finish UI
            Transform existingFinish = raceHud.transform.Find("RaceFinishUI");
            if (existingFinish == null) existingFinish = raceHud.transform.Find("RaceFinish_UI");
            if (existingFinish == null) existingFinish = raceHud.transform.Find("PoseKart_RaceFinish_UI");

            if (existingFinish != null)
            {
                var finishScript = existingFinish.GetComponent<PoseKart.UI.PoseKartRaceFinishUI>();
                if (finishScript != null)
                {
                    finishScript.AutoWireHierarchy();
                    RaceManager rm = Object.FindFirstObjectByType<RaceManager>();
                    if (rm != null)
                    {
                        SerializedObject so = new SerializedObject(finishScript);
                        so.FindProperty("raceManager").objectReferenceValue = rm;
                        so.ApplyModifiedProperties();
                    }
                    finishScript.ApplyEditorPreview();
                }
            }
            else if (finishPrefab != null)
            {
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(finishPrefab, raceHud.transform);
                inst.name = "RaceFinishUI";
                RectTransform rt = inst.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
                
                // Link raceManager reference
                PoseKart.UI.PoseKartRaceFinishUI finishScript = inst.GetComponent<PoseKart.UI.PoseKartRaceFinishUI>();
                if (finishScript != null)
                {
                    finishScript.AutoWireHierarchy();
                    RaceManager rm = Object.FindFirstObjectByType<RaceManager>();
                    if (rm != null)
                    {
                        SerializedObject so = new SerializedObject(finishScript);
                        so.FindProperty("raceManager").objectReferenceValue = rm;
                        so.ApplyModifiedProperties();
                    }
                    finishScript.ApplyEditorPreview();
                }
            }
        }
    }
}
#endif
