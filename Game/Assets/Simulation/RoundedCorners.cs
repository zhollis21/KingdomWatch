using UnityEngine;

namespace KingdomWatch.Game
{
    // The screen's rounded corners, which Screen.safeArea leaves out: held
    // sideways, a phone's corner cut off the end of the panel's bar. Android
    // 12 and later says how round each corner is; elsewhere, and before the
    // window can say, the safe area is used as it is.
    public static class RoundedCorners
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Each corner's centre (window pixels, y down) and radius, read once
        // for each screen size, so a rotation reads them again.
        private static Vector2Int readFor;
        private static Vector3[] corners;

        public static Rect Inside(Rect safe, bool liftBottom)
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size != readFor)
            {
                corners = Read();
                // Before the window is attached it has no insets: try again
                // next frame.
                if (corners != null) readFor = size;
            }
            if (corners == null || corners.Length == 0) return safe;

            // The top corners narrow the safe area, so the bar across the top
            // keeps its height; when asked, the bottom ones lift it, so a card
            // along the bottom keeps its width. Y down, as Android counts.
            var top = Screen.height - safe.yMax;
            var left = Across(corners[0], safe.xMin, top, true);
            var right = Across(corners[1], safe.xMax, top, false);
            var lift = liftBottom ? Mathf.Max(Up(corners[2], safe.xMax, false), Up(corners[3], safe.xMin, true)) : 0f;
            return Rect.MinMaxRect(safe.xMin + left, Mathf.Max(safe.yMin, lift), safe.xMax - right, safe.yMax);
        }

        // How far up from the bottom of the screen a bottom corner of the safe
        // area at `x` must sit to clear the curve below it.
        private static float Up(Vector3 corner, float x, bool leftSide)
        {
            var r = corner.z;
            if (r <= 0f) return 0f;
            var dx = leftSide ? corner.x - x : x - corner.x;
            if (dx <= 0f) return 0f;
            var edge = Mathf.Sqrt(Mathf.Max(0f, r * r - dx * dx));
            return Mathf.Max(0f, Screen.height - (corner.y + edge));
        }

        // How far in from `x` a point at height `y` must move to be clear of
        // a corner's curve: none outside the square the curve is drawn in.
        private static float Across(Vector3 corner, float x, float y, bool leftSide)
        {
            var r = corner.z;
            if (r <= 0f) return 0f;
            var dy = corner.y - y;
            if (dy <= 0f) return 0f;
            var edge = Mathf.Sqrt(Mathf.Max(0f, r * r - dy * dy));
            var clear = leftSide ? corner.x - edge : corner.x + edge;
            return Mathf.Max(0f, leftSide ? clear - x : x - clear);
        }

        // Top left, top right, bottom right, bottom left, as Android numbers
        // them; empty when the phone has none to report, null when the
        // window cannot say yet.
        private static Vector3[] Read()
        {
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    if (version.GetStatic<int>("SDK_INT") < 31) return new Vector3[0];
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
                using (var insets = decor.Call<AndroidJavaObject>("getRootWindowInsets"))
                {
                    if (insets == null) return null;
                    var found = new Vector3[4];
                    for (var i = 0; i < 4; i++)
                        using (var corner = insets.Call<AndroidJavaObject>("getRoundedCorner", i))
                        {
                            if (corner == null) continue;
                            using (var centre = corner.Call<AndroidJavaObject>("getCenter"))
                                found[i] = new Vector3(centre.Get<int>("x"), centre.Get<int>("y"), corner.Call<int>("getRadius"));
                        }
                    return found;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("RoundedCorners: " + e.Message);
                return new Vector3[0];
            }
        }
#else
        public static Rect Inside(Rect safe, bool liftBottom) => safe;
#endif
    }
}
