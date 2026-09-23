using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Knowledge;
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
    /// - and "choosing" is taking the first
    /// of <see cref="Priority"/> that is needed, reachable, and fits before
    /// dusk. It runs at dawn and at every completion, so a band whose food
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
    /// grown since summer, a woodpile the foragers crowded out. Section 9's
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
    /// placeholder in the <see cref="PrimitiveTier"/> sense: sites are
    /// infinite and identical until #26 makes them neither. Sites remember
    /// where they were found from, and a pick made from anywhere else finds
    /// them again first - so a band that moves (#54) need not tell anyone;
    /// <see cref="RefreshSites"/> is there for a caller that wants the new
    /// sites before the next pick.
    ///
    /// **A work site is one of section 12's place-picking decisions.** The
    /// site must be a cell the community knows; the route to it need not be,
    /// so a band can work something it has only glimpsed the near edge of.
    /// The trip then reveals <see cref="RevealRadius"/> around its whole
    /// route, which is how a settled community's map grows at all once it
    /// stops wandering. Only as far as the work goes, though: foraging on
    /// plains is worked underfoot, so a settlement that knows no forest and
    /// no hills makes no trips and learns nothing. Founding softens that - a
    /// band only settles where it already knows food and wood - and #85's
    /// deliberate scouting is section 12's real answer to it.
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
        /// How far a worker on the road sees, in cells. Section 12 names
        /// "foragers and hunters working out from a settlement" among the
        /// things that reveal, and this is how far they reveal.
        /// </summary>
        /// <remarks>
        /// Its own constant rather than <see cref="Nomadic.NomadicBands.RevealRadius"/>,
        /// which happens to be the same number today: a band on the march and
        /// a forager on a day trip are different sights, and deliberate
        /// scouting (#85) is likely to want to tell them apart. If the two are
        /// ever meant to move together, say so here rather than leaving it to
        /// coincidence.
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

        public Jobs(SimulationClock clock, PersonStore people, Pathfinder pathfinder, KnownMaps knownMaps)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _knownMaps = knownMaps ?? throw new ArgumentNullException(nameof(knownMaps));
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
                FindSite(from, known, job, SiteOf(tracked, job));
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
                var recipe = JobTable.Recipe(task.Job, task.Start.Season);
                var tracked = TrackedFor(task.Holder);
                _clock.Cancel(task.Completion);

                if (recipe.Inputs.Count > 0)
                {
                    tracked.Group.SharedSupplies.CancelRecipe(recipe);
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

                    TryStart(tracked, member);
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

            // Gathering has no inputs in process, so completing is a Gather;
            // the ledger tallies it as such.
            tracked.Group.SharedSupplies.CompleteRecipe(JobTable.Recipe(task.Job, task.Start.Season));
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
            var job = ChooseJob(tracked, now);

            if (job == JobKind.None)
            {
                return false;
            }

            var site = SiteOf(tracked, job);
            var recipe = JobTable.Recipe(job, now.Season);
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
            tracked.OnDuty[(int)job]++;

            // Section 12: reveal is computed from the scheduled movement, at
            // the moment it is scheduled, so a stepped agent walking the same
            // route reveals the same cells. The way home is the way out
            // reversed, so one walk of the route covers the round trip.
            _knownMaps.RevealAlong(tracked.Group.Id, site.Route, RevealRadius);
            return true;
        }

        // The first job in priority order that is needed, has a site, and
        // whose task would end by dusk. Need counts what is on its way home:
        // everyone with a task is about to deliver its output.
        private JobKind ChooseJob(Tracked tracked, SimulationTime now)
        {
            var ticksUntilDusk = TicksUntilDusk(now);
            var season = now.Season;
            var winterDays = WinterDaysAhead(now);

            // At least one: the dawn pass counts before anything starts, and
            // a pick follows either that pass - which only offers a living
            // member - or a completion, which only a worker started at that
            // pass can reach. So the daily draw in Needed is never zero.
            var living = tracked.Living;

            for (var i = 0; i < Priority.Count; i++)
            {
                var job = Priority[i];
                var site = SiteOf(tracked, job);

                if (!site.Reachable)
                {
                    continue;
                }

                if (TripTicks(site, JobTable.Recipe(job, season)) > ticksUntilDusk)
                {
                    continue;
                }

                if (Needed(tracked, job, tracked.Group.SharedSupplies, living, winterDays))
                {
                    return job;
                }
            }

            return JobKind.None;
        }

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

        // Living is at least one wherever this is reached (see ChooseJob), so
        // the daily draw is never zero.
        private bool Needed(Tracked tracked, JobKind job, ResourceLedger ledger, int living, long winterDays)
        {
            switch (job)
            {
                case JobKind.Forager:
                    var dailyDraw = (long)Hunger.DailyRation * living;
                    return Expected(tracked, ledger, ResourceKind.Food) / dailyDraw < FoodTargetDays + winterDays;
                case JobKind.Woodcutter:
                    var winterFuel = (long)tracked.Hearths * Warmth.FuelPerFire * winterDays;
                    return Expected(tracked, ledger, ResourceKind.Wood) < WoodCap + winterFuel;
                case JobKind.StoneGatherer:
                    return Expected(tracked, ledger, ResourceKind.Stone) < StoneCap;
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
        private void FindSite(WorldPosition from, ReadOnlySpan<bool> known, JobKind job, Site site)
        {
            site.Reachable = _pathfinder.TryFindNearest(
                from, Mover, JobTable.Terrain(job), known, MaxSiteRadius, site.Route, out var cost);

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
