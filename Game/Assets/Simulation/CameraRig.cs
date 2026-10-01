using UnityEngine;

namespace KingdomWatch.Game
{
    // Where the camera looks and how close (#115). Holds no input of its own:
    // ViewInput turns touches, the mouse and the keyboard into these calls.
    // Zoom is kept as screen pixels per map cell, the unit the zoom layers
    // switch on, and the orthographic size is derived from it every frame.
    //
    // While the layer showing draws pixel art (#121), the zoom shown is the
    // requested one rounded to a whole multiple of the art's 16 px, or to a
    // half or a quarter of it (#130), and the
    // camera sits on whole screen pixels: every art pixel is then the same
    // square of screen pixels, and nothing shimmers as the map pans.
    public sealed class CameraRig
    {
        // Closest zoom: about this many cells across the screen's shorter side.
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
            mapHeight = height;
        }

        // Set by the driver from the layer showing: the Far layer draws no
        // pixel art, so it zooms freely.
        public bool Snap { get; set; }

        // The zoom asked for, which the zoom layers switch on. Kept apart from
        // the zoom shown so a pinch accumulates between snapped steps.
        public float RequestedPixelsPerCell => pixelsPerCell;

        // The zoom shown.
        public float PixelsPerCell => Snap ? Snapped(pixelsPerCell) : pixelsPerCell;

        // Screen pixels per cell with the whole map in the area the panel leaves
        // free; the furthest the camera zooms out.
        public float FarthestPixelsPerCell { get; private set; }

        // The farthest zoom at which panning, and so following, is unrestricted.
        public float FreePanPixelsPerCell => FarthestPixelsPerCell * FreePanZoom;

        // A whole multiple of the art scale, so the closest zoom is crisp too.
        public float NearestPixelsPerCell => Snapped(Mathf.Min(Screen.width, Screen.height) / NearestCellsAcross);

        // Clamps and applies. Called every frame, so a rotation or resize
        // re-clamps; the first call frames the whole map.
        // `reserved` is the panel's footprint in GUI coordinates (origin top-left, y down),
        // and `topInset` the height of the bar across the whole top of the screen.
        public void Apply(Rect reserved, float topInset)
        {
            var home = HomeFraming(reserved, topInset, out var homeScale);
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

            var shown = PixelsPerCell;
            var at = Snap ? new Vector2(OnScreenPixel(focus.x, shown, Screen.width), OnScreenPixel(focus.y, shown, Screen.height)) : focus;
            camera.orthographic = true;
            camera.orthographicSize = Screen.height / (2f * shown);
            camera.transform.position = new Vector3(at.x, at.y, -10f);
        }

        // What the camera shows, in world units, as of the last Apply: the
        // panel's minimap draws it.
        public Rect VisibleRect
        {
            get
            {
                var half = camera.orthographicSize;
                var at = camera.transform.position;
                return new Rect(at.x - half * camera.aspect, at.y - half, 2f * half * camera.aspect, 2f * half);
            }
        }

        // Moves the map with a finger or cursor: `delta` is in screen pixels.
        public void PanBy(Vector2 delta) => focus -= delta / PixelsPerCell;

        // Zooms by `factor` about the middle of the screen. While the art is
        // snapped, zooming moves a whole clean size at a time as soon as the
        // request has moved StepAt from the one on show, rather than at the
        // halfway point: below 16 px the sizes double, and a wheel notch that
        // changed nothing on screen read as input swallowed (#130).
        public void ZoomBy(float factor)
        {
            var target = pixelsPerCell * factor;
            if (Snap && factor != 1f)
            {
                var shown = Snapped(pixelsPerCell);
                // A request behind the size on show - left there by the last
                // step the other way, or by coming in from the colour map -
                // starts from what is on screen, so no input goes unseen.
                if ((factor > 1f && pixelsPerCell < shown) || (factor < 1f && pixelsPerCell > shown)) target = shown * factor;
                if (target >= shown * StepAt) target = NextSize(shown, true);
                else if (target <= shown / StepAt) target = NextSize(shown, false);
            }
            pixelsPerCell = Mathf.Clamp(target, FarthestPixelsPerCell, Mathf.Max(FarthestPixelsPerCell, NearestPixelsPerCell));
        }

        // How far a zoom request must move from the size on show before the
        // snapped art steps to the next one.
        private const float StepAt = 1.15f;

        // The clean size next above or below `shown`: 16 px steps from 16 up,
        // halving below; below WorldView2D.SmallestArt, the colour map.
        private static float NextSize(float shown, bool up)
        {
            if (up) return shown >= ArtSet.PixelsPerCell ? shown + ArtSet.PixelsPerCell : shown * 2f;
            var down = shown > ArtSet.PixelsPerCell ? shown - ArtSet.PixelsPerCell : shown / 2f;
            return down >= WorldView2D.SmallestArt ? down : WorldView2D.FarEdge / StepAt;
        }

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

        // Zooms to `pixels` per cell, within the rig's limits: a scripted
        // run's stops (#132).
        public void ZoomTo(float pixels) => pixelsPerCell = pixels;

        // Zooms in to at least `pixels` per cell; closer zooms are left alone.
        public void ZoomToAtLeast(float pixels) => pixelsPerCell = Mathf.Max(pixelsPerCell, pixels);

        public void WholeMap() => pixelsPerCell = FarthestPixelsPerCell;

        private Vector2 ScreenToWorld(Vector2 screenPoint) =>
            focus + (screenPoint - new Vector2(Screen.width / 2f, Screen.height / 2f)) / PixelsPerCell;

        // Whole multiples of the art's 16 px, or below it a half, a quarter,
        // down to WorldView2D.SmallestArt: each screen pixel then covers the
        // same square of art pixels, so the art shrinks evenly (#130).
        private static float Snapped(float pixels)
        {
            if (pixels >= ArtSet.PixelsPerCell * 0.75f)
                return Mathf.Max(ArtSet.PixelsPerCell, Mathf.Round(pixels / ArtSet.PixelsPerCell) * ArtSet.PixelsPerCell);
            float shown = ArtSet.PixelsPerCell;
            while (shown / 2f >= WorldView2D.SmallestArt && pixels < shown * 0.75f) shown /= 2f;
            return shown;
        }

        // The camera position nearest `world` that puts screen pixel edges on
        // world positions the art is drawn at. The screen's middle is half a
        // pixel off an edge when its size in pixels is odd.
        private static float OnScreenPixel(float world, float pixelsPerCell, int screenPixels)
        {
            var half = screenPixels / 2f;
            return (Mathf.Round(world * pixelsPerCell - half) + half) / pixelsPerCell;
        }

        // The whole map, with a one-cell margin, fitted into whichever part of
        // the screen the panel leaves larger: beside it in landscape, below it
        // in portrait. Returns the camera centre that puts the map there.
        private Vector2 HomeFraming(Rect reserved, float topInset, out float scale)
        {
            float screenWidth = Screen.width, screenHeight = Screen.height;
            var fitWidth = mapWidth + 2f;
            var fitHeight = mapHeight + 2f;

            var beside = Rect.MinMaxRect(reserved.xMax, topInset, screenWidth, screenHeight);
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
