using System.Collections.Generic;
using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using EntityId = KingdomWatch.Core.Data.EntityId;

namespace KingdomWatch.Game
{
    // Runs Core's M1 world inside the player (#72): World.M1, the same world
    // the harness builds (Harness/WorldRun.cs), so a year's hash here
    // can be compared with `dotnet run --project Harness -- --seed N --years Y`.
    // Only the clock is driven from here; the camera, the layers and the
    // selection read, and nothing they show feeds back into the simulation
    // (section 4).
    public sealed class SimulationDriver : MonoBehaviour
    {
        private static readonly int[] DaysPerSecondSteps = { 1, 5, 30, 120 };

        public int seed = 1;
        public WorldView2D view;

        private World world;
        private CameraRig rig;
        private ViewInput input;
        private int speedStep = 1;
        private bool paused;
        private readonly YearStepper stepper = new YearStepper();
        private readonly List<ICommunity> bands = new List<ICommunity>();
        private double pendingTicks;
        private long hashedYear = -1;
        private ulong yearHash;

        private const float PanelWidth = 360f;
        private const float PanelHeight = 360f;
        private const float PanelMargin = 12f;

        // Scaled by the shorter side, so the panel fits a phone held either way.
        private float UiScale => Mathf.Max(1, Mathf.Min(Screen.width, Screen.height) / 720f);

        // The panel's footprint in screen pixels (GUI coordinates), margin included.
        private Rect PanelScreenRect => new Rect(0f, 0f, (PanelWidth + 2f * PanelMargin) * UiScale, (PanelHeight + 2f * PanelMargin) * UiScale);

        private void OnEnable() => EnhancedTouchSupport.Enable();

        private void OnDisable() => EnhancedTouchSupport.Disable();

        private void Start()
        {
            world = World.M1(unchecked((ulong)seed));
            SnapshotYear();
            // A scene missing a reference runs the simulation with no view,
            // saying so once rather than throwing every frame.
            if (view == null || view.sceneCamera == null || view.spriteShader == null)
            {
                Debug.LogError("SimulationDriver: no view drawn. Set SimulationDriver.view, and WorldView2D's sceneCamera and spriteShader.", this);
                return;
            }
            view.Show(world);
            rig = new CameraRig(view.sceneCamera, world.Grid.Width, world.Grid.Height);
            input = new ViewInput(rig, view);
            // Framed now, so input in the first frame never meets an unframed
            // rig (zoom 0, where a pan divides by zero).
            rig.Apply(PanelScreenRect);
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
        }

        // Every frame, paused or not: input, then the camera, then the redraw,
        // so a pan, a zoom across a layer edge or a rotation shows at once.
        private void LateUpdate()
        {
            if (view == null || rig == null) return;
            var reserved = PanelScreenRect;
            input.Process(reserved, UiScale, Time.unscaledDeltaTime);
            rig.Apply(reserved);
            view.Refresh(world, rig.PixelsPerCell, UiScale);
            DropVanishedSelection();
        }

        // Someone who died, or a band that settled, is no longer there to select.
        private void DropVanishedSelection()
        {
            var selected = view.Selected;
            if (selected.IsNone) return;
            var gone = selected.Kind == EntityKind.Person
                ? !world.People.TryGetHandle(selected, out _)
                : view.SizeOf(selected) < 0;
            if (gone) input.Deselect();
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

            if (view != null && input != null) ViewPanel();
            GUILayout.EndArea();
        }

        private void ViewPanel()
        {
            GUILayout.Label("Zoom: " + view.Band + (input.Following ? " (following)" : ""));
            if (GUILayout.Button("Whole map")) input.WholeMap();

            var selected = view.Selected;
            if (selected.IsNone)
            {
                GUILayout.Label("Tap a person, or a settlement when zoomed out.");
                return;
            }

            GUILayout.Label("Selected: " + Describe(selected));
            if (selected.Kind == EntityKind.Person && world.People.TryGetHandle(selected, out var person))
            {
                var people = world.People;
                var job = people.GetJob(person);
                GUILayout.Label(people.GetAgeStage(person) + ", " + people.GetAgeYears(person, world.Now) + " years / health " + people.GetHealth(person));
                GUILayout.Label((job == JobKind.None ? "No job" : job.ToString()) + (world.Jobs.HasTask(person) ? ", working" : ", idle")
                    + " / " + CommunityOf(person));
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(input.Following ? "Stop following" : "Follow")) input.ToggleFollow(UiScale);
            if (GUILayout.Button("Clear")) input.Deselect();
            GUILayout.EndHorizontal();
        }

        private string Describe(EntityId id)
        {
            switch (id.Kind)
            {
                case EntityKind.Settlement: return "settlement " + id.Value + ", " + view.SizeOf(id) + " people";
                case EntityKind.MobileGroup: return "band " + id.Value + ", " + view.SizeOf(id) + " people";
                default: return id.ToString();
            }
        }

        // Nothing in Core maps a person to their community, and there are only
        // a few communities, so look through each one's members.
        private string CommunityOf(PersonHandle person)
        {
            var settlements = world.Founding.All;
            for (var i = 0; i < settlements.Count; i++) if (Contains(settlements[i], person)) return "settlement " + settlements[i].Id.Value;
            world.Nomads.CopyTrackedTo(bands);
            for (var i = 0; i < bands.Count; i++) if (Contains(bands[i], person)) return "band " + bands[i].Id.Value;
            return "no community";
        }

        private static bool Contains(ICommunity community, PersonHandle person)
        {
            var members = community.Members;
            for (var i = 0; i < members.Count; i++) if (members[i].Equals(person)) return true;
            return false;
        }
    }
}
