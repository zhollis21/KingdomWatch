using System.Collections.Generic;
using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using UnityEngine;

namespace KingdomWatch.Game
{
    // The plainest possible 3/4 oblique view of a running world (#72): one
    // coloured pixel per terrain cell, rows squashed so the ground reads as
    // seen from above at an angle, and people as upright markers standing on
    // it, sorted so whoever is further south draws in front. Camera controls,
    // zoom, selection and real art are #115's.
    public sealed class WorldView2D : MonoBehaviour
    {
        // Vertical scale of a ground row relative to a column: the 3/4 tilt.
        public const float RowSquash = 0.75f;

        public Shader spriteShader;
        public Camera sceneCamera;

        private readonly List<SpriteRenderer> markers = new List<SpriteRenderer>();
        private readonly List<Object> owned = new List<Object>();
        private Material spriteMaterial;
        private Texture2D terrainTexture;
        private Sprite unitSprite;
        private Transform markerRoot;
        private int width, height;

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

            // Terrain is static until bridges (section 12, M6) rewrite cells;
            // then this belongs in Refresh, behind a change check.
            DrawTerrain(world.Grid);
            Refresh(world);
        }

        public void Refresh(World world)
        {
            var used = 0;
            var now = world.Now;
            foreach (var person in world.People.Alive())
            {
                var at = PositionOf(world, person, now);
                var stage = world.People.GetAgeStage(person);
                var child = stage == AgeStage.Infant || stage == AgeStage.Child;
                var marker = MarkerAt(used++);
                // A per-person offset inside the cell keeps a band from
                // collapsing onto one marker. Drawn from the id, not an rng:
                // it is presentation and must never touch the simulation. Not
                // GetHashCode: HashCode.Combine is seeded per process.
                var id = (long)((world.People.GetId(person).Value * 0x9E3779B97F4A7C15UL) >> 40);
                var jitterX = ((id & 0xff) / 255f - 0.5f) * 0.7f;
                var jitterY = (((id >> 8) & 0xff) / 255f - 0.5f) * 0.7f;
                var cellY = at.Y + 0.5f + jitterY;
                marker.transform.localPosition = new Vector3(at.X + 0.5f + jitterX, (height - cellY) * RowSquash, 0f);
                marker.transform.localScale = child ? new Vector3(0.25f, 0.4f, 1f) : new Vector3(0.3f, 0.65f, 1f);
                marker.color = child ? new Color(0.98f, 0.86f, 0.55f) : new Color(0.95f, 0.62f, 0.35f);
                // Further south (larger row) is nearer the viewer, so it draws on top.
                marker.sortingOrder = Mathf.RoundToInt(cellY * 16f);
                marker.enabled = true;
            }

            for (var i = used; i < markers.Count; i++) markers[i].enabled = false;
        }

        // Where someone is drawn: part-way along their route while a task is
        // under way (Jobs.PositionAt), otherwise the cell they are stored at.
        private static WorldPosition PositionOf(World world, PersonHandle person, SimulationTime now)
        {
            if (world.Jobs.HasTask(person))
            {
                var task = world.Jobs.TaskOf(person);
                if (task.Start.CompareTo(now) <= 0 && now.CompareTo(task.End) < 0) return world.Jobs.PositionAt(person, now);
            }
            return world.People.GetPosition(person);
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
            var marker = new GameObject("Person").AddComponent<SpriteRenderer>();
            marker.transform.SetParent(markerRoot, false);
            marker.sprite = unitSprite;
            marker.sharedMaterial = spriteMaterial;
            markers.Add(marker);
            return marker;
        }

        // Fits the whole map, with a one-cell margin, into whichever part of
        // the screen the panel leaves larger: beside it in landscape, below it
        // in portrait. Called every frame, so a rotation or resize re-frames.
        // `reserved` is in GUI coordinates (origin top-left, y down).
        public void Frame(Rect reserved)
        {
            if (sceneCamera == null || width == 0) return;
            float screenWidth = Screen.width, screenHeight = Screen.height;
            var mapWidth = width + 2f;
            var mapHeight = height * RowSquash + 2f;

            var beside = Rect.MinMaxRect(reserved.xMax, 0f, screenWidth, screenHeight);
            var below = Rect.MinMaxRect(0f, reserved.yMax, screenWidth, screenHeight);
            var besideScale = Fit(beside, mapWidth, mapHeight);
            var belowScale = Fit(below, mapWidth, mapHeight);
            var area = besideScale >= belowScale ? beside : below;
            var pixelsPerUnit = Mathf.Max(besideScale, belowScale);

            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = screenHeight / (2f * pixelsPerUnit);
            // Shift the camera so the map's centre lands on the area's centre;
            // screen y runs up where GUI y runs down.
            var offsetX = area.center.x - screenWidth / 2f;
            var offsetY = (screenHeight - area.center.y) - screenHeight / 2f;
            sceneCamera.transform.position = new Vector3(
                width / 2f - offsetX / pixelsPerUnit,
                height * RowSquash / 2f - offsetY / pixelsPerUnit,
                -10f);
        }

        private static float Fit(Rect area, float mapWidth, float mapHeight) =>
            Mathf.Max(0.01f, Mathf.Min(area.width / mapWidth, area.height / mapHeight));

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
