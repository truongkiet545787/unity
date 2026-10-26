using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using UnityEngine.SceneManagement;
using TMPro;
using PoseKart.UI;

namespace PoseKart.EditorTools
{
    /// <summary>
    /// Editor-only setup tool để tạo và cấu hình toàn bộ hệ thống ManualDialog
    /// trực tiếp trong Unity Scene Hierarchy và lưu Prefab.
    /// Menu: PoseKart > UI > Build Manual Dialog
    /// Không bao giờ chạy hoặc can thiệp lúc Runtime/Play Mode.
    /// </summary>
    public static class PoseKartManualDialogBuilder
    {
        private const string PrefabDirectory = "Assets/Prefabs/UI";
        private const string PrefabPath = "Assets/Prefabs/UI/PoseKart_ManualDialog.prefab";

        private sealed class ManualVisualSnapshot
        {
            public readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            public readonly Dictionary<string, TMP_FontAsset> fonts = new Dictionary<string, TMP_FontAsset>();
        }

        [MenuItem("PoseKart/UI/Build Manual Dialog", false, 100)]
        public static void BuildManualDialogMenu()
        {
            BuildManualDialogInActiveScene(forceRebuild: false);
        }

        [MenuItem("PoseKart/UI/Force Rebuild Manual Dialog", false, 101)]
        public static void ForceRebuildManualDialogMenu()
        {
            BuildManualDialogInActiveScene(forceRebuild: true);
        }

        public static GameObject BuildManualDialogInActiveScene(bool forceRebuild = false)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.isLoaded)
            {
                EditorUtility.DisplayDialog("Lỗi", "Không có scene nào đang mở!", "OK");
                return null;
            }

            // 1. Tìm Canvas trong Scene (Ưu tiên VehicleSelectUI nếu có)
            Canvas canvas = FindTargetCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Lỗi", "Không tìm thấy Canvas nào trong Scene để tạo ManualDialog!", "OK");
                return null;
            }

            Transform canvasTransform = canvas.transform;

            // 2. Kiểm tra xem ManualDialog đã tồn tại chưa
            Transform existingDialogTransform = canvasTransform.Find("ManualDialog");
            ManualVisualSnapshot visualSnapshot = existingDialogTransform != null
                ? CaptureVisuals(existingDialogTransform)
                : null;
            if (existingDialogTransform != null && !forceRebuild)
            {
                // Kiểm tra xem đã có ManualWindowPanel chưa
                Transform existingPanel = existingDialogTransform.Find("ManualWindowPanel");
                if (existingPanel != null)
                {
                    Debug.Log("<color=#38BDF8>[PoseKart Builder]</color> ManualDialog đã tồn tại trong Scene. Tiến hành kết nối lại references mà không ghi đè chỉnh sửa thủ công của bạn.");
                    ConfigureDialogController(existingDialogTransform.gameObject, canvasTransform);
                    EditorSceneManager.MarkSceneDirty(activeScene);
                    EditorSceneManager.SaveScene(activeScene);
                    return existingDialogTransform.gameObject;
                }
            }

            // Nếu forceRebuild hoặc chỉ là placeholder cũ, dọn dẹp an toàn
            if (existingDialogTransform != null)
            {
                Undo.DestroyObjectImmediate(existingDialogTransform.gameObject);
            }

            // Dọn dẹp đối tượng HowToPlayManuel cũ nếu còn sót
            Transform legacyPlaceholder = canvasTransform.Find("HowToPlayManuel");
            if (legacyPlaceholder != null)
            {
                Undo.DestroyObjectImmediate(legacyPlaceholder.gameObject);
            }

            // 3. Tải các tài nguyên cơ bản (Sprite, Font)
            Sprite defaultSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;

            // 4. Xây dựng Hierarchy chuẩn theo yêu cầu
            // ==========================================
            // Canvas
            // └── ManualDialog
            //     ├── DimBackground
            //     └── ManualWindowPanel
            //         ├── Header
            //         │   ├── TitleText
            //         │   ├── SubtitleText
            //         │   └── CloseButton
            //         ├── Content
            //         │   ├── HandControlPanel
            //         │   │   ├── SteeringRow
            //         │   │   ├── DriftRow
            //         │   │   ├── NitroRow
            //         │   │   ├── ItemRow
            //         │   │   └── BrakeRow
            //         │   └── KeyboardPanel
            //         │       └── IndividualControlRows
            //         └── Footer
            //             └── OKButton
            // ==========================================

            // --- 4.1 ManualDialog (Root) ---
            GameObject manualDialogGo = new GameObject("ManualDialog", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(manualDialogGo, "Create ManualDialog");
            manualDialogGo.transform.SetParent(canvasTransform, false);

            RectTransform dialogRect = manualDialogGo.GetComponent<RectTransform>();
            SetStretch(dialogRect);

            // --- 4.2 DimBackground ---
            GameObject dimBgGo = new GameObject("DimBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dimBgGo.transform.SetParent(manualDialogGo.transform, false);
            RectTransform dimRect = dimBgGo.GetComponent<RectTransform>();
            SetStretch(dimRect);
            Image dimImg = dimBgGo.GetComponent<Image>();
            dimImg.color = new Color(0.02f, 0.04f, 0.07f, 0.85f);
            dimImg.raycastTarget = true;

            // --- 4.3 ManualWindowPanel ---
            GameObject windowPanelGo = new GameObject("ManualWindowPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            windowPanelGo.transform.SetParent(manualDialogGo.transform, false);
            RectTransform winRect = windowPanelGo.GetComponent<RectTransform>();
            winRect.anchorMin = new Vector2(0.5f, 0.5f);
            winRect.anchorMax = new Vector2(0.5f, 0.5f);
            winRect.pivot = new Vector2(0.5f, 0.5f);
            winRect.anchoredPosition = Vector2.zero;
            winRect.sizeDelta = new Vector2(1060f, 650f);

            Image winImg = windowPanelGo.GetComponent<Image>();
            winImg.sprite = defaultSprite;
            winImg.type = Image.Type.Sliced;
            winImg.color = new Color(0.08f, 0.11f, 0.18f, 0.98f);
            winImg.raycastTarget = true;

            // --- 4.4 Header ---
            GameObject headerGo = new GameObject("Header", typeof(RectTransform));
            headerGo.transform.SetParent(windowPanelGo.transform, false);
            RectTransform headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, 82f);

            // TitleText
            GameObject titleGo = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(headerGo.transform, false);
            RectTransform titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.42f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(50f, 0f);
            titleRect.offsetMax = new Vector2(-50f, -4f);

            TextMeshProUGUI titleText = titleGo.GetComponent<TextMeshProUGUI>();
            if (defaultFont != null) titleText.font = defaultFont;
            titleText.text = "HƯỚNG DẪN ĐIỀU KHIỂN POSEKART";
            titleText.fontSize = 24f;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = new Color(0.98f, 0.82f, 0.28f); // Gold

            // SubtitleText
            GameObject subGo = new GameObject("SubtitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            subGo.transform.SetParent(headerGo.transform, false);
            RectTransform subRect = subGo.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0f, 0f);
            subRect.anchorMax = new Vector2(1f, 0.42f);
            subRect.offsetMin = new Vector2(50f, 0f);
            subRect.offsetMax = new Vector2(-50f, 0f);

            TextMeshProUGUI subText = subGo.GetComponent<TextMeshProUGUI>();
            if (defaultFont != null) subText.font = defaultFont;
            subText.text = "HỆ THỐNG VÔ LĂNG ẢO WEBCAM & BÀN PHÍM DỰ PHÒNG";
            subText.fontSize = 13f;
            subText.fontStyle = FontStyles.Normal;
            subText.alignment = TextAlignmentOptions.Center;
            subText.color = new Color(0.55f, 0.85f, 1f); // Cyan

            // CloseButton ('X')
            GameObject closeBtnGo = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            closeBtnGo.transform.SetParent(headerGo.transform, false);
            RectTransform closeRect = closeBtnGo.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-16f, -16f);
            closeRect.sizeDelta = new Vector2(38f, 38f);

            Image closeImg = closeBtnGo.GetComponent<Image>();
            closeImg.sprite = defaultSprite;
            closeImg.type = Image.Type.Sliced;
            closeImg.color = new Color(0.85f, 0.25f, 0.25f, 0.95f); // Red

            Button closeBtn = closeBtnGo.GetComponent<Button>();

            GameObject closeTxtGo = new GameObject("CloseText", typeof(RectTransform), typeof(TextMeshProUGUI));
            closeTxtGo.transform.SetParent(closeBtnGo.transform, false);
            SetStretch(closeTxtGo.GetComponent<RectTransform>());
            TextMeshProUGUI closeTxt = closeTxtGo.GetComponent<TextMeshProUGUI>();
            if (defaultFont != null) closeTxt.font = defaultFont;
            closeTxt.text = "✕";
            closeTxt.fontSize = 18f;
            closeTxt.fontStyle = FontStyles.Bold;
            closeTxt.alignment = TextAlignmentOptions.Center;
            closeTxt.color = Color.white;

            // --- 4.5 Content ---
            GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            contentGo.transform.SetParent(windowPanelGo.transform, false);
            RectTransform contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.anchoredPosition = new Vector2(0f, -10f);
            contentRect.sizeDelta = new Vector2(-40f, -156f);

            HorizontalLayoutGroup contentLayout = contentGo.GetComponent<HorizontalLayoutGroup>();
            contentLayout.spacing = 18f;
            contentLayout.padding = new RectOffset(8, 8, 8, 8);
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = true;

            // --- 4.6 HandControlPanel ---
            GameObject handPanelGo = new GameObject("HandControlPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            handPanelGo.transform.SetParent(contentGo.transform, false);

            Image handPanelImg = handPanelGo.GetComponent<Image>();
            handPanelImg.sprite = defaultSprite;
            handPanelImg.type = Image.Type.Sliced;
            handPanelImg.color = new Color(0.12f, 0.16f, 0.25f, 0.95f);

            VerticalLayoutGroup handVlg = handPanelGo.GetComponent<VerticalLayoutGroup>();
            handVlg.spacing = 8f;
            handVlg.padding = new RectOffset(14, 14, 12, 12);
            handVlg.childControlWidth = true;
            handVlg.childControlHeight = false;
            handVlg.childForceExpandWidth = true;
            handVlg.childForceExpandHeight = false;

            CreatePanelHeader(handPanelGo.transform, "CỬ CHỈ TAY WEBCAM (GESTURES)", new Color(0.25f, 0.82f, 1f), defaultFont);

            // Các hàng cử chỉ tay (Mỗi hàng là một GameObject riêng biệt)
            CreateInstructionRow(handPanelGo.transform, "SteeringRow", "Lái xe (Steering)",
                "Hai ngón giữa (Đang phát triển) hoặc Xoay 2 tay như vô lăng",
                knobSprite, defaultSprite, defaultFont, new Color(0.96f, 0.82f, 0.35f));

            CreateInstructionRow(handPanelGo.transform, "DriftRow", "Trượt bánh (Drift)",
                "Giơ 2 ngón tay chữ V ✌️ (ở tay trái hoặc tay phải)",
                knobSprite, defaultSprite, defaultFont, new Color(0.38f, 0.88f, 0.55f));

            CreateInstructionRow(handPanelGo.transform, "NitroRow", "Tăng tốc (Nitro)",
                "Ngón cái Thumbs Up 👍 (Đang phát triển) / Xòe bàn tay phải",
                knobSprite, defaultSprite, defaultFont, new Color(0.35f, 0.72f, 1f));

            CreateInstructionRow(handPanelGo.transform, "ItemRow", "Dùng vật phẩm (Item)",
                "Ký hiệu sừng 🤘 (Đang phát triển) / Nắm đấm tay phải ✊",
                knobSprite, defaultSprite, defaultFont, new Color(0.85f, 0.55f, 1f));

            CreateInstructionRow(handPanelGo.transform, "BrakeRow", "Phanh xe (Brake)",
                "Xòe cả 2 lòng bàn tay 🖐️🖐️ (Ưu tiên an toàn cao nhất)",
                knobSprite, defaultSprite, defaultFont, new Color(1f, 0.42f, 0.42f));

            // --- 4.7 KeyboardPanel ---
            GameObject kbPanelGo = new GameObject("KeyboardPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            kbPanelGo.transform.SetParent(contentGo.transform, false);

            Image kbPanelImg = kbPanelGo.GetComponent<Image>();
            kbPanelImg.sprite = defaultSprite;
            kbPanelImg.type = Image.Type.Sliced;
            kbPanelImg.color = new Color(0.12f, 0.16f, 0.25f, 0.95f);

            VerticalLayoutGroup kbVlg = kbPanelGo.GetComponent<VerticalLayoutGroup>();
            kbVlg.spacing = 6f;
            kbVlg.padding = new RectOffset(14, 14, 12, 12);
            kbVlg.childControlWidth = true;
            kbVlg.childControlHeight = false;
            kbVlg.childForceExpandWidth = true;
            kbVlg.childForceExpandHeight = false;

            CreatePanelHeader(kbPanelGo.transform, "BÀN PHÍM DỰ PHÒNG & PHÍM NÓNG", new Color(0.38f, 0.88f, 0.55f), defaultFont);

            // Các hàng bàn phím thực tế
            CreateInstructionRow(kbPanelGo.transform, "SteerRow", "A / D hoặc ◄ / ►",
                "Đánh lái xe Trái / Phải",
                knobSprite, defaultSprite, defaultFont, new Color(0.60f, 0.85f, 1f), rowHeight: 32f);

            CreateInstructionRow(kbPanelGo.transform, "ThrottleRow", "Tự động (Auto)",
                "Tự động tăng tốc khi không đạp phanh",
                knobSprite, defaultSprite, defaultFont, new Color(0.60f, 0.85f, 1f), rowHeight: 32f);

            CreateInstructionRow(kbPanelGo.transform, "BrakeRow", "S hoặc Phím ▼",
                "Phanh xe hoặc lùi lại khi đã dừng hẳn",
                knobSprite, defaultSprite, defaultFont, new Color(1f, 0.55f, 0.55f), rowHeight: 32f);

            CreateInstructionRow(kbPanelGo.transform, "DriftRow", "Shift (Trái/Phải)",
                "Drift ôm cua trượt bánh tích lũy Mini-Turbo",
                knobSprite, defaultSprite, defaultFont, new Color(0.96f, 0.82f, 0.35f), rowHeight: 32f);

            CreateInstructionRow(kbPanelGo.transform, "NitroRow", "Phím E",
                "Kích hoạt bình Nitro tăng tốc cực đại",
                knobSprite, defaultSprite, defaultFont, new Color(0.35f, 0.72f, 1f), rowHeight: 32f);

            CreateInstructionRow(kbPanelGo.transform, "ItemRow", "Phím Space",
                "Bắn hoặc kích hoạt vật phẩm trong kho",
                knobSprite, defaultSprite, defaultFont, new Color(0.85f, 0.55f, 1f), rowHeight: 32f);

            CreateInstructionRow(kbPanelGo.transform, "ResetRow", "Phím R",
                "Phục hồi xe khi bị lật hoặc kẹt góc",
                knobSprite, defaultSprite, defaultFont, new Color(0.98f, 0.75f, 0.35f), rowHeight: 32f);

            CreateInstructionRow(kbPanelGo.transform, "HotkeyRow", "Phím F1 / F2",
                "F1: Bật/Tắt HUD góc lái | F2: Chuyển Bàn phím ⮀ Tay",
                knobSprite, defaultSprite, defaultFont, new Color(0.95f, 0.65f, 0.85f), rowHeight: 32f);

            // --- 4.8 Footer ---
            GameObject footerGo = new GameObject("Footer", typeof(RectTransform));
            footerGo.transform.SetParent(windowPanelGo.transform, false);
            RectTransform footRect = footerGo.GetComponent<RectTransform>();
            footRect.anchorMin = new Vector2(0f, 0f);
            footRect.anchorMax = new Vector2(1f, 0f);
            footRect.pivot = new Vector2(0.5f, 0f);
            footRect.anchoredPosition = new Vector2(0f, 14f);
            footRect.sizeDelta = new Vector2(0f, 52f);

            // OKButton
            GameObject okBtnGo = new GameObject("OKButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            okBtnGo.transform.SetParent(footerGo.transform, false);
            RectTransform okRect = okBtnGo.GetComponent<RectTransform>();
            okRect.anchorMin = new Vector2(0.5f, 0.5f);
            okRect.anchorMax = new Vector2(0.5f, 0.5f);
            okRect.pivot = new Vector2(0.5f, 0.5f);
            okRect.anchoredPosition = Vector2.zero;
            okRect.sizeDelta = new Vector2(260f, 44f);

            Image okImg = okBtnGo.GetComponent<Image>();
            okImg.sprite = defaultSprite;
            okImg.type = Image.Type.Sliced;
            okImg.color = new Color(0.12f, 0.65f, 0.38f, 1f); // Emerald

            Button okBtn = okBtnGo.GetComponent<Button>();

            GameObject okTxtGo = new GameObject("OKButtonText", typeof(RectTransform), typeof(TextMeshProUGUI));
            okTxtGo.transform.SetParent(okBtnGo.transform, false);
            SetStretch(okTxtGo.GetComponent<RectTransform>());
            TextMeshProUGUI okTxt = okTxtGo.GetComponent<TextMeshProUGUI>();
            if (defaultFont != null) okTxt.font = defaultFont;
            okTxt.text = "ĐÃ HIỂU / ĐÓNG";
            okTxt.fontSize = 15f;
            okTxt.fontStyle = FontStyles.Bold;
            okTxt.alignment = TextAlignmentOptions.Center;
            okTxt.color = Color.white;

            // --- 5. Cấu hình Controller & Kết nối Serialized References ---
            ConfigureDialogController(manualDialogGo, canvasTransform);
            ApplyVisualSnapshot(manualDialogGo, visualSnapshot);

            // The manual window is a modal dialog. It must start hidden and
            // only be shown by PoseKartManualDialog.Open() from the Manual
            // button, otherwise it covers the Vehicle Select screen on entry.
            manualDialogGo.SetActive(false);

            // --- 6. Lưu Prefab & Scene ---
            EnsureDirectoryExists(PrefabDirectory);
            PrefabUtility.SaveAsPrefabAssetAndConnect(manualDialogGo, PrefabPath, InteractionMode.AutomatedAction);
            Debug.Log($"<color=#4ADE80>[PoseKart Builder]</color> Đã tạo và lưu Prefab thành công: {PrefabPath}");

            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            Debug.Log($"<color=#4ADE80>[PoseKart Builder]</color> Đã lưu Scene '{activeScene.name}' thành công!");

            Selection.activeGameObject = manualDialogGo;
            return manualDialogGo;
        }

        private static Canvas FindTargetCanvas()
        {
            // 1. Tìm GameObject tên VehicleSelectUI
            GameObject vsUI = GameObject.Find("VehicleSelectUI");
            if (vsUI != null && vsUI.TryGetComponent<Canvas>(out var vsCanvas))
            {
                return vsCanvas;
            }

            // 2. Tìm Canvas bất kỳ trong Scene
            Canvas anyCanvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            return anyCanvas;
        }

        private static ManualVisualSnapshot CaptureVisuals(Transform root)
        {
            ManualVisualSnapshot snapshot = new ManualVisualSnapshot();
            if (root == null)
                return snapshot;

            Image[] images = root.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                string path = RelativePath(root, image.transform);
                if (image.sprite != null)
                    snapshot.sprites[path] = image.sprite;
            }

            TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI text in texts)
            {
                string path = RelativePath(root, text.transform);
                if (text.font != null)
                    snapshot.fonts[path] = text.font;
            }

            return snapshot;
        }

        private static void ApplyVisualSnapshot(GameObject rootObject, ManualVisualSnapshot snapshot)
        {
            if (rootObject == null || snapshot == null)
                return;

            Transform root = rootObject.transform;
            Image[] images = root.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (snapshot.sprites.TryGetValue(RelativePath(root, image.transform), out Sprite sprite))
                    image.sprite = sprite;
            }

            TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI text in texts)
            {
                if (snapshot.fonts.TryGetValue(RelativePath(root, text.transform), out TMP_FontAsset font))
                    text.font = font;
            }
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (root == target)
                return string.Empty;

            List<string> parts = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void CreatePanelHeader(Transform parent, string headerText, Color color, TMP_FontAsset font)
        {
            GameObject headerGo = new GameObject("PanelHeader", typeof(RectTransform), typeof(TextMeshProUGUI));
            headerGo.transform.SetParent(parent, false);

            RectTransform rt = headerGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 28f);

            TextMeshProUGUI tmp = headerGo.GetComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = headerText;
            tmp.fontSize = 14f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = color;

            LayoutElement le = headerGo.AddComponent<LayoutElement>();
            le.minHeight = 28f;
            le.preferredHeight = 28f;
        }

        private static void CreateInstructionRow(
            Transform parent,
            string rowName,
            string actionTextStr,
            string descTextStr,
            Sprite iconSprite,
            Sprite bgSprite,
            TMP_FontAsset font,
            Color actionColor,
            float rowHeight = 36f)
        {
            // Row GameObject
            GameObject rowGo = new GameObject(rowName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(parent, false);

            Image rowImg = rowGo.GetComponent<Image>();
            rowImg.sprite = bgSprite;
            rowImg.type = Image.Type.Sliced;
            rowImg.color = new Color(1f, 1f, 1f, 0.04f); // Subtle row background

            HorizontalLayoutGroup hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            LayoutElement rowLe = rowGo.GetComponent<LayoutElement>();
            rowLe.minHeight = rowHeight;
            rowLe.preferredHeight = rowHeight;

            // 1. Icon GameObject
            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(rowGo.transform, false);

            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.sizeDelta = new Vector2(26f, 26f);

            Image iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = iconSprite;
            iconImg.color = new Color(1f, 1f, 1f, 0.85f);
            iconImg.preserveAspect = true;

            LayoutElement iconLe = iconGo.GetComponent<LayoutElement>();
            iconLe.minWidth = 26f;
            iconLe.preferredWidth = 26f;

            // 2. ActionText GameObject
            GameObject actionGo = new GameObject("ActionText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            actionGo.transform.SetParent(rowGo.transform, false);

            RectTransform actionRect = actionGo.GetComponent<RectTransform>();
            actionRect.sizeDelta = new Vector2(150f, 0f);

            TextMeshProUGUI actionTmp = actionGo.GetComponent<TextMeshProUGUI>();
            if (font != null) actionTmp.font = font;
            actionTmp.text = actionTextStr;
            actionTmp.fontSize = 13f;
            actionTmp.fontStyle = FontStyles.Bold;
            actionTmp.alignment = TextAlignmentOptions.MidlineLeft;
            actionTmp.color = actionColor;

            LayoutElement actionLe = actionGo.GetComponent<LayoutElement>();
            actionLe.minWidth = 145f;
            actionLe.preferredWidth = 155f;

            // 3. DescriptionText GameObject
            GameObject descGo = new GameObject("DescriptionText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            descGo.transform.SetParent(rowGo.transform, false);

            RectTransform descRect = descGo.GetComponent<RectTransform>();
            descRect.sizeDelta = new Vector2(270f, 0f);

            TextMeshProUGUI descTmp = descGo.GetComponent<TextMeshProUGUI>();
            if (font != null) descTmp.font = font;
            descTmp.text = descTextStr;
            descTmp.fontSize = 11.5f;
            descTmp.fontStyle = FontStyles.Normal;
            descTmp.alignment = TextAlignmentOptions.MidlineLeft;
            descTmp.color = new Color(0.88f, 0.90f, 0.94f);
            descTmp.textWrappingMode = TextWrappingModes.Normal;

            LayoutElement descLe = descGo.GetComponent<LayoutElement>();
            descLe.minWidth = 240f;
            descLe.preferredWidth = 275f;
            descLe.flexibleWidth = 1f;
        }

        private static void ConfigureDialogController(GameObject dialogGo, Transform canvasTransform)
        {
            PoseKartManualDialog controller = dialogGo.GetComponent<PoseKartManualDialog>();
            if (controller == null)
            {
                controller = Undo.AddComponent<PoseKartManualDialog>(dialogGo);
            }

            SerializedObject so = new SerializedObject(controller);
            so.Update();

            // manualDialogRoot
            so.FindProperty("manualDialogRoot").objectReferenceValue = dialogGo;

            // closeButton
            Transform closeBtnTr = dialogGo.transform.Find("ManualWindowPanel/Header/CloseButton");
            Button closeBtn = closeBtnTr != null ? closeBtnTr.GetComponent<Button>() : null;
            so.FindProperty("closeButton").objectReferenceValue = closeBtn;

            // okButton
            Transform okBtnTr = dialogGo.transform.Find("ManualWindowPanel/Footer/OKButton");
            Button okBtn = okBtnTr != null ? okBtnTr.GetComponent<Button>() : null;
            so.FindProperty("okButton").objectReferenceValue = okBtn;

            // manualButton
            Button manualBtn = FindManualButton(canvasTransform);
            so.FindProperty("manualButton").objectReferenceValue = manualBtn;

            // clickAudio (Chỉ lấy nếu có AudioSource sẵn trong scene, không tìm cưỡng chế lúc runtime)
            AudioSource sceneAudio = UnityEngine.Object.FindFirstObjectByType<AudioSource>();
            if (sceneAudio != null)
            {
                so.FindProperty("clickAudio").objectReferenceValue = sceneAudio;
            }

            so.ApplyModifiedProperties();

            // Kết nối Persistent Listeners
            if (closeBtn != null)
            {
                SetupPersistentListener(closeBtn, controller.Close);
            }

            if (okBtn != null)
            {
                SetupPersistentListener(okBtn, controller.Close);
            }

            if (manualBtn != null)
            {
                SetupPersistentListener(manualBtn, controller.Open);
            }
        }

        private static Button FindManualButton(Transform canvasTransform)
        {
            // 1. Tìm trong chính Canvas
            Transform manualTr = canvasTransform.Find("Manual");
            if (manualTr != null && manualTr.TryGetComponent<Button>(out var btn))
            {
                return btn;
            }

            // 2. Tìm trong MainMenu nếu có
            Transform mainMenuTr = canvasTransform.Find("MainMenu");
            if (mainMenuTr != null)
            {
                Transform mBtn = mainMenuTr.Find("ManualButton") ?? mainMenuTr.Find("Manual");
                if (mBtn != null && mBtn.TryGetComponent<Button>(out var mBtnComp))
                {
                    return mBtnComp;
                }
            }

            // 3. Tìm toàn bộ button tên 'Manual' hoặc 'ManualButton'
            Button[] allButtons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var b in allButtons)
            {
                if (b.name == "Manual" || b.name == "ManualButton")
                {
                    return b;
                }
            }

            return null;
        }

        private static void SetupPersistentListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null || action == null) return;

            // Xóa persistent call cũ nếu trùng method name và target
            int count = button.onClick.GetPersistentEventCount();
            for (int i = count - 1; i >= 0; i--)
            {
                var target = button.onClick.GetPersistentTarget(i);
                string methodName = button.onClick.GetPersistentMethodName(i);
                if (target is PoseKartManualDialog || target is GameObject go && (go.name == "HowToPlayManuel" || go.name == "ManualDialog"))
                {
                    UnityEventTools.RemovePersistentListener(button.onClick, i);
                }
            }

            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }

        private static void SetStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void EnsureDirectoryExists(string dirPath)
        {
            if (!System.IO.Directory.Exists(dirPath))
            {
                System.IO.Directory.CreateDirectory(dirPath);
                AssetDatabase.Refresh();
            }
        }
    }
}
