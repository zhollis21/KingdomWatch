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
    // Reads a capture in Game/ProfilerCaptures - the newest, from the menu
    // item, or a named one headless (Batch) - and writes what the
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
            var capture = Newest();
            if (capture == null) return;

            // Loading replaces whatever the Profiler window holds, and a
            // recording nobody saved would go without a word.
            if (ProfilerDriver.lastFrameIndex >= 0
                && !EditorUtility.DisplayDialog("Analyse " + capture.Name,
                    "This loads the capture into the Profiler, replacing what it holds now. Anything not saved is lost.",
                    "Analyse", "Cancel"))
            {
                return;
            }

            Write(capture, true);
        }

        // The same without the Editor open, for tools/Profile.ps1 and the
        // /profile skill: Unity -batchmode -projectPath Game -executeMethod
        // KingdomWatch.Game.Editor.ProfilerAnalysis.Batch [-capture <path>]...
        // Each -capture is analysed in turn, in one Unity session, as a
        // scripted run's captures are, one per stop; without any it reads
        // the newest. Exits 0 once every report is written, 1 otherwise; the
        // Editor must be closed, as Unity locks an open project.
        public static void Batch()
        {
            var args = System.Environment.GetCommandLineArgs();
            var captures = new List<FileInfo>();
            for (var i = 0; i < args.Length - 1; i++) if (args[i] == "-capture") captures.Add(new FileInfo(args[i + 1]));
            if (captures.Count == 0) captures.Add(Newest());

            var written = true;
            foreach (var capture in captures)
            {
                if (capture == null) written = false;
                else if (!capture.Exists)
                {
                    Debug.LogError("ProfilerAnalysis: " + capture.FullName + " does not exist.");
                    written = false;
                }
                else written &= Write(capture, false);
            }
            EditorApplication.Exit(written ? 0 : 1);
        }

        // The newest capture in Game/ProfilerCaptures: a .data file saved
        // from the Profiler window, or a .raw one a scripted run recorded.
        private static FileInfo Newest()
        {
            var folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ProfilerCaptures");
            var capture = Directory.Exists(folder)
                ? new DirectoryInfo(folder).GetFiles().Where(f => f.Extension == ".data" || f.Extension == ".raw")
                    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (capture == null) Debug.LogError("ProfilerAnalysis: no .data or .raw capture in " + folder + " (save one from the Profiler window there, or make one with tools/Profile.ps1).");
            return capture;
        }

        // Loads `capture` and writes <capture>.markers.txt beside it.
        private static bool Write(FileInfo capture, bool interactive)
        {
            if (!ProfilerDriver.LoadProfile(capture.FullName, false))
            {
                Debug.LogError("ProfilerAnalysis: could not load " + capture.FullName);
                return false;
            }

            var output = Path.ChangeExtension(capture.FullName, ".markers.txt");
            File.WriteAllText(output, Analyse(capture.Name, interactive));
            Debug.Log("ProfilerAnalysis: wrote " + output);
            return true;
        }

        // Walks every frame of the capture now loaded, with a cancellable
        // progress bar when someone is at the Editor to cancel it.
        private static string Analyse(string name, bool interactive)
        {
            var first = ProfilerDriver.firstFrameIndex;
            var last = ProfilerDriver.lastFrameIndex;
            var all = new Dictionary<string, Marker>();
            var spikes = new Dictionary<string, Marker>();
            var frameTimes = new List<float>();
            var children = new List<int>();
            var thisFrame = new Dictionary<string, Marker>();
            var cancelled = false;

            try
            {
                for (var frame = first; frame <= last; frame++)
                {
                    if (interactive && (frame - first) % 50 == 0
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
                        thisFrame.Clear();
                        Walk(view, view.GetRootItemID(), children, thisFrame);
                        Fold(thisFrame, all);
                        if (frameMs > BudgetMs) Fold(thisFrame, spikes);
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
                var n = frameTimes.Count;
                var median = n % 2 == 1 ? frameTimes[n / 2] : (frameTimes[(n / 2) - 1] + frameTimes[n / 2]) / 2f;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Frame ms: median {0:F2}, p95 {1:F2}, p99 {2:F2}, max {3:F2}, over 16.7: {4}, over 33.3: {5}",
                    median, NearestRank(frameTimes, 0.95), NearestRank(frameTimes, 0.99),
                    frameTimes[n - 1], frameTimes.Count(t => t > BudgetMs), frameTimes.Count(t => t > 2f * BudgetMs)));
            }
            text.AppendLine("p95 and p99 are nearest-rank. Total is a marker's time including what it calls, self its own;");
            text.AppendLine("a marker nested in itself counts once per level. Worst is the most one frame spent in it.");

            var spikeFrames = frameTimes.Count(t => t > BudgetMs);
            // Our own markers in full, whatever their rank; the rest, the top few.
            Section(text, "Kingdom Watch markers, all frames, by total", Ours(all), frameTimes.Count, m => m.Total, int.MaxValue);
            Section(text, "Kingdom Watch markers, frames over budget, by total", Ours(spikes), spikeFrames, m => m.Total, int.MaxValue);
            Section(text, "All frames, by total", all, frameTimes.Count, m => m.Total, Top);
            Section(text, "All frames, by self", all, frameTimes.Count, m => m.Self, Top);
            Section(text, "Frames over budget, by total", spikes, spikeFrames, m => m.Total, Top);
            Section(text, "Frames over budget, by self", spikes, spikeFrames, m => m.Self, Top);
            return text.ToString();
        }

        // Sums every marker under `id` into `frame`, by name. A marker can sit
        // under several parents in one frame - the view merges samples only
        // among siblings - so a frame's cost for it is the sum over all of
        // them (#134 review). `children` is scratch, refilled per level.
        private static void Walk(HierarchyFrameDataView view, int id, List<int> children, Dictionary<string, Marker> frame)
        {
            view.GetItemChildren(id, children);
            var ids = children.ToArray();
            foreach (var child in ids)
            {
                var name = view.GetItemName(child);
                if (!frame.TryGetValue(name, out var marker)) frame[name] = marker = new Marker();
                marker.Total += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
                marker.Self += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
                marker.Calls += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnCalls);
                Walk(view, child, children, frame);
            }
        }

        // Adds one frame's sums into a running total, and keeps the worst
        // frame for each marker.
        private static void Fold(Dictionary<string, Marker> frame, Dictionary<string, Marker> into)
        {
            foreach (var entry in frame)
            {
                if (!into.TryGetValue(entry.Key, out var marker)) into[entry.Key] = marker = new Marker();
                marker.Total += entry.Value.Total;
                marker.Self += entry.Value.Self;
                marker.Calls += entry.Value.Calls;
                marker.WorstTotal = System.Math.Max(marker.WorstTotal, entry.Value.Total);
                marker.WorstSelf = System.Math.Max(marker.WorstSelf, entry.Value.Self);
            }
        }

        // The nearest-rank percentile of a sorted list: the value at rank
        // ceil(p * n), counting from one. The tolerance keeps float error
        // from lifting a whole rank - 0.99 * 2000 is 1980.0000000000002.
        private static float NearestRank(List<float> sorted, double p)
        {
            var rank = (int)System.Math.Ceiling((p * sorted.Count) - 1e-9);
            return sorted[System.Math.Max(0, rank - 1)];
        }

        private static Dictionary<string, Marker> Ours(Dictionary<string, Marker> markers) =>
            markers.Where(e => e.Key.StartsWith(OurPrefix)).ToDictionary(e => e.Key, e => e.Value);

        // Up to `top` markers ordered by `by`, each with its total and self
        // milliseconds per frame over `frames`, the most one frame spent in
        // it either way, and its calls per frame. Every table has the same
        // columns, so none shows a figure for the other measure (#134 review).
        private static void Section(StringBuilder text, string title, Dictionary<string, Marker> markers, int frames, System.Func<Marker, double> by, int top)
        {
            text.AppendLine();
            text.AppendLine("## " + title + " (" + frames + " frames)");
            if (frames == 0) return;
            text.AppendLine("total/frame  self/frame  worst total  worst self  calls/frame  marker");
            foreach (var entry in markers.OrderByDescending(e => by(e.Value)).Take(top))
            {
                var m = entry.Value;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,11:F3}  {1,10:F3}  {2,11:F1}  {3,10:F1}  {4,11:F1}  {5}",
                    m.Total / frames, m.Self / frames, m.WorstTotal, m.WorstSelf, m.Calls / frames, entry.Key));
            }
        }

        private sealed class Marker
        {
            public double Total, Self, Calls, WorstTotal, WorstSelf;
        }
    }
}
