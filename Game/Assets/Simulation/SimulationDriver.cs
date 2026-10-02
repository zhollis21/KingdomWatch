using System.Collections.Generic;
using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using EntityId = KingdomWatch.Core.Data.EntityId;
using Object = UnityEngine.Object;

namespace KingdomWatch.Game
{
    // Runs Core's M1 world inside the player (#72): World.M1, the same world
    // the harness builds (Harness/WorldRun.cs), so a year's hash here
    // can be compared with `dotnet run --project Harness -- --seed N --years Y`.
    // Only the clock is driven from here; the camera, the layers, the panel and
    // the selection read, and nothing they show feeds back into the simulation
    // (section 4).
    public sealed class SimulationDriver : MonoBehaviour
    {
        // Section 4's ladder. 1x is eight real minutes a game-day - 180 ticks
        // a real second, the pace Core's walking is tuned to read at (#123) -
        // and 10,000x is about 21 days a second.
        private static readonly int[] SpeedSteps = { 1, 5, 20, 100, 1000, 10000 };
        private const double TicksPerSecondAtOneX = SimulationTime.TicksPerDay / (8.0 * 60.0);

        // Named in a capture, so a slow frame says which part was slow (#132).
        private static readonly ProfilerMarker SimulateMarker = new ProfilerMarker("KW.Simulate");
        private static readonly ProfilerMarker YearHashMarker = new ProfilerMarker("KW.YearHash");
        private static readonly ProfilerMarker InputMarker = new ProfilerMarker("KW.Input");
        private static readonly ProfilerMarker HudMarker = new ProfilerMarker("KW.Hud");

        public int seed = 1;
        public WorldView2D view;

        private World world;
        private CameraRig rig;
        private ViewInput input;
        private Hud hud;
        private readonly HudState hudState = new HudState();
        private readonly List<Object> hudOwned = new List<Object>();
        private readonly List<Vector2> settlementPositions = new List<Vector2>();
        private readonly List<Vector2> bandPositions = new List<Vector2>();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // Flies the camera in place of input when launched with -scripted-run.
        private ScriptedRun scriptedRun;
        private bool demoSelect;
#endif
        // 1000x, about two days a second: fast enough to see a year go by.
        private int speedStep = 4;
        private bool paused;
        private readonly YearStepper stepper = new YearStepper();
        private readonly List<ICommunity> bands = new List<ICommunity>();
        private double pendingTicks;
        private long hashedYear = -1;
        private ulong yearHash;

        // Scaled by the shorter side, so a tap's reach feels the same on a
        // phone held either way. The panel's own scale is the Hud's.
        private float UiScale => Mathf.Max(1, Mathf.Min(Screen.width, Screen.height) / 720f);

        private void OnEnable() => EnhancedTouchSupport.Enable();

        private void OnDisable() => EnhancedTouchSupport.Disable();

        private void OnDestroy()
        {
            if (hud != null) hud.Dispose();
            foreach (var item in hudOwned) if (item != null) Destroy(item);
        }

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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // -hud-notch insets the panel as a phone's cutout and rounded corners
            // would, to look at the layout on a screen that has neither.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hud-notch") >= 0)
                Hud.SafeAreaOverride = new Rect(Screen.width * 0.04f, Screen.height * 0.03f, Screen.width * 0.93f, Screen.height * 0.91f);
#endif
            hud = new Hud(HudArt.Load(hudOwned) ?? HudArt.Flat(hudOwned), Commands(), view, new Vector2(world.Grid.Width, world.Grid.Height));
            // Framed now, so input in the first frame never meets an unframed
            // rig (zoom 0, where a pan divides by zero).
            rig.Apply(hud.Reserved, hud.TopInset);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            scriptedRun = ScriptedRun.FromCommandLine(rig, view, world);
            // -hud-demo selects someone and opens the debug card, so a run's
            // screenshots show those parts of the panel too.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hud-demo") >= 0)
            {
                hud.ToggleDebug();
                demoSelect = true;
            }
#endif
        }

        private HudCommands Commands() => new HudCommands
        {
            Slower = () => { if (speedStep > 0) speedStep--; },
            Faster = () => { if (speedStep < SpeedSteps.Length - 1) speedStep++; },
            TogglePause = () => paused = !paused,
            WholeMap = () => input.WholeMap(),
            ToggleFollow = () => input.ToggleFollow(UiScale),
            Deselect = () => input.Deselect(),
            MoveTo = point => input.MoveTo(point),
        };

        private void Update()
        {
            if (world == null || paused) return;

            // A long frame (a hitch, a debugger pause) is capped rather than
            // caught up, so one slow frame cannot become a burst of sim years.
            var seconds = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // A scripted run counts frames, not seconds, and stops the clock
            // while a stop fills in, so two runs reach each stop on the same
            // day however fast each draws.
            if (scriptedRun != null) seconds = scriptedRun.HoldsClock ? 0f : ScriptedRun.SecondsPerFrame;
#endif
            pendingTicks += seconds * SpeedSteps[speedStep] * TicksPerSecondAtOneX;

            var ticks = (long)pendingTicks;
            pendingTicks -= ticks;

            // Stops exactly on each year boundary, where the harness hashes, so
            // the year hash is comparable; the rest carries into later frames.
            var target = stepper.Next(world.Now, ticks);
            if (target.Equals(world.Now)) return;

            using (SimulateMarker.Auto()) world.AdvanceTo(target);
            SnapshotYear();
        }

        // Every frame, paused or not: input, then the camera, then the redraw,
        // so a pan, a zoom across a layer edge or a rotation shows at once.
        private void LateUpdate()
        {
            if (view == null || rig == null) return;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (scriptedRun != null) scriptedRun.Step(Time.unscaledDeltaTime);
            else
#endif
            using (InputMarker.Auto())
            {
                ReadShortcuts();
                input.Process(hud, UiScale, Time.unscaledDeltaTime);
            }
            // Last frame's layer: snapping moves the zoom shown, never the zoom
            // the layers switch on, so this cannot feed back into the layer.
            rig.Snap = view.Band != ZoomBand.Far;
            rig.Apply(hud.Reserved, hud.TopInset);
            view.Refresh(world, rig.RequestedPixelsPerCell, UiScale, paused);
            DropVanishedSelection();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (demoSelect && Time.frameCount > 5)
            {
                demoSelect = false;
                world.Nomads.CopyTrackedTo(bands);
                if (bands.Count > 0 && bands[0].Members.Count > 0) view.Selected = world.People.GetId(bands[0].Members[0]);
            }
#endif
            using (HudMarker.Auto()) RefreshHud();
        }

        // The keys the panel's buttons name in their tooltips. Camera keys
        // are ViewInput's.
        private void ReadShortcuts()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.spaceKey.wasPressedThisFrame) paused = !paused;
            if (keys.commaKey.wasPressedThisFrame && speedStep > 0) speedStep--;
            if (keys.periodKey.wasPressedThisFrame && speedStep < SpeedSteps.Length - 1) speedStep++;
            if (keys.f3Key.wasPressedThisFrame) hud.ToggleDebug();
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
            using (YearHashMarker.Auto()) yearHash = world.Hash();
        }

        private void RefreshHud()
        {
            var s = hudState;
            var now = world.Now;
            s.Year = now.YearNumber;
            s.Season = now.Season;
            s.DayOfSeason = (int)now.DayOfSeason;
            s.Speed = SpeedSteps[speedStep];
            s.Paused = paused;
            s.Following = input.Following;
            s.Seed = seed;
            s.MapWidth = world.Grid.Width;
            s.MapHeight = world.Grid.Height;
            s.People = world.People.Count;
            s.Settlements = world.Founding.All.Count;
            s.HashYear = hashedYear;
            s.Hash = yearHash;

            var selected = view.Selected;
            s.SelectedKind = selected.Kind;
            s.SelectedId = selected.Value;
            s.SelectedPeople = 0;
            s.HasCommunity = false;
            ICommunity community = null;
            if (selected.Kind == EntityKind.Person && world.People.TryGetHandle(selected, out var person))
            {
                var people = world.People;
                s.Stage = people.GetAgeStage(person);
                s.Years = (int)people.GetAgeYears(person, now);
                s.Health = people.GetHealth(person);
                s.Job = people.GetJob(person);
                s.Working = world.Jobs.HasTask(person);
                community = CommunityOf(person);
            }
            else if (selected.Kind == EntityKind.Settlement || selected.Kind == EntityKind.MobileGroup)
            {
                s.SelectedPeople = Mathf.Max(0, view.SizeOf(selected));
                community = FindCommunity(selected);
            }

            if (community != null)
            {
                var supplies = community.SharedSupplies;
                s.HasCommunity = true;
                s.CommunityKind = community.Id.Kind;
                s.CommunityId = community.Id.Value;
                s.Food = supplies.Stock(ResourceKind.Food);
                s.Wood = supplies.Stock(ResourceKind.Wood);
                s.Stone = supplies.Stock(ResourceKind.Stone);
                s.FoodFree = supplies.Available(ResourceKind.Food);
                s.WoodFree = supplies.Available(ResourceKind.Wood);
                s.StoneFree = supplies.Available(ResourceKind.Stone);
                s.DaysOfFood = world.Hunger.DaysOfFood(community);
                s.CommunityPeople = community.Members.Count;
            }

            MarkPositions();
            hud.Refresh(s, rig.VisibleRect, settlementPositions, bandPositions, world.Grid.Width, world.Grid.Height);
        }

        // Where each community is drawn, for the minimap's dots.
        private void MarkPositions()
        {
            settlementPositions.Clear();
            bandPositions.Clear();
            var settlements = world.Founding.All;
            for (var i = 0; i < settlements.Count; i++)
                if (view.TryGetDrawnPosition(settlements[i].Id, out var at)) settlementPositions.Add(at);
            world.Nomads.CopyTrackedTo(bands);
            for (var i = 0; i < bands.Count; i++)
                if (view.TryGetDrawnPosition(bands[i].Id, out var at)) bandPositions.Add(at);
        }

        private ICommunity FindCommunity(EntityId id)
        {
            var settlements = world.Founding.All;
            for (var i = 0; i < settlements.Count; i++) if (settlements[i].Id == id) return settlements[i];
            world.Nomads.CopyTrackedTo(bands);
            for (var i = 0; i < bands.Count; i++) if (bands[i].Id == id) return bands[i];
            return null;
        }

        // Nothing in Core maps a person to their community, and there are only
        // a few communities, so look through each one's members.
        private ICommunity CommunityOf(PersonHandle person)
        {
            var settlements = world.Founding.All;
            for (var i = 0; i < settlements.Count; i++) if (Contains(settlements[i], person)) return settlements[i];
            world.Nomads.CopyTrackedTo(bands);
            for (var i = 0; i < bands.Count; i++) if (Contains(bands[i], person)) return bands[i];
            return null;
        }

        private static bool Contains(ICommunity community, PersonHandle person)
        {
            var members = community.Members;
            for (var i = 0; i < members.Count; i++) if (members[i].Equals(person)) return true;
            return false;
        }
    }
}
