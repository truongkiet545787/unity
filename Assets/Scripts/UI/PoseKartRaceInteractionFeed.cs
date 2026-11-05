using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.UI
{
    /// <summary>
    /// Compact, presentation-only feed for authoritative race interactions.
    /// Gameplay systems submit already-confirmed events through ReceiveServerEvent.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class PoseKartRaceInteractionFeed : MonoBehaviour
    {
        public enum InteractionKind
        {
            MissileHit,
            MissileBlocked,
            InkHit,
            InkBlocked,
            BananaHit,
            BananaBlocked,
            ShockwaveHit,
            SideBump
        }

        [Header("Layout")]
        [SerializeField] private Vector2 anchoredPosition = new Vector2(52f, -356f);
        [SerializeField, Min(120f)] private float width = 300f;
        [SerializeField, Min(20f)] private float rowHeight = 30f;
        [Tooltip("Three rows fit cleanly above the existing HeldItemPanel; increase only if that HUD has been moved.")]
        [SerializeField, Range(1, 8)] private int maxVisibleEntries = 3;
        [SerializeField, Min(0.5f)] private float entryLifetime = 4.5f;
        [SerializeField, Min(0.05f)] private float fadeDuration = 0.6f;
        [SerializeField] private Color panelColor = new Color(0.012f, 0.035f, 0.075f, 0.78f);
        [Tooltip("When enabled, the RectTransform can be moved manually in the Inspector without being reset on reload.")]
        [SerializeField] private bool preserveManualRectTransform = true;

        [Header("Optional icons")]
        [Tooltip("Icons are optional. Unassigned icons use a compact text fallback.")]
        [SerializeField] private Sprite missileIcon;
        [SerializeField] private Sprite inkIcon;
        [SerializeField] private Sprite bananaIcon;
        [SerializeField] private Sprite shieldIcon;
        [SerializeField] private Sprite shockwaveIcon;
        [SerializeField] private Sprite sideBumpIcon;

        private sealed class PendingEvent
        {
            public ulong id;
            public int kind;
            public string source;
            public string target;
        }

        private sealed class EntryRow
        {
            public GameObject root;
            public CanvasGroup group;
            public Image icon;
            public TMP_Text iconFallback;
            public TMP_Text label;
            public float expiresAt;
            public float fadeAt;
            public bool occupied;
        }

        private static readonly Queue<PendingEvent> PendingEvents = new Queue<PendingEvent>();
        private static readonly HashSet<ulong> PendingIds = new HashSet<ulong>();
        private static PoseKartRaceInteractionFeed activeFeed;

        private readonly List<EntryRow> rows = new List<EntryRow>();
        private readonly HashSet<ulong> receivedIds = new HashSet<ulong>();
        private RectTransform rectTransform;
        private RectTransform rowsRoot;
        private Image background;
        private bool built;

        /// <summary>
        /// Called by the authoritative Server RPC only. The feed never infers
        /// gameplay from local collision, VFX, or NetworkObject IDs.
        /// </summary>
        public static void ReceiveServerEvent(
            ulong eventId,
            int kind,
            string source,
            string target)
        {
            if (eventId == 0)
                return;

            if (activeFeed != null && activeFeed.isActiveAndEnabled)
            {
                activeFeed.Accept(eventId, kind, source, target);
                return;
            }

            if (!PendingIds.Add(eventId))
                return;

            while (PendingEvents.Count >= 32)
            {
                PendingEvent removed = PendingEvents.Dequeue();
                PendingIds.Remove(removed.id);
            }

            PendingEvents.Enqueue(new PendingEvent
            {
                id = eventId,
                kind = kind,
                source = SafeName(source),
                target = SafeName(target)
            });
        }

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            Build();
        }

        private void OnEnable()
        {
            activeFeed = this;
            Build();
            FlushPendingEvents();
        }

        private void OnDisable()
        {
            if (activeFeed == this)
                activeFeed = null;
        }

        private void OnValidate()
        {
            if (buildingOrPlaying())
                return;

            if (rectTransform == null)
                rectTransform = GetComponent<RectTransform>();
            Build();
            if (background != null)
                background.color = panelColor;
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            float now = Time.unscaledTime;
            for (int i = 0; i < rows.Count; i++)
            {
                EntryRow row = rows[i];
                if (!row.occupied)
                    continue;

                if (now >= row.expiresAt)
                {
                    row.occupied = false;
                    row.group.alpha = 0f;
                    row.root.SetActive(false);
                    continue;
                }

                row.group.alpha = now < row.fadeAt
                    ? 1f
                    : Mathf.Clamp01((row.expiresAt - now) / Mathf.Max(0.05f, fadeDuration));
            }
        }

        private bool buildingOrPlaying()
        {
            return Application.isPlaying;
        }

        private void Build()
        {
            if (rectTransform == null)
                return;

            if (built)
            {
                if (!preserveManualRectTransform)
                    ApplyPanelLayout();
                EnsureRows();
                return;
            }

            bool hasSavedRows = transform.Find("Rows") != null;
            built = true;
            if (!hasSavedRows || !preserveManualRectTransform)
                ApplyPanelLayout();

            background = GetComponent<Image>();
            if (background == null)
                background = gameObject.AddComponent<Image>();
            background.color = panelColor;
            background.raycastTarget = false;

            rowsRoot = transform.Find("Rows") as RectTransform;
            if (rowsRoot == null)
            {
                GameObject rowsObject = new GameObject("Rows", typeof(RectTransform));
                rowsObject.transform.SetParent(transform, false);
                rowsRoot = rowsObject.GetComponent<RectTransform>();
            }

            rowsRoot.anchorMin = Vector2.zero;
            rowsRoot.anchorMax = Vector2.one;
            rowsRoot.offsetMin = new Vector2(8f, 7f);
            rowsRoot.offsetMax = new Vector2(-8f, -7f);
            rowsRoot.localScale = Vector3.one;

            EnsureRows();
        }

        private void EnsureRows()
        {
            if (rowsRoot == null)
                return;

            int desiredRows = Mathf.Clamp(maxVisibleEntries, 1, 8);
            for (int i = rows.Count; i < desiredRows; i++)
                rows.Add(CreateRow(i));

            for (int i = 0; i < rows.Count; i++)
                rows[i].root.SetActive(i < desiredRows && rows[i].occupied);
        }

        private void ApplyPanelLayout()
        {
            if (rectTransform == null)
                return;

            int rowCount = Mathf.Clamp(maxVisibleEntries, 1, 8);
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = new Vector2(width, rowCount * rowHeight + 14f);
            rectTransform.localScale = Vector3.one;
        }

        private EntryRow CreateRow(int index)
        {
            GameObject rowObject = new GameObject("Interaction " + index, typeof(RectTransform));
            rowObject.transform.SetParent(rowsRoot, false);
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.offsetMin = new Vector2(0f, -((index + 1) * rowHeight));
            rowRect.offsetMax = new Vector2(0f, -(index * rowHeight));

            CanvasGroup group = rowObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            Image icon = CreateImage("Icon", rowObject.transform);
            RectTransform iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(2f, 0f);
            iconRect.sizeDelta = new Vector2(24f, 24f);
            icon.preserveAspect = true;

            TMP_Text iconFallback = CreateText("", "Icon Fallback", rowObject.transform);
            RectTransform fallbackRect = iconFallback.rectTransform;
            fallbackRect.anchorMin = new Vector2(0f, 0.5f);
            fallbackRect.anchorMax = new Vector2(0f, 0.5f);
            fallbackRect.pivot = new Vector2(0f, 0.5f);
            fallbackRect.anchoredPosition = new Vector2(3f, 0f);
            fallbackRect.sizeDelta = new Vector2(22f, 25f);
            iconFallback.fontSize = 15f;
            iconFallback.fontStyle = FontStyles.Bold;
            iconFallback.alignment = TextAlignmentOptions.Center;
            iconFallback.color = Color.white;
            iconFallback.outlineWidth = 0.18f;
            iconFallback.raycastTarget = false;

            TMP_Text label = CreateText("", "Message", rowObject.transform);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(1f, 0.5f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.offsetMin = new Vector2(30f, -rowHeight * 0.5f);
            labelRect.offsetMax = new Vector2(-4f, rowHeight * 0.5f);
            label.fontSize = 14f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Left;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.color = Color.white;
            label.outlineWidth = 0.18f;
            label.raycastTarget = false;

            return new EntryRow
            {
                root = rowObject,
                group = group,
                icon = icon,
                iconFallback = iconFallback,
                label = label,
                occupied = false
            };
        }

        private static Image CreateImage(string objectName, Transform parent)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text CreateText(string text, string objectName, Transform parent)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            TMP_Text textComponent = textObject.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            return textComponent;
        }

        private void FlushPendingEvents()
        {
            while (PendingEvents.Count > 0)
            {
                PendingEvent pending = PendingEvents.Dequeue();
                PendingIds.Remove(pending.id);
                Accept(pending.id, pending.kind, pending.source, pending.target);
            }
        }

        private void Accept(ulong eventId, int kindValue, string source, string target)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || eventId == 0)
                return;
            if (receivedIds.Contains(eventId))
                return;

            receivedIds.Add(eventId);
            if (receivedIds.Count > 256)
                receivedIds.Clear();

            Build();
            InteractionKind kind = Enum.IsDefined(typeof(InteractionKind), kindValue)
                ? (InteractionKind)kindValue
                : InteractionKind.MissileHit;
            Push(kind, SafeName(source), SafeName(target));
        }

        private void Push(InteractionKind kind, string source, string target)
        {
            int visibleCount = Mathf.Clamp(maxVisibleEntries, 1, 8);
            for (int i = visibleCount - 1; i > 0; i--)
                CopyRow(rows[i - 1], rows[i]);

            EntryRow first = rows[0];
            first.root.SetActive(true);
            first.occupied = true;
            first.expiresAt = Time.unscaledTime + Mathf.Max(0.5f, entryLifetime);
            first.fadeAt = first.expiresAt - Mathf.Max(0.05f, fadeDuration);
            first.label.text = FormatMessage(kind, source, target);
            first.icon.sprite = IconFor(kind);
            first.icon.enabled = first.icon.sprite != null;
            first.iconFallback.text = FallbackIconFor(kind);
            first.iconFallback.color = ColorFor(kind);
            first.iconFallback.gameObject.SetActive(!first.icon.enabled);
            first.group.alpha = 1f;

            for (int i = 1; i < rows.Count; i++)
                rows[i].root.SetActive(i < visibleCount && rows[i].occupied);
        }

        private void CopyRow(EntryRow source, EntryRow destination)
        {
            destination.occupied = source.occupied;
            destination.expiresAt = source.expiresAt;
            destination.fadeAt = source.fadeAt;
            destination.label.text = source.label.text;
            destination.icon.sprite = source.icon.sprite;
            destination.icon.enabled = source.icon.enabled;
            destination.iconFallback.text = source.iconFallback.text;
            destination.iconFallback.color = source.iconFallback.color;
            destination.iconFallback.gameObject.SetActive(!destination.icon.enabled);
            destination.group.alpha = source.group.alpha;
        }

        private Sprite IconFor(InteractionKind kind)
        {
            switch (kind)
            {
                case InteractionKind.MissileHit:
                    return missileIcon;
                case InteractionKind.MissileBlocked:
                    return shieldIcon != null ? shieldIcon : missileIcon;
                case InteractionKind.InkHit:
                    return inkIcon;
                case InteractionKind.InkBlocked:
                    return shieldIcon != null ? shieldIcon : inkIcon;
                case InteractionKind.BananaHit:
                    return bananaIcon;
                case InteractionKind.BananaBlocked:
                    return shieldIcon != null ? shieldIcon : bananaIcon;
                case InteractionKind.ShockwaveHit:
                    return shockwaveIcon;
                case InteractionKind.SideBump:
                    return sideBumpIcon;
                default:
                    return null;
            }
        }

        private static string FallbackIconFor(InteractionKind kind)
        {
            switch (kind)
            {
                case InteractionKind.MissileHit:
                case InteractionKind.MissileBlocked: return "M";
                case InteractionKind.InkHit:
                case InteractionKind.InkBlocked: return "I";
                case InteractionKind.BananaHit:
                case InteractionKind.BananaBlocked: return "B";
                case InteractionKind.ShockwaveHit: return "W";
                case InteractionKind.SideBump: return "+";
                default: return "•";
            }
        }

        private static Color ColorFor(InteractionKind kind)
        {
            switch (kind)
            {
                case InteractionKind.MissileHit: return new Color(1f, 0.28f, 0.16f, 1f);
                case InteractionKind.MissileBlocked: return new Color(0.18f, 0.9f, 1f, 1f);
                case InteractionKind.InkHit: return new Color(0.35f, 0.5f, 1f, 1f);
                case InteractionKind.InkBlocked: return new Color(0.18f, 0.9f, 1f, 1f);
                case InteractionKind.BananaHit: return new Color(1f, 0.78f, 0.12f, 1f);
                case InteractionKind.BananaBlocked: return new Color(0.18f, 0.9f, 1f, 1f);
                case InteractionKind.ShockwaveHit: return new Color(0.85f, 0.34f, 1f, 1f);
                default: return Color.white;
            }
        }

        private static string FormatMessage(InteractionKind kind, string source, string target)
        {
            switch (kind)
            {
                case InteractionKind.MissileHit:
                    return source + " hit " + target + " with a Missile";
                case InteractionKind.MissileBlocked:
                    return target + " blocked " + source + "'s Missile with a Shield";
                case InteractionKind.InkHit:
                    return source + " hit " + target + " with Ink";
                case InteractionKind.InkBlocked:
                    return target + " blocked " + source + "'s Ink with a Shield";
                case InteractionKind.BananaHit:
                    return source + " hit " + target + " with a Banana";
                case InteractionKind.BananaBlocked:
                    return target + " blocked " + source + "'s Banana with a Shield";
                case InteractionKind.ShockwaveHit:
                    return source + " knocked " + target + " off line with Shockwave";
                case InteractionKind.SideBump:
                    return source + " and " + target + " got a Side Bump Boost";
                default:
                    return source + " interacted with " + target;
            }
        }

        private static string SafeName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        }
    }
}
