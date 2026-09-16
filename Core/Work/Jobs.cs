using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
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
    /// wood and stone below their caps - and "choosing" is taking the first
    /// of <see cref="Priority"/> that is needed, reachable, and fits before
    /// dusk. It runs at dawn and at every completion, so a band whose food
    /// is covered by noon sends its afternoon hands to wood without waiting
    /// for tomorrow. Nothing is stored between picks: how many are on a job
    /// is a scan of the band's members, which section 5 blesses at this
    /// scale. What is already on its way home counts toward the threshold,
    /// so a round of pickers cannot all see the same shortfall and all fill
    /// it. Skill weighting is #22's; a settlement-wide plan is #23's and
    /// replaces "decide need" without touching "choose".
    ///
    /// **One task at a time, inside a dawn-to-dusk window.** A task is
    /// booked when it starts, never a day ahead, and is started only if it
    /// ends by <see cref="Dusk"/>. The window is a stand-in for #21's daily
    /// schedule - eat, work, socialise, sleep - and exists so that a forager
    /// makes two or three trips a day rather than six around the clock,
    /// which is what keeps hunger a constraint. Someone idle at dawn stays
    /// idle until the next dawn; nothing wakes them mid-day, because the
    /// meal that could create a shortfall lands before dawn.
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
    /// **Sites are per band, per job, found at dawn.** The cheapest cell the
    /// job works on that a route reaches within <see cref="MaxSiteRadius"/>
    /// - one bounded search per job (<see cref="Pathfinder.TryFindNearest"/>),
    /// however many candidates there are and whether or not any of them
    /// connect - so every worker on a job walks the same route to the same
    /// cell. Three searches per band per day rather than one per task, and
    /// placeholder in the <see cref="PrimitiveTier"/> sense: sites are
    /// infinite and identical until #26 makes them neither. A band that
    /// moves (#54) calls <see cref="RefreshSites"/>; otherwise the dawn pass
    /// does.
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
        /// Ticks of walking per unit of route cost. At one, a plains cell
        /// (cost 10, straight step 10) takes 100 ticks - under two minutes -
        /// and a forest cell twice that. Placeholder; the cell has no size yet.
        /// </summary>
        public const long TicksPerCostUnit = 1L;

        /// <summary>Foragers are needed while the band's food, counting what is on its way home, covers fewer days than this.</summary>
        public const int FoodTargetDays = 10;

        /// <summary>Woodcutters are needed while the band's wood, counting what is on its way home, is below this.</summary>
        public const int WoodCap = 200;

        /// <summary>Stone gatherers are needed while the band's stone, counting what is on its way home, is below this.</summary>
        public const int StoneCap = 100;

        /// <summary>How far out from the band a site is looked for, in cells.</summary>
        public const int MaxSiteRadius = 16;

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
        private readonly TerrainGrid _grid;

        // A list, scanned by id, for the same reason Hunger's is.
        private readonly List<Tracked> _tracked = new List<Tracked>();

        // One slot per person slot in the store, addressed by handle index
        // and checked against the handle's generation, so a recycled slot
        // never reads its last occupant's task as the new one's.
        private Slot?[] _slots = Array.Empty<Slot?>();

        // Scratch for a pick: how many members are on each job.
        private readonly int[] _onDuty = new int[JobKindCount];

        // Scratch for a site search: the route to the candidate being tried.
        private readonly List<WorldPosition> _scratchRoute = new List<WorldPosition>();

        public Jobs(SimulationClock clock, PersonStore people, Pathfinder pathfinder)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _grid = pathfinder.Grid;
        }

        /// <summary>How many bands have work days scheduled.</summary>
        public int TrackedCount => _tracked.Count;

        /// <summary>Whether people at this stage take jobs: adults and elders.</summary>
        public static bool Works(AgeStage stage) => stage == AgeStage.Adult || stage == AgeStage.Elder;

        /// <summary>
        /// Starts a band working: its first dawn pass is booked for the next
        /// <see cref="Dawn"/> after now, and each pass books the next.
        /// Refuses a band already tracked - two passes a day would assign
        /// twice.
        /// </summary>
        public void Track(MobileGroup group)
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

            // Booked before recorded, as Hunger does: the booking is the one
            // thing here that can be refused.
            _clock.Schedule(_clock.Now.Plus(TicksUntilDawn(_clock.Now)), Phase, ScheduledEventKind.WorkDayDue, group.Id, EntityId.None);
            _tracked.Add(new Tracked(group, JobKindCount));
        }

        /// <summary>Whether this person is on a task.</summary>
        public bool HasTask(PersonHandle person) => SlotOf(person) is object;

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
        public bool HasSite(MobileGroup group, JobKind job) => SiteOf(TrackedFor(group), job).Reachable;

        /// <summary>Where the band works this job. Throws if it has nowhere.</summary>
        public WorldPosition SiteFor(MobileGroup group, JobKind job)
        {
            var site = SiteOf(TrackedFor(group), job);

            if (!site.Reachable)
            {
                throw new InvalidOperationException(group.Id + " has no reachable site for " + job + ".");
            }

            return site.Destination;
        }

        /// <summary>
        /// Finds the band's sites again from where it stands now. The dawn
        /// pass does this; a band that moves between dawns does it itself.
        /// Tasks under way keep the route they left with.
        /// </summary>
        public void RefreshSites(MobileGroup group)
        {
            var tracked = TrackedFor(group);

            // The grids own off-map contract, up front: a scan from far off the
            // map touches no cell the pathfinder could refuse, and would record
            // no sites for a band that then idles without a word.
            _grid.IndexOf(tracked.Group.Position);

            for (var i = 0; i < Priority.Count; i++)
            {
                var job = Priority[i];
                FindSite(tracked.Group.Position, job, SiteOf(tracked, job));
            }
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
                var recipe = JobTable.Recipe(task.Job);
                _clock.Cancel(task.Completion);

                if (recipe.Inputs.Count > 0)
                {
                    TrackedFor(task.Holder).Group.SharedSupplies.CancelRecipe(recipe);
                }

                slot.Clear();
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
                    WorkDay(scheduled.PrimaryEntity);
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
        private void WorkDay(EntityId holder)
        {
            var index = IndexOf(holder);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    ScheduledEventKind.WorkDayDue + " came due for a band Jobs is not tracking: " + holder + ".");
            }

            var tracked = _tracked[index];
            RefreshSites(tracked.Group);

            var members = tracked.Group.Members;

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                // Membership lags death until the cascade strikes the dead
                // from the band. No task outlives dusk, so nobody has one at
                // dawn; a pass run at any other hour leaves those out working
                // rather than booking a second task over the first.
                if (!IsWorker(member) || HasTask(member))
                {
                    continue;
                }

                TryStart(tracked, member);
            }

            // The next dawn, not a day from now: the handler is callable at
            // any hour, and a pass run at another one would otherwise drag
            // the daily pass to that hour for good. The stream ends with time
            // itself, as Hunger's does - measured to the dawn in question,
            // which from an off-hour pass on the world's last evening is
            // nearer than a day.
            var now = _clock.Now;
            var untilDawn = TicksUntilDawn(now);

            if (untilDawn <= long.MaxValue - now.Ticks)
            {
                _clock.Schedule(now.Plus(untilDawn), Phase, ScheduledEventKind.WorkDayDue, holder, EntityId.None);
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

            // Gathering has no inputs in process, so completing is a Gather;
            // the ledger tallies it as such.
            tracked.Group.SharedSupplies.CompleteRecipe(JobTable.Recipe(task.Job));
            slot.Clear();
            _people.SetJob(worker, JobKind.None);

            TryStart(tracked, worker);
        }

        // Picks a job for a free worker and starts one task of it, or leaves
        // them idle when nothing is needed, reachable and short enough.
        private bool TryStart(Tracked tracked, PersonHandle worker)
        {
            var now = _clock.Now;
            var job = ChooseJob(tracked, TicksUntilDusk(now));

            if (job == JobKind.None)
            {
                return false;
            }

            var site = SiteOf(tracked, job);
            var recipe = JobTable.Recipe(job);
            var end = now.Plus(TripTicks(site, recipe));

            // Inputs first: BeginRecipe refuses before it moves anything, and
            // a booking for an instant after now cannot be refused, so
            // neither step can leave the other half done.
            var ledger = tracked.Group.SharedSupplies;
            ledger.BeginRecipe(recipe);
            var completion = _clock.Schedule(
                end, Phase, ScheduledEventKind.TaskCompleted, _people.GetId(worker), EntityId.None);

            var slot = SlotFor(worker.Index);
            slot.Task = new WorkTask(
                worker, tracked.Group.Id, job, now, site.Cost * TicksPerCostUnit, recipe.Duration, site.ReturnCost * TicksPerCostUnit,
                tracked.Group.Position, site.Destination, completion);
            slot.CopyRoute(site.Route);
            _people.SetJob(worker, job);
            return true;
        }

        // The first job in priority order that is needed, has a site, and
        // whose task would end by dusk. Need counts what is on its way home:
        // everyone with a task is about to deliver its output.
        private JobKind ChooseJob(Tracked tracked, long ticksUntilDusk)
        {
            var members = tracked.Group.Members;
            var living = 0;
            Array.Clear(_onDuty, 0, _onDuty.Length);

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                if (!_people.IsAlive(member))
                {
                    continue;
                }

                living++;

                // From the task, not the record: the records Job is a mirror
                // the bulk span can write, and this count must not be.
                if (SlotOf(member) is Slot slot)
                {
                    _onDuty[(int)slot.Task.Job]++;
                }
            }

            for (var i = 0; i < Priority.Count; i++)
            {
                var job = Priority[i];
                var site = SiteOf(tracked, job);

                if (!site.Reachable)
                {
                    continue;
                }

                if (TripTicks(site, JobTable.Recipe(job)) > ticksUntilDusk)
                {
                    continue;
                }

                if (Needed(job, tracked.Group.SharedSupplies, living))
                {
                    return job;
                }
            }

            return JobKind.None;
        }

        // The picker is alive and a member, so living is at least one and the
        // daily draw is never zero.
        private bool Needed(JobKind job, ResourceLedger ledger, int living)
        {
            switch (job)
            {
                case JobKind.Forager:
                    var dailyDraw = (long)Hunger.DailyRation * living;
                    return Expected(ledger, ResourceKind.Food) / dailyDraw < FoodTargetDays;
                case JobKind.Woodcutter:
                    return Expected(ledger, ResourceKind.Wood) < WoodCap;
                case JobKind.StoneGatherer:
                    return Expected(ledger, ResourceKind.Stone) < StoneCap;
                default:
                    throw new ArgumentOutOfRangeException(nameof(job), job, "Not a job with a need.");
            }
        }

        // Available now plus what those on duty will bring home.
        private long Expected(ResourceLedger ledger, ResourceKind kind)
        {
            long expected = ledger.Available(kind);

            for (var i = 0; i < Priority.Count; i++)
            {
                var job = Priority[i];
                var onDuty = _onDuty[(int)job];

                if (onDuty == 0)
                {
                    continue;
                }

                var outputs = JobTable.Recipe(job).Outputs;

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
        private void FindSite(WorldPosition from, JobKind job, Site site)
        {
            site.Reachable = _pathfinder.TryFindNearest(
                from, Mover, JobTable.Terrain(job), MaxSiteRadius, site.Route, out var cost);

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
        private static long TripTicks(Site site, Recipe recipe) =>
            (site.Cost * TicksPerCostUnit) + recipe.Duration + (site.ReturnCost * TicksPerCostUnit);

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

        // Ticks to the first dawn strictly after now: a band tracked at dawn
        // itself starts tomorrow, since the instant is already being
        // dispatched or about to be. A delta rather than an instant so the
        // caller can ask whether the clock has room for it; Track does not
        // ask, and lets Plus refuse a dawn past the end of time the way any
        // other booking past it is refused.
        private static long TicksUntilDawn(SimulationTime now)
        {
            var sinceDawn = now.TickOfDay - Dawn;
            return sinceDawn < 0L ? -sinceDawn : SimulationTime.TicksPerDay - sinceDawn;
        }

        // How long the work day has left, and never past the end of time:
        // the world's last day ends at 15:30, so a task that would run to
        // dusk there is one the clock could not book.
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

        private Tracked TrackedFor(MobileGroup group)
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
            public Tracked(MobileGroup group, int jobKinds)
            {
                Group = group;
                Sites = new Site[jobKinds];

                for (var i = 0; i < jobKinds; i++)
                {
                    Sites[i] = new Site();
                }
            }

            public MobileGroup Group { get; }

            public Site[] Sites { get; }
        }

        private sealed class Site
        {
            public bool Reachable { get; set; }

            public WorldPosition Destination { get; set; }

            public long Cost { get; set; }

            public long ReturnCost { get; set; }

            public List<WorldPosition> Route { get; } = new List<WorldPosition>();
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
