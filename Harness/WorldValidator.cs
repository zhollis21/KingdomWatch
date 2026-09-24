using System;
using System.Collections.Generic;
using System.Text;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// Section 5's invariant checks, run against a world between advances.
    /// Reports every rule it can reach rather than throwing on the first, so
    /// one pass says everything that is wrong.
    /// </summary>
    /// <remarks>
    /// **Here rather than in Core** because section 5 scopes it to harness
    /// runs, and Core's public surface is the simulation's API rather than a
    /// place for diagnostics. <c>Core.Tests</c> references this project, so
    /// the tests reach it the same way they reach
    /// <see cref="DrawCollisionDetector"/>.
    ///
    /// **It reads; it never writes.** Same contract as
    /// <see cref="Core.Rng.IRandomDrawObserver"/> and for the same reason: the
    /// validator runs in the harness and not on device, so anything it changed
    /// would land on one side of the cross-platform comparison and not the
    /// other. Unlike the observer it does not run inside the tick loop, which
    /// makes the hazard smaller but not different in kind.
    ///
    /// **Not every rule in section 5 is here yet.** The ones needing systems
    /// nobody has built - polities and rulers, reservation ownership - belong
    /// with the issues that introduce them (#39, #24), and a rule that cannot
    /// fail is worse than an absent one because it reads as coverage.
    ///
    /// Checks are separate methods rather than one <c>Validate(world)</c>:
    /// <see cref="Core.World"/> (#17) is one world, but the test fixtures
    /// still wire their own with fewer systems in them. A caller runs the
    /// checks its world has; <see cref="WorldRun"/> runs all of them.
    /// </remarks>
    public sealed class WorldValidator
    {
        // A line of descent deeper than this is a genealogy nobody meant to
        // build: 512 generations is some eight thousand years of sixteen-year
        // generations. A depth, not a count of ancestors: the seen set already
        // ends every walk, and a count grows with the population - #17's seed
        // 1 passed 512 distinct forebears in year 267 with no cycle anywhere.
        private const int MaxGenerations = 512;

        // Built once rather than per call. EnumGuard's mask - what
        // ResourceLedger and WorldHash index over - is internal to Core, and
        // this runs only on the desktop, so reflection ordering cannot reach
        // the cross-platform comparison from here.
        private static readonly ResourceKind[] Kinds = DefinedKinds();

        private readonly List<ValidationFinding> _findings = new List<ValidationFinding>();
        private readonly List<ScheduledEvent> _pending = new List<ScheduledEvent>();
        private readonly HashSet<EventId> _queued = new HashSet<EventId>();
        private readonly HashSet<EntityId> _seen = new HashSet<EntityId>();
        private readonly HashSet<EntityId> _known = new HashSet<EntityId>();
        // The genealogy pass: each person's deepest line of descent once it is
        // known, who is on the line being walked right now, the walk itself,
        // and the missing parents already reported.
        private readonly Dictionary<EntityId, int> _generations = new Dictionary<EntityId, int>();
        private readonly HashSet<EntityId> _onLine = new HashSet<EntityId>();
        private readonly Stack<(EntityId Person, bool Expanded)> _walk = new Stack<(EntityId Person, bool Expanded)>();
        private readonly HashSet<EntityId> _missingParents = new HashSet<EntityId>();
        private readonly Dictionary<EntityId, EntityId> _placed = new Dictionary<EntityId, EntityId>();
        private readonly List<PersonHandle> _workers = new List<PersonHandle>();

        /// <summary>Everything found since the last <see cref="Reset"/>.</summary>
        public IReadOnlyList<ValidationFinding> Findings => _findings;

        /// <summary>Whether the last pass found nothing.</summary>
        public bool IsClean => _findings.Count == 0;

        /// <summary>Clears the findings. A reused validator must be reset first.</summary>
        public WorldValidator Reset()
        {
            _findings.Clear();
            return this;
        }

        /// <summary>
        /// Storage integrity and the per-record rules: identity round-trips,
        /// defined enum values, and the cached age stage agreeing with the age.
        /// </summary>
        /// <remarks>
        /// <see cref="PersonStore.RecordSpan"/> hands out records by reference
        /// so tight loops stay allocation-free, which means a caller can write
        /// identity fields without going through <c>Add</c>/<c>Remove</c>.
        /// The store's remarks forbid it and nothing enforces it, so these are
        /// the rules that catch it having happened.
        /// </remarks>
        public WorldValidator CheckPeople(PersonStore people, SimulationClock clock, DemographicSettings settings)
        {
            Require(people, nameof(people));
            Require(clock, nameof(clock));
            Require(settings, nameof(settings));

            var now = clock.Now;
            var records = people.RecordSpan();
            var occupied = 0;

            _seen.Clear();

            for (var slot = 0; slot < records.Length; slot++)
            {
                var record = records[slot];

                if (record.Id.IsNone)
                {
                    continue;
                }

                occupied++;

                if (record.Handle.Index != slot)
                {
                    Add(ValidationRule.PersonHandleSlot, now, record.Id,
                        record.Handle + " sits in slot " + slot + ".");
                    continue;
                }

                if (!people.IsAlive(record.Handle) || people.GetId(record.Handle) != record.Id)
                {
                    Add(ValidationRule.PersonHandleSlot, now, record.Id,
                        record.Handle + " does not resolve to the record holding it.");
                    continue;
                }

                if (!_seen.Add(record.Id))
                {
                    Add(ValidationRule.DuplicateEntityId, now, record.Id, "held by more than one slot.");
                }

                if (!people.TryGetHandle(record.Id, out var back) || back != record.Handle)
                {
                    Add(ValidationRule.PersonIdRoundTrip, now, record.Id,
                        "does not resolve back to " + record.Handle + ".");
                }

                if (record.Id.Kind != EntityKind.Person)
                {
                    Add(ValidationRule.PersonFieldUndefined, now, record.Id,
                        "is filed as " + record.Id.Kind + " in the person store.");
                }

                if (record.AgeStage == AgeStage.None || !Enum.IsDefined(typeof(AgeStage), record.AgeStage))
                {
                    Add(ValidationRule.PersonFieldUndefined, now, record.Id,
                        "has age stage " + (int)record.AgeStage + ".");
                }

                if (record.Sex == Sex.None || !Enum.IsDefined(typeof(Sex), record.Sex))
                {
                    Add(ValidationRule.PersonFieldUndefined, now, record.Id,
                        "has sex " + (int)record.Sex + ".");
                }

                if (!Enum.IsDefined(typeof(JobKind), record.Job))
                {
                    Add(ValidationRule.PersonFieldUndefined, now, record.Id,
                        "has job " + (int)record.Job + ".");
                }

                if (record.BornTick > now.Ticks)
                {
                    Add(ValidationRule.BornInFuture, now, record.Id,
                        "was born at tick " + record.BornTick + ", which is ahead of now.");
                }
                else if (Enum.IsDefined(typeof(AgeStage), record.AgeStage))
                {
                    // Aging refreshes the stage at boundaries only, so a
                    // worldgen that seeded the wrong one stays wrong until the
                    // next birthday rather than being corrected (#11).
                    var age = people.GetAgeYears(record.Handle, now);
                    var expected = settings.StageAt(age);

                    if (expected != record.AgeStage)
                    {
                        Add(ValidationRule.AgeStageStale, now, record.Id,
                            "is " + record.AgeStage + " at age " + age + ", which is " + expected + ".");
                    }
                }
            }

            if (people.Count != occupied)
            {
                Add(ValidationRule.PersonCount, now, EntityId.None,
                    "Count is " + people.Count + " over " + occupied + " occupied slots.");
            }

            return this;
        }

        /// <summary>
        /// Household membership agrees in both directions, and every listed
        /// member is someone the store still holds.
        /// </summary>
        public WorldValidator CheckHouseholds(Households households, PersonStore people, SimulationClock clock)
        {
            Require(households, nameof(households));
            Require(people, nameof(people));
            Require(clock, nameof(clock));

            var now = clock.Now;

            for (var i = 0; i < households.All.Count; i++)
            {
                var household = households.All[i];

                _seen.Clear();

                for (var m = 0; m < household.Members.Count; m++)
                {
                    var member = household.Members[m];

                    if (!people.IsAlive(member))
                    {
                        Add(ValidationRule.HouseholdMemberMissing, now, household.Id,
                            "lists " + member + ", who is not in the store.");
                        continue;
                    }

                    var id = people.GetId(member);

                    if (!_seen.Add(id))
                    {
                        Add(ValidationRule.HouseholdMembership, now, household.Id,
                            "lists " + id + " more than once.");
                    }

                    var named = people.GetHousehold(member);

                    if (named != household.Id)
                    {
                        Add(ValidationRule.HouseholdMembership, now, household.Id,
                            "lists " + id + ", who names " + named + ".");
                    }
                }
            }

            foreach (var person in people.Alive())
            {
                var named = people.GetHousehold(person);

                if (named.IsNone)
                {
                    continue;
                }

                if (!households.TryGet(named, out var household))
                {
                    Add(ValidationRule.HouseholdMembership, now, people.GetId(person),
                        "names household " + named + ", which the registry does not hold.");
                    continue;
                }

                if (!Lists(household.Members, person))
                {
                    Add(ValidationRule.HouseholdMembership, now, people.GetId(person),
                        "names " + named + ", whose member list leaves them out.");
                }
            }

            return this;
        }

        /// <summary>
        /// Every living person is recorded, parent links point at people who
        /// are themselves recorded, and no one is their own ancestor by any
        /// line of descent.
        /// </summary>
        public WorldValidator CheckGenealogy(Genealogy genealogy, PersonStore people, SimulationClock clock)
        {
            Require(genealogy, nameof(genealogy));
            Require(people, nameof(people));
            Require(clock, nameof(clock));

            var now = clock.Now;
            _generations.Clear();
            _onLine.Clear();
            _walk.Clear();
            _missingParents.Clear();

            foreach (var person in people.Alive())
            {
                var id = people.GetId(person);

                if (!genealogy.IsRecorded(id))
                {
                    // FamilyFormation and Deaths both assume the record is
                    // there; a living person without one is a hole that only
                    // shows up when someone marries or dies (#70).
                    Add(ValidationRule.GenealogyMissing, now, id, "has no genealogy record.");
                    continue;
                }

                var parents = genealogy.Parents(id);

                CheckParent(parents.Mother, id, now);
                CheckParent(parents.Father, id, now);

                if (GenerationsAbove(genealogy, id, now) > MaxGenerations)
                {
                    Add(ValidationRule.KinshipCycle, now, id,
                        "has a line of descent more than " + MaxGenerations + " generations deep.");
                }
            }

            return this;
        }

        /// <summary>
        /// Every event still due names a primary entity that resolves - a
        /// person, a community or a household - and every booked id a
        /// <see cref="PersonRecord"/> names is still in the queue.
        /// </summary>
        /// <remarks>
        /// The second half is the rule #80 exists because of: a stream that
        /// rebooks from inside its own handler and does not record what it
        /// booked leaves the record and the queue free to disagree, and
        /// nothing else notices.
        /// </remarks>
        /// <param name="communities">
        /// Every band and settlement in the world. Most scheduled kinds are
        /// owned by a community rather than by a person - work days, meals,
        /// evening fires, courtship rounds, councils, arrivals - so without
        /// these the majority of the queue's targets go unexamined.
        /// </param>
        /// <param name="households">The registry a <c>BirthCheck</c> is booked against.</param>
        public WorldValidator CheckSchedule(
            SimulationClock clock,
            PersonStore people,
            IReadOnlyList<ICommunity> communities,
            Households households)
        {
            Require(clock, nameof(clock));
            Require(people, nameof(people));
            Require(communities, nameof(communities));
            Require(households, nameof(households));

            var now = clock.Now;

            RefreshQueued(clock);

            _known.Clear();

            for (var i = 0; i < communities.Count; i++)
            {
                _known.Add(communities[i].Id);
            }

            for (var i = 0; i < _pending.Count; i++)
            {
                CheckTarget(_pending[i], people, households, now);
            }

            foreach (var person in people.Alive())
            {
                var id = people.GetId(person);

                CheckBooked(people.GetPregnancyDue(person), id, "pregnancy", now);
                CheckBooked(people.GetPendingMortalityCheck(person), id, "mortality check", now);
                CheckBooked(people.GetPendingAgeStage(person), id, "stage boundary", now);
            }

            return this;
        }

        /// <summary>
        /// Every booking a system is holding names an event the queue still
        /// has.
        /// </summary>
        /// <remarks>
        /// <see cref="CheckSchedule"/> asks this of the three ids a
        /// <see cref="PersonRecord"/> carries. The periodic streams keep the
        /// same kind of record for the communities they run for - a work day,
        /// a meal, an evening's fires, a courtship round, a council, an
        /// arrival, a birth check -
        /// and those are what this covers. A booking the queue has forgotten
        /// is a stream that has silently stopped; the owner is waiting for a
        /// wake-up that is never coming, and nothing else says so.
        /// </remarks>
        public WorldValidator CheckBookings(IReadOnlyList<PendingBooking> bookings, SimulationClock clock)
        {
            Require(bookings, nameof(bookings));
            Require(clock, nameof(clock));

            RefreshQueued(clock);

            for (var i = 0; i < bookings.Count; i++)
            {
                if (!_queued.Contains(bookings[i].Booked))
                {
                    Add(ValidationRule.PendingEventMissing, clock.Now, bookings[i].Owner,
                        "names " + bookings[i].Booked + " as its next " + bookings[i].Kind
                        + ", which the queue does not hold.");
                }
            }

            return this;
        }

        /// <summary>
        /// No task outlives its worker, and the job mirror on a record agrees
        /// with whether that person is on a task.
        /// </summary>
        /// <remarks>
        /// The first rule cannot be asked of <see cref="PersonStore"/>: the
        /// death cascade removes the record, so a task left behind belongs to
        /// someone who can no longer be enumerated. It is read from
        /// <see cref="Jobs.CopyTaskWorkersTo"/> instead.
        /// </remarks>
        public WorldValidator CheckJobs(Jobs jobs, PersonStore people, SimulationClock clock)
        {
            Require(jobs, nameof(jobs));
            Require(people, nameof(people));
            Require(clock, nameof(clock));

            var now = clock.Now;

            jobs.CopyTaskWorkersTo(_workers);

            for (var i = 0; i < _workers.Count; i++)
            {
                if (!people.IsAlive(_workers[i]))
                {
                    Add(ValidationRule.TaskOutlivedWorker, now, EntityId.None,
                        _workers[i] + " holds a work task and is not in the store.");
                }
            }

            foreach (var person in people.Alive())
            {
                // Section 5: Jobs alone writes the field, exactly while the
                // person has a task, and nothing in Jobs reads it back - it is
                // a mirror for readers and for this check.
                var job = people.GetJob(person);
                var mirrored = job != JobKind.None;
                var working = jobs.HasTask(person);

                if (mirrored != working)
                {
                    Add(ValidationRule.JobMirrorStale, now, people.GetId(person),
                        working
                            ? "is on a task with no job recorded."
                            : "records job " + job + " with no task.");
                }
            }

            return this;
        }

        /// <summary>
        /// Every listed member is someone the store still holds, and nobody
        /// stands in two communities at once.
        /// </summary>
        /// <remarks>
        /// Call once with every spatial container in the world - settlements
        /// and wandering bands together - since the double-membership rule is
        /// only meaningful across the whole set.
        /// </remarks>
        public WorldValidator CheckCommunities(
            IReadOnlyList<ICommunity> communities, PersonStore people, SimulationClock clock)
        {
            Require(communities, nameof(communities));
            Require(people, nameof(people));
            Require(clock, nameof(clock));

            var now = clock.Now;

            _placed.Clear();

            for (var i = 0; i < communities.Count; i++)
            {
                var community = communities[i];

                for (var m = 0; m < community.Members.Count; m++)
                {
                    var member = community.Members[m];

                    if (!people.IsAlive(member))
                    {
                        Add(ValidationRule.CommunityMemberMissing, now, community.Id,
                            "lists " + member + ", who is not in the store.");
                        continue;
                    }

                    var id = people.GetId(member);

                    if (_placed.TryGetValue(id, out var already))
                    {
                        Add(ValidationRule.DoubleMembership, now, id,
                            "is in " + already + " and " + community.Id + ".");
                    }
                    else
                    {
                        _placed.Add(id, community.Id);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Every community a system has booked events for still holds people
        /// the store holds.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="CheckCommunities"/>, which asks where
        /// people physically are and so refuses to see anyone twice. A
        /// community is legitimately tracked by several systems at once - a
        /// band has work days, meals, fires, birth checks and courtship all booked
        /// against it - so this rule is only about the members resolving, and
        /// is called once per system with that system's own tracked set.
        /// </remarks>
        public WorldValidator CheckTracked(
            IReadOnlyList<ICommunity> tracked, PersonStore people, SimulationClock clock)
        {
            Require(tracked, nameof(tracked));
            Require(people, nameof(people));
            Require(clock, nameof(clock));

            var now = clock.Now;

            for (var i = 0; i < tracked.Count; i++)
            {
                var community = tracked[i];

                for (var m = 0; m < community.Members.Count; m++)
                {
                    if (!people.IsAlive(community.Members[m]))
                    {
                        Add(ValidationRule.CommunityMemberMissing, now, community.Id,
                            "is tracked and lists " + community.Members[m] + ", who is not in the store.");
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// No stock is negative and the flows account for what is on hand.
        /// </summary>
        public WorldValidator CheckSupplies(ResourceLedger supplies, EntityId owner, SimulationClock clock)
        {
            Require(supplies, nameof(supplies));
            Require(clock, nameof(clock));

            var now = clock.Now;

            foreach (var kind in Kinds)
            {
                if (supplies.Available(kind) < 0 || supplies.Reserved(kind) < 0
                    || supplies.Carried(kind) < 0 || supplies.InProcess(kind) < 0)
                {
                    Add(ValidationRule.NegativeResource, now, owner,
                        kind + " is negative: available " + supplies.Available(kind)
                        + ", reserved " + supplies.Reserved(kind)
                        + ", carried " + supplies.Carried(kind)
                        + ", in process " + supplies.InProcess(kind) + ".");
                }

                if (!supplies.AuditBalances(kind))
                {
                    Add(ValidationRule.ConservationBroken, now, owner,
                        kind + " does not balance against its flows.");
                }
            }

            return this;
        }

        /// <summary>A one-line report of everything found, for a sweep's output.</summary>
        public string Report(ulong seed)
        {
            if (IsClean)
            {
                return "seed " + seed + ": clean";
            }

            var report = new StringBuilder("seed ")
                .Append(seed)
                .Append(": ")
                .Append(_findings.Count)
                .Append(" finding(s)");

            for (var i = 0; i < _findings.Count; i++)
            {
                report.Append(Environment.NewLine).Append("  ").Append(_findings[i]);
            }

            return report.ToString();
        }

        // One snapshot of the queue per check that needs it. Cancellation is
        // lazy, so "is this id still live" cannot be answered by looking for
        // the entry - only by asking the clock, which applies the same test
        // dispatch does.
        private void RefreshQueued(SimulationClock clock)
        {
            clock.CopyPendingTo(_pending);
            _queued.Clear();

            for (var i = 0; i < _pending.Count; i++)
            {
                _queued.Add(_pending[i].Id);
            }
        }

        private void CheckParent(EntityId parent, EntityId child, SimulationTime now)
        {
            if (!parent.IsNone && parent.Kind != EntityKind.Person)
            {
                Add(ValidationRule.ParentInvalid, now, child,
                    "names " + parent + " as a parent, which is not a person.");
            }
        }

        // The deepest line of descent above a person, in generations, and
        // every cycle and missing record found on the way - one pass over the
        // whole genealogy per check, however many living people share an
        // ancestor, because each person's depth is remembered once found.
        //
        // Both parents, not one line. Walking mothers alone misses any cycle
        // that uses a father edge: if a's father is b and b's mother is a,
        // then walking a stops at a's own mother and walking b reaches a and
        // stops there - neither ever closes the loop. The rule says nobody is
        // their own ancestor, so it has to follow every edge that makes
        // somebody an ancestor.
        //
        // Depth is the deepest line, not the first one found: a person is
        // finished only once both parents are, and takes the greater of them
        // plus one. Remembering the first depth an ancestor was reached at
        // instead hid a deep line behind a shallow one (the #103 review).
        // Cycles are found among the dead too, since the walk climbs every
        // recorded ancestor rather than stopping at the living.
        //
        // Iterative, with the walk and the sets as fields reused across
        // checks: a genealogy thousands of generations deep must not blow the
        // call stack, and a check per simulated year allocates only when a
        // set outgrows the largest genealogy it has held.
        private int GenerationsAbove(Genealogy genealogy, EntityId start, SimulationTime now)
        {
            if (_generations.TryGetValue(start, out var known))
            {
                return known;
            }

            _walk.Push((start, false));

            while (_walk.Count > 0)
            {
                var (person, expanded) = _walk.Pop();
                var parents = genealogy.Parents(person);

                if (expanded)
                {
                    // Both parents have been walked, since they were pushed
                    // after this entry and so came off before it.
                    _onLine.Remove(person);
                    _generations[person] = Math.Max(Above(parents.Mother), Above(parents.Father));
                    continue;
                }

                // Pushed twice, by two children, and finished in between.
                if (_generations.ContainsKey(person))
                {
                    continue;
                }

                _walk.Push((person, true));
                _onLine.Add(person);
                Climb(genealogy, parents.Mother, person, now);
                Climb(genealogy, parents.Father, person, now);
            }

            return _generations[start];
        }

        // Queues a parent to be walked, unless there is nothing to walk:
        // no parent, a parent with no record (reported once), a parent already
        // finished, or one on the line being walked - which is a cycle.
        private void Climb(Genealogy genealogy, EntityId parent, EntityId child, SimulationTime now)
        {
            if (parent.IsNone)
            {
                return;
            }

            // Genealogy.Parents throws for somebody it has no record of, and
            // a validator that crashes on corrupt data reports nothing about
            // it. Record.RequireRecordedParent makes this unreachable through
            // the front door; a world rebuilt from a save (#42) has no such
            // door, and a parent link into nothing is exactly what this should
            // be able to say out loud.
            if (!genealogy.IsRecorded(parent))
            {
                if (_missingParents.Add(parent))
                {
                    Add(ValidationRule.GenealogyMissing, now, parent,
                        "is named as a parent of " + child + " and has no record of their own.");
                }

                return;
            }

            if (_onLine.Contains(parent))
            {
                Add(ValidationRule.KinshipCycle, now, parent, "is their own ancestor.");
                return;
            }

            if (!_generations.ContainsKey(parent))
            {
                _walk.Push((parent, false));
            }
        }

        // A parent's contribution to a finished child's depth: none for no
        // parent, a missing record, or the edge that closes a cycle - none of
        // which has a depth of its own - and one more than the parent's
        // deepest line otherwise.
        private int Above(EntityId parent) =>
            !parent.IsNone && _generations.TryGetValue(parent, out var generations) ? generations + 1 : 0;

        // Section 5's rule ends "unless it explicitly targets a durable
        // historical entity", and SecondaryEntity is where those live: a
        // BirthDue names the mother and the father, and Fertility.GiveBirth
        // passes the father straight to Genealogy.Record, which is the whole
        // reason it is carried. A father who dies during the pregnancy is
        // still the child's father, so checking the second party for liveness
        // reports correct worlds as broken. Only the primary - "who this is
        // mainly about", the entity the handler acts on - has to resolve.
        private void CheckTarget(
            ScheduledEvent scheduled, PersonStore people, Households households, SimulationTime now)
        {
            var target = scheduled.PrimaryEntity;

            if (target.IsNone)
            {
                return;
            }

            bool resolves;

            switch (target.Kind)
            {
                case EntityKind.Person:
                    resolves = people.TryGetHandle(target, out _);
                    break;
                case EntityKind.Household:
                    resolves = households.TryGet(target, out _);
                    break;
                case EntityKind.MobileGroup:
                case EntityKind.Settlement:
                    resolves = _known.Contains(target);
                    break;
                default:
                    // Polities, dynasties and named animals exist in section 5
                    // and in EntityKind, and in nothing that schedules yet
                    // (#39, #45). A kind that cannot be booked against cannot
                    // dangle, and a rule that cannot fire is worse than an
                    // absent one - it reads as coverage. This arm becomes real
                    // when the first of them books an event.
                    return;
            }

            if (!resolves)
            {
                Add(ValidationRule.ScheduledTargetMissing, now, target,
                    scheduled + " is still due and names them.");
            }
        }

        private void CheckBooked(EventId booked, EntityId person, string what, SimulationTime now)
        {
            if (!booked.IsNone && !_queued.Contains(booked))
            {
                Add(ValidationRule.PendingEventMissing, now, person,
                    "names " + booked + " as their " + what + ", which the queue does not hold.");
            }
        }

        private void Add(ValidationRule rule, SimulationTime at, EntityId subject, string detail) =>
            _findings.Add(new ValidationFinding(rule, at, subject, detail));

        private static ResourceKind[] DefinedKinds()
        {
            var all = (ResourceKind[])Enum.GetValues(typeof(ResourceKind));
            var kinds = new List<ResourceKind>(all.Length);

            foreach (var kind in all)
            {
                if (kind != ResourceKind.None)
                {
                    kinds.Add(kind);
                }
            }

            return kinds.ToArray();
        }

        private static bool Lists(IReadOnlyList<PersonHandle> members, PersonHandle person)
        {
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i] == person)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Require(object? value, string name)
        {
            if (value is null)
            {
                throw new ArgumentNullException(name);
            }
        }
    }
}
