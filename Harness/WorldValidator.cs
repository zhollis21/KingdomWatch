using System;
using System.Collections.Generic;
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
    /// Checks are separate methods rather than one <c>Validate(world)</c>
    /// because there is no world type to pass: the systems are wired per
    /// caller (#17 is where that changes). A caller runs the checks its world
    /// has.
    /// </remarks>
    public sealed class WorldValidator
    {
        // A kinship walk that has gone this far up has found a cycle or a
        // genealogy nobody meant to build; either way it is a finding, and an
        // unbounded walk on corrupt data does not return.
        private const int MaxAncestorDepth = 512;

        // Built once rather than per call. EnumGuard's mask - what
        // ResourceLedger and WorldHash index over - is internal to Core, and
        // this runs only on the desktop, so reflection ordering cannot reach
        // the cross-platform comparison from here.
        private static readonly ResourceKind[] Kinds = DefinedKinds();

        private readonly List<ValidationFinding> _findings = new List<ValidationFinding>();
        private readonly List<ScheduledEvent> _pending = new List<ScheduledEvent>();
        private readonly HashSet<EventId> _queued = new HashSet<EventId>();
        private readonly HashSet<EntityId> _seen = new HashSet<EntityId>();
        private readonly HashSet<EntityId> _ancestors = new HashSet<EntityId>();
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
                else if (settings is object && Enum.IsDefined(typeof(AgeStage), record.AgeStage))
                {
                    // Aging refreshes the stage at boundaries only, so a
                    // worldgen that seeded the wrong one stays wrong until the
                    // next birthday rather than being corrected (#11).
                    var expected = settings.StageAt(people.GetAgeYears(record.Handle, now));

                    if (expected != record.AgeStage)
                    {
                        Add(ValidationRule.AgeStageStale, now, record.Id,
                            "is " + record.AgeStage + " at age "
                            + people.GetAgeYears(record.Handle, now) + ", which is " + expected + ".");
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

                    if (people.GetHousehold(member) != household.Id)
                    {
                        Add(ValidationRule.HouseholdMembership, now, household.Id,
                            "lists " + id + ", who names " + people.GetHousehold(member) + ".");
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
        /// Every living person is recorded, parent links point at people, and
        /// no one is their own ancestor.
        /// </summary>
        public WorldValidator CheckGenealogy(Genealogy genealogy, PersonStore people, SimulationClock clock)
        {
            Require(genealogy, nameof(genealogy));
            Require(people, nameof(people));
            Require(clock, nameof(clock));

            var now = clock.Now;

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
                CheckAncestry(genealogy, id, now);
            }

            return this;
        }

        /// <summary>
        /// Every event still due names something that resolves, and every
        /// booked id a record names is still in the queue.
        /// </summary>
        /// <remarks>
        /// The second half is the rule #80 exists because of: a stream that
        /// rebooks from inside its own handler and does not record what it
        /// booked leaves the record and the queue free to disagree, and
        /// nothing else notices.
        /// </remarks>
        public WorldValidator CheckSchedule(SimulationClock clock, PersonStore people)
        {
            Require(clock, nameof(clock));
            Require(people, nameof(people));

            var now = clock.Now;

            clock.CopyPendingTo(_pending);
            _queued.Clear();

            for (var i = 0; i < _pending.Count; i++)
            {
                var scheduled = _pending[i];

                _queued.Add(scheduled.Id);

                CheckTarget(scheduled, people, now);
            }

            foreach (var person in people.Alive())
            {
                var id = people.GetId(person);

                CheckBooked(people.GetPregnancyDue(person), id, "pregnancy", now);
                CheckBooked(people.GetPendingMortalityCheck(person), id, "mortality check", now);
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
                var mirrored = people.GetJob(person) != JobKind.None;
                var working = jobs.HasTask(person);

                if (mirrored != working)
                {
                    Add(ValidationRule.JobMirrorStale, now, people.GetId(person),
                        working
                            ? "is on a task with no job recorded."
                            : "records job " + people.GetJob(person) + " with no task.");
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
        /// band has work days, meals, birth checks and courtship all booked
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

            var report = "seed " + seed + ": " + _findings.Count + " finding(s)";

            for (var i = 0; i < _findings.Count; i++)
            {
                report += Environment.NewLine + "  " + _findings[i];
            }

            return report;
        }

        private void CheckParent(EntityId parent, EntityId child, SimulationTime now)
        {
            if (!parent.IsNone && parent.Kind != EntityKind.Person)
            {
                Add(ValidationRule.ParentInvalid, now, child,
                    "names " + parent + " as a parent, which is not a person.");
            }
        }

        private void CheckAncestry(Genealogy genealogy, EntityId person, SimulationTime now)
        {
            _ancestors.Clear();
            _ancestors.Add(person);

            // Breadth would need a queue and an allocation per person; depth
            // up the maternal line finds a self-reference just as well, and a
            // cycle through a father is caught when that father is walked.
            var walker = person;

            for (var depth = 0; depth < MaxAncestorDepth; depth++)
            {
                var mother = genealogy.Parents(walker).Mother;

                if (mother.IsNone)
                {
                    return;
                }

                if (!_ancestors.Add(mother))
                {
                    Add(ValidationRule.KinshipCycle, now, person,
                        "reaches " + mother + " twice walking up the maternal line.");
                    return;
                }

                walker = mother;
            }

            Add(ValidationRule.KinshipCycle, now, person,
                "has more than " + MaxAncestorDepth + " generations of mothers.");
        }

        // Section 5's rule ends "unless it explicitly targets a durable
        // historical entity", and SecondaryEntity is where those live: a
        // BirthDue names the mother and the father, and Fertility.GiveBirth
        // passes the father straight to Genealogy.Record, which is the whole
        // reason it is carried. A father who dies during the pregnancy is
        // still the child's father, so checking the second party for liveness
        // reports correct worlds as broken. Only the primary - "who this is
        // mainly about", the entity the handler acts on - has to resolve.
        private void CheckTarget(ScheduledEvent scheduled, PersonStore people, SimulationTime now)
        {
            var target = scheduled.PrimaryEntity;

            if (target.IsNone || target.Kind != EntityKind.Person)
            {
                return;
            }

            if (!people.TryGetHandle(target, out _))
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
