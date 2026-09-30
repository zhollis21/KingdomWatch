using System.Collections.Generic;
using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using Unity.Profiling;
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
    // people are paper dolls that idle and walk; only the part of the map in
    // view is built (#130). At Far zoom the ground is one pixel per cell in
    // the art's colours, season and all. Without the art it is one pixel per
    // cell by terrain kind, and people are upright markers. Either way whoever is further south draws in front,
    // and at Far zoom the people give way to one marker per settlement or
    // band (#115). The art draws the tilt itself, so a row is as tall as a
    // column is wide.
    public sealed class WorldView2D : MonoBehaviour
    {
        // Layer edges in UI-scaled pixels per cell (see SimulationDriver.UiScale),
        // with slack either side so a zoom resting on an edge does not flicker.
        // The art draws down to SmallestArt screen pixels a cell - 16 is the
        // art at full size, 8 half, 4 a quarter - and the colour map takes
        // over just below it, at FarEdge (#130). Both are screen pixels, not
        // UI-scaled ones: they are about the art's pixels, which CameraRig
        // snaps in screen pixels. Try 8 or 4 here to compare.
        public const float SmallestArt = 4f;
        public const float FarEdge = SmallestArt * 0.75f;
        public const float NearFrom = 20f;

        // How far past FarEdge, as a share of it, a zoom must go to change
        // layer, so a zoom resting on the edge does not flicker.
        private const float FarSlack = 0.1f;

        // The same for NearFrom, in UI-scaled pixels.
        private const float BandSlack = 2f;

        // The art view builds the map a chunk of this many cells square at a
        // time, and only the chunks the camera can see (#130): at 1080 by 1080
        // the whole map is over a million tiles and a third of a million
        // trees and rocks, too many to keep, let alone animate every frame.
        private const int ChunkSize = 32;

        // Milliseconds of chunk building a frame may spend, at least one chunk
        // whatever it costs, so a zoom or a fast pan fills in over a few
        // frames rather than hitching on one. The colour map lies under the
        // art, so a chunk not built yet still shows.
        private const double BuildBudgetMs = 4.0;

        // Screen pixels per cell from which flowers and sprouts are stood up:
        // half size and bigger. At quarter size they are a pixel or two, and
        // a screen of quarter-size art is some 170 chunks (#130).
        private const float DecorFrom = 6f;

        // Screen pixels per cell from which each tree, rock, bush and flower
        // is its own renderer: full size and bigger, where people walk behind
        // trees. Below it, at half and quarter size, a chunk's scenery is
        // tiles in two tilemaps, laid in one call and drawn in a few batches
        // (#131): a screen of quarter-size art was some 50,000 renderers and
        // filled in over many frames. The tilemaps draw under everyone, so
        // there people are always in front. Halfway between half and full
        // size, the edge CameraRig snaps on.
        private const float LiveSceneryFrom = ArtSet.PixelsPerCell * 0.75f;

        // Scenery renderers made up front, when the world is shown, so the
        // first zoom to full size does not stop to make them: about what the
        // art view shows at full size on a phone.
        private const int SceneryMadeUpFront = 8000;

        // Named in a capture, so a slow frame says which part was slow (#132).
        private static readonly ProfilerMarker BuildMarker = new ProfilerMarker("KW.View.BuildChunks");
        private static readonly ProfilerMarker LayLandMarker = new ProfilerMarker("KW.View.LayLand");
        private static readonly ProfilerMarker AnimateMarker = new ProfilerMarker("KW.View.Animate");
        private static readonly ProfilerMarker PeopleMarker = new ProfilerMarker("KW.View.People");
        private static readonly ProfilerMarker CommunitiesMarker = new ProfilerMarker("KW.View.Communities");

        // A community marker's side in UI-scaled pixels, whatever the zoom.
        private const float CommunityMarkerSize = 14f;

        // Doll frames per second.
        private const float FramesPerSecond = 8f;

        // A new season spreads out from SpreadSeeds places, picked at random
        // each season, a step at a time: SpreadStepsPerDay steps a sim day,
        // each passing it from a cell at its edge to each neighbour with
        // SpreadChance, so it grows in ragged blobs rather than diamonds (see
        // Spread). At these values it covers the map in about ten days of a
        // thirty-day season, half of it in about three.
        private const int SpreadSeeds = 12;
        private const float SpreadStepsPerDay = 70f;
        private const double SpreadChance = 0.5;
        private const long TicksPerSeason = SimulationTime.DaysPerSeason * SimulationTime.TicksPerDay;

        // The zoomed-out map is sent which cells have turned at most once in
        // this many frames: nobody sees the difference at a pixel a cell.
        private const int TurnedSendFrames = 4;

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
        // Draws the zoomed-out map as a season spreads (SeasonSpread.shader).
        // Without it, the map changes season everywhere at once.
        public Shader spreadShader;
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
        // The scenery below LiveSceneryFrom: flowers and sprouts flat on the
        // ground, and trees, rocks and bushes standing up.
        private Tilemap flatMap, standingMap;
        private TileChangeData[] flatBlock, standingBlock;
        private Transform sceneryRoot;
        // Which shoreline piece each corner takes, by ShoreMask; row-major,
        // (width + 1) by (height + 1).
        private byte[] shoreMasks;
        private bool[] drawnWater;
        // The season every cell of the map shows, built or not: the spread
        // writes it, and Build lays from it. A corner's shoreline piece
        // shows its owner cell's season (see CornerOwner).
        private byte[] landSeasons;
        // The spread under way (see Spread): the season it lays, which
        // season of the clock it began in, the cells at its edge, the steps
        // it has taken, and its draws.
        private Season spreadTo;
        private long spreadSeason;
        private List<int> spreadEdge = new List<int>(), nextEdge = new List<int>();
        private int spreadSteps;
        private System.Random spreadRandom;
        // Which cells show spreadTo, a byte a cell in texture rows for the
        // zoomed-out map's shader, and when it was last sent.
        private byte[] turned;
        private Texture2D turnedTexture;
        private bool turnedChanged;
        private int turnedSentFrame = -TurnedSendFrames;
        private float animationTime;
        // Every chunk by (row * chunksAcross + column), null while not built;
        // and the built ones, in the order they were built.
        private Chunk[] chunks;
        private int chunksAcross, chunksDown;
        private readonly List<Chunk> resident = new List<Chunk>();
        private readonly Stack<SpriteRenderer> sceneryPool = new Stack<SpriteRenderer>();
        private TileBase[] landBlock, shoreBlock;
        private readonly List<(int Column, int Row, int Distance)> toBuild = new List<(int, int, int)>();
        private readonly System.Diagnostics.Stopwatch buildClock = new System.Diagnostics.Stopwatch();
        private readonly List<Vector3Int> changedCells = new List<Vector3Int>();
        private readonly List<TileBase> changedTiles = new List<TileBase>();
        private readonly List<Vector3Int> changedCorners = new List<Vector3Int>();
        private readonly List<TileBase> changedCornerTiles = new List<TileBase>();
        private readonly List<TileChangeData> changedFlowers = new List<TileChangeData>();
        // The zoomed-out map in the art's colours (#130), one per season,
        // made when the world is shown; and the season on show.
        private ArtColours artColours;
        private Sprite[] seasonMaps;
        private int shownSeason = -1;
        // The zoomed-out map's material while a season spreads over it, and
        // the texture it reads when each cell turns; null without the shader.
        private Material spreadMaterial;
        private static readonly int PreviousId = Shader.PropertyToID("_Previous");
        private static readonly int TurnedId = Shader.PropertyToID("_Turned");
        private readonly List<Camp> camps = new List<Camp>();
        private Transform peopleRoot;
        private Transform communityRoot;
        private SpriteRenderer highlight;
        private int width, height;
        private int usedPeople, usedCommunities;
        private float uiScale = 1f;
        private GUIStyle labelStyle;

        public ZoomBand Band { get; private set; } = ZoomBand.Far;

        // Chunks in view still waiting to be built after this frame: while it
        // is above zero the screen is still filling in (#131, #132).
        public int ChunksWaiting { get; private set; }

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
            // Under the art's ground, so it shows through wherever a chunk is
            // not built yet.
            colourGround.sortingOrder = -10;

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
            art = ArtSet.Load(owned);
            if (art != null)
            {
                TileTerrain();
                artColours = ArtColours.Measure(art);
                BuildSeasonMaps();
                StartSpread(world.Now);
                ShowSpread();
            }
            else
            {
                DrawTerrain(grid);
                Debug.Log("WorldView2D: no usable art at Assets/Art (see docs/unity.md); drawing plain markers.");
            }
        }

        // Redraws from the world as it stands. Called every frame, paused or
        // not, so zooming across a layer edge redraws without a sim step.
        // `paused` stops every animation along with the simulation.
        public void Refresh(World world, float pixelsPerCell, float scale, bool paused)
        {
            uiScale = scale;
            Band = BandFor(pixelsPerCell, scale);
            if (!paused) animationTime += Time.unscaledDeltaTime;
            var tiled = art != null && Band != ZoomBand.Far;
            if (tiledGround != null && tiledGround.activeSelf != tiled)
            {
                // Hidden, not released: the chunks built stay, with their
                // season kept current (LayLand), so leaving the art costs
                // nothing and coming back to the same place builds nothing.
                // Clearing them all at once hitched (#135 review). Coming back
                // elsewhere, what is out of view is released under the build
                // budget like any chunk a pan leaves behind.
                tiledGround.SetActive(tiled);
                sceneryRoot.gameObject.SetActive(tiled);
            }
            if (!tiled) ChunksWaiting = 0;
            // Every frame, zoomed in or out, so the season has got as far
            // wherever the view goes next.
            if (art != null) using (LayLandMarker.Auto()) LayLand(world.Now);
            if (tiled)
            {
                // The water's own tile animation, which the Tilemap runs.
                landMap.animationFrameRate = paused ? 0f : 1f;
                flatMap.animationFrameRate = landMap.animationFrameRate;
                using (BuildMarker.Auto()) BuildWhatShows(pixelsPerCell >= DecorFrom, pixelsPerCell < LiveSceneryFrom);
                using (AnimateMarker.Auto()) Animate();
            }
            if (seasonMaps != null && shownSeason != (int)world.Now.Season)
            {
                shownSeason = (int)world.Now.Season;
                colourGround.sprite = seasonMaps[shownSeason];
                if (spreadMaterial != null) spreadMaterial.SetTexture(PreviousId, seasonMaps[(shownSeason + 3) % 4].texture);
            }
            if (spreadMaterial != null && Band == ZoomBand.Far && turnedChanged && Time.frameCount - turnedSentFrame >= TurnedSendFrames)
            {
                turnedTexture.SetPixelData(turned, 0);
                turnedTexture.Apply(false);
                turnedChanged = false;
                turnedSentFrame = Time.frameCount;
            }
            using (PeopleMarker.Auto()) DrawPeople(world);
            using (CommunitiesMarker.Auto()) DrawCommunities(world, pixelsPerCell, tiled);
            DrawHighlight();
        }

        private ZoomBand BandFor(float pixelsPerCell, float scale)
        {
            var farEdge = FarEdge * (Band == ZoomBand.Far ? 1f + FarSlack : 1f - FarSlack);
            var nearEdge = Band == ZoomBand.Near ? NearFrom - BandSlack : NearFrom + BandSlack;
            if (pixelsPerCell < farEdge) return ZoomBand.Far;
            return pixelsPerCell / scale >= nearEdge ? ZoomBand.Near : ZoomBand.Medium;
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
            camp.Fire.sprite = art.Campfire[(int)(animationTime * ArtSet.DecorFramesPerSecond + index) % art.Campfire.Length];

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

        // The zoomed-out map in the art's colours, once for each season, when
        // the world is shown. Refresh swaps between them as the seasons turn,
        // which costs nothing; recolouring the map as a season spread across
        // it cost a frame's budget at 10,000x, where a season is over in
        // about a second (#130). The spread shows by choosing between two of
        // them on the GPU instead (ShowSpread).
        private void BuildSeasonMaps()
        {
            seasonMaps = new Sprite[4];
            var pixels = new Color32[width * height];
            for (var season = 0; season < seasonMaps.Length; season++)
            {
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++) pixels[(height - 1 - y) * width + x] = FarColour(x, y, season);
                }
                var texture = season == 0 ? terrainTexture : Own(new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
                texture.SetPixels32(pixels);
                // Uploaded and then dropped from main memory: nothing reads it
                // back.
                texture.Apply(false, true);
                seasonMaps[season] = season == 0 ? colourGround.sprite : Own(Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.zero, 1f));
            }
        }

        // Lets the zoomed-out map show a season spreading, cell for cell as
        // the art view does (#131): SeasonSpread.shader draws each cell from
        // this season's map once it has turned and from last season's until
        // then, reading which have turned from a byte a cell. Refresh sends
        // that byte map again while the season is spreading and the map is
        // zoomed out, at most every TurnedSendFrames frames.
        private void ShowSpread()
        {
            if (spreadShader == null)
            {
                Debug.LogWarning("WorldView2D: no spreadShader set; the zoomed-out map changes season everywhere at once.");
                return;
            }
            turnedTexture = Own(new Texture2D(width, height, TextureFormat.R8, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Season spread" });
            turnedTexture.SetPixelData(turned, 0);
            turnedTexture.Apply(false);
            spreadMaterial = Own(new Material(spreadShader));
            spreadMaterial.SetTexture(TurnedId, turnedTexture);
            colourGround.sharedMaterial = spreadMaterial;
        }

        // One cell as the art view draws it, reduced to a colour: its ground,
        // the shoreline pieces on its four corners, and what stands on it.
        private Color32 FarColour(int x, int y, int season)
        {
            var index = y * width + x;
            var hash = CellHash(x, y);
            var water = drawnWater[index];
            var tuft = !water && artColours.Tufts[season].Length > 0 && Decorated(hash) && Tufted(hash);
            var colour = water ? artColours.Water
                : tuft ? artColours.Tufts[season][(hash >> 12) % (uint)artColours.Tufts[season].Length]
                : artColours.Plain[season];

            // The cell's own tile row, and the corners at its four corners in
            // world units; each shoreline piece lays its facing quarter over
            // this cell, a quarter of the cell's area.
            var t = height - 1 - y;
            var shore = artColours.Shore[season];
            colour = ArtColours.Over(colour, shore[shoreMasks[t * (width + 1) + x]][3], 0.25f);
            colour = ArtColours.Over(colour, shore[shoreMasks[t * (width + 1) + x + 1]][2], 0.25f);
            colour = ArtColours.Over(colour, shore[shoreMasks[(t + 1) * (width + 1) + x]][1], 0.25f);
            colour = ArtColours.Over(colour, shore[shoreMasks[(t + 1) * (width + 1) + x + 1]][0], 0.25f);

            if (!water)
            {
                var kind = grid[new WorldPosition(x, y)];
                if (kind == TerrainKind.Forest) colour = ArtColours.Over(colour, artColours.Trees[TreeOf(hash)]);
                else if (kind == TerrainKind.Hills) colour = ArtColours.Over(colour, artColours.Rocks[hash % (uint)artColours.Rocks.Length]);
                else if (kind == TerrainKind.Plains && Decorated(hash) && !Tufted(hash))
                {
                    var pick = hash >> 12;
                    if (pick % 8 == 0) colour = ArtColours.Over(colour, artColours.Bushes[pick / 8 % (uint)artColours.Bushes.Length]);
                    else if (season != (int)Season.Winter) colour = ArtColours.Over(colour, artColours.FlatDecor[pick / 8 % (uint)artColours.FlatDecor.Length]);
                }
            }
            return ArtColours.ToColour(colour);
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
        //
        // Only the frame is set up here: the ground itself is laid a chunk at
        // a time as the camera comes to it (BuildWhatShows).
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
            // Over the ground and under everyone else. The standing scenery
            // draws its northern rows first, so a tree hides the foot of the
            // one behind it, as the renderers do by row.
            flatMap = NewTilemap("Flat scenery", Vector3.zero, -2, false);
            standingMap = NewTilemap("Standing scenery", Vector3.zero, -1, false);
            standingMap.GetComponent<TilemapRenderer>().sortOrder = TilemapRenderer.SortOrder.TopLeft;
            var mask = new GameObject("Map clip").AddComponent<SpriteMask>();
            mask.transform.SetParent(tiledGround.transform, false);
            mask.sprite = unitSprite;
            mask.transform.localPosition = new Vector3(width / 2f, 0f, 0f);
            mask.transform.localScale = new Vector3(width, height, 1f);

            // Outside the tiled ground, so switching that on and off does not
            // wake or put to sleep every pooled renderer with it: a chunk's
            // scenery is shown and hidden renderer by renderer instead.
            sceneryRoot = new GameObject("Scenery").transform;
            sceneryRoot.SetParent(transform, false);
            for (var i = 0; i < SceneryMadeUpFront; i++) sceneryPool.Push(NewScenery());

            chunksAcross = (width + ChunkSize - 1) / ChunkSize;
            chunksDown = (height + ChunkSize - 1) / ChunkSize;
            chunks = new Chunk[chunksAcross * chunksDown];
            landSeasons = new byte[width * height];
            turned = new byte[width * height];

            DrawnWater();
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

            tiledGround.SetActive(false);
        }

        // Builds the chunks the camera can see that are not built yet, a few
        // per frame, and then releases those well out of sight. A chunk one past
        // the edge of the view is kept, so a pan back and forth does not
        // rebuild it every time.
        private void BuildWhatShows(bool withDecor, bool sceneryTiled)
        {
            var halfHeight = sceneCamera.orthographicSize;
            var halfWidth = halfHeight * sceneCamera.aspect;
            var centre = sceneCamera.transform.position;
            // Core rows count down from the north edge; world y counts up.
            var minColumn = Mathf.FloorToInt((centre.x - halfWidth) / ChunkSize);
            var maxColumn = Mathf.FloorToInt((centre.x + halfWidth) / ChunkSize);
            var minRow = Mathf.FloorToInt((height - (centre.y + halfHeight)) / ChunkSize);
            var maxRow = Mathf.FloorToInt((height - (centre.y - halfHeight)) / ChunkSize);

            // What needs building: a chunk not built, or built for the other
            // side of DecorFrom or LiveSceneryFrom. Nearest the middle of the
            // screen first, so what is still filling in is at the edges.
            var centreColumn = Mathf.FloorToInt(centre.x / ChunkSize);
            var centreRow = Mathf.FloorToInt((height - centre.y) / ChunkSize);
            toBuild.Clear();
            for (var row = Mathf.Max(0, minRow); row <= Mathf.Min(chunksDown - 1, maxRow); row++)
            {
                for (var column = Mathf.Max(0, minColumn); column <= Mathf.Min(chunksAcross - 1, maxColumn); column++)
                {
                    var chunk = chunks[row * chunksAcross + column];
                    if (chunk != null && chunk.WithDecor == withDecor && chunk.SceneryTiled == sceneryTiled) continue;
                    var dx = column - centreColumn;
                    var dy = row - centreRow;
                    toBuild.Add((column, row, dx * dx + dy * dy));
                }
            }
            toBuild.Sort(NearestFirst);

            // As many as fit the frame's budget, and always at least one.
            buildClock.Restart();
            ChunksWaiting = toBuild.Count;
            foreach (var (column, row, _) in toBuild)
            {
                ChunksWaiting--;
                var old = chunks[row * chunksAcross + column];
                if (old != null)
                {
                    Release(old);
                    resident.Remove(old);
                }
                var chunk = Build(column, row, withDecor, sceneryTiled);
                chunks[row * chunksAcross + column] = chunk;
                resident.Add(chunk);
                if (buildClock.Elapsed.TotalMilliseconds >= BuildBudgetMs) break;
            }

            // Then what is out of sight, in what the budget has left and at
            // least one a frame: a step out from quarter size leaves over a
            // hundred chunks behind, and releasing them all at once took an
            // eighth of a second (#131). Out of sight, one kept a few frames
            // longer shows nothing.
            for (var i = resident.Count - 1; i >= 0; i--)
            {
                var chunk = resident[i];
                if (chunk.Column >= minColumn - 1 && chunk.Column <= maxColumn + 1 && chunk.Row >= minRow - 1 && chunk.Row <= maxRow + 1) continue;
                Release(chunk);
                resident.RemoveAt(i);
                if (buildClock.Elapsed.TotalMilliseconds >= BuildBudgetMs) break;
            }
        }

        private static readonly System.Comparison<(int Column, int Row, int Distance)> NearestFirst =
            (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
                : a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Column.CompareTo(b.Column);

        // Lays one chunk's ground and shoreline in two block writes, and its
        // scenery either in two more or on renderers from the pool.
        private Chunk Build(int column, int row, bool withDecor, bool sceneryTiled)
        {
            var chunk = new Chunk(column, row, width, height) { WithDecor = withDecor, SceneryTiled = sceneryTiled };
            var land = chunk.Land;
            if (landBlock == null || landBlock.Length != land.size.x * land.size.y) landBlock = new TileBase[land.size.x * land.size.y];
            if (sceneryTiled)
            {
                SizeSceneryBlock(ref flatBlock, land);
                SizeSceneryBlock(ref standingBlock, land);
            }
            for (var t = 0; t < land.size.y; t++)
            {
                var y = height - 1 - (land.yMin + t);
                for (var s = 0; s < land.size.x; s++)
                {
                    var x = land.xMin + s;
                    var index = y * width + x;
                    var hash = CellHash(x, y);
                    var block = t * land.size.x + s;
                    if (sceneryTiled)
                    {
                        // Empty unless StandScenery puts something there.
                        var empty = new TileChangeData(new Vector3Int(x, land.yMin + t, 0), null, Color.white, Matrix4x4.identity);
                        flatBlock[block] = empty;
                        standingBlock[block] = empty;
                    }
                    if (drawnWater[index])
                    {
                        landBlock[block] = WaterTile(hash);
                        continue;
                    }
                    var cellSeason = (Season)landSeasons[index];
                    landBlock[block] = GroundTile(cellSeason, hash);
                    StandScenery(chunk, x, y, block, hash, cellSeason);
                }
            }
            landMap.SetTilesBlock(land, landBlock);
            if (sceneryTiled)
            {
                // Each tile carries its own nudge, which lock flags would drop.
                flatMap.SetTiles(flatBlock, true);
                standingMap.SetTiles(standingBlock, true);
            }


            var shore = chunk.Shore;
            if (shoreBlock == null || shoreBlock.Length != shore.size.x * shore.size.y) shoreBlock = new TileBase[shore.size.x * shore.size.y];
            for (var j = 0; j < shore.size.y; j++)
            {
                for (var i = 0; i < shore.size.x; i++)
                {
                    var index = (shore.yMin + j) * (width + 1) + shore.xMin + i;
                    var cornerSeason = (Season)landSeasons[CornerOwner(shore.xMin + i, shore.yMin + j)];
                    shoreBlock[j * shore.size.x + i] = art.GroundOf(cornerSeason).Shore[shoreMasks[index]];
                }
            }
            shoreMap.SetTilesBlock(shore, shoreBlock);
            return chunk;
        }

        // One scenery tile write per cell of `land`, reused from chunk to
        // chunk; a chunk on the map's edge is smaller.
        private static void SizeSceneryBlock(ref TileChangeData[] block, BoundsInt land)
        {
            if (block == null || block.Length != land.size.x * land.size.y) block = new TileChangeData[land.size.x * land.size.y];
        }

        // Clears one chunk's tiles and hands its scenery back to the pool.
        private void Release(Chunk chunk)
        {
            var land = chunk.Land;
            if (landBlock == null || landBlock.Length != land.size.x * land.size.y) landBlock = new TileBase[land.size.x * land.size.y];
            System.Array.Clear(landBlock, 0, landBlock.Length);
            landMap.SetTilesBlock(land, landBlock);
            if (chunk.SceneryTiled)
            {
                flatMap.SetTilesBlock(land, landBlock);
                standingMap.SetTilesBlock(land, landBlock);
            }

            var shore = chunk.Shore;
            if (shoreBlock == null || shoreBlock.Length != shore.size.x * shore.size.y) shoreBlock = new TileBase[shore.size.x * shore.size.y];
            System.Array.Clear(shoreBlock, 0, shoreBlock.Length);
            shoreMap.SetTilesBlock(shore, shoreBlock);

            foreach (var renderer in chunk.Scenery)
            {
                renderer.enabled = false;
                sceneryPool.Push(renderer);
            }
            chunks[chunk.Row * chunksAcross + chunk.Column] = null;
        }

        // The tree, rock, bush or flower a land cell has, if any: into the
        // scenery blocks at `block` when the chunk's scenery is tiled, else
        // on a renderer.
        private void StandScenery(Chunk chunk, int x, int y, int block, uint hash, Season season)
        {
            var kind = grid[new WorldPosition(x, y)];
            if (kind == TerrainKind.Forest)
            {
                var tree = TreeOf(hash);
                Stand(chunk, art.Trees[tree], art.TreeTiles[tree], x, y, block, 0.3f);
            }
            else if (kind == TerrainKind.Hills)
            {
                var rock = hash % (uint)art.Rocks.Length;
                Stand(chunk, art.Rocks[rock], art.RockTiles[rock], x, y, block, 0.3f);
            }
            else if (kind == TerrainKind.Plains && Decorated(hash) && !Tufted(hash))
            {
                // Mostly flowers and sprouts, now and then a bush.
                var pick = hash >> 12;
                if (pick % 8 == 0)
                {
                    var bush = pick / 8 % (uint)art.Bushes.Length;
                    Stand(chunk, art.Bushes[bush], art.BushTiles[bush], x, y, block, 0.5f);
                    return;
                }
                // A pixel or two at quarter size: not worth drawing there.
                if (!chunk.WithDecor) return;
                var decor = pick / 8 % (uint)art.FlatDecor.Length;
                if (chunk.SceneryTiled)
                {
                    var tiles = art.FlatDecorTiles[decor];
                    var tile = new TileChangeData(flatBlock[block].position, tiles[hash % (uint)tiles.Length], Color.white, Nudged(x, y, 0.5f));
                    chunk.FlowerTiles.Add((tile, y * width + x));
                    // Nothing flowers under snow.
                    if (season != Season.Winter) flatBlock[block] = tile;
                    return;
                }
                var frames = art.FlatDecor[decor];
                var flower = Lay(chunk, frames[0], frames, x, y, 0.5f, false);
                // Nothing flowers under snow.
                flower.enabled = season != Season.Winter;
                chunk.Flowers.Add((flower, y * width + x));
            }
        }

        // Something standing in a cell: a tile in the standing scenery when
        // the chunk's scenery is tiled, else a renderer that sorts like
        // people, so people walk behind it.
        private void Stand(Chunk chunk, Sprite sprite, Tile tile, int x, int y, int block, float nudge)
        {
            if (chunk.SceneryTiled) standingBlock[block] = new TileChangeData(standingBlock[block].position, tile, Color.white, Nudged(x, y, nudge));
            else Lay(chunk, sprite, null, x, y, nudge, true);
        }

        private TileBase GroundTile(Season season, uint hash)
        {
            var ground = art.GroundOf(season);
            var tuft = ground.Tufts.Length > 0 && Decorated(hash) && Tufted(hash);
            return tuft ? ground.Tufts[(hash >> 12) % (uint)ground.Tufts.Length] : ground.Plain;
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

        // Every cell as the season at `now`, with nothing spreading: when the
        // world is shown.
        private void StartSpread(SimulationTime now)
        {
            spreadTo = now.Season;
            spreadSeason = now.Ticks / TicksPerSeason;
            for (var i = 0; i < landSeasons.Length; i++) landSeasons[i] = (byte)spreadTo;
            for (var i = 0; i < turned.Length; i++) turned[i] = 255;
        }

        // Carries the season across the map (#131). When a new one begins it
        // starts at SpreadSeeds random cells; then, SpreadStepsPerDay times a
        // sim day, each cell at its edge passes it to each neighbour with
        // SpreadChance, and stays at the edge while any neighbour has not
        // turned. So snow creeps across the land in ragged fronts and melts
        // back the same way. Sim time, so it pauses and speeds up with the
        // simulation. Each step touches only the edge, so the work goes with
        // what turns, not with the size of the map. The draws are seeded by
        // the season's number, so a world spreads the same way every run, and
        // differently every season. Presentation only: nothing here feeds the
        // simulation. A cell that turns in a built chunk queues its tiles for
        // LayLand.
        private void Spread(SimulationTime now)
        {
            var season = now.Ticks / TicksPerSeason;
            if (season != spreadSeason) BeginSpread(now, season);
            var due = (long)(now.Ticks % TicksPerSeason / (float)SimulationTime.TicksPerDay * SpreadStepsPerDay);
            while (spreadSteps < due && spreadEdge.Count > 0)
            {
                nextEdge.Clear();
                foreach (var cell in spreadEdge)
                {
                    int x = cell % width, y = cell / width;
                    var open = false;
                    if (x > 0) open |= Reach(cell - 1);
                    if (x < width - 1) open |= Reach(cell + 1);
                    if (y > 0) open |= Reach(cell - width);
                    if (y < height - 1) open |= Reach(cell + width);
                    if (open) nextEdge.Add(cell);
                }
                (spreadEdge, nextEdge) = (nextEdge, spreadEdge);
                spreadSteps++;
            }
        }

        // Whether `cell` is still to turn after this step's draw for it.
        private bool Reach(int cell)
        {
            if (landSeasons[cell] == (byte)spreadTo) return false;
            if (spreadRandom.NextDouble() >= SpreadChance) return true;
            Turn(cell);
            nextEdge.Add(cell);
            return false;
        }

        // A new season's spread from fresh places. A spread still under way
        // finishes at once first, so no cell is ever more than one season
        // behind; at SpreadStepsPerDay one finishes in about a third of a
        // season, so that is for a clock that jumps.
        private void BeginSpread(SimulationTime now, long season)
        {
            if (spreadEdge.Count > 0)
                for (var i = 0; i < landSeasons.Length; i++)
                    if (landSeasons[i] != (byte)spreadTo) Turn(i);

            spreadTo = now.Season;
            spreadSeason = season;
            spreadSteps = 0;
            spreadRandom = new System.Random(unchecked((int)season * 7919 + width * 31 + height));
            System.Array.Clear(turned, 0, turned.Length);
            turnedChanged = true;
            // The zoomed-out map shows the fresh start at once, not a few
            // frames of the new season everywhere.
            turnedSentFrame = Time.frameCount - TurnedSendFrames;
            spreadEdge.Clear();
            for (var i = 0; i < SpreadSeeds; i++)
            {
                var cell = spreadRandom.Next(landSeasons.Length);
                if (landSeasons[cell] == (byte)spreadTo) continue;
                Turn(cell);
                spreadEdge.Add(cell);
            }
        }

        // One cell to spreadTo: its season, the zoomed-out map's byte, and,
        // where its chunk is built, its ground tile, the shoreline pieces it
        // owns and its flowers.
        private void Turn(int cell)
        {
            landSeasons[cell] = (byte)spreadTo;
            int x = cell % width, y = cell / width;
            turned[(height - 1 - y) * width + x] = 255;
            turnedChanged = true;

            var chunk = ChunkAt(x, y);
            if (chunk != null && !drawnWater[cell])
            {
                changedCells.Add(new Vector3Int(x, height - 1 - y, 0));
                changedTiles.Add(GroundTile(spreadTo, CellHash(x, y)));
                chunk.FlowersStale = true;
            }

            // The corners this cell owns (see CornerOwner): the one at its
            // north-west in world units, and along the map's east and north
            // edges the ones past it.
            TurnCorner(x, height - y);
            if (x == width - 1) TurnCorner(width, height - y);
            if (y == height - 1)
            {
                TurnCorner(x, 0);
                if (x == width - 1) TurnCorner(width, 0);
            }
        }

        private void TurnCorner(int i, int j)
        {
            // A corner is laid by the chunk whose cell lies north-east of it
            // (see Chunk), not by its owner's.
            if (ChunkAt(Mathf.Min(i, width - 1), Mathf.Clamp(height - 1 - j, 0, height - 1)) == null) return;
            changedCorners.Add(new Vector3Int(i, j, 0));
            changedCornerTiles.Add(art.GroundOf(spreadTo).Shore[shoreMasks[j * (width + 1) + i]]);
        }

        // The built chunk holding a cell, or null.
        private Chunk ChunkAt(int x, int y) => chunks[y / ChunkSize * chunksAcross + x / ChunkSize];

        // The cell whose season a corner's shoreline piece shows: the one
        // south-east of it, clamped to the map, so the piece turns with the
        // ground it lies on. Corner (i, j) is in world units, y counting up
        // from the south edge.
        private int CornerOwner(int i, int j) => Mathf.Clamp(height - j, 0, height - 1) * width + Mathf.Min(i, width - 1);

        // Moves the season on (Spread) and writes what it turned in the built
        // chunks to the tilemaps, each once a frame - a tile at a time, a fast
        // season change cost a tenth of a second - and shows or hides the
        // flowers it reached. Zoomed out too, while the art is hidden: the
        // chunks are kept for coming back in, so they must not fall behind.
        private void LayLand(SimulationTime now)
        {
            Spread(now);
            WriteChanged(landMap, changedCells, changedTiles);
            WriteChanged(shoreMap, changedCorners, changedCornerTiles);
            changedCells.Clear();
            changedTiles.Clear();
            changedCorners.Clear();
            changedCornerTiles.Clear();

            foreach (var chunk in resident)
            {
                if (!chunk.FlowersStale) continue;
                chunk.FlowersStale = false;

                // Nothing flowers under snow; a tiled flower is written only
                // when it comes or goes.
                foreach (var (renderer, cell) in chunk.Flowers) renderer.enabled = landSeasons[cell] != (byte)Season.Winter;
                foreach (var (tile, cell) in chunk.FlowerTiles)
                {
                    var blooming = landSeasons[cell] != (byte)Season.Winter;
                    if (flatMap.HasTile(tile.position) == blooming) continue;
                    var change = tile;
                    if (!blooming) change.tile = null;
                    changedFlowers.Add(change);
                }
            }
            // As in Build, each flower keeps its nudge. Allocates, and only on
            // a frame where a flower came or went.
            if (changedFlowers.Count > 0) flatMap.SetTiles(changedFlowers.ToArray(), true);
            changedFlowers.Clear();
        }

        // Writes what the spread turned to one tilemap in one call. Allocates
        // the call's arrays, and only on a frame where something turned.
        private static void WriteChanged(Tilemap map, List<Vector3Int> cells, List<TileBase> tiles)
        {
            if (cells.Count > 0) map.SetTiles(cells.ToArray(), tiles.ToArray());
        }

        // Swaying grass and flickering campfires, on a clock that stops while
        // the simulation is paused. Presentation only.
        private void Animate()
        {
            var time = animationTime;
            foreach (var chunk in resident)
                foreach (var (renderer, frames, phase) in chunk.Animated)
                    renderer.sprite = frames[(int)(time * ArtSet.DecorFramesPerSecond + phase) % frames.Length];
        }

        // A pooled scenery renderer, hidden until a chunk stands it up.
        private SpriteRenderer NewScenery()
        {
            var renderer = new GameObject("Scenery").AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(sceneryRoot, false);
            renderer.sharedMaterial = spriteMaterial;
            renderer.enabled = false;
            return renderer;
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

        // Something in a cell, nudged within it (see Jitter), on a renderer
        // from the pool. Upright things sort like people, so people walk
        // behind them; flat ones lie under everyone. `frames`, when it has
        // more than one, loops.
        private SpriteRenderer Lay(Chunk chunk, Sprite sprite, Sprite[] frames, int x, int y, float nudge, bool upright)
        {
            var hash = CellHash(x, y);
            var jitter = Jitter(hash, nudge);
            var cellY = y + 0.5f + jitter.y;
            var renderer = sceneryPool.Count > 0 ? sceneryPool.Pop() : NewScenery();
            renderer.enabled = true;
            renderer.transform.localPosition = new Vector3(OnArtPixel(x + 0.5f + jitter.x), OnArtPixel(height - cellY), 0f);
            renderer.sprite = sprite;
            renderer.sortingOrder = upright ? OrderAt(cellY) : -2;
            chunk.Scenery.Add(renderer);
            if (frames != null && frames.Length > 1) chunk.Animated.Add((renderer, frames, hash % (uint)frames.Length));
            return renderer;
        }

        // How far something stands from the middle of its cell, by up to
        // `nudge` of a cell each way, so a forest is not a grid; in cells,
        // with y counting south as Core's rows do.
        private static Vector2 Jitter(uint hash, float nudge) =>
            new Vector2(((hash & 0xff) / 255f - 0.5f) * nudge, (((hash >> 8) & 0xff) / 255f - 0.5f) * nudge);

        // The same nudge as Lay gives a renderer, as a tile's transform: from
        // the middle of the tile's cell to where the renderer would stand.
        private Matrix4x4 Nudged(int x, int y, float nudge)
        {
            var jitter = Jitter(CellHash(x, y), nudge);
            var middleY = height - y - 0.5f;
            var offset = new Vector3(OnArtPixel(x + 0.5f + jitter.x) - (x + 0.5f), OnArtPixel(middleY - jitter.y) - middleY, 0f);
            return Matrix4x4.Translate(offset);
        }

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

        // One square of the art view's map while it is built: where its tiles
        // are, the scenery standing on it, and how far a spreading season has
        // reached across it.
        private sealed class Chunk
        {
            public Chunk(int column, int row, int mapWidth, int mapHeight)
            {
                Column = column;
                Row = row;
                var x = column * ChunkSize;
                var y = row * ChunkSize;
                var across = Mathf.Min(ChunkSize, mapWidth - x);
                var down = Mathf.Min(ChunkSize, mapHeight - y);
                // Tile rows count up from the south edge, Core rows down from
                // the north.
                var tileRow = mapHeight - y - down;
                Land = new BoundsInt(x, tileRow, 0, across, down, 1);
                // Each corner belongs to the chunk north-east of it in world
                // units; the map's last column and first row of corners go to
                // the chunks along those edges.
                var last = x + across == mapWidth ? 1 : 0;
                var top = y == 0 ? 1 : 0;
                Shore = new BoundsInt(x, tileRow, 0, across + last, down + top, 1);
            }

            public int Column { get; }
            public int Row { get; }
            public BoundsInt Land { get; }
            public BoundsInt Shore { get; }

            // Whether a land cell turned this frame, so its flowers may have
            // come or gone.
            public bool FlowersStale;

            // Whether its flowers and sprouts were stood up (see DecorFrom).
            public bool WithDecor;

            // Whether its scenery is tiles rather than renderers (see
            // LiveSceneryFrom): FlowerTiles when it is, Scenery, Flowers and
            // Animated when it is not.
            public bool SceneryTiled;
            public readonly List<SpriteRenderer> Scenery = new List<SpriteRenderer>();
            public readonly List<(SpriteRenderer Renderer, int Cell)> Flowers = new List<(SpriteRenderer, int)>();

            // Each flower's tile as it is when blooming, and its cell.
            public readonly List<(TileChangeData Tile, int Cell)> FlowerTiles = new List<(TileChangeData, int)>();
            public readonly List<(SpriteRenderer Renderer, Sprite[] Frames, uint Phase)> Animated = new List<(SpriteRenderer, Sprite[], uint)>();
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
