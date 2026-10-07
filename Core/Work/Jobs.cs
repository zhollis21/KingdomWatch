using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Land;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Core.Work
{
    /// <summary>
    /// People work. Owns <see cref="ScheduledEventKind.WorkDayDue"/> and
    /// <see cref="ScheduledEventKind.TaskCompleted"/>: at dawn each tracked
    /// band's free workers take the job the band most needs and set out on
    /// one task of it; each task that completes delivers its output to the
    /// band's ledger and, if there is daylight left, picks the next.
    /// </summary>
    /// <remarks>
    /// **Need is decided by the band, the job is chosen by the person, and
    /// the choice is made when they are free.** Section 12's work manager
    /// decides need and citizens choose among posted jobs; here need is
    /// three thresholds read live - food below <see cref="FoodTargetDays"/>,
    /// wood and stone below their caps, the first two raised ahead of winter
    /// - and "choosing" is taking, of the jobs that are needed, reachable,
    /// and fit before dusk, the one whose store is furthest below its target
    /// as a share of it, <see cref="Priority"/> breaking a tie. Taking the
    /// first needed job instead sent every hand to food for as long as food
    /// was short, and a settlement short of food all year froze with an empty
    /// woodpile (#17). It runs at dawn and at every completion, so a band whose food
    /// is covered by noon sends its afternoon hands to wood without waiting
    /// for tomorrow. What is already on its way home counts toward the
    /// threshold, so a round of pickers cannot all see the same shortfall and
    /// all fill it. That tally, and the band's head count, are built by the
    /// dawn pass and carried through the day rather than rescanned per pick -
    /// a scan there made a day's work quadratic in population (#83). Skill
    /// weighting is #22's; a settlement-wide plan is #23's and replaces
    /// "decide need" without touching "choose".
    ///
    /// **A band provisions for winter** (#53). Winter foraging does not feed
    /// the forager (<see cref="PrimitiveTier.ForageWinter"/>) and every
    /// hearth burns wood each winter night (<see cref="Warmth"/>), so from
    /// the first day of summer both thresholds rise by what the coming
    /// winter will draw - a winter of meals for everyone living, and a
    /// winter of fires for every hearth - and in winter they rise by what
    /// is left of it (<see cref="WinterDaysAhead"/>). With no look-ahead a
    /// band that keeps ten days of food starves every winter, which is
    /// the winter-as-regulator section 6 warns against; with it, a famine
    /// or a cold house is something that went wrong - too few hands, a band
    /// grown since summer. Section 9's
    /// "farmers do something else in January" falls out of the same rules:
    /// once the stores are full, winter hands go to whatever is still needed.
    /// A task's season is the one it starts in; no task outlives dusk and no
    /// season turns before midnight, so it is also the one it ends in.
    ///
    /// **One task at a time, inside a dawn-to-dusk window.** A task is
    /// booked when it starts, never a day ahead, and is started only if it
    /// ends by <see cref="Dusk"/>. The window is a stand-in for #21's daily
    /// schedule - eat, work, socialise, sleep - and exists so that a forager
    /// makes two or three trips a day rather than six around the clock,
    /// which is what keeps hunger a constraint. Someone idle at dawn stays
    /// idle until the next dawn: nothing wakes them mid-day, so a shortfall
    /// that opens after the pass - a meal lands whenever the holder's stream
    /// was started, not necessarily before dawn - waits for the next one.
    ///
    /// **A task is section 4's scheduled task.** <see cref="WorkTask"/>
    /// holds the start, the three legs, the origin and the destination; the
    /// coarse route is kept here beside it, copied from the band's site
    /// route at the start, and <see cref="PositionAt"/> is the
    /// reconstruction - where the worker is at any instant, without a step
    /// having been simulated. The route is stored rather than recomputed so
    /// that a grid change mid-task (a bridge, one day) cannot put someone on
    /// the far side of a river they never crossed.
    ///
    /// **Sites are per band, per job, found at dawn, and known.** The
    /// cheapest cell the job works on that the community has seen and that a
    /// route reaches within <see cref="MaxSiteRadius"/> - one bounded search
    /// per job (<see cref="Pathfinder.TryFindNearest"/>),
    /// however many candidates there are and whether or not any of them
    /// connect - so every worker on a job walks the same route to the same
    /// cell. Three searches per band per day rather than one per task, and
    /// one more whenever a site is spent (#26): a trip claims its harvest
    /// from <see cref="LandCover"/> as it sets out, and a pick that finds its
    /// site bare searches again from the same place, so a band strips one
    /// bush and moves to the next. Stone stays infinite. Sites remember
    /// where they were found from, and a pick made from anywhere else finds
    /// them again first - so a band that moves (#54) need not tell anyone;
    /// <see cref="RefreshSites"/> is there for a caller that wants the new
    /// sites before the next pick.
    ///
    /// **A work site is one of section 12's place-picking decisions.** The
    /// site must be a cell the community knows; the route to it need not be,
    /// so a band can work something it has only glimpsed the near edge of.
    /// The trip then reveals <see cref="RevealRadius"/> around its whole
    /// route. For a community founded by a band that adds nothing today: a
    /// band sees <see cref="Nomadic.NomadicBands.RevealRadius"/> around its
    /// camp, past every trip <see cref="MaxSiteRadius"/> allows, so a
    /// settlement's map stays what its band saw (#123). #85's deliberate
    /// scouting is section 12's answer to a map that must keep growing.
    ///
    /// **A task starts and ends where the band stood when it started.** The
    /// worker walks out from the band's position and back to it, and that
    /// origin is the task's, not the band's live one - so a band that moved
    /// while workers were out would leave them at the old camp when they
    /// return. <see cref="Nomadic.NomadicBands"/> never does: its council
    /// sits at first light, before this pass, and a band with a
    /// <see cref="ICommunity.Destination"/> at dawn starts nobody - everyone
    /// walks with the band, and tomorrow's pass finds sites from the new
    /// camp. Nothing here reads or writes a person's own position, because
    /// nothing moves one step by step yet (#25).
    ///
    /// **Settlements also build and farm** (#100). A settlement's approved
    /// building and its fields are two more needs, ranked by the same
    /// shortfall rule: <see cref="Construction.Buildings"/> says how many
    /// worker-ticks nobody has claimed against a target, and a Builder or
    /// Farmer trip claims a share of them rather than a harvest. After the
    /// dawn pass, the hands nobody needed are what lets the settlement
    /// approve its next building. Grain in store counts toward food, milled.
    ///
    /// **Bands and settlements alike.** What is tracked is an
    /// <see cref="ICommunity"/>: a settlement works exactly as a band does,
    /// from a position that happens never to change. The settlement-wide
    /// plan that will replace "decide need" is #23's.
    ///
    /// **Who works:** the living adults and elders of a band, tierless and at
    /// full output. Section 6's reduced work for elders is a tier effect and
    /// arrives with #22's skills; adolescents' work assistance is #22's
    /// apprenticeship mapping.
    ///
    /// **Death vacates.** <see cref="Lifecycle.Deaths"/> calls
    /// <see cref="Vacate"/> as a step of the cascade - the pending
    /// completion is cancelled, inputs in process go back, the job is
    /// cleared - so a completion arriving for someone with no task is a
    /// wiring bug and throws, the stance <see cref="Hunger"/> takes on an
    /// untracked holder. Reposting is implicit: the next picker sees the
    /// shortfall.
    ///
    /// **The state names its events.** A task records the completion it
    /// booked and a band records the dawn it booked, and only those run:
    /// any other <see cref="ScheduledEventKind.TaskCompleted"/> or
    /// <see cref="ScheduledEventKind.WorkDayDue"/> - a duplicate, or one
    /// rebuilt from a save that disagrees - throws rather than delivering
    /// early or starting a second daily stream. Nothing but this class
    /// books either kind, so there is nothing else it could mean.
    ///
    /// No domain events: a task is a few hours of one person's day, and
    /// thousands a day would drown the journal. The chronicle reads the
    /// ledger's flows. No randomness either - every choice is a function
    /// of band state and member order, so two runs agree without a key.
    ///
    /// The numbers are placeholders: plausible, not tuned.
    ///
    /// Allocation-free once every person's slot has been used and every
    /// site has been found: slots and route buffers grow on first use, the
    /// way <see cref="PersonStore"/> grows, and a site's route list, made
    /// at <see cref="Track"/>, grows to the longest route it has held.
    /// </remarks>
    public sealed class Jobs : IScheduledEventHandler
    {
        /// <summary>Tick of day the work day starts: the dawn pass runs here.</summary>
        public const long Dawn = 6L * SimulationTime.TicksPerHour;

        /// <summary>Tick of day the work day ends: no task is started that would end after it.</summary>
        public const long Dusk = 18L * SimulationTime.TicksPerHour;

        /// <summary>
        /// Ticks of walking per unit of route cost. At two, a plains cell
        /// (cost 10, straight step 10) takes 200 ticks and a forest cell twice
        /// that. Tuned for the screen rather than the tape measure (#123): a
        /// cell is about 1.5 m, and at section 4's 1x - 180 ticks a real
        /// second - 200 ticks a cell reads as a walk of about 1.35 m/s. A
        /// real walking pace would be about one tick a cell, and would look a
        /// hundred times too fast at every speed, since the clock itself runs
        /// 180 times faster than life. Distances elsewhere are sized in
        /// walking time against this, not in metres.
        /// </summary>
        public const long TicksPerCostUnit = 2L;

        /// <summary>Foragers are needed while the band's food, counting what is on its way home, covers fewer days than this.</summary>
        public const int FoodTargetDays = 10;

        /// <summary>Woodcutters are needed while the band's wood, counting what is on its way home, is below this.</summary>
        public const int WoodCap = 200;

        /// <summary>Stone gatherers are needed while the band's stone, counting what is on its way home, is below this.</summary>
        public const int StoneCap = 100;

        /// <summary>How far out from the band a site is looked for, in cells.</summary>
        public const int MaxSiteRadius = 16;

        /// <summary>
        /// How far out a settlement's woodcutters look, in cells, when no tree
        /// stands within <see cref="MaxSiteRadius"/> (#149).
        /// </summary>
        public const int EmergencyWoodRadius = 2 * MaxSiteRadius;

        /// <summary>
        /// How far a worker on the road sees, in cells. Section 12 names
        /// "foragers and hunters working out from a settlement" among the
        /// things that reveal, and this is how far they reveal.
        /// </summary>
        /// <remarks>
        /// Its own constant rather than <see cref="Nomadic.NomadicBands.RevealRadius"/>:
        /// a band on the march and a forager on a day trip are different
        /// sights, and deliberate scouting (#85) is likely to want to tell
        /// them apart. The two matched until #123 widened the band's to a quarter
        /// of a day's walk; this one was left alone, and while every trip starts
        /// inside a band's sight it reveals nothing new, so its size does not
        /// matter until something works from where no band stood.
        /// </remarks>
        public const int RevealRadius = 6;

        /// <summary>
        /// Both kinds run in the physical phase: a completion is a resource
        /// change, and the dawn pass starts the tasks that will be.
        /// </summary>
        public const SimulationPhase Phase = SimulationPhase.Physical;

        /// <summary>Everyone walks; boats are M8's.</summary>
        public const Transport Mover = Transport.Foot;

        /// <summary>The order jobs are offered in: food before materials, wood before stone.</summary>
        public static readonly IReadOnlyList<JobKind> Priority =
            new ReadOnlyCollection<JobKind>(new[] { JobKind.Forager, JobKind.Woodcutter, JobKind.StoneGatherer });

        private static readonly int JobKindCount = EnumGuard.BuildMask(typeof(JobKind)).Length;

        private const int InitialSlots = 64;

        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly Pathfinder _pathfinder;
        private readonly KnownMaps _knownMaps;
        private readonly TerrainGrid _grid;
        private readonly LandCover _land;
        private readonly Buildings? _buildings;

        // What a gathering site must be: ripe, and not ground approved for a
        // building and waiting to be cleared (the #148 review).
        private readonly WorkableFilter _workable;

        // A list, scanned by id, for the same reason Hunger's is.
        private readonly List<Tracked> _tracked = new List<Tracked>();

        // The dawn count's distinct households, reused (Warmth.CountHearths).
        private readonly List<EntityId> _hearthScratch = new List<EntityId>();

        // One slot per person slot in the store, addressed by handle index
        // and checked against the handle's generation, so a recycled slot
        // never reads its last occupant's task as the new one's.
        private Slot?[] _slots = Array.Empty<Slot?>();

        // Scratch for a site search: the route to the candidate being tried.
        private readonly List<WorldPosition> _scratchRoute = new List<WorldPosition>();

        /// <param name="buildings">
        /// What settlements build and farm (#100); null for a world where
        /// nobody does, as in fixtures about gathering alone.
        /// </param>
        public Jobs(
            SimulationClock clock, PersonStore people, Pathfinder pathfinder, KnownMaps knownMaps, LandCover land, Buildings? buildings = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _knownMaps = knownMaps ?? throw new ArgumentNullException(nameof(knownMaps));
            _land = land ?? throw new ArgumentNullException(nameof(land));
            _buildings = buildings;
            _workable = new WorkableFilter(land, buildings);
            _grid = pathfinder.Grid;
        }

        /// <summary>How many bands have work days scheduled.</summary>
        public int TrackedCount => _tracked.Count;

        /// <summary>
        /// Fills <paramref name="into"/> with every community with work days scheduled, in the order they were
        /// tracked. Clears the list first.
        /// </summary>
        /// <remarks>
        /// For the validator (issue 13), which cannot otherwise tell that a
        /// tracked community still exists, or that the people it holds are
        /// alive. The list is the caller's so a check taken once per
        /// simulated day reuses one buffer.
        ///
        /// Read-only in the list sense only: the entries are the live
        /// communities, and <see cref="ICommunity"/> can add and remove
        /// members. Same as <see cref="Lifecycle.Households.All"/>. It is
        /// handed out for reading, and writing through it is a caller bug
        /// rather than something this can prevent.
        /// </remarks>
        public void CopyTrackedTo(List<ICommunity> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            for (var i = 0; i < _tracked.Count; i++)
            {
                into.Add(_tracked[i].Group);
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every work day this system has
        /// booked and not yet seen come due (#80). Clears the list first.
        /// </summary>
        /// <remarks>
        /// The validator confirms each one is still in the queue, and the
        /// world hash folds them in: a world that agrees on its people and
        /// disagrees on what it has booked for them has already diverged, it
        /// has just not shown yet.
        /// </remarks>
        public void CopyBookingsTo(List<PendingBooking> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            for (var i = 0; i < _tracked.Count; i++)
            {
                var booked = _tracked[i].PendingDawn;

                if (!booked.IsNone)
                {
                    into.Add(new PendingBooking(
                        _tracked[i].Group.Id, ScheduledEventKind.WorkDayDue, booked));
                }
            }
        }

        /// <summary>Whether this community is tracked here.</summary>
        public bool IsTracked(ICommunity community) =>
            IndexOf((community ?? throw new ArgumentNullException(nameof(community))).Id) >= 0;

        /// <summary>Whether people at this stage take jobs: adults and elders.</summary>
        public static bool Works(AgeStage stage) => stage == AgeStage.Adult || stage == AgeStage.Elder;

        /// <summary>
        /// Starts a band working: its first dawn pass is booked for the next
        /// <see cref="Dawn"/> after now, and each pass books the next.
        /// Refuses a band already tracked - two passes a day would assign
        /// twice.
        /// </summary>
        public void Track(ICommunity group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            if (IndexOf(group.Id) >= 0)
            {
                throw new InvalidOperationException(
                    group.Id + " is already tracked; a second work day would assign twice.");
            }

            RequireStandable(group.Position);

            // Booked before recorded, as Hunger does: the booking is the one
            // thing here that can be refused.
            var dawn = _clock.Schedule(
                _clock.Now.Plus(TicksUntilDawn(_clock.Now)), Phase, ScheduledEventKind.WorkDayDue, group.Id, EntityId.None);
            _tracked.Add(new Tracked(group, JobKindCount) { PendingDawn = dawn });
        }

        /// <summary>
        /// Stops a band working: its pending dawn is cancelled and the
        /// stream ends. Refuses a band with anyone out on a task, because a
        /// completion would then arrive for a holder nobody tracks - untrack
        /// at first light or after dusk, when nobody is. A band that has
        /// settled (#54) hands its people to the settlement, which is
        /// tracked in its place.
        /// </summary>
        public void Untrack(ICommunity group)
        {
            var tracked = TrackedFor(group);
            var members = tracked.Group.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (HasTask(members[i]))
                {
                    throw new InvalidOperationException(
                        group.Id + " cannot be untracked while " + members[i] + " is out on a task.");
                }
            }

            _clock.Cancel(tracked.PendingDawn);
            _tracked.RemoveAt(IndexOf(group.Id));
        }

        /// <summary>
        /// Whether a community could be tracked standing here: on the map,
        /// on a cell the mover can stand on. What <see cref="Track"/> and
        /// every dawn require, as a question - for <see cref="Settlements.Founding"/>
        /// to ask before it moves anyone.
        /// </summary>
        public bool CanStandAt(WorldPosition at) => _grid.Contains(at) && _pathfinder.IsPassable(at, Mover);

        /// <summary>Whether this person is on a task.</summary>
        public bool HasTask(PersonHandle person) => SlotOf(person) is object;

        /// <summary>
        /// Fills <paramref name="into"/> with the worker of every task in
        /// progress, in slot order. Clears the list first.
        /// </summary>
        /// <remarks>
        /// The validator's "no dead person has active tasks" rule (section 5)
        /// cannot be answered from <see cref="PersonStore"/>: the death
        /// cascade removes the record (<see cref="Lifecycle.Deaths.Die"/>),
        /// so a task left behind belongs to a person who can no longer be
        /// enumerated. It has to be read from this side.
        ///
        /// A slot keeps its handle, generation included, so a worker whose
        /// slot has since been reused is still distinguishable here - which
        /// is what makes the rule checkable rather than merely stated.
        /// </remarks>
        public void CopyTaskWorkersTo(List<PersonHandle> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];

                if (slot is object && !slot.Task.IsNone)
                {
                    into.Add(slot.Task.Worker);
                }
            }
        }

        /// <summary>This person's task. Throws if they have none.</summary>
        public WorkTask TaskOf(PersonHandle person) => RequireSlot(person).Task;

        /// <summary>
        /// The coarse route of this person's task, origin first and
        /// destination last. Throws if they have none.
        /// </summary>
        public ReadOnlySpan<WorldPosition> RouteOf(PersonHandle person)
        {
            var slot = RequireSlot(person);
            return new ReadOnlySpan<WorldPosition>(slot.Route, 0, slot.RouteCount);
        }

        /// <summary>
        /// Where this person's task puts them at an instant: section 4's
        /// reconstruction. On a walking leg, the route cell reached by the
        /// fraction of the leg elapsed; at work, the destination. Throws if
        /// they have no task, or the instant is outside it.
        /// </summary>
        /// <remarks>
        /// Progress along a leg is by cell count rather than by each cell's
        /// cost, so a walk that crosses a forest is placed a little ahead of
        /// where the terrain would have it. Close enough for a coarse route;
        /// the stepped simulation that cares (#25) will have its own idea of
        /// where feet go.
        /// </remarks>
        public WorldPosition PositionAt(PersonHandle person, SimulationTime now)
        {
            var slot = RequireSlot(person);
            var task = slot.Task;
            var elapsed = task.Start.TicksUntil(now);

            switch (task.PhaseAt(now))
            {
                case TaskPhase.Outbound:
                    return AlongRoute(slot, elapsed, task.TravelTicks, forward: true);
                case TaskPhase.Working:
                    return task.Destination;
                case TaskPhase.Returning:
                    return AlongRoute(slot, elapsed - task.TravelTicks - task.WorkTicks, task.ReturnTicks, forward: false);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(now), now, person + "'s task runs from " + task.Start + " to " + task.End + ".");
            }
        }

        /// <summary>Whether the band has somewhere reachable to work this job, as of its last site refresh.</summary>
        public bool HasSite(ICommunity group, JobKind job) => SiteOf(TrackedFor(group), job).Reachable;

        /// <summary>Where the band works this job. Throws if it has nowhere.</summary>
        public WorldPosition SiteFor(ICommunity group, JobKind job)
        {
            var site = SiteOf(TrackedFor(group), job);

            if (!site.Reachable)
            {
                throw new InvalidOperationException(group.Id + " has no reachable site for " + job + ".");
            }

            return site.Destination;
        }

        /// <summary>
        /// What the band's last site search found for this job, reachable or
        /// not. For the world hash; <see cref="SiteFor"/> is the question the
        /// simulation asks.
        /// </summary>
        public SiteSurvey SurveyOf(ICommunity group, JobKind job)
        {
            var site = SiteOf(TrackedFor(group), job);
            return new SiteSurvey(site.Reachable, site.Destination, site.Cost, site.ReturnCost, site.LandReturns);
        }

        /// <summary>
        /// The route the band's last site search found for this job, the
        /// band's position first. Tasks copy it when they start.
        /// </summary>
        public IReadOnlyList<WorldPosition> SiteRouteOf(ICommunity group, JobKind job) =>
            SiteOf(TrackedFor(group), job).RouteView;

        /// <summary>Where the band stood when its sites were last found.</summary>
        public WorldPosition SitesFoundFrom(ICommunity group) => TrackedFor(group).SitesFrom;

        /// <summary>
        /// How many of the band are out on this job now. Rebuilt at dawn and
        /// kept exact through the day.
        /// </summary>
        public int OnDuty(ICommunity group, JobKind job)
        {
            var tracked = TrackedFor(group);

            if (!JobTable.IsJob(job))
            {
                throw new ArgumentOutOfRangeException(nameof(job), job, "Not a job anyone is on.");
            }

            return tracked.OnDuty[(int)job];
        }

        /// <summary>
        /// Living members as counted at the band's last dawn - the snapshot
        /// that sizes the day's picks, not a live count.
        /// </summary>
        public int LivingAtDawn(ICommunity group) => TrackedFor(group).Living;

        /// <summary>
        /// Hearths as counted at the band's last dawn - the snapshot that
        /// sizes the woodpile's winter reserve, not a live count.
        /// </summary>
        public int HearthsAtDawn(ICommunity group) => TrackedFor(group).Hearths;

        /// <summary>
        /// Finds the band's sites again from where it stands now. The dawn
        /// pass does this; a band that moves between dawns does it itself.
        /// Tasks under way keep the route they left with.
        /// </summary>
        public void RefreshSites(ICommunity group)
        {
            var tracked = TrackedFor(group);

            RefreshSites(tracked);
        }

        private void RefreshSites(Tracked tracked)
        {
            var from = tracked.Group.Position;

            RequireStandable(from);

            // Section 12's map belongs to the community, not to Jobs: a band's
            // own tracking makes it, and a settlement takes it over at
            // founding. Work only reads it - a community with none has never
            // seen anywhere to work, and KnownMaps says so rather than
            // quietly finding nowhere.
            var known = _knownMaps.For(tracked.Group.Id);

            for (var i = 0; i < Priority.Count; i++)
            {
                var job = Priority[i];
                FindSite(tracked, from, known, job, SiteOf(tracked, job));
            }

            tracked.SitesFrom = from;
        }

        /// <summary>
        /// Takes a person off whatever they are doing: the pending completion
        /// is cancelled, any inputs in process go back to the band's ledger,
        /// and the job is cleared. A step of the death cascade; harmless on
        /// someone with no task.
        /// </summary>
        public void Vacate(PersonHandle person)
        {
            var slot = SlotOf(person);

            if (slot is object)
            {
                var task = slot.Task;
                var tracked = TrackedFor(task.Holder);
                _clock.Cancel(task.Completion);

                if (JobTable.IsGathering(task.Job))
                {
                    var recipe = JobTable.Recipe(task.Job, task.Start.Season);

                    if (recipe.Inputs.Count > 0)
                    {
                        tracked.Group.SharedSupplies.CancelRecipe(recipe);
                    }

                    // The trip claimed its harvest when it set out (#26); one that
                    // never comes home leaves the fruit on the bush.
                    _land.Return(task.Destination);
                }
                else
                {
                    // Hours claimed on a building go back the same way (#100).
                    RequireBuildings().Unclaim(task.Destination, task.Job, task.WorkTicks);
                }

                slot.Clear();

                // The band keeps its dawn count of the living: this person is
                // usually dead, and Jobs is told about that but not about the
                // births and joins that would balance it (see Tracked.Living).
                tracked.OnDuty[(int)task.Job]--;
            }

            _people.SetJob(person, JobKind.None);
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            // The clock the router passes through must be the one the tasks
            // were booked on: a completion cancelled on any other clock would
            // still fire here.
            if (!ReferenceEquals(clock, _clock))
            {
                throw new InvalidOperationException(
                    "Jobs is bound to the clock its tasks are booked on, but was dispatched by a different one.");
            }

            switch (scheduled.Kind)
            {
                case ScheduledEventKind.WorkDayDue:
                    WorkDay(scheduled.Id, scheduled.PrimaryEntity);
                    break;
                case ScheduledEventKind.TaskCompleted:
                    Complete(scheduled.Id, scheduled.PrimaryEntity);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Jobs owns " + ScheduledEventKind.WorkDayDue + " and "
                        + ScheduledEventKind.TaskCompleted + ", but was handed " + scheduled + ".");
            }
        }

        // Dawn: sites from where the band stands today, then every free
        // worker picks, in member order.
        private void WorkDay(EventId dawn, EntityId holder)
        {
            var index = IndexOf(holder);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    ScheduledEventKind.WorkDayDue + " came due for a band Jobs is not tracking: " + holder + ".");
            }

            var tracked = _tracked[index];

            // The band names the dawn it booked, and only that one runs the
            // pass - the rule a task applies to its completion. Any other
            // WorkDayDue would run a second pass and book a second stream,
            // doubling every dawn from then on; and because only the stream
            // runs passes, a pass always runs at dawn.
            if (dawn != tracked.PendingDawn)
            {
                throw new InvalidOperationException(
                    ScheduledEventKind.WorkDayDue + " " + dawn + " came due for " + holder
                    + ", whose next work day is " + tracked.PendingDawn + ".");
            }

            tracked.PendingDawn = EventId.None;

            // Before the destination check, not inside it: a moving day starts
            // no tasks, but Vacate can still strike the band mid-march, and it
            // adjusts counts it expects the day to have rebuilt.
            Recount(tracked);

            // A moving day: the band's council (#54) set a destination before
            // this pass, and everyone walks with the band rather than out
            // from a camp it is leaving. Sites are found again tomorrow,
            // from wherever it arrived.
            if (tracked.Group.Destination is null)
            {
                RefreshSites(tracked);

                var members = tracked.Group.Members;
                var idle = 0;

                for (var i = 0; i < members.Count; i++)
                {
                    var member = members[i];

                    // Membership lags death until the cascade strikes the dead
                    // from the band. Nobody has a task at dawn: no task outlives
                    // dusk, and only the stream runs a pass.
                    if (!IsWorker(member))
                    {
                        continue;
                    }

                    if (!TryStart(tracked, member))
                    {
                        idle++;
                    }
                }

                // After the picks, so that hands nobody needed this morning
                // are the spare labour a new building waits for (#100).
                if (_buildings is object && tracked.Group.Id.Kind == EntityKind.Settlement)
                {
                    _buildings.AtDawn(tracked.Group, idle, tracked.Living);
                }
            }

            // The stream ends with time itself, as Hunger's does: a dawn with
            // no tomorrow to book into books nothing, and the band's pending
            // dawn stays None.
            var now = _clock.Now;
            var untilDawn = TicksUntilDawn(now);

            if (untilDawn <= long.MaxValue - now.Ticks)
            {
                tracked.PendingDawn = _clock.Schedule(
                    now.Plus(untilDawn), Phase, ScheduledEventKind.WorkDayDue, holder, EntityId.None);
            }
        }

        // The worker is home: deliver, then pick again if the day allows.
        private void Complete(EventId completion, EntityId workerId)
        {
            if (!_people.TryGetHandle(workerId, out var worker) || !(SlotOf(worker) is Slot slot))
            {
                throw new InvalidOperationException(
                    ScheduledEventKind.TaskCompleted + " came due for " + workerId
                    + ", who has no task. The death cascade cancels a dead worker's completion,"
                    + " so this one was booked by something other than Jobs.");
            }

            var task = slot.Task;

            // The task names the completion it booked, and only that one
            // finishes it: a duplicate, or one rebuilt from a save that
            // disagrees with the task, would otherwise deliver early and
            // leave the real completion to arrive for nobody.
            if (completion != task.Completion)
            {
                throw new InvalidOperationException(
                    ScheduledEventKind.TaskCompleted + " " + completion + " came due for " + workerId
                    + ", whose task is waiting on " + task.Completion + ".");
            }

            var tracked = TrackedFor(task.Holder);

            if (JobTable.IsGathering(task.Job))
            {
                // Gathering has no inputs in process, so completing is a Gather;
                // the ledger tallies it as such.
                tracked.Group.SharedSupplies.CompleteRecipe(JobTable.Recipe(task.Job, task.Start.Season));
            }
            else
            {
                RequireBuildings().Credit(task.Destination, task.Job, task.WorkTicks, tracked.Group.SharedSupplies);
            }

            slot.Clear();
            tracked.OnDuty[(int)task.Job]--;
            _people.SetJob(worker, JobKind.None);

            TryStart(tracked, worker);
        }

        // Picks a job for a free worker and starts one task of it, or leaves
        // them idle when nothing is needed, reachable and short enough.
        private bool TryStart(Tracked tracked, PersonHandle worker)
        {
            // Sites were found from wherever the band stood at dawn. A band
            // that has moved since (#54) picks from where it stands now, or a
            // task would start at the new camp and walk a route from the old
            // one; refreshing here keeps that a fact rather than a contract
            // the mover has to remember.
            if (tracked.SitesFrom != tracked.Group.Position)
            {
                RefreshSites(tracked);
            }

            var now = _clock.Now;
            var job = ChooseJob(tracked, now, out var building);

            if (job == JobKind.None)
            {
                return false;
            }

            var site = SiteOf(tracked, job);
            var work = WorkTicksOf(job, building, now);
            var end = now.Plus(TripTicks(site, work));

            // Inputs first: BeginRecipe refuses before it moves anything, and
            // a booking for an instant after now cannot be refused, so
            // neither step can leave the other half done.
            var ledger = tracked.Group.SharedSupplies;

            if (building is null)
            {
                ledger.BeginRecipe(JobTable.Recipe(job, now.Season));
            }

            var completion = _clock.Schedule(
                end, Phase, ScheduledEventKind.TaskCompleted, _people.GetId(worker), EntityId.None);

            if (building is null)
            {
                // The harvest is claimed now, not on the way home (#26): whoever
                // takes a bush's last trip leaves it bare for the next picker,
                // whose ChooseJob then finds the next bush. Cannot refuse -
                // ChooseJob only offers a site that can be worked.
                _land.Take(site.Destination);
            }
            else
            {
                // So are a building's hours (#100): the next Builder or
                // Farmer sees only what is left.
                RequireBuildings().Claim(building, job, work);
            }

            var slot = SlotFor(worker.Index);
            slot.Task = new WorkTask(
                worker, tracked.Group.Id, job, now, site.Cost * TicksPerCostUnit, work, site.ReturnCost * TicksPerCostUnit,
                tracked.Group.Position, site.Destination, completion);
            slot.CopyRoute(site.Route);
            _people.SetJob(worker, job);
            tracked.OnDuty[(int)job]++;

            // Section 12: reveal is computed from the scheduled movement, at
            // the moment it is scheduled, so a stepped agent walking the same
            // route reveals the same cells. The way home is the way out
            // reversed, so one walk of the route covers the round trip.
            _knownMaps.RevealAlong(tracked.Group.Id, site.Route, RevealRadius);
            return true;
        }

        // Of the jobs that are needed, have a site, and whose task would end by
        // dusk, the one whose store is furthest below its target, as a share
        // of that target; an even shortfall goes in priority order. Need
        // counts what is on its way home: everyone with a task is about to
        // deliver its output, so a round of pickers spreads across the short
        // stores rather than all filling the first. Taking the first needed
        // job instead sent every hand to food while food was short, and a
        // settlement short of food all year froze with an empty woodpile
        // (#17's first 200-year runs).
        private JobKind ChooseJob(Tracked tracked, SimulationTime now, out Building? building)
        {
            var ticksUntilDusk = TicksUntilDusk(now);
            var season = now.Season;
            var winterDays = WinterDaysAhead(now);
            var ledger = tracked.Group.SharedSupplies;

            // At least one: the dawn pass counts before anything starts, and
            // a pick follows either that pass - which only offers a living
            // member - or a completion, which only a worker started at that
            // pass can reach. So the daily draw in TargetOf is never zero.
            var living = tracked.Living;
            var chosen = JobKind.None;
            long chosenShort = 0L;
            long chosenTarget = 1L;
            building = null;

            for (var i = 0; i < Priority.Count; i++)
            {
                var job = Priority[i];
                var site = SiteOf(tracked, job);

                // A site spent since it was found - by this band's last pick
                // or another band's - is found again, from the same place
                // and over the same known map, so pickers move on to the
                // next bush or tree rather than walk to a bare one (#26).
                // A search that found nothing looks again once a claim has
                // been given back since, here or by another band (#141 review).
                // So is one cleared for a building since (#100): plains
                // would otherwise read as workable forever. And one approved
                // for a building since, which nobody gathers on any more.
                if ((site.Reachable && (!_land.IsWorkable(site.Destination) || !JobTable.WorksOn(job, _grid[site.Destination])
                        || (_buildings is object && _buildings.IsReserved(site.Destination))))
                    || (!site.Reachable && site.LandReturns != _land.Returns))
                {
                    FindSite(tracked, tracked.SitesFrom, _knownMaps.For(tracked.Group.Id), job, site);
                }

                if (!site.Reachable)
                {
                    continue;
                }

                if (TripTicks(site, JobTable.Recipe(job, season).Duration) > ticksUntilDusk)
                {
                    continue;
                }

                var target = TargetOf(tracked, job, living, winterDays);
                var shortBy = target - Expected(tracked, ledger, ResourceOf(job));

                // Grain is eaten too, milled (#100), so it counts toward the
                // food a forager would otherwise be sent for.
                if (job == JobKind.Forager)
                {
                    shortBy -= (long)ledger.Available(ResourceKind.Grain) * PrimitiveTier.MealsPerGrain;
                }

                // Needed at all, and strictly further short than the best so
                // far - compared as fractions by cross-multiplying. Food's
                // target is up to 40 per person and wood's about 10, so the
                // products fit a long until some 150 million people share one
                // community. Checked so that a world past that throws rather
                // than quietly sending hands to the wrong store (the #103
                // review).
                if (shortBy > 0L && checked(shortBy * chosenTarget) > checked(chosenShort * target))
                {
                    chosen = job;
                    chosenShort = shortBy;
                    chosenTarget = target;
                }
            }

            // A settlement's building and its fields rank by the same rule:
            // the hours nobody has claimed, against a target of the hours
            // times Buildings.NeedScale (#100). Worker-ticks rather than
            // units of stock, so the products stay far inside a long.
            if (_buildings is object && tracked.Group.Id.Kind == EntityKind.Settlement)
            {
                var id = tracked.Group.Id;

                if (_buildings.TryBuildWork(id, out var project, out var buildShort, out var buildTarget)
                    && Fits(tracked, JobKind.Builder, project, now, ticksUntilDusk)
                    && checked(buildShort * chosenTarget) > checked(chosenShort * buildTarget))
                {
                    chosen = JobKind.Builder;
                    chosenShort = buildShort;
                    chosenTarget = buildTarget;
                    building = project;
                }

                if (_buildings.TryFieldWork(id, out var field, out var fieldShort, out var fieldTarget)
                    && Fits(tracked, JobKind.Farmer, field, now, ticksUntilDusk)
                    && checked(fieldShort * chosenTarget) > checked(chosenShort * fieldTarget))
                {
                    chosen = JobKind.Farmer;
                    building = field;
                }
            }

            if (chosen != JobKind.Builder && chosen != JobKind.Farmer)
            {
                building = null;
            }

            return chosen;
        }

        // Whether a trip to work this building can be routed and is home by
        // dusk. The route is the job's site, kept until the target or the
        // ground changes - clearing rewrites cells, and with them the cost.
        private bool Fits(Tracked tracked, JobKind job, Building building, SimulationTime now, long ticksUntilDusk)
        {
            var site = SiteOf(tracked, job);

            if (!site.Reachable || site.Destination != building.Anchor || site.Rewrites != _grid.Rewrites)
            {
                site.Rewrites = _grid.Rewrites;
                site.Reachable = _pathfinder.TryFindRoute(
                    tracked.SitesFrom, building.Anchor, Mover, 2 * MaxSiteRadius, site.Route, out var cost);

                if (!site.Reachable)
                {
                    return false;
                }

                site.Destination = building.Anchor;
                site.Cost = cost;
                _scratchRoute.Clear();
                _scratchRoute.AddRange(site.Route);
                _scratchRoute.Reverse();
                site.ReturnCost = _pathfinder.CostOfRoute(_scratchRoute, Mover);
            }

            return TripTicks(site, WorkTicksOf(job, building, now)) <= ticksUntilDusk;
        }

        // How long one task of a job works at its site: a gathering recipe's
        // duration, or a share of a building's hours.
        private long WorkTicksOf(JobKind job, Building? building, SimulationTime now) =>
            building is null ? JobTable.Recipe(job, now.Season).Duration : RequireBuildings().ShareOf(building, job);

        private Buildings RequireBuildings() =>
            _buildings ?? throw new InvalidOperationException(
                "A Builder or Farmer task needs the buildings it works on; this Jobs was made without them.");

        // One walk of the membership per band per dawn, where a walk per pick
        // used to be: the pick is what scales with workers, so the scan inside
        // it made a day quadratic in population (#83).
        private void Recount(Tracked tracked)
        {
            var members = tracked.Group.Members;
            var living = 0;
            var onDuty = tracked.OnDuty;
            Array.Clear(onDuty, 0, onDuty.Length);

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                if (!_people.IsAlive(member))
                {
                    continue;
                }

                living++;

                // From the task, not the record: the record's Job is a mirror
                // the bulk span can write, and this count must not be.
                if (SlotOf(member) is Slot slot)
                {
                    onDuty[(int)slot.Task.Job]++;
                }
            }

            tracked.Living = living;
            tracked.Hearths = Warmth.CountHearths(members, _people, _hearthScratch);
        }

        // The stock a job's store is worked up to. Living is at least one
        // wherever this is reached (see ChooseJob), so food's is never zero.
        private static long TargetOf(Tracked tracked, JobKind job, int living, long winterDays)
        {
            switch (job)
            {
                case JobKind.Forager:
                    return (long)Hunger.DailyRation * living * (FoodTargetDays + winterDays);
                case JobKind.Woodcutter:
                    return WoodCap + ((long)tracked.Hearths * Warmth.FuelPerFire * winterDays);
                case JobKind.StoneGatherer:
                    return StoneCap;
                default:
                    throw new ArgumentOutOfRangeException(nameof(job), job, "Not a job with a need.");
            }
        }

        private static ResourceKind ResourceOf(JobKind job)
        {
            switch (job)
            {
                case JobKind.Forager:
                    return ResourceKind.Food;
                case JobKind.Woodcutter:
                    return ResourceKind.Wood;
                case JobKind.StoneGatherer:
                    return ResourceKind.Stone;
                default:
                    throw new ArgumentOutOfRangeException(nameof(job), job, "Not a job with a need.");
            }
        }

        // Available now plus what those on duty will bring home.
        private long Expected(Tracked tracked, ResourceLedger ledger, ResourceKind kind)
        {
            long expected = ledger.Available(kind);

            for (var i = 0; i < Priority.Count; i++)
            {
                var job = Priority[i];
                var onDuty = tracked.OnDuty[(int)job];

                if (onDuty == 0)
                {
                    continue;
                }

                // Everyone on duty started today, so today's season is theirs.
                var outputs = JobTable.Recipe(job, _clock.Now.Season).Outputs;

                for (var j = 0; j < outputs.Count; j++)
                {
                    if (outputs[j].Kind == kind)
                    {
                        expected += (long)onDuty * outputs[j].Quantity;
                    }
                }
            }

            return expected;
        }

        // The cheapest cell the job works on that a route reaches, within
        // MaxSiteRadius of the band: one bounded search whatever the terrain
        // looks like, since the pathfinder settles cells in cost order and
        // stops at the first the job accepts. The way home is the same cells
        // in the other order, and costs what they cost entered from that
        // side - the camp cell instead of the site cell, at the least.
        //
        // A settlement's woodcutters with no tree in that reach look out to
        // EmergencyWoodRadius (#149): a village that cleared its first stand
        // has nothing near it to fell for the five years the stumps take to
        // stand again, and a winter begun on an empty woodpile killed it
        // whole. Only then, so the wider search is paid only by a village
        // already short, and a band - which moves on instead - never pays it.
        private void FindSite(Tracked tracked, WorldPosition from, ReadOnlySpan<bool> known, JobKind job, Site site)
        {
            site.LandReturns = _land.Returns;
            site.Reachable = _pathfinder.TryFindNearest(
                from, Mover, JobTable.Terrain(job), known, _workable, MaxSiteRadius, site.Route, out var cost);

            if (!site.Reachable && job == JobKind.Woodcutter && tracked.Group.Id.Kind == EntityKind.Settlement)
            {
                site.Reachable = _pathfinder.TryFindNearest(
                    from, Mover, JobTable.Terrain(job), known, _workable, EmergencyWoodRadius, site.Route, out cost);
            }

            if (!site.Reachable)
            {
                return;
            }

            site.Destination = site.Route[site.Route.Count - 1];
            site.Cost = cost;
            _scratchRoute.Clear();
            _scratchRoute.AddRange(site.Route);
            _scratchRoute.Reverse();
            site.ReturnCost = _pathfinder.CostOfRoute(_scratchRoute, Mover);
        }

        // Out, work, and back - the back leg priced on its own, since the
        // cells entered walking home are not the cells entered walking out.
        private static long TripTicks(Site site, long workTicks) =>
            (site.Cost * TicksPerCostUnit) + workTicks + (site.ReturnCost * TicksPerCostUnit);

        private bool IsWorker(PersonHandle member) =>
            _people.IsAlive(member) && Works(_people.GetAgeStage(member));

        private static WorldPosition AlongRoute(Slot slot, long elapsed, long leg, bool forward)
        {
            var last = slot.RouteCount - 1;

            // A leg of no length is a route of one cell: origin and
            // destination coincide, and the worker is on it.
            //
            // Plain 64-bit arithmetic: elapsed is at most the leg, the leg is
            // the route's cost times TicksPerCostUnit, and a route's cost is
            // at most 14 * TerrainRule.MaxCost per cell - so the product
            // exceeds a long only past some 800 million cells, a grid whose
            // search arrays alone run to tens of gigabytes. A wider multiply
            // here would be a guard nothing can exercise, the stance the
            // ledger takes on its flow counters.
            var step = leg == 0L ? last : (int)(elapsed * last / leg);
            return slot.Route[forward ? step : last - step];
        }

        // Off the map is the grid's refusal (the indexer throws); a cell the
        // mover cannot stand on is this one. No site is reachable from such
        // an origin - a search from there touches no cell the pathfinder
        // could refuse - so a community there would idle at every dawn
        // without a word. Checked when tracked and again at every refresh,
        // since Position is the community's own to set.
        private void RequireStandable(WorldPosition at)
        {
            // The indexer throws for off the map, which is the refusal the
            // grid owns; only a cell on the map nobody can stand on is ours.
            _grid.IndexOf(at);

            if (!CanStandAt(at))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(at), at, "Nobody can stand on that cell; no site would ever be reachable from it.");
            }
        }

        // Ticks to the first dawn strictly after now: a band tracked at dawn
        // itself starts tomorrow, since the instant is already being
        // dispatched or about to be. A delta rather than an instant so the
        // caller can ask whether the clock has room for it; Track does not
        // ask, and lets Plus refuse a dawn past the end of time the way any
        // other booking past it is refused.
        /// <summary>
        /// The winter days the stores must still cover from today: none in
        /// spring, a whole winter through summer and autumn, and in winter
        /// what is left of it, today included. What the food and wood
        /// thresholds rise by (#53).
        /// </summary>
        public static long WinterDaysAhead(SimulationTime now)
        {
            switch (now.Season)
            {
                case Season.Spring:
                    return 0L;
                case Season.Winter:
                    return now.DaysUntilSpring;
                default:
                    return SimulationTime.DaysPerSeason;
            }
        }

        private static long TicksUntilDawn(SimulationTime now)
        {
            var sinceDawn = now.TickOfDay - Dawn;
            return sinceDawn < 0L ? -sinceDawn : SimulationTime.TicksPerDay - sinceDawn;
        }

        // How long the work day has left, and never past the end of time:
        // the world's last day ends at 15:30, so a task that would run to
        // dusk there is one the clock could not book. Nothing runs before
        // dawn - only the stream runs a pass, and it runs at dawn - so the
        // window needs no lower edge here.
        private static long TicksUntilDusk(SimulationTime now) =>
            Math.Min(Dusk - now.TickOfDay, long.MaxValue - now.Ticks);

        private Slot? SlotOf(PersonHandle person)
        {
            if (person.IsNone || person.Index >= _slots.Length)
            {
                return null;
            }

            var slot = _slots[person.Index];
            return slot is object && slot.Task.Worker == person ? slot : null;
        }

        private Slot RequireSlot(PersonHandle person) =>
            SlotOf(person) ?? throw new InvalidOperationException(person + " has no task.");

        private Slot SlotFor(int index)
        {
            if (index >= _slots.Length)
            {
                Array.Resize(ref _slots, Math.Max(index + 1, _slots.Length == 0 ? InitialSlots : _slots.Length * 2));
            }

            return _slots[index] ??= new Slot();
        }

        private static Site SiteOf(Tracked tracked, JobKind job)
        {
            if (!JobTable.IsJob(job))
            {
                throw new ArgumentOutOfRangeException(nameof(job), job, "Not a job with a site.");
            }

            return tracked.Sites[(int)job];
        }

        private Tracked TrackedFor(ICommunity group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            return TrackedFor(group.Id);
        }

        private Tracked TrackedFor(EntityId holder)
        {
            var index = IndexOf(holder);

            if (index < 0)
            {
                throw new InvalidOperationException(holder + " is not tracked by Jobs.");
            }

            return _tracked[index];
        }

        private int IndexOf(EntityId holder)
        {
            for (var i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].Group.Id == holder)
                {
                    return i;
                }
            }

            return -1;
        }

        // A band and where it works each job. Sites are indexed by JobKind,
        // with an unused slot for None.
        private sealed class Tracked
        {
            public Tracked(ICommunity group, int jobKinds)
            {
                Group = group;
                Sites = new Site[jobKinds];
                OnDuty = new int[jobKinds];

                for (var i = 0; i < jobKinds; i++)
                {
                    Sites[i] = new Site();
                }
            }

            public ICommunity Group { get; }

            public Site[] Sites { get; }

            /// <summary>
            /// How many of the band are on each job. Rebuilt by the dawn pass
            /// and kept exact through the day: a task starting, completing or
            /// being vacated are the only ways a slot changes, and all three
            /// adjust this.
            /// </summary>
            public int[] OnDuty { get; }

            /// <summary>
            /// Living members, as counted at dawn. Unlike <see cref="OnDuty"/>
            /// this is a snapshot, not a running total: Jobs hears about a
            /// death (through <see cref="Jobs.Vacate"/>) but not a birth or a
            /// join, so a count maintained in-day would drift one way only.
            /// A day's picks therefore size the band's appetite by who was
            /// alive at dawn, which is also the moment its sites were found.
            /// </summary>
            public int Living { get; set; }

            /// <summary>
            /// Hearths a winter night would light, as counted at dawn
            /// (<see cref="Warmth.CountHearths"/>) - a snapshot for the same
            /// reason as <see cref="Living"/>. Sizes the woodpile's winter
            /// reserve.
            /// </summary>
            public int Hearths { get; set; }

            /// <summary>The WorkDayDue booked for this band's next dawn; None when the world ends first.</summary>
            public EventId PendingDawn { get; set; }

            /// <summary>
            /// Where the band stood when its sites were found. Every pick
            /// follows a pass or a task, so sites have always been found by
            /// the time this is compared.
            /// </summary>
            public WorldPosition SitesFrom { get; set; }
        }

        private sealed class WorkableFilter : ISiteFilter
        {
            private readonly LandCover _land;
            private readonly Buildings? _buildings;

            public WorkableFilter(LandCover land, Buildings? buildings)
            {
                _land = land;
                _buildings = buildings;
            }

            public bool Accepts(int cell) =>
                _land.Ripe.Accepts(cell) && (_buildings is null || !_buildings.IsReserved(cell));
        }

        private sealed class Site
        {
            public Site()
            {
                RouteView = Route.AsReadOnly();
            }

            public bool Reachable { get; set; }

            public WorldPosition Destination { get; set; }

            public long Cost { get; set; }

            public long ReturnCost { get; set; }

            // LandCover.Returns when this site was last searched for.
            public long LandReturns { get; set; }

            // TerrainGrid.Rewrites when a Builder's or Farmer's route was
            // last found: clearing changes what the walk costs (#100).
            public long Rewrites { get; set; }

            public List<WorldPosition> Route { get; } = new List<WorldPosition>();

            // Handed out by SiteRouteOf, so a caller cannot cast the list
            // back and edit a route a task will copy.
            public ReadOnlyCollection<WorldPosition> RouteView { get; }
        }

        // One person's task and the route it walks. The route buffer is kept
        // between tasks and only grows, so a slot allocates once.
        private sealed class Slot
        {
            public WorkTask Task;

            public WorldPosition[] Route = Array.Empty<WorldPosition>();

            public int RouteCount;

            public void CopyRoute(List<WorldPosition> route)
            {
                if (Route.Length < route.Count)
                {
                    Route = new WorldPosition[Math.Max(route.Count, Route.Length * 2)];
                }

                route.CopyTo(Route);
                RouteCount = route.Count;
            }

            public void Clear()
            {
                Task = WorkTask.None;
                RouteCount = 0;
            }
        }
    }
}
