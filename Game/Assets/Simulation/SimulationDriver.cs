using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using UnityEngine;

namespace KingdomWatch.Game
{
    // Runs Core's M1 world inside the player (#72): the same composition root
    // and defaults as the harness (Harness/WorldRun.cs), so a year's hash here
    // can be compared with `dotnet run --project Harness -- --seed N --years Y`.
    // Only the clock is driven from here; the view reads, and nothing it
    // shows feeds back into the simulation (section 4).
    public sealed class SimulationDriver : MonoBehaviour
    {
        // Harness/WorldRun.cs: WorldRun.Width, Height, WestSize, EastSize.
        private const int MapWidth = 48;
        private const int MapHeight = 48;
        private const int WestSize = 60;
        private const int EastSize = 45;

        private static readonly int[] DaysPerSecondSteps = { 1, 5, 30, 120 };

        public int seed = 1;
        public WorldView2D view;

        private World world;
        private int speedStep = 1;
        private bool paused;
        private double pendingTicks;
        private long hashedYear = -1;
        private ulong yearHash;

        private float UiScale => Mathf.Max(1, Screen.height / 720f);

        private void Start()
        {
            world = World.TwoBands(unchecked((ulong)seed), MapWidth, MapHeight, WestSize, EastSize);
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
            if (ticks <= 0L) return;
            pendingTicks -= ticks;

            // Stop exactly on each year boundary, where the harness hashes, so
            // the year hash is comparable; the rest carries into later frames.
            var nextYear = SimulationTime.FromYears(world.Now.YearNumber + 1L);
            var target = world.Now.Plus(ticks);
            if (target.CompareTo(nextYear) > 0)
            {
                pendingTicks += nextYear.TicksUntil(target);
                target = nextYear;
            }

            world.AdvanceTo(target);
            SnapshotYear();
            if (view != null) view.Refresh(world);
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
            GUILayout.BeginArea(new Rect(12, 12, 360, 230), GUI.skin.box);
            GUILayout.Label("KINGDOM WATCH / CORE ON DEVICE");
            GUILayout.Label("Seed " + seed + " / " + MapWidth + "x" + MapHeight + " / year " + world.Now.YearNumber
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
