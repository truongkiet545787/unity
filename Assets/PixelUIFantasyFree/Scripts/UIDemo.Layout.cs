using UnityEngine;
using UnityEngine.UI;

namespace HeyHeyThere.PixelUIFantasyFree
{
    /// <summary>
    /// The demo's screen: an RPG's hero portrait and bars, settings, an inventory of rarity slots, a
    /// hotbar, a quest scroll and a line of dialogue, on a dungeon wall.
    /// </summary>
    public partial class UIDemo
    {
        static readonly Color Background = new Color(0.07f, 0.05f, 0.06f);

        /// <summary>A dim dungeon wall behind the UI.</summary>
        static Texture2D Backdrop()
        {
            var t = Paper(640, 360, out var rect);
            var rng = new System.Random(11);
            for (int y = 0; y < 400; y += 12)
            {
                int shift = y / 12 % 2 == 1 ? 12 : 0;
                for (int x = -24; x < 700; x += 24)
                {
                    var v = 0.08f + 0.04f * (float)rng.NextDouble();
                    rect(x + shift, y, 23, 11, new Color(v * 1.1f, v * 0.9f, v));
                }
            }
            t.Apply();
            return t;
        }

        void Layout()
        {
            Hud();
            Settings();
            Inventory();
            Hotbar();
            Quests();
            Dialogue();
            Switcher(6, 334, 3);
        }

        /// <summary>A 24x24 item slot (rarity "" for a plain one) holding the icon, and its count above 1.</summary>
        RectTransform Slot(Transform parent, string item, int count = 1, string rarity = "")
        {
            var type = rarity != "" && theme.Has(Title(rarity) + "Slot") ? Title(rarity) + "Slot" : "InsetPanel";
            var slot = Make(type, parent);
            MinSize(slot, 24, 24);
            if (item == "" || !Has(item))
                return slot;
            Tip(Icon(slot, item), Title(item));
            var pad = slot.GetComponent<VerticalLayoutGroup>().padding;
            if (count > 1)
                Over(slot, count.ToString(), new Vector2(pad.left + 19, pad.top + 6), true, Color.white, Color.black);
            return slot;
        }

        /// <summary>The icon at twice its size in the portrait frame, 48x48 in all.</summary>
        RectTransform Portrait(Transform parent, string item)
        {
            var frame = Make(theme.Has("PortraitPanel") ? "PortraitPanel" : "InsetPanel", parent);
            var sprite = Sprite(icons, item);
            Icon(frame, item, sprite == null ? 1 : Mathf.Max(1, 32 / (int)Mathf.Max(sprite.rect.width, sprite.rect.height)));
            return Centre(frame);
        }

        void Hud()
        {
            var top = Place(Row(screen, 4), 6, 6);
            Portrait(top, "helmet");
            var rows = Column(top, 0);
            Label(rows, "Aria  Lv 7", "Nameplate");
            foreach (var (icon, colour, value) in new[] { ("heart", "Red", 72), ("mana", "Blue", 48), ("xp", "Gold", 64) })
            {
                var r = Row(rows, 2);
                Icon(r, icon);
                Centre((RectTransform)Bar(r, colour, value, 96, 10).transform);
            }
            var purse = Make("Panel", screen);
            Fit(purse);
            purse.anchorMin = purse.anchorMax = purse.pivot = Vector2.one;
            purse.anchoredPosition = new Vector2(-6, -6);
            var money = Row(purse, 2);
            foreach (var (icon, text) in new[] { ("coin", "1250"), ("gem", "12"), ("key", "3") })
            {
                Icon(money, icon);
                MinSize(Label(money, text), 28);
            }
        }

        void Settings()
        {
            var box = Window("Settings", 6, 76, 166);
            Slider(box, "Music", 70, 42);
            Slider(box, "Sound", 45, 42);
            Check(box, "Show hints", true);
            var modes = Row(box, 6);
            var group = modes.gameObject.AddComponent<ToggleGroup>();
            foreach (var m in new[] { "Classic", "Heroic" })
            {
                var radio = Check(modes, m, false, true);
                radio.group = group;
                radio.isOn = m == "Heroic";
            }
            Dropdown(box, 0, "Text: normal", "Text: fast", "Text: instant");
            Input(box, "Hero name");
            var buttons = Row(box);
            foreach (var t in new[] { "Back", "Apply" })
                Expand(Button(buttons, t));
        }

        void Inventory()
        {
            var box = Window("Inventory", 178, 30, 256);
            var pages = Tabs(box, "Bag", "Gear");
            var bag = Grid(pages[0], 9, 2);
            var items = new[] { ("sword", 1, "rare"), ("shield", 1, "uncommon"), ("bow", 1, "common"), ("staff", 1, "epic"),
                ("amulet", 1, "legendary"), ("ring", 1, "rare"), ("potion_red", 5, "common"), ("potion_blue", 3, "common"),
                ("potion_green", 2, "uncommon"), ("bread", 4, "common"), ("meat", 2, "common"), ("herb", 9, "common"),
                ("mushroom", 3, "uncommon"), ("scroll", 2, "rare"), ("gem", 12, "epic"), ("key", 1, "common"),
                ("", 1, ""), ("", 1, "") };
            foreach (var (item, count, rarity) in items)
            {
                bool shown = item != "" && Has(item);
                Slot(bag, shown ? item : "", count, shown ? rarity : "");
            }
            foreach (var (item, text, bonus) in new[] { ("helmet", "Iron helm", "+4"), ("chestplate", "Knight's plate", "+9"), ("boots", "Swift boots", "+2") })
            {
                var r = Row(pages[1]);
                Slot(r, item);
                Expand(Label(r, text));
                Label(r, bonus);
            }
            Make("Separator", box);
            var weight = Row(box);
            Icon(weight, "pouch");
            Label(weight, "Weight 34/50");
            var load = Bar(weight, "Green", 68, 0, 10);
            Expand(load);
            Centre((RectTransform)load.transform);
            var actions = Row(box);
            foreach (var (icon, text) in new[] { ("check", "Use"), ("cross", "Drop"), ("quest", "Info") })
                Expand(Button(actions, text, icon));
        }

        void Hotbar()
        {
            var panel = Place(Make("Panel", screen), 183, 198);
            var r = Row(panel, 2);
            var keys = new[] { "sword", "bow", "fireball", "frost", "lightning", "potion_red", "potion_blue", "torch", "map" };
            for (int i = 0; i < keys.Length; i++)
                Over(Slot(r, keys[i]), (i + 1).ToString(), new Vector2(3, -1), false, new Color(1, 1, 1, 0.8f), Color.black);
        }

        void Quests()
        {
            var scroll = Place(Make(theme.Has("ParchmentPanel") ? "ParchmentPanel" : "Panel", screen), 442, 42);
            MinSize(scroll, 192);
            scroll.GetComponent<VerticalLayoutGroup>().spacing = 2;
            Label(scroll, "Quest Log", "Ribbon").Text.alignment = TMPro.TextAlignmentOptions.Top;
            foreach (var (icon, text) in new[] { ("check", "Find the lost key"), ("check", "Light the old beacon"), ("quest", "Slay the swamp troll"),
                         ("quest", "Return to the sage"), ("lock", "The sunken crypt") })
            {
                var r = Row(scroll, 3);
                Icon(r, icon);
                Label(r, text, "Ink");
            }
            Label(scroll, "Reward: 300 gold", "Ink").Text.alignment = TMPro.TextAlignmentOptions.Top;
        }

        void Dialogue()
        {
            var panel = Place(Make("Panel", screen), 6, 262);
            MinSize(panel, 628);
            var r = Row(panel, 6);
            Portrait(r, Has("spellbook") ? "spellbook" : "scroll");
            var box = Column(r, 0);
            Expand(box);
            Label(Row(box, 0), "Old Sage", "Ribbon");
            Label(box, "The troll guards the bridge by night. Take this torch,\nand the frost scroll: fire alone won't do.");
            Centre((RectTransform)Button(r, "", "arrow_right").transform);
        }
    }
}
