using System.Collections.Generic;
using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using UnityEngine;
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

    // The plainest possible 3/4 oblique view of a running world (#72): one
    // coloured pixel per terrain cell, rows squashed so the ground reads as
    // seen from above at an angle, and people as upright markers standing on
    // it, sorted so whoever is further south draws in front. At Far zoom the
    // people give way to one marker per settlement or band (#115). Real art
    // is #121's.
    public sealed class WorldView2D : MonoBehaviour
    {
        // Vertical scale of a ground row relative to a column: the 3/4 tilt.
        public const float RowSquash = 0.75f;

        // Layer edges in UI-scaled pixels per cell (see SimulationDriver.UiScale),
        // with slack either side so a zoom resting on an edge does not flicker.
        public const float FarBelow = 24f;
        public const float NearFrom = 40f;
        private const float BandSlack = 2f;

        // A community marker's side in UI-scaled pixels, whatever the zoom.
        private const float CommunityMarkerSize = 14f;

        public Shader spriteShader;
        public Camera sceneCamera;

        private readonly List<SpriteRenderer> markers = new List<SpriteRenderer>();
        private readonly List<EntityId> markerIds = new List<EntityId>();
        private readonly List<SpriteRenderer> communityMarkers = new List<SpriteRenderer>();
        private readonly List<EntityId> communityIds = new List<EntityId>();
        private readonly List<int> communitySizes = new List<int>();
        private readonly List<ICommunity> bands = new List<ICommunity>();
        private readonly List<Object> owned = new List<Object>();
        private Material spriteMaterial;
        private Texture2D terrainTexture;
        private Sprite unitSprite;
        private Transform markerRoot;
        private Transform communityRoot;
        private SpriteRenderer highlight;
        private int width, height;
        private int usedMarkers, usedCommunities;
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
            var ground = new GameObject("Terrain").AddComponent<SpriteRenderer>();
            ground.transform.SetParent(transform, false);
            ground.transform.localScale = new Vector3(1f, RowSquash, 1f);
            ground.sprite = Own(Sprite.Create(terrainTexture, new Rect(0, 0, width, height), Vector2.zero, 1f));
            ground.sharedMaterial = spriteMaterial;
            ground.sortingOrder = -1;

            markerRoot = new GameObject("People").transform;
            markerRoot.SetParent(transform, false);
            communityRoot = new GameObject("Communities").transform;
            communityRoot.SetParent(transform, false);

            highlight = NewMarker("Selection", transform);
            highlight.color = new Color(1f, 0.92f, 0.2f);
            highlight.enabled = false;

            // Terrain is static until bridges (section 12, M6) rewrite cells;
            // then this belongs in Refresh, behind a change check.
            DrawTerrain(world.Grid);
        }

        // Redraws from the world as it stands. Called every frame, paused or
        // not, so zooming across a layer edge redraws without a sim step.
        public void Refresh(World world, float pixelsPerCell, float scale)
        {
            uiScale = scale;
            Band = BandFor(pixelsPerCell / scale);
            DrawPeople(world);
            DrawCommunities(world, pixelsPerCell);
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
            // The span rather than People.Alive(): Alive allocates an iterator
            // per call, and this runs every frame. Free slots have no id.
            // Placed even when hidden, so following someone survives a zoom out.
            foreach (var record in world.People.RecordSpan())
            {
                if (record.Id.IsNone) continue;
                var person = record.Handle;
                var at = PositionOf(world, person, record.Position, now);
                var child = record.AgeStage == AgeStage.Infant || record.AgeStage == AgeStage.Child;
                var marker = MarkerAt(used);
                markerIds[used++] = record.Id;
                // A per-person offset inside the cell keeps a band from
                // collapsing onto one marker. Drawn from the id, not an rng:
                // it is presentation and must never touch the simulation. Not
                // GetHashCode: HashCode.Combine is seeded per process.
                var id = (long)((record.Id.Value * 0x9E3779B97F4A7C15UL) >> 40);
                var jitterX = ((id & 0xff) / 255f - 0.5f) * 0.7f;
                var jitterY = (((id >> 8) & 0xff) / 255f - 0.5f) * 0.7f;
                var cellY = at.Y + 0.5f + jitterY;
                marker.transform.localPosition = new Vector3(at.X + 0.5f + jitterX, (height - cellY) * RowSquash, 0f);
                marker.transform.localScale = child ? new Vector3(0.25f, 0.4f, 1f) : new Vector3(0.3f, 0.65f, 1f);
                marker.color = child ? new Color(0.98f, 0.86f, 0.55f) : ColourOf(record.Job);
                // Further south (larger row) is nearer the viewer, so it draws on top.
                marker.sortingOrder = Mathf.RoundToInt(cellY * 16f);
                marker.enabled = visible;
            }

            for (var i = used; i < markers.Count; i++) markers[i].enabled = false;
            usedMarkers = used;
        }

        private void DrawCommunities(World world, float pixelsPerCell)
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
            marker.transform.localPosition = new Vector3(at.X + 0.5f, (height - cellY) * RowSquash - size / 2f, 0f);
            marker.transform.localScale = new Vector3(size, size, 1f);
            marker.color = settled ? new Color(0.93f, 0.9f, 0.82f) : new Color(0.85f, 0.35f, 0.3f);
            marker.sortingOrder = 100000 + index;
            marker.enabled = visible;
        }

        private void DrawHighlight()
        {
            highlight.enabled = false;
            if (Selected.IsNone) return;
            var marker = VisibleMarkerOf(Selected);
            if (marker == null) return;
            // A slightly larger copy behind the selected marker.
            var scale = marker.transform.localScale;
            var pad = Mathf.Max(scale.x, scale.y) * 0.35f;
            highlight.transform.localPosition = marker.transform.localPosition - new Vector3(0f, pad / 2f, 0f);
            highlight.transform.localScale = new Vector3(scale.x + pad, scale.y + pad, 1f);
            highlight.sortingOrder = marker.sortingOrder - 1;
            highlight.enabled = true;
        }

        // Where something is drawn, in world units: the middle of its marker.
        // True for a person whether or not the current layer shows them.
        public bool TryGetDrawnPosition(EntityId id, out Vector2 position)
        {
            var marker = MarkerOf(id);
            if (marker == null)
            {
                position = default;
                return false;
            }
            position = Centre(marker);
            return true;
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
                for (var i = 0; i < usedCommunities; i++) Consider(communityMarkers[i], communityIds[i]);
            }
            else
            {
                for (var i = 0; i < usedMarkers; i++) Consider(markers[i], markerIds[i]);
            }

            found.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Id.CompareTo(b.Id));
            foreach (var entry in found) into.Add(entry.Id);

            void Consider(SpriteRenderer marker, EntityId id)
            {
                var screen = (Vector2)sceneCamera.WorldToScreenPoint(Centre(marker));
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

        private SpriteRenderer MarkerOf(EntityId id)
        {
            if (id.Kind == EntityKind.Person)
            {
                for (var i = 0; i < usedMarkers; i++) if (markerIds[i] == id) return markers[i];
                return null;
            }
            for (var i = 0; i < usedCommunities; i++) if (communityIds[i] == id) return communityMarkers[i];
            return null;
        }

        private SpriteRenderer VisibleMarkerOf(EntityId id)
        {
            var marker = MarkerOf(id);
            return marker != null && marker.enabled ? marker : null;
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

        private SpriteRenderer MarkerAt(int index)
        {
            if (index < markers.Count) return markers[index];
            markers.Add(NewMarker("Person", markerRoot));
            markerIds.Add(EntityId.None);
            return markers[index];
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
    }
}
