using System.Collections.Generic;
using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using EntityId = KingdomWatch.Core.Data.EntityId;

namespace KingdomWatch.Game
{
    // Section 16's layers. Which one is showing depends only on how many
    // screen pixels a map cell covers, never on what the simulation is doing.
    public enum ZoomBand
    {
        Far,
        // Roads, districts, farms and buildings belong here (section 16); Core
        // has none yet, so until it does Medium draws what Near draws.
        Medium,
        Near,
    }

    // A running world in 3/4 oblique (#72). With the art submodule (#121) the
    // ground is tiled grass and water with trees and rocks standing on it, and
    // people are paper dolls that idle and walk. Without it, and always at
    // Far zoom, the ground is one coloured pixel per cell and people are
    // upright markers. Either way whoever is further south draws in front,
    // and at Far zoom the people give way to one marker per settlement or
    // band (#115). The art draws the tilt itself, so a row is as tall as a
    // column is wide.
    public sealed class WorldView2D : MonoBehaviour
    {
        // Layer edges in UI-scaled pixels per cell (see SimulationDriver.UiScale),
        // with slack either side so a zoom resting on an edge does not flicker.
        public const float FarBelow = 24f;
        public const float NearFrom = 40f;
        private const float BandSlack = 2f;

        // A community marker's side in UI-scaled pixels, whatever the zoom.
        private const float CommunityMarkerSize = 14f;

        // Doll frames per second.
        private const float FramesPerSecond = 8f;

        // Swaying grass and campfires, in frames per second.
        private const float DecorFramesPerSecond = 6f;

        // Days into a new season before every cell has turned (see LayLand);
        // a season is 30 days.
        private const float SeasonSpreadDays = 8f;

        // Cells per second a cloud drifts at, give or take 40%, and how far
        // past the map's edges it goes before wrapping round.
        private const float CloudSpeed = 0.4f;
        private const float CloudMargin = 3f;

        // Over people and scenery, under the Far layer's community markers.
        private const int CloudOrder = 90000;

        // Tents per camp: one per this many people, up to MaxTents.
        private const int PeoplePerTent = 12;
        private const int MaxTents = 5;

        // Where a camp's tents stand, in cells from its fire: the big tent
        // behind it, the small ones either side and further back.
        private static readonly Vector2[] TentOffsets =
        {
            new Vector2(0f, -1.6f), new Vector2(-2.4f, -0.9f), new Vector2(2.4f, -0.9f), new Vector2(-1.7f, -3f), new Vector2(1.7f, -3f),
        };

        // A settled camp's well, woodpile and stone pile: in front of the
        // fire, clear of the tents.
        private static readonly Vector2[] PropOffsets =
        {
            new Vector2(1.6f, 1.2f), new Vector2(-1.5f, 1.1f), new Vector2(-0.6f, 1.6f),
        };

        public Shader spriteShader;
        public Camera sceneCamera;

        private readonly List<Figure> people = new List<Figure>();
        private readonly List<EntityId> peopleIds = new List<EntityId>();
        private readonly List<SpriteRenderer> communityMarkers = new List<SpriteRenderer>();
        private readonly List<EntityId> communityIds = new List<EntityId>();
        private readonly List<int> communitySizes = new List<int>();
        private readonly List<ICommunity> bands = new List<ICommunity>();
        private readonly List<Object> owned = new List<Object>();
        private ArtSet art;
        private Material spriteMaterial;
        private Texture2D terrainTexture;
        private Sprite unitSprite;
        private SpriteRenderer colourGround;
        private GameObject tiledGround;
        private TerrainGrid grid;
        private Tilemap landMap, shoreMap;
        private Transform flatDecor, clouds;
        // Which shoreline piece each corner takes, by ShoreMask; row-major,
        // (width + 1) by (height + 1).
        private byte[] shoreMasks;
        private bool[] drawnWater;
        // The season every land cell has caught up to, or null while a new
        // one is still spreading; and each cell's and corner's own season,
        // 255 before it is first laid.
        private Season? landSeason;
        private byte[] landSeasons, cornerSeasons;
        private readonly List<(SpriteRenderer Renderer, int Cell)> flowers = new List<(SpriteRenderer, int)>();
        private float animationTime;
        private readonly List<(SpriteRenderer Renderer, Sprite[] Frames, uint Phase)> animated = new List<(SpriteRenderer, Sprite[], uint)>();
        private readonly List<Camp> camps = new List<Camp>();
        private Transform peopleRoot;
        private Transform communityRoot;
        private SpriteRenderer highlight;
        private int width, height;
        private int usedPeople, usedCommunities;
        private float uiScale = 1f;
        private GUIStyle labelStyle;

        public ZoomBand Band { get; private set; } = ZoomBand.Far;

        // Presentation state only: set by ViewInput, read by the panel.
        public EntityId Selected { get; set; }

        public void Show(World world)
        {
            width = world.Grid.Width;
            height = world.Grid.Height;
            spriteMaterial = Own(new Material(spriteShader));

            var white = Own(new Texture2D(1, 1) { filterMode = FilterMode.Point });
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            // Pivot at the feet: a marker stands on its cell rather than centred on it.
            unitSprite = Own(Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0f), 1f));

            terrainTexture = Own(new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            colourGround = new GameObject("Terrain").AddComponent<SpriteRenderer>();
            colourGround.transform.SetParent(transform, false);
            colourGround.sprite = Own(Sprite.Create(terrainTexture, new Rect(0, 0, width, height), Vector2.zero, 1f));
            colourGround.sharedMaterial = spriteMaterial;
            colourGround.sortingOrder = -1;

            peopleRoot = new GameObject("People").transform;
            peopleRoot.SetParent(transform, false);
            communityRoot = new GameObject("Communities").transform;
            communityRoot.SetParent(transform, false);

            highlight = NewMarker("Selection", transform);
            highlight.color = new Color(1f, 0.92f, 0.2f);
            highlight.enabled = false;

            // Terrain is static until bridges (section 12, M6) rewrite cells;
            // then this belongs in Refresh, behind a change check.
            grid = world.Grid;
            shoreMasks = new byte[(width + 1) * (height + 1)];
            landSeasons = new byte[width * height];
            cornerSeasons = new byte[shoreMasks.Length];
            for (var i = 0; i < landSeasons.Length; i++) landSeasons[i] = byte.MaxValue;
            for (var i = 0; i < cornerSeasons.Length; i++) cornerSeasons[i] = byte.MaxValue;
            DrawTerrain(grid);
            art = ArtSet.Load(owned);
            if (art != null) TileTerrain();
            else Debug.Log("WorldView2D: no usable art at Assets/Art (see docs/unity.md); drawing plain markers.");
        }

        // Redraws from the world as it stands. Called every frame, paused or
        // not, so zooming across a layer edge redraws without a sim step.
        // `paused` stops every animation along with the simulation.
        public void Refresh(World world, float pixelsPerCell, float scale, bool paused)
        {
            uiScale = scale;
            Band = BandFor(pixelsPerCell / scale);
            if (!paused) animationTime += Time.unscaledDeltaTime;
            var tiled = art != null && Band != ZoomBand.Far;
            colourGround.enabled = !tiled;
            if (tiledGround != null && tiledGround.activeSelf != tiled) tiledGround.SetActive(tiled);
            if (tiled)
            {
                // The water's own tile animation, which the Tilemap runs.
                landMap.animationFrameRate = paused ? 0f : 1f;
                LayLand(world.Now);
                Animate();
            }
            DrawPeople(world);
            DrawCommunities(world, pixelsPerCell, tiled);
            DrawHighlight();
        }

        private ZoomBand BandFor(float scaledPixelsPerCell)
        {
            var farEdge = Band == ZoomBand.Far ? FarBelow + BandSlack : FarBelow - BandSlack;
            var nearEdge = Band == ZoomBand.Near ? NearFrom - BandSlack : NearFrom + BandSlack;
            if (scaledPixelsPerCell < farEdge) return ZoomBand.Far;
            return scaledPixelsPerCell >= nearEdge ? ZoomBand.Near : ZoomBand.Medium;
        }

        private void DrawPeople(World world)
        {
            var used = 0;
            var now = world.Now;
            var visible = Band != ZoomBand.Far;
            // One clock for every doll, offset per person so a crowd does not
            // step in time. Real time: it is presentation, not simulation.
            var tick = animationTime * FramesPerSecond;
            // The span rather than People.Alive(): Alive allocates an iterator
            // per call, and this runs every frame. Free slots have no id.
            // Placed even when hidden, so following someone survives a zoom out.
            foreach (var record in world.People.RecordSpan())
            {
                if (record.Id.IsNone) continue;
                var person = record.Handle;
                var at = PositionOf(world, person, record.Position, now);
                var figure = FigureAt(used);
                peopleIds[used++] = record.Id;
                // A per-person offset inside the cell keeps a band from
                // collapsing onto one figure. Drawn from the id, not an rng:
                // it is presentation and must never touch the simulation. Not
                // GetHashCode: HashCode.Combine is seeded per process.
                var id = (long)((record.Id.Value * 0x9E3779B97F4A7C15UL) >> 40);
                var jitterX = ((id & 0xff) / 255f - 0.5f) * 0.7f;
                var jitterY = (((id >> 8) & 0xff) / 255f - 0.5f) * 0.7f;
                var cellY = at.Y + 0.5f + jitterY;
                figure.Root.localPosition = new Vector3(OnArtPixel(at.X + 0.5f + jitterX), OnArtPixel(height - cellY), 0f);
                // Further south (larger row) is nearer the viewer, so it draws on top.
                figure.Order = OrderAt(cellY);
                figure.SetVisible(visible);

                if (figure.Doll)
                {
                    if (figure.Job != record.Job || figure.DressedFor != record.Id)
                    {
                        figure.Job = record.Job;
                        figure.DressedFor = record.Id;
                        figure.Look = art.LookOf(record.Job, (uint)(record.Id.Value * 0xD6E8FEB86659FD93UL >> 32));
                    }
                    if (visible) Pose(figure, world, person, now, (int)(tick + (id >> 16)) % ArtSet.FramesPerPose);
                }
                else
                {
                    var child = record.AgeStage == AgeStage.Infant || record.AgeStage == AgeStage.Child;
                    figure.Layers[0].transform.localScale = child ? new Vector3(0.25f, 0.4f, 1f) : new Vector3(0.3f, 0.65f, 1f);
                    figure.Layers[0].color = child ? new Color(0.98f, 0.86f, 0.55f) : ColourOf(record.Job);
                }
            }

            for (var i = used; i < people.Count; i++) people[i].SetVisible(false);
            usedPeople = used;
        }

        // Walking while on the way out or back, facing where they are going;
        // otherwise standing, facing the viewer.
        private void Pose(Figure figure, World world, PersonHandle person, SimulationTime now, int frame)
        {
            var walking = Walking(world, person, now, out var dx, out var dy);
            var facing = ArtSet.Facing.Down;
            var flip = false;
            if (walking)
            {
                if (Mathf.Abs(dx) >= Mathf.Abs(dy))
                {
                    facing = ArtSet.Facing.Side;
                    flip = dx < 0;
                }
                else
                {
                    // Rows count down the map: a larger row is further south.
                    facing = dy > 0 ? ArtSet.Facing.Down : ArtSet.Facing.Up;
                }
            }

            for (var layer = 0; layer < ArtSet.Layers; layer++)
            {
                var renderer = figure.Layers[layer];
                renderer.sprite = art.Frame(figure.Look, layer, walking, facing, frame);
                renderer.flipX = flip;
            }
        }

        private static bool Walking(World world, PersonHandle person, SimulationTime now, out int dx, out int dy)
        {
            dx = dy = 0;
            if (!world.Jobs.HasTask(person)) return false;
            var task = world.Jobs.TaskOf(person);
            var elapsed = now.Ticks - task.Start.Ticks;
            if (elapsed < 0) return false;
            var back = elapsed - task.TravelTicks - task.WorkTicks;
            int sign;
            if (elapsed < task.TravelTicks) sign = 1;
            else if (back >= 0 && back < task.ReturnTicks) sign = -1;
            else return false;
            dx = sign * (task.Destination.X - task.Origin.X);
            dy = sign * (task.Destination.Y - task.Origin.Y);
            return true;
        }

        // A marker per community at Far zoom; with the art, a camp per
        // community at Medium and Near. Settlements camp too, until Core has
        // buildings for them to live in (#23), but with a well, a woodpile
        // and a stone pile that a band on the move would not have.
        private void DrawCommunities(World world, float pixelsPerCell, bool camped)
        {
            var used = 0;
            var visible = Band == ZoomBand.Far;
            var size = CommunityMarkerSize * uiScale / pixelsPerCell;
            var settlements = world.Founding.All;
            for (var i = 0; i < settlements.Count; i++) DrawCommunity(settlements[i], true, size, visible, used++);
            world.Nomads.CopyTrackedTo(bands);
            for (var i = 0; i < bands.Count; i++) DrawCommunity(bands[i], false, size, visible, used++);
            for (var i = used; i < communityMarkers.Count; i++) communityMarkers[i].enabled = false;
            usedCommunities = used;

            if (art == null) return;
            // Settlements were drawn first, so they are the first indices.
            for (var i = 0; i < used; i++) DrawCamp(i, camped, i < settlements.Count);
            for (var i = used; i < camps.Count; i++) camps[i].Hide();
        }

        // A campfire on the community's cell and a tent for every dozen people.
        private void DrawCamp(int index, bool visible, bool settled)
        {
            while (camps.Count <= index) camps.Add(NewCamp());
            var camp = camps[index];
            if (!visible)
            {
                camp.Hide();
                return;
            }

            // The marker was just placed on the community's cell centre.
            var centre = communityMarkers[index].transform.localPosition + new Vector3(0f, communityMarkers[index].transform.localScale.y / 2f, 0f);
            var cellY = height - centre.y;
            camp.Fire.enabled = true;
            camp.Fire.transform.localPosition = new Vector3(OnArtPixel(centre.x), OnArtPixel(centre.y), 0f);
            camp.Fire.sortingOrder = OrderAt(cellY);
            camp.Fire.sprite = art.Campfire[(int)(animationTime * DecorFramesPerSecond + index) % art.Campfire.Length];

            var tents = Mathf.Clamp(Mathf.CeilToInt(communitySizes[index] / (float)PeoplePerTent), 1, MaxTents);
            for (var i = 0; i < camp.Tents.Length; i++) Place(camp.Tents[i], i < tents, centre, TentOffsets[i]);
            for (var i = 0; i < camp.Props.Length; i++) Place(camp.Props[i], settled, centre, PropOffsets[i]);
        }

        // `offset` is in cells from the camp's centre, counting rows down the
        // map as Core does; world y counts up.
        private void Place(SpriteRenderer part, bool shown, Vector3 centre, Vector2 offset)
        {
            part.enabled = shown;
            if (!shown) return;
            part.transform.localPosition = new Vector3(OnArtPixel(centre.x + offset.x), OnArtPixel(centre.y - offset.y), 0f);
            part.sortingOrder = OrderAt(height - centre.y + offset.y);
        }

        private Camp NewCamp()
        {
            var root = new GameObject("Camp").transform;
            root.SetParent(communityRoot, false);
            SpriteRenderer Part(string name, Sprite sprite)
            {
                var part = new GameObject(name).AddComponent<SpriteRenderer>();
                part.transform.SetParent(root, false);
                part.sprite = sprite;
                part.sharedMaterial = spriteMaterial;
                part.enabled = false;
                return part;
            }

            var tents = new SpriteRenderer[MaxTents];
            for (var i = 0; i < tents.Length; i++) tents[i] = Part("Tent", i == 0 ? art.BigTent : art.SmallTent);
            var props = new[] { Part("Well", art.Well), Part("Woodpile", art.Woodpile), Part("Stone pile", art.StonePile) };
            return new Camp(Part("Campfire", art.Campfire[0]), tents, props);
        }

        private void DrawCommunity(ICommunity community, bool settled, float size, bool visible, int index)
        {
            while (communityMarkers.Count <= index)
            {
                communityMarkers.Add(NewMarker("Community", communityRoot));
                communityIds.Add(EntityId.None);
                communitySizes.Add(0);
            }

            var marker = communityMarkers[index];
            communityIds[index] = community.Id;
            communitySizes[index] = community.Members.Count;
            var at = community.Position;
            var cellY = at.Y + 0.5f;
            // Centred on the cell: the pivot is at the feet, so drop by half.
            marker.transform.localPosition = new Vector3(at.X + 0.5f, height - cellY - size / 2f, 0f);
            marker.transform.localScale = new Vector3(size, size, 1f);
            marker.color = settled ? new Color(0.93f, 0.9f, 0.82f) : new Color(0.85f, 0.35f, 0.3f);
            marker.sortingOrder = 100000 + index;
            marker.enabled = visible;
        }

        private void DrawHighlight()
        {
            highlight.enabled = false;
            if (Selected.IsNone) return;
            Vector3 feet;
            Vector2 size;
            int order;
            if (Selected.Kind == EntityKind.Person)
            {
                var figure = FigureOf(Selected);
                if (figure == null || !figure.Visible) return;
                feet = figure.Root.localPosition;
                size = figure.Size;
                order = figure.Order;
            }
            else
            {
                var marker = CommunityMarkerOf(Selected);
                if (marker == null || !marker.enabled) return;
                feet = marker.transform.localPosition;
                size = marker.transform.localScale;
                order = marker.sortingOrder;
            }
            // A slightly larger box behind whatever is selected.
            var pad = Mathf.Max(size.x, size.y) * 0.35f;
            highlight.transform.localPosition = feet - new Vector3(0f, pad / 2f, 0f);
            highlight.transform.localScale = new Vector3(size.x + pad, size.y + pad, 1f);
            highlight.sortingOrder = order - 1;
            highlight.enabled = true;
        }

        // Where something is drawn, in world units: the middle of its figure or
        // marker. True for a person whether or not the current layer shows them.
        public bool TryGetDrawnPosition(EntityId id, out Vector2 position)
        {
            if (id.Kind == EntityKind.Person)
            {
                var figure = FigureOf(id);
                position = figure?.Centre ?? default;
                return figure != null;
            }
            var marker = CommunityMarkerOf(id);
            position = marker != null ? Centre(marker) : default;
            return marker != null;
        }

        // Everything drawn within `radius` screen pixels of `screenPoint` in the
        // current layer, nearest first; ties go to the lower id so repeated
        // taps cycle in a fixed order.
        public void Pick(Vector2 screenPoint, float radius, List<EntityId> into)
        {
            into.Clear();
            var found = new List<(float Distance, EntityId Id)>();
            if (Band == ZoomBand.Far)
            {
                for (var i = 0; i < usedCommunities; i++) Consider(Centre(communityMarkers[i]), communityIds[i]);
            }
            else
            {
                for (var i = 0; i < usedPeople; i++) Consider(people[i].Centre, peopleIds[i]);
            }

            found.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Id.CompareTo(b.Id));
            foreach (var entry in found) into.Add(entry.Id);

            void Consider(Vector2 centre, EntityId id)
            {
                var screen = (Vector2)sceneCamera.WorldToScreenPoint(centre);
                var distance = Vector2.Distance(screenPoint, screen);
                if (distance <= radius) found.Add((distance, id));
            }
        }

        // How many people a community had at the last redraw, or -1.
        public int SizeOf(EntityId community)
        {
            for (var i = 0; i < usedCommunities; i++) if (communityIds[i] == community) return communitySizes[i];
            return -1;
        }

        private Figure FigureOf(EntityId id)
        {
            for (var i = 0; i < usedPeople; i++) if (peopleIds[i] == id) return people[i];
            return null;
        }

        private SpriteRenderer CommunityMarkerOf(EntityId id)
        {
            for (var i = 0; i < usedCommunities; i++) if (communityIds[i] == id) return communityMarkers[i];
            return null;
        }

        private static Vector2 Centre(SpriteRenderer marker)
        {
            var p = marker.transform.position;
            return new Vector2(p.x, p.y + marker.transform.lossyScale.y / 2f);
        }

        // Where someone is drawn: part-way along their route while a task is
        // under way (Jobs.PositionAt), otherwise the cell they are stored at.
        private static WorldPosition PositionOf(World world, PersonHandle person, WorldPosition stored, SimulationTime now)
        {
            if (world.Jobs.HasTask(person))
            {
                var task = world.Jobs.TaskOf(person);
                if (task.Start.CompareTo(now) <= 0 && now.CompareTo(task.End) < 0) return world.Jobs.PositionAt(person, now);
            }
            return stored;
        }

        // Sorting by row, in art pixels so things within a cell still sort.
        private static int OrderAt(float cellY) => Mathf.RoundToInt(cellY * ArtSet.PixelsPerCell);

        // Onto the art's pixel grid, so a figure never straddles a screen pixel
        // at the whole-number zooms CameraRig snaps to.
        private static float OnArtPixel(float cells) => Mathf.Round(cells * ArtSet.PixelsPerCell) / ArtSet.PixelsPerCell;

        private static Color ColourOf(JobKind job)
        {
            switch (job)
            {
                case JobKind.Forager: return new Color(0.78f, 0.82f, 0.3f);
                case JobKind.Woodcutter: return new Color(0.72f, 0.45f, 0.25f);
                case JobKind.StoneGatherer: return new Color(0.72f, 0.72f, 0.76f);
                default: return new Color(0.95f, 0.62f, 0.35f);
            }
        }

        private void DrawTerrain(TerrainGrid grid)
        {
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    // Row 0 is the north edge, drawn at the top; texture rows count up from the bottom.
                    var shade = ((x + y) & 1) == 0 ? 0 : 8;
                    pixels[(height - 1 - y) * width + x] = ColourOf(grid[new WorldPosition(x, y)], shade);
                }
            }
            terrainTexture.SetPixels32(pixels);
            terrainTexture.Apply(false);
        }

        private static Color32 ColourOf(TerrainKind kind, int shade)
        {
            switch (kind)
            {
                case TerrainKind.Plains: return new Color32((byte)(127 - shade), (byte)(168 - shade), (byte)(90 - shade), 255);
                case TerrainKind.Forest: return new Color32((byte)(70 - shade), (byte)(112 - shade), (byte)(58 - shade), 255);
                case TerrainKind.Hills: return new Color32((byte)(154 - shade), (byte)(150 - shade), (byte)(98 - shade), 255);
                case TerrainKind.SmallRiver: return new Color32((byte)(79 - shade), (byte)(143 - shade), (byte)(192 - shade), 255);
                case TerrainKind.DeepWater: return new Color32((byte)(44 - shade), (byte)(86 - shade), (byte)(140 - shade), 255);
                default: return new Color32(255, 0, 255, 255);
            }
        }

        // The Medium and Near ground: rippling water and the season's land per
        // cell, shoreline pieces over the joins, a tree on every forest cell,
        // a rock on every hills cell (#124 settles what hills are), and a
        // light scatter of tufts, flowers and bushes on the plains. Which
        // variant goes where is drawn from the cell's position, so the map
        // looks the same every time. Both kinds of water draw alike until
        // worldgen places deep water (#127).
        private void TileTerrain()
        {
            tiledGround = new GameObject("Tiled terrain");
            tiledGround.transform.SetParent(transform, false);
            tiledGround.AddComponent<Grid>();
            landMap = NewTilemap("Ground", Vector3.zero, -5, false);
            // Shoreline pieces sit on the corners where four cells meet, half a
            // cell off the ground grid, so each piece joins up to four cells
            // and sixteen pieces cover every mix of land and water. Along the
            // map's edge they would hang half a cell outside it, so they are
            // clipped to the map.
            shoreMap = NewTilemap("Shore", new Vector3(-0.5f, -0.5f, 0f), -3, true);
            var mask = new GameObject("Map clip").AddComponent<SpriteMask>();
            mask.transform.SetParent(tiledGround.transform, false);
            mask.sprite = unitSprite;
            mask.transform.localPosition = new Vector3(width / 2f, 0f, 0f);
            mask.transform.localScale = new Vector3(width, height, 1f);

            var scenery = new GameObject("Scenery").transform;
            scenery.SetParent(tiledGround.transform, false);
            flatDecor = new GameObject("Flowers").transform;
            flatDecor.SetParent(tiledGround.transform, false);

            DrawnWater();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var kind = grid[new WorldPosition(x, y)];
                    var hash = CellHash(x, y);
                    if (drawnWater[y * width + x]) landMap.SetTile(CellOf(x, y), WaterTile(hash));
                    else if (kind == TerrainKind.Forest) Stand(art.Trees[TreeOf(hash)], x, y, scenery, 0.3f, true);
                    else if (kind == TerrainKind.Hills) Stand(art.Rocks[hash % (uint)art.Rocks.Length], x, y, scenery, 0.3f, true);
                    else if (kind == TerrainKind.Plains && Decorated(hash) && !Tufted(hash))
                    {
                        // Mostly flowers and sprouts, now and then a bush.
                        var pick = hash >> 12;
                        if (pick % 8 == 0) Stand(art.Bushes[pick / 8 % (uint)art.Bushes.Length], x, y, scenery, 0.5f, true);
                        else flowers.Add((Stand(art.FlatDecor[pick / 8 % (uint)art.FlatDecor.Length], x, y, flatDecor, 0.5f, false), y * width + x));
                    }
                }
            }

            // Corner (i, j) in world units, where y counts up from the south
            // edge; the cells around it are columns i - 1 and i, and rows
            // (counted from the north, as Core does) height - 1 - j above it
            // and height - j below.
            for (var j = 0; j <= height; j++)
            {
                for (var i = 0; i <= width; i++)
                {
                    var shoreMask = ArtSet.ShoreMask(
                        WaterAt(i - 1, height - 1 - j), WaterAt(i, height - 1 - j),
                        WaterAt(i - 1, height - j), WaterAt(i, height - j));
                    shoreMasks[j * (width + 1) + i] = (byte)shoreMask;
                }
            }

            clouds = new GameObject("Clouds").transform;
            clouds.SetParent(tiledGround.transform, false);
            // About one cloud per 150 cells, each with its own place, shape
            // and pace.
            var count = Mathf.Max(3, width * height / 150);
            for (var i = 0; i < count; i++)
            {
                var hash = CellHash(i, -1);
                var cloud = new GameObject("Cloud").AddComponent<SpriteRenderer>();
                cloud.transform.SetParent(clouds, false);
                cloud.sprite = art.Clouds[hash % (uint)art.Clouds.Length];
                cloud.sharedMaterial = spriteMaterial;
                cloud.sortingOrder = CloudOrder;
                cloud.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                cloud.transform.localPosition = new Vector3((hash & 0xff) / 255f * width, ((hash >> 8) & 0xff) / 255f * height, 0f);
            }

            tiledGround.SetActive(false);
        }

        // Plain water mostly, with an accent here and there: about one cell in
        // eight sparkles, one in thirty has a droplet's ring, one in sixty a fish.
        private TileBase WaterTile(uint hash)
        {
            var roll = hash % 60u;
            var phase = (int)((hash >> 8) % ArtSet.AccentPhases);
            if (roll == 0) return art.Fish[phase];
            if (roll <= 2) return art.Droplets[phase];
            if (roll <= 9) return art.Sparkles[phase];
            return art.Water;
        }

        // Lays each land cell and shoreline piece in its season. A new season
        // does not arrive everywhere at once: each cell and corner lags it by
        // up to SeasonSpreadDays, by its position, so snow creeps across the
        // map over the first days of winter and melts the same way in spring.
        // Sim time, so it pauses and speeds up with the simulation. Only what
        // changed is re-laid, and nothing at all once every cell has caught up.
        private void LayLand(SimulationTime now)
        {
            var settled = SeasonAt(now, SeasonSpreadDays) == now.Season;
            if (settled && landSeason == now.Season) return;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (drawnWater[index]) continue;
                    var season = SeasonAt(now, Lag(x, y));
                    if (landSeasons[index] == (byte)season) continue;
                    landSeasons[index] = (byte)season;
                    var ground = art.GroundOf(season);
                    var hash = CellHash(x, y);
                    var tuft = ground.Tufts.Length > 0 && Decorated(hash) && Tufted(hash);
                    landMap.SetTile(CellOf(x, y), tuft ? ground.Tufts[(hash >> 12) % (uint)ground.Tufts.Length] : ground.Plain);
                }
            }

            for (var j = 0; j <= height; j++)
            {
                for (var i = 0; i <= width; i++)
                {
                    var index = j * (width + 1) + i;
                    var season = SeasonAt(now, Lag(i, j + height + 1));
                    if (cornerSeasons[index] == (byte)season) continue;
                    cornerSeasons[index] = (byte)season;
                    shoreMap.SetTile(new Vector3Int(i, j, 0), art.GroundOf(season).Shore[shoreMasks[index]]);
                }
            }

            // Nothing flowers under snow.
            foreach (var (renderer, cell) in flowers) renderer.enabled = landSeasons[cell] != (byte)Season.Winter;
            landSeason = settled ? now.Season : (Season?)null;
        }

        // The season `lagDays` before `now`; the first season before the clock
        // has run that long.
        private static Season SeasonAt(SimulationTime now, float lagDays)
        {
            var ticks = now.Ticks - (long)(lagDays * SimulationTime.TicksPerDay);
            return new SimulationTime(ticks > 0 ? ticks : 0).Season;
        }

        // How many days behind the season a cell or corner is: its own hash,
        // not CellHash, so the spread does not follow where the flowers are.
        private static float Lag(int x, int y) => CellHash(x + 7919, y - 7919) % 1024u / 1024f * SeasonSpreadDays;

        // Swaying grass, flickering campfires and drifting clouds, on a clock
        // that stops while the simulation is paused. Presentation only.
        private void Animate()
        {
            var time = animationTime;
            foreach (var (renderer, frames, phase) in animated)
                renderer.sprite = frames[(int)(time * DecorFramesPerSecond + phase) % frames.Length];

            // Clouds drift east and wrap round, starting from where TileTerrain
            // put them.
            for (var i = 0; i < clouds.childCount; i++)
            {
                var cloud = clouds.GetChild(i);
                var hash = CellHash(i, -1);
                var speed = CloudSpeed * (0.6f + ((hash >> 16) & 0xff) / 255f * 0.8f);
                var span = width + 2f * CloudMargin;
                var x = Mathf.Repeat((hash & 0xff) / 255f * width + time * speed + CloudMargin, span) - CloudMargin;
                cloud.localPosition = new Vector3(OnArtPixel(x), cloud.localPosition.y, 0f);
            }
        }

        private Tilemap NewTilemap(string name, Vector3 offset, int order, bool clipped)
        {
            var go = new GameObject(name);
            go.transform.SetParent(tiledGround.transform, false);
            go.transform.localPosition = offset;
            var map = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sharedMaterial = spriteMaterial;
            renderer.sortingOrder = order;
            if (clipped) renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            return map;
        }

        // Something standing in a cell, nudged within it by up to `nudge` of a
        // cell so a forest is not a grid. Upright things sort like people, so
        // people walk behind them; flat ones lie under everyone. More than one
        // frame means it loops.
        private SpriteRenderer Stand(Sprite[] frames, int x, int y, Transform parent, float nudge, bool upright)
        {
            var hash = CellHash(x, y);
            var jitterX = ((hash & 0xff) / 255f - 0.5f) * nudge;
            var jitterY = (((hash >> 8) & 0xff) / 255f - 0.5f) * nudge;
            var cellY = y + 0.5f + jitterY;
            var renderer = new GameObject("Scenery").AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(parent, false);
            renderer.transform.localPosition = new Vector3(OnArtPixel(x + 0.5f + jitterX), OnArtPixel(height - cellY), 0f);
            renderer.sprite = frames[0];
            renderer.sharedMaterial = spriteMaterial;
            renderer.sortingOrder = upright ? OrderAt(cellY) : -2;
            if (frames.Length > 1) animated.Add((renderer, frames, hash % (uint)frames.Length));
            return renderer;
        }

        private void Stand(Sprite sprite, int x, int y, Transform parent, float nudge, bool upright) =>
            Stand(new[] { sprite }, x, y, parent, nudge, upright);

        // Oak, spruce or fruit tree, medium half the time and small or big
        // the rest.
        private static int TreeOf(uint hash)
        {
            var species = (int)(hash % 3u);
            var size = (int)((hash >> 4) % 4u);
            return species * 3 + (size == 0 ? 0 : size == 3 ? 2 : 1);
        }

        // One plains cell in six is decorated: half of those with a tufted
        // tile, the rest with a flower, sprout or bush.
        private static bool Decorated(uint hash) => (hash >> 16) % 6u == 0;

        private static bool Tufted(uint hash) => (hash >> 20) % 2u == 0;

        // Not GetHashCode, which is seeded per process: the same cell must
        // pick the same variant in every run.
        private static uint CellHash(int x, int y)
        {
            var h = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            return h;
        }

        private Vector3Int CellOf(int x, int y) => new Vector3Int(x, height - 1 - y, 0);

        private static bool IsWater(TerrainKind kind) => kind == TerrainKind.SmallRiver || kind == TerrainKind.DeepWater;

        // Which cells are drawn as water: the water cells, plus a land cell
        // wherever two water cells touch only at a corner, as a river does
        // where it steps diagonally. Shorelines cannot join water through a
        // corner, so without it the river would pinch into a chain of ponds.
        // The southern of the two land cells gives way, and a cell that
        // becomes water can make a new corner touch, so this repeats until
        // nothing changes. Drawing only: Core still has land there, and a
        // tree or rock on it is not drawn.
        private void DrawnWater()
        {
            drawnWater = new bool[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    drawnWater[y * width + x] = IsWater(grid[new WorldPosition(x, y)]);

            bool At(int x, int y) => drawnWater[y * width + x];
            for (var changed = true; changed;)
            {
                changed = false;
                for (var y = 0; y < height - 1; y++)
                {
                    for (var x = 0; x < width - 1; x++)
                    {
                        // The 2x2 block with (x, y) at its north-west; rows count south.
                        bool nw = At(x, y), ne = At(x + 1, y), sw = At(x, y + 1), se = At(x + 1, y + 1);
                        if (nw && se && !ne && !sw) drawnWater[(y + 1) * width + x] = changed = true;
                        else if (ne && sw && !nw && !se) drawnWater[(y + 1) * width + x + 1] = changed = true;
                    }
                }
            }
        }

        // Past the map's edge counts as whatever is at the edge, so a river
        // runs off the map rather than ending in a shore.
        private bool WaterAt(int x, int y) => drawnWater[Mathf.Clamp(y, 0, height - 1) * width + Mathf.Clamp(x, 0, width - 1)];

        private Figure FigureAt(int index)
        {
            if (index < people.Count) return people[index];
            people.Add(art != null ? NewDoll() : new Figure(NewMarker("Person", peopleRoot)));
            peopleIds.Add(EntityId.None);
            return people[index];
        }

        private Figure NewDoll()
        {
            var root = new GameObject("Person");
            root.transform.SetParent(peopleRoot, false);
            var layers = new SpriteRenderer[ArtSet.Layers];
            for (var i = 0; i < layers.Length; i++)
            {
                layers[i] = new GameObject("Layer " + i).AddComponent<SpriteRenderer>();
                layers[i].transform.SetParent(root.transform, false);
                layers[i].sharedMaterial = spriteMaterial;
                layers[i].sortingOrder = i;
            }
            return new Figure(root.transform, layers, root.AddComponent<SortingGroup>());
        }

        private SpriteRenderer NewMarker(string name, Transform parent)
        {
            var marker = new GameObject(name).AddComponent<SpriteRenderer>();
            marker.transform.SetParent(parent, false);
            marker.sprite = unitSprite;
            marker.sharedMaterial = spriteMaterial;
            return marker;
        }

        // "Settlement 3 · 42": which community and how many live in it. Core
        // names nothing yet, so the id stands in until #109 gives settlements names.
        private static string LabelOf(EntityId id, int size) =>
            (id.Kind == EntityKind.Settlement ? "Settlement " : "Band ") + id.Value + " · " + size;

        // A label over each community at Far zoom.
        private void OnGUI()
        {
            if (Band != ZoomBand.Far || sceneCamera == null) return;
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter };
            GUI.matrix = Matrix4x4.Scale(Vector3.one * uiScale);
            for (var i = 0; i < usedCommunities; i++)
            {
                var marker = communityMarkers[i];
                var top = marker.transform.position + new Vector3(0f, marker.transform.lossyScale.y, 0f);
                var screen = sceneCamera.WorldToScreenPoint(top);
                GUI.Label(new Rect(screen.x / uiScale - 80f, (Screen.height - screen.y) / uiScale - 24f, 160f, 22f), LabelOf(communityIds[i], communitySizes[i]), labelStyle);
            }
        }

        private T Own<T>(T asset) where T : Object
        {
            owned.Add(asset);
            return asset;
        }

        private void OnDestroy()
        {
            foreach (var asset in owned) if (asset != null) Destroy(asset);
        }

        private sealed class Camp
        {
            public Camp(SpriteRenderer fire, SpriteRenderer[] tents, SpriteRenderer[] props)
            {
                Fire = fire;
                Tents = tents;
                Props = props;
            }

            public SpriteRenderer Fire { get; }
            public SpriteRenderer[] Tents { get; }

            // The well, woodpile and stone pile, in PropOffsets order.
            public SpriteRenderer[] Props { get; }

            public void Hide()
            {
                Fire.enabled = false;
                foreach (var tent in Tents) tent.enabled = false;
                foreach (var prop in Props) prop.enabled = false;
            }
        }

        // One person as drawn: a paper doll of ArtSet.Layers sprites held
        // together by a sorting group, or without the art a single marker.
        private sealed class Figure
        {
            private readonly SortingGroup group;

            public Figure(SpriteRenderer marker)
            {
                Root = marker.transform;
                Layers = new[] { marker };
            }

            public Figure(Transform root, SpriteRenderer[] layers, SortingGroup group)
            {
                Root = root;
                Layers = layers;
                this.group = group;
            }

            public Transform Root { get; }
            public SpriteRenderer[] Layers { get; }
            public bool Doll => group != null;

            // What the doll is dressed as, and for whom and which job: the
            // slot a figure draws can pass to someone else when people die.
            public ArtSet.Look Look;
            public EntityId DressedFor;
            public JobKind Job;

            public bool Visible => Layers[0].enabled;

            public int Order
            {
                get => Doll ? group.sortingOrder : Layers[0].sortingOrder;
                set
                {
                    if (Doll) group.sortingOrder = value;
                    else Layers[0].sortingOrder = value;
                }
            }

            // Width and height in world units, standing on Root.
            public Vector2 Size => Doll ? new Vector2(ArtSet.FigureWidth, ArtSet.FigureHeight) : (Vector2)Root.localScale;

            public Vector2 Centre
            {
                get
                {
                    var p = Root.position;
                    return new Vector2(p.x, p.y + Size.y / 2f);
                }
            }

            public void SetVisible(bool visible)
            {
                foreach (var layer in Layers) layer.enabled = visible;
            }
        }
    }
}
