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
        public bool Following;

        // The community whose stock the resource card shows: the selected
        // settlement or band, or a selected person's own.
        public bool HasCommunity;
        public EntityKind CommunityKind;
        public ulong CommunityId;
        public int Food, Wood, Stone, DaysOfFood;
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
        public Action Slower, Faster, TogglePause, WholeMap, ToggleFollow, Deselect;

        // The minimap was clicked: a point in world units.
        public Action<Vector2> MoveTo;
    }

    public interface IPointerBlocker
    {
        // `screenPoint` is in screen pixels, y up.
        bool Blocks(Vector2 screenPoint);
    }

    // The panel, in uGUI (#128): a bar across the top with the date and the
    // speed controls, and a column down the left with a minimap, the stock of
    // the selected community and a card for the selection. Everything is laid
    // out in art pixels and drawn at a whole number of screen pixels each, so
    // the pack's art stays crisp. Built in code, as ArtSet builds the map's
    // sprites, so nothing depends on a prefab that could drift from it.
    public sealed class Hud : IPointerBlocker
    {
        public const float BarHeight = 26f;
        private const float Gap = 3f;
        private const float ColumnWidth = 104f;
        private const float Pad = 8f;
        private const float InnerWidth = ColumnWidth - 2f * Pad;
        private const int MinimapSize = 90;
        private const float ButtonSize = 16f;
        private const float BarEase = 8f;
        private const float MinimapCardHeight = 124f;
        private const float PlateHeight = 19f;

        // The width of each of the stock's three entries, the room the speed
        // controls take on the bar's right, and the gap between its groups.
        private const float StockStride = 40f;
        private const float RightControls = 124f;
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

        // Top bar.
        private readonly RectTransform bar;
        private readonly Label date, speed;
        private Image pauseIcon;
        private Label pauseLabel;
        private long dateKey = -1, speedKey = -1;
        private bool shownPaused = true;

        // Minimap.
        private readonly RectTransform minimapCard;
        private readonly RectTransform minimap;
        private readonly Image[] cameraEdges = new Image[4];
        private readonly List<Image> dots = new List<Image>();

        // The selected community's stock, in the bar.
        private readonly RectTransform stockGroup;
        private readonly Label foodNumber, woodNumber, stoneNumber;
        private readonly Tip foodTip, woodTip, stoneTip;
        private long stockKey = -1;

        // People: the whole world's, and the selected community's with its name.
        private readonly RectTransform worldGroup, communityGroup, communityIcon;
        private readonly Label worldCount, communityName, communityCount;
        private readonly Tip communityTip;
        private long worldKey = -1, communityKey = -1;

        // Names over the communities at Far zoom.
        private readonly WorldView2D view;
        private readonly RectTransform mapLabels;
        private readonly List<RectTransform> communityPlates = new List<RectTransform>();
        private readonly List<Label> communityLabels = new List<Label>();

        // Selection card.
        private readonly RectTransform selectionCard;
        private readonly Label selectionTitle, selectionLine, selectionCommunity, hint1, hint2;
        private readonly RectTransform healthBar;
        private readonly Image healthFill;
        private readonly Tip healthTip;
        private readonly GameObject buttonRow;
        private readonly RectTransform followButton, clearButton;
        private readonly Label followLabel;
        private readonly RectTransform[] cards;
        private long selectionKey = -1;
        private float shownHealth;

        // Debug card, shown from the bar's toggle or F3.
        private readonly RectTransform debugCard;
        private readonly Label[] debugLines = new Label[5];
        private long debugKey = -1;
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

            // The top bar.
            bar = Frame(root.transform, "Bar", art.Panel, 0f, 0f, 0f, BarHeight, Tint(false));
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, BarHeight);
            blocking.Add(bar);
            date = new Label(bar, art, TextAlign.Left, Ink);
            date.Place(Pad, 9f, 130f);
            speed = AddBarControls();
            worldGroup = Child(bar, "World", 0f, 0f, 60f, BarHeight);
            PeopleIcon(worldGroup, out var worldTip);
            worldTip.Text = "People in the world";
            worldCount = BarText(worldGroup, 18f);
            communityGroup = Child(bar, "Community", 0f, 0f, 200f, BarHeight);
            communityName = BarText(communityGroup, 0f);
            communityIcon = PeopleIcon(communityGroup, out communityTip);
            communityCount = BarText(communityGroup, 0f);
            stockGroup = Child(bar, "Stock", 0f, 0f, 3f * StockStride, BarHeight);
            foodNumber = BuildStock(0f, art.Berries, out foodTip);
            woodNumber = BuildStock(StockStride, art.Wood, out woodTip);
            stoneNumber = BuildStock(2f * StockStride, art.Stone, out stoneTip);

            // The column.
            minimapCard = Card("Minimap", 0f, MinimapCardHeight);
            minimap = Child(minimapCard, "Map", Pad, Pad - 1f, MinimapSize, MinimapSize);
            BuildMinimap(view, mapSize);
            MakeButton(minimapCard, Pad, Pad + MinimapSize + 1f, MinimapSize, ButtonSize, null, "Whole map", "Show the whole map (Home)", commands.WholeMap, out _);

            selectionCard = Card("Selection", 0f, 70f);
            selectionTitle = Text(selectionCard, Pad, 7f, InnerWidth, Ink);
            selectionLine = Text(selectionCard, Pad, 7f + 10f, InnerWidth, Ink);
            selectionCommunity = Text(selectionCard, Pad, 7f + 20f, InnerWidth, Ink);
            hint1 = Text(selectionCard, Pad, 7f, InnerWidth, Ink);
            hint2 = Text(selectionCard, Pad, 7f + 10f, InnerWidth, Ink);
            hint1.Value = "Tap a person, or";
            hint2.Value = "a settlement.";
            healthBar = Child(selectionCard, "Health", Pad, 7f + 31f, InnerWidth, 5f);
            Fill(healthBar, "Back", Faint, art.White);
            healthFill = Fill(healthBar, "Fill", new Color(0.85f, 0.25f, 0.25f), art.White);
            healthTip = healthBar.gameObject.AddComponent<Tip>();
            healthTip.Hud = this;
            buttonRow = new GameObject("Buttons", typeof(RectTransform));
            var row = (RectTransform)buttonRow.transform;
            row.SetParent(selectionCard, false);
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = Vector2.zero;
            row.sizeDelta = new Vector2(ColumnWidth, 140f);
            var half = (InnerWidth - 2f) / 2f;
            followButton = MakeButton(row, Pad, 7f + 41f, half, ButtonSize, null, "Follow", "Follow the selection (F)", commands.ToggleFollow, out followLabel);
            clearButton = MakeButton(row, Pad + half + 2f, 7f + 41f, half, ButtonSize, null, "Clear", "Clear the selection (Esc)", commands.Deselect, out _);

            debugCard = Card("Debug", 0f, 14f + debugLines.Length * 9f);
            for (var i = 0; i < debugLines.Length; i++) debugLines[i] = Text(debugCard, Pad, 7f + i * 9f, InnerWidth, Ink);
            cards = new[] { minimapCard, selectionCard, debugCard };

            // The tooltip floats above everything, and takes no clicks.
            tooltip = Frame(root.transform, "Tooltip", art.Panel, 0f, 0f, 60f, 22f, Tint(false));
            tooltip.GetComponent<Image>().raycastTarget = false;
            tooltipText = new Label(tooltip, art, TextAlign.Left, Ink);
            tooltipText.Place(Pad, 7f, 200f);
            tooltip.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
        }

        // Screen pixels to an art pixel: the shorter side holds about 360 of
        // them, rounded to whole pixels so every art pixel is one flat square.
        public float Pixel => Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) / 360f));

        // The footprint of what the camera must keep the map clear of, in GUI
        // coordinates (origin top-left, y down), and the bar across the top.
        public Rect Reserved => new Rect(0f, 0f, (ColumnWidth + 2f * Gap) * Pixel, (BarHeight + Gap + MinimapCardHeight + Gap) * Pixel);

        public float TopInset => BarHeight * Pixel;

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
            scaler.scaleFactor = Pixel;

            RefreshBar(state);
            RefreshMinimap(visible, settlements, bands, mapWidth, mapHeight);
            RefreshStock(state);
            RefreshPeople(state);
            LayOutBar(state);
            RefreshMapLabels();
            RefreshSelection(state);
            RefreshDebug(state);
            LayOutColumn();
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
            right += ButtonSize + 2f;
            var faster = MakeButton(bar, 0f, y, ButtonSize, ButtonSize, null, ">>", "Faster (.)", commands.Faster, out _);
            AnchorRight(faster, right);
            right += ButtonSize + 2f;
            var pauseButton = MakeButton(bar, 0f, y, ButtonSize, ButtonSize, art.Pause, art.Pause == null ? "II" : null, "Pause (Space)", commands.TogglePause, out pauseLabel, out pauseIcon);
            AnchorRight(pauseButton, right);
            right += ButtonSize + 2f;
            var slower = MakeButton(bar, 0f, y, ButtonSize, ButtonSize, null, "<<", "Slower (,)", commands.Slower, out _);
            AnchorRight(slower, right);
            right += ButtonSize + 4f;
            var speedLabel = new Label(bar, art, TextAlign.Right, Ink);
            speedLabel.Rect.anchorMin = speedLabel.Rect.anchorMax = new Vector2(1f, 1f);
            speedLabel.Rect.pivot = new Vector2(1f, 1f);
            speedLabel.Rect.anchoredPosition = new Vector2(-right, -9f);
            speedLabel.Rect.sizeDelta = new Vector2(50f, HudArt.LineHeight);
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

        private void RefreshMinimap(Rect visible, IReadOnlyList<Vector2> settlements, IReadOnlyList<Vector2> bands, float mapWidth, float mapHeight)
        {
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

        // An icon and its count, side by side, `x` along the group.
        private Label BuildStock(float x, Sprite icon, out Tip tip)
        {
            var iconRect = Child(stockGroup, "Icon", x, (BarHeight - 16f) / 2f - 1f, 16f, 16f);
            var image = iconRect.gameObject.AddComponent<Image>();
            image.sprite = icon;
            if (icon == null) image.color = Faint;
            tip = iconRect.gameObject.AddComponent<Tip>();
            tip.Hud = this;
            var number = new Label(stockGroup, art, TextAlign.Left, Ink);
            number.Place(x + 18f, 9f, StockStride - 18f);
            return number;
        }

        // Whose stock it is shows in the selection card beneath, so the bar
        // keeps to the numbers.
        private void RefreshStock(HudState s)
        {
            stockGroup.gameObject.SetActive(s.HasCommunity);
            if (!s.HasCommunity) return;
            var key = ((s.Food * 31L + s.Wood) * 31L + s.Stone) * 31L + s.DaysOfFood;
            if (!Changed(ref stockKey, key)) return;
            foodNumber.Value = s.Food.ToString();
            woodNumber.Value = s.Wood.ToString();
            stoneNumber.Value = s.Stone.ToString();
            foodTip.Text = s.DaysOfFood == int.MaxValue ? "Food: " + s.Food : "Food: " + s.Food + ", enough for " + s.DaysOfFood + (s.DaysOfFood == 1 ? " day" : " days");
            woodTip.Text = "Wood: " + s.Wood;
            stoneTip.Text = "Stone: " + s.Stone;
        }

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
            if (Changed(ref worldKey, s.People)) worldCount.Value = s.People.ToString();
            communityGroup.gameObject.SetActive(s.HasCommunity);
            if (!s.HasCommunity) return;
            var key = ((long)s.CommunityId * 2L + (s.CommunityKind == EntityKind.Settlement ? 1L : 0L)) * 100003L + s.CommunityPeople;
            if (!Changed(ref communityKey, key)) return;
            var name = (s.CommunityKind == EntityKind.Settlement ? "Settlement " : "Band ") + s.CommunityId;
            communityName.Value = name;
            communityCount.Value = s.CommunityPeople.ToString();
            communityTip.Text = "People in " + name;
        }

        // The bar's groups, left to right after the date: the world's people,
        // the community with its people, then its stock. The world's go first
        // when a narrow screen runs out of room.
        private void LayOutBar(HudState s)
        {
            var limit = canvasRect.rect.width - RightControls;
            var x = Pad + art.WidthOf(date.Value) + BarGap;
            var community = s.HasCommunity;
            var nameWidth = community ? art.WidthOf(communityName.Value) : 0f;
            var communityWidth = community ? nameWidth + 6f + 18f + art.WidthOf(communityCount.Value) : 0f;
            var worldWidth = 18f + art.WidthOf(worldCount.Value);
            var rest = community ? communityWidth + BarGap + 3f * StockStride : 0f;
            var showWorld = x + worldWidth + BarGap + rest <= limit;
            worldGroup.gameObject.SetActive(showWorld);
            if (showWorld)
            {
                worldGroup.anchoredPosition = new Vector2(x, 0f);
                x += worldWidth + BarGap;
            }
            if (!community) return;
            communityGroup.anchoredPosition = new Vector2(x, 0f);
            communityIcon.anchoredPosition = new Vector2(nameWidth + 6f, communityIcon.anchoredPosition.y);
            communityCount.Rect.anchoredPosition = new Vector2(nameWidth + 6f + 18f, communityCount.Rect.anchoredPosition.y);
            stockGroup.anchoredPosition = new Vector2(x + communityWidth + BarGap, 0f);
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
                    communityPlates.Add(plate);
                    communityLabels.Add(made);
                }
                var text = view.LabelAt(i, out var screen);
                var label = communityLabels[i];
                var plateRect = communityPlates[i];
                plateRect.gameObject.SetActive(true);
                if (label.Value != text)
                {
                    label.Value = text;
                    plateRect.sizeDelta = new Vector2(art.WidthOf(text) + 2f * Pad, PlateHeight);
                }
                plateRect.anchoredPosition = new Vector2(Mathf.Round(screen.x / scale - plateRect.sizeDelta.x / 2f), -Mathf.Round((Screen.height - screen.y) / scale - PlateHeight - 2f));
            }
            for (var i = count; i < communityPlates.Count; i++) communityPlates[i].gameObject.SetActive(false);
        }

        // ---- Selection ----

        private void RefreshSelection(HudState s)
        {
            var none = s.SelectedKind == EntityKind.None;
            hint1.Object.SetActive(none);
            hint2.Object.SetActive(none);
            selectionTitle.Object.SetActive(!none);
            selectionLine.Object.SetActive(!none);
            selectionCommunity.Object.SetActive(s.SelectedKind == EntityKind.Person && s.HasCommunity);
            healthBar.gameObject.SetActive(s.SelectedKind == EntityKind.Person);
            buttonRow.SetActive(!none);
            selectionCard.sizeDelta = new Vector2(ColumnWidth, none ? 36f : s.SelectedKind == EntityKind.Person ? 72f : 54f);
            if (none) return;

            var buttonY = s.SelectedKind == EntityKind.Person ? 7f + 41f : 7f + 22f;
            followButton.anchoredPosition = new Vector2(followButton.anchoredPosition.x, -buttonY);
            clearButton.anchoredPosition = new Vector2(clearButton.anchoredPosition.x, -buttonY);
            followLabel.Value = s.Following ? "Stop" : "Follow";

            var person = s.SelectedKind == EntityKind.Person;
            var key = (((long)s.SelectedId * 7L + (int)s.SelectedKind) * 1009L + s.Years) * 13L + (int)s.Stage + ((int)s.Job << 8) + (s.Working ? 1L << 20 : 0L) + s.SelectedPeople * 3571L + (long)s.CommunityId * 104729L + (int)s.CommunityKind;
            if (Changed(ref selectionKey, key))
            {
                if (person)
                {
                    selectionTitle.Value = s.Stage + ", " + s.Years + " yrs";
                    selectionLine.Value = (s.Job == JobKind.None ? "No job" : s.Job.ToString()) + (s.Working ? ", working" : ", idle");
                    selectionCommunity.Value = (s.CommunityKind == EntityKind.Settlement ? "Settlement " : "Band ") + s.CommunityId;
                }
                else
                {
                    selectionTitle.Value = (s.SelectedKind == EntityKind.Settlement ? "Settlement " : "Band ") + s.SelectedId;
                    selectionLine.Value = s.SelectedPeople + " people";
                }
            }

            if (person)
            {
                // Eased, so a health change reads as movement rather than a jump.
                var target = Mathf.Clamp01(s.Health / 100f);
                shownHealth = Mathf.Abs(shownHealth - target) < 0.002f ? target : Mathf.Lerp(shownHealth, target, 1f - Mathf.Exp(-BarEase * Time.unscaledDeltaTime));
                var fill = healthFill.rectTransform;
                fill.sizeDelta = new Vector2(Mathf.Round(InnerWidth * shownHealth), 5f);
                healthFill.color = Color.Lerp(new Color(0.85f, 0.2f, 0.2f), new Color(0.35f, 0.75f, 0.3f), shownHealth);
                healthTip.Text = "Health " + s.Health + " of 100";
            }
        }

        // ---- Debug ----

        private void RefreshDebug(HudState s)
        {
            debugCard.gameObject.SetActive(debug);
            if (!debug) return;
            var key = s.Seed * 1000003L + s.People * 31L + s.Settlements + s.HashYear * 7919L + (long)(s.Hash & 0xFFFFFFFF);
            if (!Changed(ref debugKey, key)) return;
            debugLines[0].Value = "Seed " + s.Seed + " / " + s.MapWidth + "x" + s.MapHeight;
            debugLines[1].Value = "People " + s.People;
            debugLines[2].Value = "Settlements " + s.Settlements;
            debugLines[3].Value = "Hash at year " + s.HashYear;
            debugLines[4].Value = s.Hash.ToString("x16");
        }

        // ---- Column layout ----

        private void LayOutColumn()
        {
            var y = BarHeight + Gap;
            blocking.Clear();
            blocking.Add(bar);
            foreach (var card in cards)
            {
                if (!card.gameObject.activeSelf) continue;
                card.anchoredPosition = new Vector2(Gap, -y);
                y += card.sizeDelta.y + Gap;
                blocking.Add(card);
            }
        }

        // ---- Tooltips ----

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
            var canvasSize = canvasRect.rect.size;
            var at = new Vector2(anchor.x, anchor.y) / Pixel;
            var x = Mathf.Clamp(at.x - width / 2f, 2f, Mathf.Max(2f, canvasSize.x - width - 2f));
            var yTop = Mathf.Clamp(canvasSize.y - at.y + 12f, 2f, canvasSize.y - 24f);
            tooltip.anchoredPosition = new Vector2(x, -yTop);
        }

        // ---- Building blocks ----

        private static bool Changed(ref long last, long key)
        {
            if (last == key) return false;
            last = key;
            return true;
        }

        private Color Tint(bool button) =>
            flat ? (button ? new Color(0.55f, 0.55f, 0.6f, 0.95f) : new Color(0.2f, 0.18f, 0.25f, 0.9f)) : Color.white;

        private RectTransform Card(string name, float x, float height)
        {
            var card = Frame(root.transform, name, art.Panel, x, 0f, ColumnWidth, height, Tint(false));
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
