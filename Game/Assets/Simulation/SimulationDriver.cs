using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using UnityEngine;

namespace KingdomWatch.Game
{
    // Runs Core's M1 world inside the player (#72): World.M1, the same world
    // the harness builds (Harness/WorldRun.cs), so a year's hash here
    // can be compared with `dotnet run --project Harness -- --seed N --years Y`.
    // Only the clock is driven from here; the view reads, and nothing it
    // shows feeds back into the simulation (section 4).
    public sealed class SimulationDriver : MonoBehaviour
    {
        private static readonly int[] DaysPerSecondSteps = { 1, 5, 30, 120 };

        public int seed = 1;
        public WorldView2D view;

        private World world;
        private int speedStep = 1;
        private bool paused;
        private readonly YearStepper stepper = new YearStepper();
        private double pendingTicks;
        private long hashedYear = -1;
        private ulong yearHash;

        private const float PanelWidth = 360f;
        private const float PanelHeight = 170f;
        private const float PanelMargin = 12f;

        // Scaled by the shorter side, so the panel fits a phone held either way.
        private float UiScale => Mathf.Max(1, Mathf.Min(Screen.width, Screen.height) / 720f);

        // The panel's footprint in screen pixels (GUI coordinates), margin included.
        private Rect PanelScreenRect => new Rect(0f, 0f, (PanelWidth + 2f * PanelMargin) * UiScale, (PanelHeight + 2f * PanelMargin) * UiScale);

        private void Start()
        {
            world = World.M1(unchecked((ulong)seed));
            SnapshotYear();
            if (view != null) view.Show(world);
        }

        private void Update()
        {
            if (world == null || paused) return;

            // A long frame (a hitch, a debugger pause) is capped rather than
            // caught up, so one slow frame cannot become a burst of sim years.
            var seconds = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            pendingTicks += seconds * DaysPerSecondSteps[speedStep] * (double)SimulationTime.TicksPerDay;

            var ticks = (long)pendingTicks;
            pendingTicks -= ticks;

            // Stops exactly on each year boundary, where the harness hashes, so
            // the year hash is comparable; the rest carries into later frames.
            var target = stepper.Next(world.Now, ticks);
            if (target.Equals(world.Now)) return;

            world.AdvanceTo(target);
            SnapshotYear();
            if (view != null) view.Refresh(world);
        }

        // Framed every frame, paused or not, so rotating the phone re-fits the map.
        private void LateUpdate()
        {
            if (view != null) view.Frame(PanelScreenRect);
        }

        private void SnapshotYear()
        {
            var year = world.Now.YearNumber;
            if (world.Now.Ticks % SimulationTime.TicksPerYear != 0L || year == hashedYear) return;
            hashedYear = year;
            yearHash = world.Hash();
        }

        private void OnGUI()
        {
            if (world == null) return;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * UiScale);
            GUILayout.BeginArea(new Rect(PanelMargin, PanelMargin, PanelWidth, PanelHeight), GUI.skin.box);
            GUILayout.Label("KINGDOM WATCH / CORE ON DEVICE");
            GUILayout.Label("Seed " + seed + " / " + World.M1Width + "x" + World.M1Height + " / year " + world.Now.YearNumber
                + ", day " + (world.Now.DayOfYear + 1) + " (" + world.Now.Season + ")");
            GUILayout.Label("People " + world.People.Count + " / settlements " + world.Founding.All.Count);
            GUILayout.Label("Hash at year " + hashedYear + ": " + yearHash.ToString("x16"));
            GUILayout.Label("Speed: " + DaysPerSecondSteps[speedStep] + " days/s" + (paused ? " (paused)" : ""));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Slower") && speedStep > 0) speedStep--;
            if (GUILayout.Button(paused ? "Run" : "Pause")) paused = !paused;
            if (GUILayout.Button("Faster") && speedStep < DaysPerSecondSteps.Length - 1) speedStep++;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
