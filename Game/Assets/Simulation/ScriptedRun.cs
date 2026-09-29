#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KingdomWatch.Core;
using UnityEngine;
using UnityEngine.Profiling;

namespace KingdomWatch.Game
{
    // A fixed camera route through the zoom layers, flown by a development
    // build launched with `-scripted-run <folder>` (#132), so a rendering
    // change can be measured before and after without anyone at the Editor.
    // It records a profiler capture of each stop, takes a screenshot at
    // each, logs how long each took to fill in, and quits.
    // tools/Profile.ps1 builds, launches and analyses it.
    //
    // Compiled into development builds and the Editor only: a release
    // player never reads the flag. Desktop timings are for finding spikes and comparing before
    // with after, not for phone performance.
    public sealed class ScriptedRun
    {
        // Stops are counted in frames, not seconds, so a run that draws
        // faster covers the same ground in the same frames and two runs
        // compare frame for frame. Each stop is its own capture, well under
        // the 2000 frames the Profiler keeps of one.
        //
        // How many frames a stop may take to fill in before the run gives up
        // on it and says so, and how many it then holds still for.
        private const int FillLimitFrames = 1200;
        private const int HoldFrames = 240;

        // A pan sweeps this share of the screen's height a frame, south,
        // for PanFrames: a screen and a quarter, past the chunk kept either
        // side of the view, so it builds new ones all the way. South rather
        // than across, because a wide screen at quarter size shows over half
        // the map's width, and a pan across would run off its edge.
        private const float PanScreensPerFrame = 1.25f / 180f;
        private const int PanFrames = 180;

        // The real time one frame of the run stands for, as the simulation
        // clock counts it (see SimulationDriver.Update).
        public const float SecondsPerFrame = 1f / 60f;

        private readonly CameraRig rig;
        private readonly WorldView2D view;
        private readonly World world;
        private readonly Vector2 middle;
        private readonly string folder, stamp;
        private readonly List<Stop> stops = new List<Stop>();
        // Sized for the longest stop up front, so they never grow, and so
        // never allocate, while a capture is recording.
        private readonly List<float> moveMs = new List<float>(PanFrames + FillLimitFrames);
        private readonly List<float> holdMs = new List<float>(HoldFrames);
        private readonly StringBuilder log = new StringBuilder();
        private RenderTexture shot;
        private int index = -1;
        private int stageFrames;
        private float fillSeconds;
        private Stage stage;
        private bool finished;

        private ScriptedRun(CameraRig rig, WorldView2D view, World world, string folder)
        {
            this.rig = rig;
            this.view = view;
            this.world = world;
            middle = new Vector2(world.Grid.Width / 2f, world.Grid.Height / 2f);
            this.folder = folder;
            stamp = "Run_" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

            // The whole map, then each size the art snaps to from the
            // smallest up, still and then panning, then the whole map again.
            stops.Add(Stop.Whole("whole-map"));
            foreach (var (name, pixels) in new[] { ("quarter", 4f), ("half", 8f), ("full", 16f), ("double", 32f) })
            {
                stops.Add(Stop.At(name, pixels, false));
                stops.Add(Stop.At(name + "-pan", pixels, true));
            }
            stops.Add(Stop.Whole("whole-map-again"));
        }

        // A run when the player was launched with `-scripted-run <folder>`,
        // else null.
        public static ScriptedRun FromCommandLine(CameraRig rig, WorldView2D view, World world)
        {
            var args = System.Environment.GetCommandLineArgs();
            var at = System.Array.IndexOf(args, "-scripted-run");
            if (at < 0 || at + 1 >= args.Length) return null;
            var folder = args[at + 1];
            Directory.CreateDirectory(folder);
            var run = new ScriptedRun(rig, view, world, folder);
            run.Begin();
            return run;
        }

        private void Begin()
        {
            // Uncapped, so a frame's time is what it cost rather than the
            // wait for the next vertical blank.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            Profiler.maxUsedMemory = 512 * 1024 * 1024;

            Line("Scripted run " + stamp + ", " + Screen.width + "x" + Screen.height + ", " + SystemInfo.graphicsDeviceName);
            Line("Move: the pan, or the frames after a zoom until filled. Hold: held still once filled.");
            Line("stop               pixels/cell  fill frames  fill s  move median ms  move p95 ms  hold median ms  hold p95 ms  filled  sim day");
            Next();
        }

        // Whether the simulation clock stands still this frame: while a stop
        // fills in, which takes a different number of frames in each build,
        // so every stop is reached on the same sim day however long the ones
        // before it took to fill (#135 review).
        public bool HoldsClock => stage == Stage.Filling;

        // Called by the driver in place of input, before the rig is applied.
        public void Step(float deltaTime)
        {
            if (finished) return;
            var stop = stops[index];
            stageFrames++;
            switch (stage)
            {
                case Stage.Moving:
                    moveMs.Add(deltaTime * 1000f);
                    if (stop.Pan) rig.PanBy(new Vector2(0f, Screen.height * PanScreensPerFrame));
                    if (!stop.Pan || stageFrames >= PanFrames) Enter(Stage.Filling);
                    break;
                case Stage.Filling:
                    // Counted from the zoom, or from the end of the pan.
                    fillSeconds += deltaTime;
                    moveMs.Add(deltaTime * 1000f);
                    if (view.ChunksWaiting == 0 || stageFrames >= FillLimitFrames)
                    {
                        stop.FillFrames = stageFrames;
                        stop.FillSeconds = fillSeconds;
                        stop.Filled = view.ChunksWaiting == 0;
                        Enter(Stage.Holding);
                    }
                    break;
                case Stage.Holding:
                    holdMs.Add(deltaTime * 1000f);
                    if (stageFrames >= HoldFrames)
                    {
                        // The stop's capture ends with its hold. The screenshot
                        // and the save after it are tooling, and recorded they
                        // would be the capture's worst frames (#135 review).
                        StopCapture();
                        Enter(Stage.Capturing);
                    }
                    break;
                case Stage.Capturing:
                    // A frame after the capture stopped, so none of this frame
                    // lands in it. Into a texture of our own: CaptureScreenshot
                    // reads whatever target is active, which a profiler capture
                    // sometimes leaves on its own thumbnail.
                    if (shot == null) shot = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(shot);
                    Enter(Stage.Saving);
                    break;
                case Stage.Saving:
                    // Untimed: the frame that saves the screenshot, then the
                    // one it slowed, before the next stop starts.
                    if (stageFrames == 1)
                    {
                        Save(Path.Combine(folder, StopName(index) + ".png"));
                        Report(stop);
                    }
                    else Next();
                    break;
            }
        }

        // Run_<stamp>_NN_<stop>: the stop's screenshot and capture.
        private string StopName(int stop) => stamp + "_" + (stop + 1).ToString("00", CultureInfo.InvariantCulture) + "_" + stops[stop].Name;

        private void Next()
        {
            // Each stop to its own capture: the Profiler keeps only the last
            // 2000 frames of one, which a whole route outruns.
            StopCapture();
            index++;
            if (index >= stops.Count)
            {
                Finish();
                return;
            }
            Profiler.logFile = Path.Combine(folder, StopName(index));
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;

            var stop = stops[index];
            if (stop.PixelsPerCell <= 0f) rig.WholeMap();
            else if (!stop.Pan)
            {
                rig.CentreOn(middle);
                rig.ZoomTo(stop.PixelsPerCell);
            }
            fillSeconds = 0f;
            moveMs.Clear();
            holdMs.Clear();
            Enter(Stage.Moving);
        }

        private static void StopCapture()
        {
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
        }

        private void Enter(Stage next)
        {
            stage = next;
            stageFrames = 0;
        }

        private void Report(Stop stop)
        {
            Line(string.Format(CultureInfo.InvariantCulture, "{0,-18} {1,11:F0}  {2,11}  {3,6:F2}  {4,14:F2}  {5,11:F2}  {6,14:F2}  {7,11:F2}  {8,-6}  year {9}, day {10} ({11})",
                stop.Name, rig.PixelsPerCell, stop.FillFrames, stop.FillSeconds, Median(moveMs), P95(moveMs), Median(holdMs), P95(holdMs),
                stop.Filled ? "yes" : "no", world.Now.YearNumber, world.Now.DayOfYear + 1, world.Now.Season));
        }

        private static float Median(List<float> ms)
        {
            if (ms.Count == 0) return 0f;
            ms.Sort();
            return ms[ms.Count / 2];
        }

        // Nearest-rank, as the capture analysis reports it.
        private static float P95(List<float> ms)
        {
            if (ms.Count == 0) return 0f;
            ms.Sort();
            return ms[Mathf.Max(0, Mathf.CeilToInt(ms.Count * 0.95f) - 1)];
        }

        // Writes the screen captured into `shot` last frame as a PNG.
        private void Save(string path)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = shot;
            var pixels = new Texture2D(shot.width, shot.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, shot.width, shot.height), 0, 0, false);
            // Where texture rows count down from the top (Direct3D, Metal),
            // the screen lands in the texture upside down.
            if (SystemInfo.graphicsUVStartsAtTop) FlipRows(pixels);
            RenderTexture.active = previous;
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Object.Destroy(pixels);
        }

        private static void FlipRows(Texture2D texture)
        {
            var rows = texture.GetPixels32();
            var flipped = new Color32[rows.Length];
            var w = texture.width;
            for (var y = 0; y < texture.height; y++) System.Array.Copy(rows, y * w, flipped, (texture.height - 1 - y) * w, w);
            texture.SetPixels32(flipped);
        }

        private void Finish()
        {
            finished = true;
            Object.Destroy(shot);
            Profiler.logFile = "";
            Line("Captures: " + stamp + "_NN_<stop>.raw, one per stop");
            File.WriteAllText(Path.Combine(folder, stamp + ".log"), log.ToString());
            Application.Quit();
        }

        private void Line(string text)
        {
            log.AppendLine(text);
            Debug.Log("ScriptedRun: " + text);
        }

        private enum Stage
        {
            Moving,
            Filling,
            Holding,
            Capturing,
            Saving,
        }

        private sealed class Stop
        {
            public string Name;
            // Zero for the whole map.
            public float PixelsPerCell;
            public bool Pan;
            public int FillFrames;
            public float FillSeconds;
            public bool Filled;

            public static Stop Whole(string name) => new Stop { Name = name };

            public static Stop At(string name, float pixelsPerCell, bool pan) => new Stop { Name = name, PixelsPerCell = pixelsPerCell, Pan = pan };
        }
    }
}
#endif
