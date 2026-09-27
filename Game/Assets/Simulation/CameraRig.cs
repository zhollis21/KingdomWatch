using UnityEngine;

namespace KingdomWatch.Game
{
    // Where the camera looks and how close (#115). Holds no input of its own:
    // ViewInput turns touches, the mouse and the keyboard into these calls.
    // Zoom is kept as screen pixels per map cell, the unit the zoom layers
    // switch on, and the orthographic size is derived from it every frame.
    public sealed class CameraRig
    {
        // Closest zoom: this many cells across the screen's shorter side.
        private const float NearestCellsAcross = 4f;

        // How many times the whole-map zoom panning is fully free: below it,
        // the pan range narrows toward the whole-map framing.
        private const float FreePanZoom = 2f;

        private readonly Camera camera;
        private readonly float mapWidth, mapHeight;
        private Vector2 focus;
        private float pixelsPerCell;
        private bool framed;

        public CameraRig(Camera camera, int width, int height)
        {
            this.camera = camera;
            mapWidth = width;
            mapHeight = height * WorldView2D.RowSquash;
        }

        public float PixelsPerCell => pixelsPerCell;

        // Screen pixels per cell with the whole map in the area the panel leaves
        // free; the furthest the camera zooms out.
        public float FarthestPixelsPerCell { get; private set; }

        // The farthest zoom at which panning, and so following, is unrestricted.
        public float FreePanPixelsPerCell => FarthestPixelsPerCell * FreePanZoom;

        public float NearestPixelsPerCell => Mathf.Min(Screen.width, Screen.height) / NearestCellsAcross;

        // Clamps and applies. Called every frame, so a rotation or resize
        // re-clamps; the first call frames the whole map.
        // `reserved` is the panel's footprint in GUI coordinates (origin top-left, y down).
        public void Apply(Rect reserved)
        {
            var home = HomeFraming(reserved, out var homeScale);
            FarthestPixelsPerCell = homeScale;
            if (!framed)
            {
                focus = home;
                pixelsPerCell = homeScale;
                framed = true;
            }

            pixelsPerCell = Mathf.Clamp(pixelsPerCell, FarthestPixelsPerCell, Mathf.Max(FarthestPixelsPerCell, NearestPixelsPerCell));
            // Zoomed in, panning stops once a map edge reaches the middle of the
            // screen. Zooming out narrows that range onto `home`, where the whole
            // map sits beside or below the panel, so the map eases into place
            // rather than jumping there on the last notch. `home` can lie past
            // the map's edge on a small portrait screen, which lerping allows.
            var t = Mathf.Clamp01(Mathf.Log(pixelsPerCell / FarthestPixelsPerCell) / Mathf.Log(FreePanZoom));
            focus.x = Mathf.Clamp(focus.x, Mathf.Lerp(home.x, 0f, t), Mathf.Lerp(home.x, mapWidth, t));
            focus.y = Mathf.Clamp(focus.y, Mathf.Lerp(home.y, 0f, t), Mathf.Lerp(home.y, mapHeight, t));

            camera.orthographic = true;
            camera.orthographicSize = Screen.height / (2f * pixelsPerCell);
            camera.transform.position = new Vector3(focus.x, focus.y, -10f);
        }

        // Moves the map with a finger or cursor: `delta` is in screen pixels.
        public void PanBy(Vector2 delta) => focus -= delta / pixelsPerCell;

        // Zooms by `factor` about the middle of the screen.
        public void ZoomBy(float factor) =>
            pixelsPerCell = Mathf.Clamp(pixelsPerCell * factor, FarthestPixelsPerCell, Mathf.Max(FarthestPixelsPerCell, NearestPixelsPerCell));

        // Zooms by `factor` while keeping the world point under `screenPoint`
        // still: a pinch zooms where the fingers are.
        public void ZoomAt(Vector2 screenPoint, float factor)
        {
            var before = ScreenToWorld(screenPoint);
            ZoomBy(factor);
            focus += before - ScreenToWorld(screenPoint);
        }

        // Centres on a world point, used by following. Leaves zoom alone.
        public void CentreOn(Vector2 world) => focus = world;

        // Zooms in to at least `pixels` per cell; closer zooms are left alone.
        public void ZoomToAtLeast(float pixels) => pixelsPerCell = Mathf.Max(pixelsPerCell, pixels);

        public void WholeMap() => pixelsPerCell = FarthestPixelsPerCell;

        private Vector2 ScreenToWorld(Vector2 screenPoint) =>
            focus + (screenPoint - new Vector2(Screen.width / 2f, Screen.height / 2f)) / pixelsPerCell;

        // The whole map, with a one-cell margin, fitted into whichever part of
        // the screen the panel leaves larger: beside it in landscape, below it
        // in portrait. Returns the camera centre that puts the map there.
        private Vector2 HomeFraming(Rect reserved, out float scale)
        {
            float screenWidth = Screen.width, screenHeight = Screen.height;
            var fitWidth = mapWidth + 2f;
            var fitHeight = mapHeight + 2f;

            var beside = Rect.MinMaxRect(reserved.xMax, 0f, screenWidth, screenHeight);
            var below = Rect.MinMaxRect(0f, reserved.yMax, screenWidth, screenHeight);
            var besideScale = Fit(beside, fitWidth, fitHeight);
            var belowScale = Fit(below, fitWidth, fitHeight);
            var area = besideScale >= belowScale ? beside : below;
            scale = Mathf.Max(besideScale, belowScale);

            // Shift so the map's centre lands on the area's centre; screen y
            // runs up where GUI y runs down.
            var offsetX = area.center.x - screenWidth / 2f;
            var offsetY = (screenHeight - area.center.y) - screenHeight / 2f;
            return new Vector2(mapWidth / 2f - offsetX / scale, mapHeight / 2f - offsetY / scale);
        }

        private static float Fit(Rect area, float width, float height) =>
            Mathf.Max(0.01f, Mathf.Min(area.width / width, area.height / height));
    }
}
