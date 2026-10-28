using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KingdomWatch.Game
{
    // What the panel shows this frame. The driver fills it from the world and
    // the view; nothing here feeds back into the simulation.
    public sealed class HudState
    {
        public long Year;
        public Season Season;
        public int DayOfSeason;
        public int Speed;
        public bool Paused;

        public EntityKind SelectedKind;
        public ulong SelectedId;
        public int SelectedPeople;
        public AgeStage Stage;
        public int Years;
        public int Health;
        public JobKind Job;
        public bool Working;
        public bool GoingHome;
        public bool Following;

        // The community whose stock the resource card shows: the selected
        // settlement or band, or a selected person's own.
        public bool HasCommunity;
        public EntityKind CommunityKind;
        public ulong CommunityId;
        // What the community owns, and how much of each is free to use.
        public int Food, Grain, Wood, Stone, FoodFree, GrainFree, WoodFree, StoneFree, DaysOfFood;
        public int CommunityPeople;

        public long Seed;
        public int MapWidth, MapHeight;
        public int People, Settlements;
        public long HashYear;
        public ulong Hash;
    }

    // What the panel's controls do; the driver supplies each.
    public sealed class HudCommands
    {
        public Action Slower, Faster, TogglePause, ToggleFollow;

        // The minimap was clicked: a point in world units.
        public Action<Vector2> MoveTo;
    }

    public interface IPointerBlocker
    {
        // `screenPoint` is in screen pixels, y up.
        bool Blocks(Vector2 screenPoint);

        // How far the middle of the map the panel leaves open is from the
        // middle of the screen, in screen pixels (y up): following centres there.
        Vector2 OpenCentreOffset { get; }
    }

    // The panel, in uGUI (#128): a bar across the top with the date, the
    // world's people and the speed controls, and cards for the minimap (once
    // zoomed in), the selection with its community's stock, and the debug
    // readout. On a wide screen the cards stack down the left; on an upright
    // one the selection sits along the bottom, in reach of a thumb. Everything
    // is laid out in art pixels and drawn at a whole number of screen pixels
    // each, so the pack's art stays crisp. Built in code, as ArtSet builds the
    // map's sprites, so nothing depends on a prefab that could drift from it.
    public sealed class Hud : IPointerBlocker
    {
        public const float BarHeight = 26f;

        // A narrow screen puts the people and the speed on a second row: its
        // height, how far down that row starts, the room the speed and its
        // buttons take at the right of one row, and the buttons alone.
        private const float TwoRowHeight = 44f;
        private const float SecondRow = 18f;
        private const float RightControls = RightButtons + ButtonGap + SpeedWidth;
        private const float RightButtons = 4f * ButtonSize + 3f * ButtonGap + Pad;
        private const float SpeedWidth = 50f;

        // Between the bar's buttons: wide enough that each one's touch area,
        // 24 art pixels across (48dp at 2dp each), stays clear of the next.
        private const float ButtonGap = 8f;

        // The narrowest an upright screen may be, in art pixels: a 360dp
        // phone at 2dp an art pixel, which fits the speed and the buttons on
        // the bar's first row and the date and the people on its second.
        private const float UprightWidth = 170f;

        // A stock entry's height when the four wrap onto more rows.
        private const float StockRow = 18f;
        private const float Gap = 3f;
        private const float ColumnWidth = 104f;
        private const float Pad = 8f;
        private const float InnerWidth = ColumnWidth - 2f * Pad;
        private const int MinimapSize = 90;
        private const float ButtonSize = 16f;
        private const float BarEase = 8f;
        private const float MinimapCardHeight = Pad - 1f + MinimapSize + Pad;
        private const float PlateHeight = 19f;
        // From a plate's foot to the top of its marker: the tail hangs 3 of it.
        private const float PlateGap = 5f;

        // The width of each of the stock's three entries, and the gap
        // between the bar's groups.
        private const float StockGap = 8f;
        private const float BarGap = 12f;

        private static readonly Color Ink = new Color(0.13f, 0.1f, 0.16f);
        private static readonly Color Faint = new Color(0.4f, 0.34f, 0.3f);

        private readonly HudArt art;
        private readonly HudCommands commands;
        private readonly bool flat;
        private readonly List<RectTransform> blocking = new List<RectTransform>();
        private readonly GameObject root;
        private readonly CanvasScaler scaler;
        private readonly RectTransform canvasRect;

        // Everything but the map's own labels lives under this, inset to the
        // screen's safe area so a camera cutout or rounded corner cannot cover it.
        private readonly RectTransform safeRoot;

        // Set from the command line in a development build, to look at the
        // panel with a notch's worth of inset on a screen that has none.
        public static Rect? SafeAreaOverride;

        // Top bar.
        private readonly RectTransform bar;
        private float barHeight = BarHeight;
        private readonly Label date, speed;
        private Image pauseIcon;
        private Label pauseLabel;
        private Tip pauseTip, followTip;
        private long dateKey = -1, speedKey = -1;
        private bool shownPaused = true;

        // Minimap.
        private readonly RectTransform minimapCard;
        private readonly RectTransform minimap;
        private readonly Image[] cameraEdges = new Image[4];
        private readonly List<Image> dots = new List<Image>();

        // The selected community's stock, in the selection card.
        private readonly RectTransform stockGroup;
        private readonly Label foodNumber, grainNumber, woodNumber, stoneNumber;
        private readonly RectTransform[] stockIcons = new RectTransform[4];
        private readonly Label[] stockNumbers = new Label[4];
        private readonly Tip foodTip, grainTip, woodTip, stoneTip;
        private (int, int, int, int, int, int, int, int, int) stockSeen = (int.MinValue, 0, 0, 0, 0, 0, 0, 0, 0);

        // The world's people, in the bar.
        private readonly RectTransform peopleGroup, peopleIcon;
        private readonly Label peopleName, peopleCount;
        private int peopleSeen = -1;

        // Names over the communities at Far zoom.
        private readonly WorldView2D view;
        private readonly RectTransform mapLabels;
        private readonly List<RectTransform> communityPlates = new List<RectTransform>();
        private readonly List<Label> communityLabels = new List<Label>();
        private readonly List<(ulong, EntityKind, int)> plateSeen = new List<(ulong, EntityKind, int)>();

        // Selection card.
        private readonly RectTransform selectionCard;
        private readonly Label selectionName, selectionTitle, selectionLine, selectionStatus, selectionCommunity, hint1, hint2;
        private readonly RectTransform healthBar;
        private readonly Image healthFill;
        private readonly Tip healthTip;
        private readonly GameObject buttonRow;
        private readonly RectTransform followButton;
        private readonly Label followLabel;
        private readonly RectTransform[] cards;

        // The selection card's width: the column's, or across an upright screen.
        private float cardWidth = ColumnWidth;
        private float CardInner => cardWidth - 2f * Pad;
        private (ulong, EntityKind, int, AgeStage, JobKind, bool, bool, int, ulong, EntityKind) selectionSeen = (ulong.MaxValue, EntityKind.None, int.MinValue, AgeStage.None, JobKind.None, false, false, 0, 0, EntityKind.None);
        private float shownHealth;
        private int shownHealthTip = -1;

        // Debug card, shown from the bar's toggle or F3.
        private readonly RectTransform debugCard;
        private readonly Label[] debugLines = new Label[6];
        private (long, int, int, long, ulong) debugSeen = (long.MinValue, 0, 0, 0, 0);
        private bool debug;

        // Tooltip.
        private readonly RectTransform tooltip;
        private readonly Label tooltipText;
        private Tip hovered;
        private float hoveredFor;

        public Hud(HudArt art, HudCommands commands, WorldView2D view, Vector2 mapSize)
        {
            this.art = art;
            this.commands = commands;
            this.view = view;
            flat = art.FontTexture == null;

            EnsureEventSystem();
            root = new GameObject("Hud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            // The sprites are one unit to a pixel, so a frame's border counts
            // in the same units as the rect it is stretched over.
            scaler.referencePixelsPerUnit = 1f;
            canvasRect = (RectTransform)root.transform;

            // Behind everything else, so a card or the bar covers a label.
            mapLabels = Child(canvasRect, "MapLabels", 0f, 0f, 0f, 0f);
            Stretch(mapLabels);
            safeRoot = new GameObject("Safe area", typeof(RectTransform)).GetComponent<RectTransform>();
            safeRoot.SetParent(canvasRect, false);
            safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;

            // The top bar.
            bar = Frame(safeRoot, "Bar", art.Panel, 0f, 0f, 0f, BarHeight, Tint(false));
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, BarHeight);
            blocking.Add(bar);
            date = new Label(bar, art, TextAlign.Left, Ink);
            date.Place(Pad, 9f, 130f);
            speed = AddBarControls();
            peopleGroup = Child(bar, "People", 0f, 0f, 200f, BarHeight);
            peopleName = BarText(peopleGroup, 0f);
            peopleName.Value = "World";
            peopleIcon = PeopleIcon(peopleGroup, out var peopleTip);
            peopleTip.Text = "People in the world";
            peopleCount = BarText(peopleGroup, 0f);

            // The cards. The minimap has no Whole map button: pinching out, or
            // Home, shows the whole map, and then the minimap hides.
            minimapCard = Card("Minimap", 0f, MinimapCardHeight);
            minimap = Child(minimapCard, "Map", Pad, Pad - 1f, MinimapSize, MinimapSize);
            BuildMinimap(view, mapSize);

            selectionCard = Card("Selection", 0f, 70f);
            stockGroup = Child(selectionCard, "Stock", Pad, 0f, 300f, BarHeight);
            foodNumber = BuildStock(0, art.Berries, out foodTip);
            grainNumber = BuildStock(1, art.Grain, out grainTip);
            woodNumber = BuildStock(2, art.Wood, out woodTip);
            stoneNumber = BuildStock(3, art.Stone, out stoneTip);
            selectionName = Text(selectionCard, Pad, 7f, InnerWidth, Ink);
            selectionTitle = Text(selectionCard, Pad, 7f, InnerWidth, Ink);
            selectionLine = Text(selectionCard, Pad, 7f + 10f, InnerWidth, Ink);
            selectionStatus = Text(selectionCard, Pad, 7f + 20f, InnerWidth, Ink);
            selectionCommunity = Text(selectionCard, Pad, 7f + 20f, InnerWidth, Ink);
            hint1 = Text(selectionCard, Pad, 7f, InnerWidth, Ink);
            hint2 = Text(selectionCard, Pad, 7f + 10f, InnerWidth, Ink);
            hint1.Value = "Tap a person, or";
            hint2.Value = "a settlement.";
            healthBar = Child(selectionCard, "Health", Pad, 7f + 51f, InnerWidth, 5f);
            // The back takes pointer events, so the bar's tooltip can be reached.
            Fill(healthBar, "Back", Faint, art.White).raycastTarget = true;
            healthFill = Fill(healthBar, "Fill", new Color(0.85f, 0.25f, 0.25f), art.White);
            healthTip = healthBar.gameObject.AddComponent<Tip>();
            healthTip.Hud = this;
            buttonRow = new GameObject("Buttons", typeof(RectTransform));
            var row = (RectTransform)buttonRow.transform;
            row.SetParent(selectionCard, false);
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = Vector2.zero;
            row.sizeDelta = new Vector2(ColumnWidth, 140f);
            // Tapping empty map, or Esc, clears the selection, so Follow has
            // the row to itself.
            followButton = MakeButton(row, Pad, 7f + 61f, InnerWidth, ButtonSize, null, "Follow", "Follow the selection (F)", commands.ToggleFollow, out followLabel);
            followTip = followButton.GetComponent<Tip>();

            debugCard = Card("Debug", 0f, 14f + debugLines.Length * 9f);
            for (var i = 0; i < debugLines.Length; i++) debugLines[i] = Text(debugCard, Pad, 7f + i * 9f, InnerWidth, Ink);
            cards = new[] { minimapCard, selectionCard, debugCard };

            // The tooltip floats above everything, and takes no clicks.
            tooltip = Frame(safeRoot, "Tooltip", art.Panel, 0f, 0f, 60f, 22f, Tint(false));
            tooltip.GetComponent<Image>().raycastTarget = false;
            tooltipText = new Label(tooltip, art, TextAlign.Left, Ink);
            tooltipText.Place(Pad, 7f, 200f);
            tooltip.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
        }

        // Screen pixels to an art pixel, rounded to whole pixels so every art
        // pixel is one flat square: about 2dp, so a button is a size a finger
        // can hit on any phone, and never less than a 360th of the shorter
        // side, the size a monitor, whose density says little, has always had.
        // A smaller scale is used when the panel would not fit the screen at
        // that one, so everything stays reachable.
        public float Pixel
        {
            get
            {
                var byDensity = Screen.dpi > 0f ? Mathf.RoundToInt(Screen.dpi / 80f) : 0;
                // Two-argument calls: Unity's longer Max takes a params array,
                // which would allocate on every read.
                var preferred = Mathf.Max(Mathf.Max(1, byDensity), Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) / 360f));
                if (cards == null) return preferred;

                // Across an upright screen, the bar; down a wide one, the bar,
                // as tall as it is at each scale tried, and the column. The
                // minimap sits apart from the column, so it is not counted.
                var safe = SafeArea;
                if (Upright) return Mathf.Clamp(Mathf.Floor(safe.width / UprightWidth), 1, preferred);
                var column = Gap;
                foreach (var card in cards)
                {
                    if (card == minimapCard || !card.gameObject.activeSelf) continue;
                    column += card.sizeDelta.y + Gap;
                }
                var scale = preferred;
                while (scale > 1 && ((OneRow(safe.width / scale) ? BarHeight : TwoRowHeight) + column) * scale > safe.height) scale--;
                return scale;
            }
        }

        // The footprint of what the camera must keep the map clear of, in GUI
        // coordinates (origin top-left, y down): the bar across the top, and on
        // a wide screen the column down the left, all the way down, so the
        // whole map always goes beside it. The bottom card and the minimap come
        // and go with the selection and the zoom, so the whole map is framed
        // clear of neither, and does not jump as they do.
        public Rect Reserved => Upright
            ? new Rect(0f, 0f, Screen.width, TopInset + Gap * Pixel)
            : new Rect(0f, 0f, SafeArea.xMin + (ColumnWidth + 2f * Gap) * Pixel, Screen.height);

        public float TopInset => TopInsetOfSafeArea + barHeight * Pixel;

        // Upright, what the whole map keeps clear of along the bottom, in
        // screen pixels: the card there now. A taller card lets the camera
        // zoom out further, so the whole map still fits above it, while a
        // short one leaves the map the most room.
        public float BottomInset => Upright ? SafeArea.yMin + (selectionCard.sizeDelta.y + 2f * Gap) * Pixel : 0f;

        // The middle of what the bar and the cards on show leave of the map.
        public Vector2 OpenCentreOffset
        {
            get
            {
                var scale = Pixel;
                var top = TopInset + Gap * scale;
                var left = 0f;
                var bottom = 0f;
                if (Upright)
                {
                    if (minimapCard.gameObject.activeSelf) top += (MinimapCardHeight + Gap) * scale;
                    if (selectionCard.gameObject.activeSelf) bottom = SafeArea.yMin + (selectionCard.sizeDelta.y + 2f * Gap) * scale;
                }
                else left = SafeArea.xMin + (ColumnWidth + 2f * Gap) * scale;
                return new Vector2(left / 2f, (bottom - top) / 2f);
            }
        }

        // The part of the screen the panel may use, in screen pixels (y up),
        // clear of the cutout and the rounded corners.
        private static Rect SafeArea => SafeAreaOverride ?? RoundedCorners.Inside(Screen.safeArea, Upright);

        // How far down from the top of the screen the safe area starts, in pixels.
        private static float TopInsetOfSafeArea => Screen.height - SafeArea.yMax;

        public void ToggleDebug() => debug = !debug;

        public bool Blocks(Vector2 screenPoint)
        {
            for (var i = 0; i < blocking.Count; i++)
                if (blocking[i].gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(blocking[i], screenPoint, null)) return true;
            return false;
        }

        // Called every frame, after the camera has settled.
        public void Refresh(HudState state, Rect visible, IReadOnlyList<Vector2> settlements, IReadOnlyList<Vector2> bands, float mapWidth, float mapHeight)
        {
            // A minimised window can report no size at all, and the anchors below
            // divide by it.
            if (Screen.width <= 0 || Screen.height <= 0) return;
            scaler.scaleFactor = Pixel;
            var safe = SafeArea;
            safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);

            RefreshBar(state);
            RefreshMinimap(visible, settlements, bands, mapWidth, mapHeight);
            RefreshStock(state);
            RefreshPeople(state);
            LayOutBar();
            RefreshMapLabels();
            RefreshSelection(state);
            RefreshDebug(state);
            LayOutCards();
            RefreshTooltip();
        }

        // ---- Bar ----

        private Label AddBarControls()
        {
            var right = Pad;
            var y = (BarHeight - ButtonSize) / 2f - 1f;
            // Built right to left from the edge of the bar.
            var debugButton = MakeButton(bar, 0f, y, ButtonSize, ButtonSize, null, "i", "Debug information (F3)", ToggleDebug, out _);
            AnchorRight(debugButton, right);
            right += ButtonSize + ButtonGap;
            var faster = MakeButton(bar, 0f, y, ButtonSize, ButtonSize, null, ">>", "Faster (.)", commands.Faster, out _);
            AnchorRight(faster, right);
            right += ButtonSize + ButtonGap;
            var pauseButton = MakeButton(bar, 0f, y, ButtonSize, ButtonSize, art.Pause, art.Pause == null ? "II" : null, "Pause (Space)", commands.TogglePause, out pauseLabel, out pauseIcon);
            pauseTip = pauseButton.GetComponent<Tip>();
            AnchorRight(pauseButton, right);
            right += ButtonSize + ButtonGap;
            var slower = MakeButton(bar, 0f, y, ButtonSize, ButtonSize, null, "<<", "Slower (,)", commands.Slower, out _);
            AnchorRight(slower, right);
            right += ButtonSize + ButtonGap;
            var speedLabel = new Label(bar, art, TextAlign.Right, Ink);
            speedLabel.Rect.anchorMin = speedLabel.Rect.anchorMax = new Vector2(1f, 1f);
            speedLabel.Rect.pivot = new Vector2(1f, 1f);
            speedLabel.Rect.anchoredPosition = new Vector2(-right, -9f);
            speedLabel.Rect.sizeDelta = new Vector2(SpeedWidth, HudArt.LineHeight);
            return speedLabel;
        }

        private static void AnchorRight(RectTransform rect, float fromRight)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-fromRight, rect.anchoredPosition.y);
        }

        private void RefreshBar(HudState s)
        {
            var key = s.Year * 1000L + (int)s.Season * 100 + s.DayOfSeason;
            if (Changed(ref dateKey, key)) date.Value = "Year " + s.Year + ", " + s.Season + " " + (s.DayOfSeason + 1);
            if (Changed(ref speedKey, s.Paused ? -1 : s.Speed)) speed.Value = s.Paused ? "Paused" : s.Speed + "x";
            if (shownPaused != s.Paused)
            {
                shownPaused = s.Paused;
                if (pauseIcon != null) pauseIcon.sprite = s.Paused ? art.Play : art.Pause;
                if (pauseLabel != null) pauseLabel.Value = s.Paused ? ">" : "II";
                pauseTip.Text = s.Paused ? "Resume (Space)" : "Pause (Space)";
            }
        }

        // ---- Minimap ----

        private void BuildMinimap(WorldView2D view, Vector2 mapSize)
        {
            var picture = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            picture.transform.SetParent(minimap, false);
            Stretch((RectTransform)picture.transform);
            var raw = picture.GetComponent<RawImage>();
            raw.texture = view.MakeMinimap(MinimapSize);
            var tip = picture.AddComponent<Tip>();
            tip.Hud = this;
            tip.Text = "Click to move the camera";
            var pointer = picture.AddComponent<MinimapPointer>();
            pointer.Setup(minimap, MinimapSize, mapSize, commands.MoveTo);
            for (var i = 0; i < cameraEdges.Length; i++) cameraEdges[i] = Fill(minimap, "Edge", Color.white, art.White);
        }

        // Shown only once zoomed in: with the whole map on screen it would
        // repeat what is beside it. Never on a phone, where it would take a
        // good part of a small screen, and a pinch out and a tap on a
        // settlement goes anywhere it could.
        private void RefreshMinimap(Rect visible, IReadOnlyList<Vector2> settlements, IReadOnlyList<Vector2> bands, float mapWidth, float mapHeight)
        {
            var whole = visible.xMin <= 0f && visible.yMin <= 0f && visible.xMax >= mapWidth && visible.yMax >= mapHeight;
            var shown = !whole && !Application.isMobilePlatform;
            minimapCard.gameObject.SetActive(shown);
            if (!shown) return;
            var sx = MinimapSize / mapWidth;
            var sy = MinimapSize / mapHeight;
            var left = Mathf.Clamp(visible.xMin * sx, 0f, MinimapSize);
            var right = Mathf.Clamp(visible.xMax * sx, 0f, MinimapSize);
            var bottom = Mathf.Clamp(visible.yMin * sy, 0f, MinimapSize);
            var top = Mathf.Clamp(visible.yMax * sy, 0f, MinimapSize);
            Place(cameraEdges[0], left, bottom, right - left, 1f);
            Place(cameraEdges[1], left, Mathf.Max(bottom, top - 1f), right - left, 1f);
            Place(cameraEdges[2], left, bottom, 1f, top - bottom);
            Place(cameraEdges[3], Mathf.Max(left, right - 1f), bottom, 1f, top - bottom);

            var used = 0;
            for (var i = 0; i < settlements.Count; i++) ShowDot(ref used, settlements[i], sx, sy, new Color(1f, 0.95f, 0.8f));
            for (var i = 0; i < bands.Count; i++) ShowDot(ref used, bands[i], sx, sy, new Color(0.9f, 0.2f, 0.2f));
            for (var i = used; i < dots.Count; i++) dots[i].gameObject.SetActive(false);
        }

        private void ShowDot(ref int used, Vector2 world, float sx, float sy, Color colour)
        {
            if (used == dots.Count) dots.Add(Fill(minimap, "Dot", colour, art.White));
            var dot = dots[used++];
            dot.gameObject.SetActive(true);
            dot.color = colour;
            Place(dot, Mathf.Round(world.x * sx) - 1f, Mathf.Round(world.y * sy) - 1f, 3f, 3f);
        }

        // Places `image` by its bottom left corner, in minimap pixels from the
        // bottom left; the panel's own layout counts from the top left.
        private void Place(Image image, float x, float yFromBottom, float w, float h)
        {
            var rect = image.rectTransform;
            rect.anchoredPosition = new Vector2(x, -(MinimapSize - yFromBottom - h));
            rect.sizeDelta = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
        }

        // ---- Stock ----

        // An icon and its count, side by side. LayOutBar spaces the four along
        // the group by how wide each count is.
        private Label BuildStock(int index, Sprite icon, out Tip tip)
        {
            var iconRect = Child(stockGroup, "Icon", 0f, (BarHeight - 16f) / 2f - 1f, 16f, 16f);
            stockIcons[index] = iconRect;
            var image = iconRect.gameObject.AddComponent<Image>();
            image.sprite = icon;
            if (icon == null) image.color = Faint;
            tip = iconRect.gameObject.AddComponent<Tip>();
            tip.Hud = this;
            var number = new Label(stockGroup, art, TextAlign.Left, Ink);
            number.Place(0f, 9f, 80f);
            stockNumbers[index] = number;
            return number;
        }

        // Whose stock it is shows above it in the selection card.
        private void RefreshStock(HudState s)
        {
            stockGroup.gameObject.SetActive(s.HasCommunity);
            if (!s.HasCommunity) return;
            if (!Changed(ref stockSeen, (s.Food, s.Grain, s.Wood, s.Stone, s.FoodFree, s.GrainFree, s.WoodFree, s.StoneFree, s.DaysOfFood))) return;
            foodNumber.Value = s.Food.ToString();
            grainNumber.Value = s.Grain.ToString();
            woodNumber.Value = s.Wood.ToString();
            stoneNumber.Value = s.Stone.ToString();
            // Days counts the Grain too, at the meals it mills into.
            var days = s.DaysOfFood == int.MaxValue ? "" : ", enough for " + s.DaysOfFood + (s.DaysOfFood == 1 ? " day" : " days") + (s.GrainFree > 0 ? " with the Grain" : "");
            foodTip.Text = StockTip("Food", s.Food, s.FoodFree) + days;
            grainTip.Text = StockTip("Grain", s.Grain, s.GrainFree) + ", " + PrimitiveTier.MealsPerGrain + " meals each milled";
            woodTip.Text = StockTip("Wood", s.Wood, s.WoodFree);
            stoneTip.Text = StockTip("Stone", s.Stone, s.StoneFree);
        }

        // The total the community owns; when some is reserved, carried or in
        // use, how much of it is free.
        private static string StockTip(string name, int total, int free) =>
            free == total ? name + ": " + total : name + ": " + total + " (" + free + " free)";

        // ---- People ----

        private Label BarText(RectTransform parent, float x)
        {
            var label = new Label(parent, art, TextAlign.Left, Ink);
            label.Place(x, 9f, 100f);
            return label;
        }

        // A villager's head: skin and hair, two layers of the one sheet.
        private RectTransform PeopleIcon(RectTransform parent, out Tip tip)
        {
            var rect = Child(parent, "People", 0f, (BarHeight - 16f) / 2f - 1f, 16f, 16f);
            var skin = rect.gameObject.AddComponent<Image>();
            skin.sprite = art.HeadSkin;
            if (art.HeadSkin == null) skin.color = Faint;
            else
            {
                var hair = Child(rect, "Hair", 0f, 0f, 16f, 16f).gameObject.AddComponent<Image>();
                hair.sprite = art.HeadHair;
                hair.raycastTarget = false;
            }
            tip = rect.gameObject.AddComponent<Tip>();
            tip.Hud = this;
            return rect;
        }

        private void RefreshPeople(HudState s)
        {
            if (Changed(ref peopleSeen, s.People)) peopleCount.Value = s.People.ToString();
        }

        // The date, the world's people after it, and the speed left of its
        // buttons, on one row when they fit. Narrower, the people and the
        // speed drop to a second row, under the date and under the buttons;
        // narrower still, the date drops too, leaving the first row to the
        // speed and its buttons.
        private void LayOutBar()
        {
            var nameWidth = art.WidthOf(peopleName.Value);
            var peopleWidth = PeopleWidth;
            var dateWidth = DateWidth;
            var width = safeRoot.rect.width;

            var oneRow = OneRow(width);
            var dateFirst = oneRow || dateWidth <= width - RightButtons;
            barHeight = oneRow ? BarHeight : TwoRowHeight;
            bar.sizeDelta = new Vector2(0f, barHeight);

            var secondRow = -SecondRow;
            date.Rect.anchoredPosition = new Vector2(Pad, dateFirst ? -9f : secondRow - 9f);
            peopleGroup.anchoredPosition = oneRow ? new Vector2(dateWidth, 0f) : dateFirst ? new Vector2(Pad, secondRow) : new Vector2(dateWidth, secondRow);
            peopleIcon.anchoredPosition = new Vector2(nameWidth + 6f, peopleIcon.anchoredPosition.y);
            peopleCount.Rect.anchoredPosition = new Vector2(nameWidth + 6f + 18f, peopleCount.Rect.anchoredPosition.y);
            speed.Rect.anchoredPosition = oneRow || !dateFirst ? new Vector2(-(RightButtons + ButtonGap), -9f) : new Vector2(-Pad, secondRow - 9f);
        }

        // Whether the date, the people and the speed with its buttons share
        // one row of a bar `width` art pixels wide.
        private bool OneRow(float width) => DateWidth + PeopleWidth <= width - RightControls;

        private float DateWidth => Pad + art.WidthOf(date.Value) + BarGap;

        private float PeopleWidth => art.WidthOf(peopleName.Value) + 6f + 18f + art.WidthOf(peopleCount.Value);

        // The four stock entries, left to right, onto as many rows of the
        // card's width as they need. Returns the height they take.
        private float LayOutStock()
        {
            float x = 0f, y = 0f;
            for (var i = 0; i < stockIcons.Length; i++)
            {
                var width = 18f + art.WidthOf(stockNumbers[i].Value);
                if (x > 0f && x + width > CardInner)
                {
                    x = 0f;
                    y += StockRow;
                }
                stockIcons[i].anchoredPosition = new Vector2(x, -y);
                stockNumbers[i].Rect.anchoredPosition = new Vector2(x + 18f, -(y + 5f));
                x += width + StockGap;
            }
            return y + StockRow;
        }

        // ---- Names over the map ----

        // One name plate over each community while the whole map shows, in the
        // panel's frame and font so it belongs to the same interface.
        private void RefreshMapLabels()
        {
            var count = view.LabelCount;
            var scale = Pixel;
            for (var i = 0; i < count; i++)
            {
                if (i == communityPlates.Count)
                {
                    var plate = Frame(mapLabels, "Plate", art.Panel, 0f, 0f, 0f, PlateHeight, Tint(false));
                    plate.GetComponent<Image>().raycastTarget = false;
                    var made = new Label(plate, art, TextAlign.Left, Ink);
                    made.Place(Pad, 7f, 200f);
                    if (art.Tail != null)
                    {
                        var tail = Child(plate, "Tail", 0f, PlateHeight - 2f, HudArt.TailWidth, HudArt.TailHeight);
                        var tailImage = tail.gameObject.AddComponent<Image>();
                        tailImage.sprite = art.Tail;
                        tailImage.raycastTarget = false;
                    }
                    communityPlates.Add(plate);
                    communityLabels.Add(made);
                    plateSeen.Add((ulong.MaxValue, EntityKind.None, -1));
                }
                view.LabelAt(i, out var id, out var size, out var screen, out var foot);
                var label = communityLabels[i];
                var plateRect = communityPlates[i];

                // A marker off the screen has no plate: pinning it to an edge
                // would point at somewhere else.
                var markerX = screen.x / scale;
                var markerY = (Screen.height - screen.y) / scale;
                var onScreen = markerX >= 0f && markerX <= canvasRect.rect.width && markerY >= 0f && markerY <= canvasRect.rect.height;
                plateRect.gameObject.SetActive(onScreen);
                if (!onScreen) continue;

                var seen = plateSeen[i];
                var now = (id.Value, id.Kind, size);
                if (!seen.Equals(now))
                {
                    plateSeen[i] = now;
                    var text = (id.Kind == EntityKind.Settlement ? "Settlement " : "Band ") + id.Value + ": " + size;
                    label.Value = text;
                    plateRect.sizeDelta = new Vector2(art.WidthOf(text) + 2f * Pad, PlateHeight);
                }

                // Centred over the marker, but kept on the screen: near an edge the
                // plate slides in and its tail keeps pointing at the marker.
                var plateWidth = plateRect.sizeDelta.x;
                var left = Mathf.Clamp(markerX - plateWidth / 2f, 2f, Mathf.Max(2f, canvasRect.rect.width - plateWidth - 2f));
                // With no room above a marker below the top bar, the plate goes
                // below it and its tail points up: the bar covers the labels.
                var above = markerY - PlateHeight - PlateGap;
                var below = above < TopInset / scale + 2f;
                var top = below ? (Screen.height - foot.y) / scale + PlateGap : above;
                plateRect.anchoredPosition = new Vector2(Mathf.Round(left), -Mathf.Round(top));
                if (art.Tail != null)
                {
                    var tail = (RectTransform)plateRect.Find("Tail");
                    var tailLeft = Mathf.Clamp(markerX - left - HudArt.TailWidth / 2f, 4f, Mathf.Max(4f, plateWidth - HudArt.TailWidth - 4f));
                    // Flipped, the tail grows upward from its top edge.
                    tail.localScale = new Vector3(1f, below ? -1f : 1f, 1f);
                    tail.anchoredPosition = new Vector2(Mathf.Round(tailLeft), below ? -2f : -(PlateHeight - 2f));
                }
            }
            for (var i = count; i < communityPlates.Count; i++) communityPlates[i].gameObject.SetActive(false);
        }

        // ---- Selection ----

        private void RefreshSelection(HudState s)
        {
            var none = s.SelectedKind == EntityKind.None;
            hint1.Object.SetActive(none);
            hint2.Object.SetActive(none);
            selectionName.Object.SetActive(s.SelectedKind == EntityKind.Person);
            selectionTitle.Object.SetActive(!none);
            selectionLine.Object.SetActive(!none);
            selectionStatus.Object.SetActive(s.SelectedKind == EntityKind.Person);
            selectionCommunity.Object.SetActive(s.SelectedKind == EntityKind.Person && s.HasCommunity);
            healthBar.gameObject.SetActive(s.SelectedKind == EntityKind.Person);
            buttonRow.SetActive(!none);
            stockGroup.gameObject.SetActive(!none && s.HasCommunity);
            cardWidth = Upright ? safeRoot.rect.width - 2f * Gap : ColumnWidth;
            if (none)
            {
                selectionCard.sizeDelta = new Vector2(cardWidth, 36f);
                return;
            }

            var person = s.SelectedKind == EntityKind.Person;
            var inner = CardInner;
            if (Changed(ref selectionSeen, (s.SelectedId, s.SelectedKind, s.Years, s.Stage, s.Job, s.Working, s.GoingHome, s.SelectedPeople, s.CommunityId, s.CommunityKind)))
            {
                if (person)
                {
                    selectionName.Value = "Person " + s.SelectedId;
                    selectionTitle.Value = s.Stage + ", " + s.Years + " yrs";
                    selectionLine.Value = s.Job == JobKind.None ? "No job" : s.Job.ToString();
                    selectionStatus.Value = s.GoingHome ? "Going home" : s.Working ? "Working" : "Idle";
                    selectionCommunity.Value = (s.CommunityKind == EntityKind.Settlement ? "Settlement " : "Band ") + s.CommunityId;
                }
                else
                {
                    selectionTitle.Value = (s.SelectedKind == EntityKind.Settlement ? "Settlement " : "Band ") + s.SelectedId;
                    selectionLine.Value = s.SelectedPeople + " people";
                }
            }

            // A person has a name line above the rest; Core gives no names yet, so
            // it is their id, as a band or a settlement is named by its own.
            selectionName.Place(Pad, 7f, inner);
            var y = person ? 17f : 7f;
            selectionTitle.Place(Pad, y, inner);
            selectionLine.Place(Pad, y + 10f, inner);
            y += 20f;
            if (person && Upright)
            {
                // Across the screen there is room for the job, what they are
                // doing and their community on the one line, which keeps the
                // card low enough to leave the map most of the screen.
                y -= 10f;
                var x = Pad + art.WidthOf(selectionLine.Value) + BarGap;
                selectionStatus.Place(x, y, inner);
                selectionCommunity.Place(x + art.WidthOf(selectionStatus.Value) + BarGap, y, inner);
                y += 10f;
            }
            else if (person)
            {
                selectionStatus.Place(Pad, y, inner);
                y += 10f;
                if (s.HasCommunity)
                {
                    selectionCommunity.Place(Pad, y, inner);
                    y += 10f;
                }
            }
            if (person)
            {
                healthBar.anchoredPosition = new Vector2(Pad, -(y + 1f));
                healthBar.sizeDelta = new Vector2(inner, 5f);
                healthBar.Find("Back").GetComponent<RectTransform>().sizeDelta = new Vector2(inner, 5f);
                y += 9f;
            }
            if (s.HasCommunity)
            {
                stockGroup.anchoredPosition = new Vector2(Pad, -y);
                y += LayOutStock();
            }

            followButton.anchoredPosition = new Vector2(Pad, -(y + 1f));
            followButton.sizeDelta = new Vector2(inner, ButtonSize);
            followLabel.Rect.sizeDelta = new Vector2(inner - 2f, HudArt.LineHeight);
            followLabel.Value = s.Following ? "Stop following" : "Follow";
            followTip.Text = s.Following ? "Stop following (F)" : "Follow the selection (F)";
            selectionCard.sizeDelta = new Vector2(cardWidth, y + 1f + ButtonSize + Pad);

            if (person)
            {
                // Eased, so a health change reads as movement rather than a jump.
                var target = Mathf.Clamp01(s.Health / 100f);
                shownHealth = Mathf.Abs(shownHealth - target) < 0.002f ? target : Mathf.Lerp(shownHealth, target, 1f - Mathf.Exp(-BarEase * Time.unscaledDeltaTime));
                var fill = healthFill.rectTransform;
                fill.sizeDelta = new Vector2(Mathf.Round(inner * shownHealth), 5f);
                healthFill.color = Color.Lerp(new Color(0.85f, 0.2f, 0.2f), new Color(0.35f, 0.75f, 0.3f), shownHealth);
                if (s.Health != shownHealthTip)
                {
                    shownHealthTip = s.Health;
                    healthTip.Text = "Health " + s.Health + " of 100";
                }
            }
        }

        // ---- Debug ----

        private void RefreshDebug(HudState s)
        {
            debugCard.gameObject.SetActive(debug);
            if (!debug) return;
            if (!Changed(ref debugSeen, (s.Seed, s.People, s.Settlements, s.HashYear, s.Hash))) return;
            debugLines[0].Value = "Seed " + s.Seed;
            debugLines[1].Value = "Map " + s.MapWidth + "x" + s.MapHeight;
            debugLines[2].Value = "People " + s.People;
            debugLines[3].Value = "Settlements " + s.Settlements;
            debugLines[4].Value = "Hash at year " + s.HashYear;
            debugLines[5].Value = s.Hash.ToString("x16");
        }

        // ---- Cards ----

        // A screen taller than it is wide has no room beside the map, so the
        // selection goes along the bottom instead.
        private static bool Upright => Screen.height > Screen.width;

        // The cards, stacked down the left, with the minimap in the bottom
        // right corner. On an upright screen the selection spans the bottom,
        // in reach of a thumb, and the others sit side by side under the bar.
        private void LayOutCards()
        {
            var x = Gap;
            var y = barHeight + Gap;
            blocking.Clear();
            blocking.Add(bar);
            foreach (var card in cards)
            {
                if (!card.gameObject.activeSelf) continue;
                blocking.Add(card);
                if (Upright && card == selectionCard)
                {
                    card.anchoredPosition = new Vector2(Gap, -(safeRoot.rect.height - Gap - card.sizeDelta.y));
                    continue;
                }
                // Wide, the minimap takes the bottom right corner of the map
                // rather than a place in the column, which is then short enough
                // for the panel's full size on a phone held sideways.
                if (!Upright && card == minimapCard)
                {
                    card.anchoredPosition = new Vector2(safeRoot.rect.width - Gap - ColumnWidth, -(safeRoot.rect.height - Gap - MinimapCardHeight));
                    continue;
                }
                card.anchoredPosition = new Vector2(x, -y);
                if (Upright) x += ColumnWidth + Gap;
                else y += card.sizeDelta.y + Gap;
            }
        }

        // ---- Tooltips ----

        // True while the tooltip on show is `tip`'s.
        public bool IsShowing(Tip tip) => hovered == tip && tooltip.gameObject.activeSelf;

        public void Hover(Tip tip, bool on, bool touch)
        {
            if (on)
            {
                hovered = tip;
                hoveredFor = touch ? -0.45f : -0.1f;
            }
            else if (hovered == tip)
            {
                hovered = null;
                if (tooltip != null) tooltip.gameObject.SetActive(false);
            }
        }

        private void RefreshTooltip()
        {
            if (hovered == null || string.IsNullOrEmpty(hovered.Text)) return;
            hoveredFor += Time.unscaledDeltaTime;
            if (hoveredFor < 0.35f) return;
            tooltipText.Value = hovered.Text;
            var width = art.WidthOf(hovered.Text) + 2f * Pad;
            tooltip.sizeDelta = new Vector2(width, 22f);
            tooltip.gameObject.SetActive(true);
            // Beside the thing it names, kept on the screen.
            var anchor = RectTransformUtility.WorldToScreenPoint(null, hovered.transform.position);
            var safe = SafeArea;
            var safeSize = safeRoot.rect.size;
            var at = (new Vector2(anchor.x, anchor.y) - new Vector2(safe.xMin, safe.yMin)) / Pixel;
            var x = Mathf.Clamp(at.x - width / 2f, 2f, Mathf.Max(2f, safeSize.x - width - 2f));
            var yTop = Mathf.Clamp(safeSize.y - at.y + 12f, 2f, safeSize.y - 24f);
            tooltip.anchoredPosition = new Vector2(x, -yTop);
        }

        // ---- Building blocks ----

        private static bool Changed<T>(ref T last, T value) where T : struct, IEquatable<T>
        {
            if (last.Equals(value)) return false;
            last = value;
            return true;
        }

        // Flat frames are as light as the art's, so the dark text reads on them.
        private Color Tint(bool button) =>
            flat ? (button ? new Color(0.72f, 0.76f, 0.88f) : new Color(0.89f, 0.65f, 0.45f)) : Color.white;

        private RectTransform Card(string name, float x, float height)
        {
            var card = Frame(safeRoot, name, art.Panel, x, 0f, ColumnWidth, height, Tint(false));
            return card;
        }

        private RectTransform Frame(Transform parent, string name, Sprite sprite, float x, float y, float w, float h, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = colour;
            return rect;
        }

        private static RectTransform Child(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Image Fill(RectTransform parent, string name, Color colour, Sprite white)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = parent.rect.size == Vector2.zero ? parent.sizeDelta : parent.rect.size;
            var image = go.GetComponent<Image>();
            image.sprite = white;
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        private Label Text(RectTransform parent, float x, float y, float width, Color colour, TextAlign align = TextAlign.Left)
        {
            var label = new Label(parent, art, align, colour);
            label.Place(x, y, width);
            return label;
        }

        private RectTransform MakeButton(RectTransform parent, float x, float y, float w, float h, Sprite icon, string text, string tip, Action onClick, out Label label)
            => MakeButton(parent, x, y, w, h, icon, text, tip, onClick, out label, out _);

        private RectTransform MakeButton(RectTransform parent, float x, float y, float w, float h, Sprite icon, string text, string tip, Action onClick, out Label label, out Image iconImage)
        {
            var rect = Frame(parent, text ?? "Button", art.Button, x, y, w, h, Tint(true));
            var image = rect.GetComponent<Image>();
            // A finger is wider than the art: the hit area reaches 4 px past the
            // frame all round, so a 16 px button is 24 across, 48dp at 2dp a
            // pixel, and the bar's 8 px gaps keep neighbours from overlapping.
            image.raycastPadding = new Vector4(-4f, -4f, -4f, -4f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = new ColorBlock
            {
                normalColor = new Color(0.9f, 0.9f, 0.95f),
                highlightedColor = Color.white,
                pressedColor = new Color(0.65f, 0.65f, 0.75f),
                selectedColor = new Color(0.9f, 0.9f, 0.95f),
                disabledColor = new Color(0.5f, 0.5f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.05f,
            };
            if (onClick != null) button.onClick.AddListener(() => onClick());
            var hint = rect.gameObject.AddComponent<Tip>();
            hint.Hud = this;
            hint.Text = tip;

            label = null;
            iconImage = null;
            if (icon != null)
            {
                var iconRect = Child(rect, "Icon", (w - 16f) / 2f, (h - 16f) / 2f, 16f, 16f);
                iconImage = iconRect.gameObject.AddComponent<Image>();
                iconImage.sprite = icon;
                iconImage.raycastTarget = false;
            }
            if (text != null)
            {
                label = new Label(rect, art, TextAlign.Centre, Ink);
                label.Rect.anchorMin = label.Rect.anchorMax = new Vector2(0.5f, 0.5f);
                label.Rect.pivot = new Vector2(0.5f, 0.5f);
                label.Rect.anchoredPosition = new Vector2(0f, 0f);
                label.Rect.sizeDelta = new Vector2(w - 2f, HudArt.LineHeight);
                label.Value = text;
            }
            return rect;
        }

        private static void EnsureEventSystem()
        {
            var existing = EventSystem.current;
            if (existing != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var system = go.GetComponent<EventSystem>();
            // A click leaves its button selected, and Space would then press
            // it again: the keyboard shortcuts are the driver's.
            system.sendNavigationEvents = false;
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}
