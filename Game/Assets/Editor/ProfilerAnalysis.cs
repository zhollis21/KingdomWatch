using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace KingdomWatch.Game.Editor
{
    // Reads the newest capture in Game/ProfilerCaptures and writes what the
    // main thread spent its time on beside it, as <capture>.markers.txt:
    // frame times, and the top markers by total and self time over every
    // frame and over only the frames over budget (#132). A capture's .data
    // file can only be decoded inside Unity, and this is what lets anyone -
    // or an agent - read one without the Profiler window open.
    public static class ProfilerAnalysis
    {
        private const float BudgetMs = 1000f / 60f;
        private const int Top = 40;

        // Profiler markers our own code declares start with this, so they are
        // listed whatever their rank.
        private const string OurPrefix = "KW.";

        [MenuItem("Kingdom Watch/Analyse newest profiler capture")]
        public static void AnalyseNewest()
        {
            var folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ProfilerCaptures");
            var capture = Directory.Exists(folder)
                ? new DirectoryInfo(folder).GetFiles("*.data").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (capture == null)
            {
                Debug.LogError("ProfilerAnalysis: no .data capture in " + folder + " (save one from the Profiler window there).");
                return;
            }

            // Loading replaces whatever the Profiler window holds, and a
            // recording nobody saved would go without a word.
            if (ProfilerDriver.lastFrameIndex >= 0
                && !EditorUtility.DisplayDialog("Analyse " + capture.Name,
                    "This loads the capture into the Profiler, replacing what it holds now. Anything not saved is lost.",
                    "Analyse", "Cancel"))
            {
                return;
            }

            if (!ProfilerDriver.LoadProfile(capture.FullName, false))
            {
                Debug.LogError("ProfilerAnalysis: could not load " + capture.FullName);
                return;
            }

            var output = Path.ChangeExtension(capture.FullName, ".markers.txt");
            File.WriteAllText(output, Analyse(capture.Name));
            Debug.Log("ProfilerAnalysis: wrote " + output);
        }

        // Walks every frame of the capture now loaded.
        private static string Analyse(string name)
        {
            var first = ProfilerDriver.firstFrameIndex;
            var last = ProfilerDriver.lastFrameIndex;
            var all = new Dictionary<string, Marker>();
            var spikes = new Dictionary<string, Marker>();
            var frameTimes = new List<float>();
            var children = new List<int>();
            var cancelled = false;

            try
            {
                for (var frame = first; frame <= last; frame++)
                {
                    if ((frame - first) % 50 == 0
                        && EditorUtility.DisplayCancelableProgressBar("Profiler analysis", "Frame " + frame, (frame - first) / (float)(last - first + 1)))
                    {
                        cancelled = true;
                        break;
                    }

                    // Thread 0 is the main thread.
                    using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                    {
                        if (view == null || !view.valid) continue;
                        var frameMs = view.frameTimeMs;
                        frameTimes.Add(frameMs);
                        Walk(view, view.GetRootItemID(), children, all, frameMs > BudgetMs ? spikes : null);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var text = new StringBuilder();
            text.AppendLine("Capture: " + name);
            text.AppendLine("Frames: " + frameTimes.Count + " (" + first + "-" + last + ")"
                + (cancelled ? ", cancelled part-way: the figures cover only the frames read" : ""));
            frameTimes.Sort();
            if (frameTimes.Count > 0)
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Frame ms: median {0:F2}, p95 {1:F2}, p99 {2:F2}, max {3:F2}, over 16.7: {4}, over 33.3: {5}",
                    frameTimes[frameTimes.Count / 2], frameTimes[(int)(frameTimes.Count * 0.95f)], frameTimes[(int)(frameTimes.Count * 0.99f)],
                    frameTimes[frameTimes.Count - 1], frameTimes.Count(t => t > BudgetMs), frameTimes.Count(t => t > 2f * BudgetMs)));
            }
            text.AppendLine("Total is a marker's time including what it calls; a marker nested in itself counts once per level.");

            var spikeFrames = frameTimes.Count(t => t > BudgetMs);
            Section(text, "Kingdom Watch markers, all frames", Ours(all), frameTimes.Count, m => m.Total);
            Section(text, "Kingdom Watch markers, frames over budget", Ours(spikes), spikeFrames, m => m.Total);
            Section(text, "All frames, by total ms per frame", all, frameTimes.Count, m => m.Total);
            Section(text, "All frames, by self ms per frame", all, frameTimes.Count, m => m.Self);
            Section(text, "Frames over budget, by total ms per frame", spikes, spikeFrames, m => m.Total);
            Section(text, "Frames over budget, by self ms per frame", spikes, spikeFrames, m => m.Self);
            return text.ToString();
        }

        // Adds every marker under `id` to `all`, and to `spikes` when the
        // frame is over budget. `children` is scratch, refilled per level.
        private static void Walk(HierarchyFrameDataView view, int id, List<int> children, Dictionary<string, Marker> all, Dictionary<string, Marker> spikes)
        {
            view.GetItemChildren(id, children);
            var ids = children.ToArray();
            foreach (var child in ids)
            {
                var name = view.GetItemName(child);
                var total = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
                var self = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
                var calls = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnCalls);
                Add(all, name, total, self, calls);
                if (spikes != null) Add(spikes, name, total, self, calls);
                Walk(view, child, children, all, spikes);
            }
        }

        private static Dictionary<string, Marker> Ours(Dictionary<string, Marker> markers) =>
            markers.Where(e => e.Key.StartsWith(OurPrefix)).ToDictionary(e => e.Key, e => e.Value);

        private static void Add(Dictionary<string, Marker> into, string name, float total, float self, float calls)
        {
            if (!into.TryGetValue(name, out var marker)) into[name] = marker = new Marker();
            marker.Total += total;
            marker.Self += self;
            marker.Calls += calls;
            marker.Max = Mathf.Max(marker.Max, total);
        }

        // The top markers by `by`, as milliseconds and calls per frame over
        // `frames`, and the most any one frame spent in each.
        private static void Section(StringBuilder text, string title, Dictionary<string, Marker> markers, int frames, System.Func<Marker, double> by)
        {
            text.AppendLine();
            text.AppendLine("## " + title + " (" + frames + " frames)");
            if (frames == 0) return;
            text.AppendLine("ms/frame  max ms  calls/frame  marker");
            foreach (var entry in markers.OrderByDescending(e => by(e.Value)).Take(Top))
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,8:F3}  {1,6:F1}  {2,11:F1}  {3}",
                    by(entry.Value) / frames, entry.Value.Max, entry.Value.Calls / frames, entry.Key));
            }
        }

        private sealed class Marker
        {
            public double Total, Self, Calls;
            public float Max;
        }
    }
}
