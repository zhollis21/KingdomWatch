using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.History;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Nomadic;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Settlements;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.Validation
{
    /// <summary>
    /// Section 5's canonical world-state hash: sort by durable id, serialize
    /// deterministic fields in a defined order, hash that. Built section by
    /// section, so a caller hashes the systems its world actually has.
    /// </summary>
    /// <remarks>
    /// **Canonical, not a hash of memory or layout.** The whole point is that
    /// desktop .NET and Android IL2CPP reach the same number from the same
    /// seed, and they will not agree on slot order, array capacity, dictionary
    /// iteration or object addresses even when the logical world is identical.
    /// So nothing here reads a container's own order where that order is only
    /// storage: people and households are sorted by <see cref="EntityId"/>,
    /// pending events by
    /// <see cref="ScheduledEvent.CompareTo"/>, and the fields of each record
    /// are folded in a fixed order written out longhand rather than reflected
    /// over.
    ///
    /// **Storage handles are deliberately not hashed.** A
    /// <see cref="PersonHandle"/> is a slot index and a generation - the
    /// representation section 5 warns about, not the logical world. A
    /// household's members are hashed as the durable ids they resolve to, in
    /// the order the household lists them - that order is behaviour, not
    /// representation (see <c>MixMembers</c>).
    ///
    /// **Integer arithmetic only.** Core contains no <c>float</c>,
    /// <c>double</c> or <c>decimal</c>, which is what makes this tractable at
    /// all; the usual cross-platform divergence is floating point. If a
    /// floating-point field ever lands in simulation state, hashing its bits
    /// is not enough - the value itself has to be pinned first.
    ///
    /// **The mixer is <see cref="SplitMix64"/>, not a BCL digest.** A hash
    /// whose job is to prove two runtimes agree should not have a third
    /// implementation sitting between them. SplitMix64 is a dozen lines of
    /// integer arithmetic compiled from the same source into both builds, and
    /// the folding idiom is the one <see cref="RandomKey.Mix(ulong)"/> already
    /// uses. It is not a cryptographic digest and is not meant to be: this
    /// detects divergence, it does not resist an adversary.
    ///
    /// **Sections are order-sensitive and tagged.** Each <c>Add</c> folds in
    /// its own <see cref="Section"/> tag and count before any content, so
    /// adding no settlements and adding no settlements *section* are different
    /// hashes, and two callers that cover different ground cannot collide by
    /// accident. Both sides of a comparison must add the same sections in the
    /// same order.
    ///
    /// **Anything new belongs here.** A system whose state is not folded in is
    /// a system whose divergence this will not catch, silently. See
    /// <c>AGENTS.md</c>.
    /// </remarks>
    public sealed class WorldHash
    {
        // Appended to, never renumbered: a tag's value is part of every hash
        // ever produced with it. Same rule as RandomDomain and RandomSite.
        private enum Section
        {
            People = 1,
            Households = 2,
            Settlements = 3,
            Pending = 4,
            Bookings = 5,
            Bands = 6,
            KnownMaps = 7,
            Terrain = 8,
            Ids = 9,
            Partnerships = 10,
            Genealogy = 11,
            Memories = 12,
            Work = 13,
            Councils = 14,
            Famine = 15,
            Journal = 16,
            Tracked = 17,
        }

        // Kept between calls: a hash taken once per simulated day over a long
        // run would otherwise allocate these on every take.
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(ResourceKind));
        private static readonly bool[] DefinedEntityKinds = EnumGuard.BuildMask(typeof(EntityKind));
        private static readonly bool[] DefinedJobKinds = EnumGuard.BuildMask(typeof(JobKind));

        private readonly List<PersonRecord> _people = new List<PersonRecord>();
        private readonly List<Household> _households = new List<Household>();
        private readonly List<Settlement> _settlements = new List<Settlement>();
        private readonly List<MobileGroup> _bands = new List<MobileGroup>();
        private readonly List<EntityId> _holders = new List<EntityId>();
        private readonly List<ScheduledEvent> _pending = new List<ScheduledEvent>();
        private readonly List<PendingBooking> _bookings = new List<PendingBooking>();
        private readonly List<EntityId> _ids = new List<EntityId>();
        private readonly List<ICommunity> _communities = new List<ICommunity>();
        private readonly List<PersonHandle> _workers = new List<PersonHandle>();
        private readonly List<Worker> _workerIds = new List<Worker>();

        private ulong _state;

        /// <summary>The hash of everything folded in since the last <see cref="Reset"/>.</summary>
        public ulong Value => _state;

        /// <summary>Starts a fresh hash. A reused instance must be reset first.</summary>
        public WorldHash Reset()
        {
            _state = 0UL;
            return this;
        }

        /// <summary>
        /// Folds in every living person, ordered by durable id.
        /// </summary>
        public WorldHash AddPeople(PersonStore people)
        {
            if (people is null)
            {
                throw new ArgumentNullException(nameof(people));
            }

            _people.Clear();
            var records = people.RecordSpan();

            for (var i = 0; i < records.Length; i++)
            {
                if (!records[i].Id.IsNone)
                {
                    _people.Add(records[i]);
                }
            }

            _people.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            Open(Section.People, _people.Count);

            for (var i = 0; i < _people.Count; i++)
            {
                var record = _people[i];

                // Longhand and in a fixed order on purpose. A reflective walk
                // would reorder itself the first time someone adds a field,
                // and silently rewrite every hash the project has recorded.
                Mix(record.Id);
                Mix((long)record.Sex);
                Mix((long)record.AgeStage);
                Mix(record.BornTick);
                Mix(record.Health);
                Mix(record.Position);
                Mix(record.BirthCulture);
                Mix(record.Assimilation);
                Mix(record.LastFedAt.Ticks);
                Mix(record.LastWarmedAt.Ticks);
                Mix((long)record.Job);
                Mix(record.Household);
                Mix(record.PregnancyDue);
                Mix(record.PendingMortalityCheck);
                Mix(record.PendingAgeStage);
            }

            return this;
        }

        /// <summary>
        /// Folds in every household, ordered by durable id, each with its
        /// members as the durable ids they resolve to.
        /// </summary>
        public WorldHash AddHouseholds(Households households, PersonStore people)
        {
            if (households is null)
            {
                throw new ArgumentNullException(nameof(households));
            }

            if (people is null)
            {
                throw new ArgumentNullException(nameof(people));
            }

            _households.Clear();

            for (var i = 0; i < households.All.Count; i++)
            {
                _households.Add(households.All[i]);
            }

            _households.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            Open(Section.Households, _households.Count);

            for (var i = 0; i < _households.Count; i++)
            {
                var household = _households[i];

                Mix(household.Id);
                Mix(household.Home);
                Mix(household.FormedAt.Ticks);
                MixMembers(household.Members, people);
            }

            return this;
        }

        /// <summary>
        /// Folds in every settlement, ordered by durable id, each with its
        /// members and its shared supplies.
        /// </summary>
        public WorldHash AddSettlements(Founding settlements, PersonStore people)
        {
            if (settlements is null)
            {
                throw new ArgumentNullException(nameof(settlements));
            }

            if (people is null)
            {
                throw new ArgumentNullException(nameof(people));
            }

            _settlements.Clear();

            for (var i = 0; i < settlements.All.Count; i++)
            {
                _settlements.Add(settlements.All[i]);
            }

            _settlements.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            Open(Section.Settlements, _settlements.Count);

            for (var i = 0; i < _settlements.Count; i++)
            {
                var settlement = _settlements[i];

                Mix(settlement.Id);
                Mix(settlement.Position);
                MixMembers(settlement.Members, people);
                MixSupplies(settlement.SharedSupplies);
            }

            return this;
        }

        /// <summary>
        /// Folds in every band given, ordered by durable id: where it stands
        /// and is heading, who leads it, its members and its shared supplies.
        /// </summary>
        /// <remarks>
        /// A band is the world's only kind of community until it settles, and
        /// what it carries and where it is going decide its next council and
        /// its next day's work (#17). The caller passes the bands it has -
        /// the ones still wandering - since nothing owns every band ever made.
        /// </remarks>
        public WorldHash AddBands(IReadOnlyList<MobileGroup> bands, PersonStore people)
        {
            if (bands is null)
            {
                throw new ArgumentNullException(nameof(bands));
            }

            if (people is null)
            {
                throw new ArgumentNullException(nameof(people));
            }

            _bands.Clear();

            for (var i = 0; i < bands.Count; i++)
            {
                _bands.Add(bands[i]);
            }

            _bands.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            Open(Section.Bands, _bands.Count);

            for (var i = 0; i < _bands.Count; i++)
            {
                var band = _bands[i];

                Mix(band.Id);
                Mix((long)band.Purpose);
                Mix(band.Position);

                // Heading nowhere and heading to the origin are different.
                Mix(band.Destination.HasValue ? 1 : 0);
                Mix(band.Destination ?? default);

                // The leader as the durable id it resolves to, for the reason
                // members are: a handle is a storage position.
                Mix(people.IsAlive(band.Leader) ? people.GetId(band.Leader) : EntityId.None);
                MixMembers(band.Members, people);
                MixSupplies(band.SharedSupplies);
            }

            return this;
        }

        /// <summary>
        /// Folds in every holder's known map, ordered by durable id: which
        /// cells it has seen, 64 to a word in cell-index order.
        /// </summary>
        /// <remarks>
        /// Known cells decide where a community looks for work and where a
        /// band may settle (#81, #84), so two worlds that saw different land
        /// have diverged even while their people agree.
        /// </remarks>
        public WorldHash AddKnownMaps(KnownMaps maps)
        {
            if (maps is null)
            {
                throw new ArgumentNullException(nameof(maps));
            }

            maps.CopyHoldersTo(_holders);
            Open(Section.KnownMaps, _holders.Count);

            for (var i = 0; i < _holders.Count; i++)
            {
                var known = maps.For(_holders[i]);
                Mix(_holders[i]);
                Mix(known.Length);
                var word = 0UL;

                for (var cell = 0; cell < known.Length; cell++)
                {
                    if (known[cell])
                    {
                        word |= 1UL << (cell & 63);
                    }

                    if ((cell & 63) == 63 || cell == known.Length - 1)
                    {
                        Mix(word);
                        word = 0UL;
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Folds in the map: its size and every cell's terrain, in cell-index
        /// order.
        /// </summary>
        /// <remarks>
        /// Worldgen draws it from the seed, the pathfinder and every site
        /// search read it, and bridges (#35) will rewrite cells mid-run, so a
        /// world that disagrees on one cell has diverged (the #103 review).
        /// </remarks>
        public WorldHash AddTerrain(TerrainGrid grid)
        {
            if (grid is null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            Open(Section.Terrain, grid.CellCount);
            Mix(grid.Width);
            Mix(grid.Height);

            for (var i = 0; i < grid.CellCount; i++)
            {
                Mix((long)grid[grid.PositionAt(i)]);
            }

            return this;
        }

        /// <summary>
        /// Folds in the next id each entity kind and the event stream will
        /// hand out.
        /// </summary>
        /// <remarks>
        /// They belong to no system, so no other section reaches them, and the
        /// next person, household or event is named by them: two worlds one id
        /// apart have diverged before the id is ever seen (the #103 review).
        /// Kinds are walked over the enum's own numbering, as supplies are.
        /// </remarks>
        public WorldHash AddIds(IdAllocator ids)
        {
            if (ids is null)
            {
                throw new ArgumentNullException(nameof(ids));
            }

            Open(Section.Ids, DefinedEntityKinds.Length);

            for (var kind = 1; kind < DefinedEntityKinds.Length; kind++)
            {
                if (DefinedEntityKinds[kind])
                {
                    Mix((long)kind);
                    Mix(ids.PeekNext((EntityKind)kind));
                }
            }

            Mix(ids.PeekNextEvent());
            return this;
        }

        /// <summary>
        /// Folds in every partnership, ended ones included: each person who
        /// has ever been partnered, ordered by durable id, with their history
        /// in the order it is kept.
        /// </summary>
        /// <remarks>
        /// Households hash their members, not who is partnered with whom, and
        /// Fertility and Matchmaking both read the links (#104). A record sits
        /// in both partners' histories, so each is folded in twice - once from
        /// each side, which is how the store holds it.
        /// </remarks>
        public WorldHash AddPartnerships(Partnerships partnerships)
        {
            if (partnerships is null)
            {
                throw new ArgumentNullException(nameof(partnerships));
            }

            partnerships.CopyPartneredTo(_ids);
            Open(Section.Partnerships, _ids.Count);

            for (var i = 0; i < _ids.Count; i++)
            {
                var history = partnerships.History(_ids[i]);
                Mix(_ids[i]);
                Mix(history.Length);

                for (var j = 0; j < history.Length; j++)
                {
                    var record = history[j];

                    Mix(record.First);
                    Mix(record.Second);
                    Mix(record.FormedBy);
                    Mix(record.FormedAt.Ticks);
                    Mix(record.EndedBy);
                    Mix(record.EndedAt.Ticks);
                }
            }

            return this;
        }

        /// <summary>
        /// Folds in the family tree: everyone recorded, the dead included,
        /// ordered by durable id, with their parents and their children in
        /// the order they were recorded.
        /// </summary>
        /// <remarks>
        /// Fertility's postpartum gate and Matchmaking's kinship ban read it,
        /// and it outlives everyone in it, so a world that disagrees about
        /// a dead grandparent has diverged (#104).
        /// </remarks>
        public WorldHash AddGenealogy(Genealogy genealogy)
        {
            if (genealogy is null)
            {
                throw new ArgumentNullException(nameof(genealogy));
            }

            genealogy.CopyRecordedTo(_ids);
            Open(Section.Genealogy, _ids.Count);

            for (var i = 0; i < _ids.Count; i++)
            {
                var parents = genealogy.Parents(_ids[i]);
                var children = genealogy.Children(_ids[i]);

                Mix(_ids[i]);
                Mix(parents.Mother);
                Mix(parents.Father);
                Mix(children.Length);

                for (var j = 0; j < children.Length; j++)
                {
                    Mix(children[j]);
                }
            }

            return this;
        }

        /// <summary>
        /// Folds in every memory: each holder ordered by durable id, with what
        /// it remembers in the order it was recorded, and each memory's
        /// witnesses in the order they were added.
        /// </summary>
        /// <remarks>
        /// Nothing writes a memory in M1, so this is an empty count until
        /// something does - added now so that the first writer is covered
        /// without having to remember (#104).
        /// </remarks>
        public WorldHash AddMemories(Memories memories)
        {
            if (memories is null)
            {
                throw new ArgumentNullException(nameof(memories));
            }

            memories.CopyHoldersTo(_ids);
            Open(Section.Memories, _ids.Count);

            for (var i = 0; i < _ids.Count; i++)
            {
                var held = memories.Held(_ids[i]);
                Mix(_ids[i]);
                Mix(held.Length);

                for (var j = 0; j < held.Length; j++)
                {
                    var memory = held[j];
                    var witnesses = memory.WitnessList;

                    Mix(memory.OriginEvent);
                    Mix(memory.Holder);
                    Mix(memory.Subject);
                    Mix(memory.Valence);
                    Mix((long)memory.Tier);
                    Mix(memory.FormedAt.Ticks);
                    Mix(witnesses.Count);

                    for (var k = 0; k < witnesses.Count; k++)
                    {
                        Mix(witnesses[k]);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Folds in the work in hand: every task under way, ordered by its
        /// worker's durable id, with the route it walks; then every band Jobs
        /// tracks, ordered by durable id, with its dawn counts, who is on each
        /// job and what its last site search found.
        /// </summary>
        /// <remarks>
        /// Only a task's completion reaches the queue, and a task's route and
        /// timings decide where its worker stands (section 4's
        /// reconstruction). The band side is state too: the site search is
        /// refreshed when the band moves, not when its map grows, and the dawn
        /// counts size a whole day's picks (#104).
        /// </remarks>
        public WorldHash AddWork(Jobs jobs, PersonStore people)
        {
            if (jobs is null)
            {
                throw new ArgumentNullException(nameof(jobs));
            }

            if (people is null)
            {
                throw new ArgumentNullException(nameof(people));
            }

            jobs.CopyTaskWorkersTo(_workers);
            _workerIds.Clear();

            for (var i = 0; i < _workers.Count; i++)
            {
                // A worker the store no longer holds is the validator's to
                // report ("no dead person has active tasks"); None keeps the
                // two sides comparable, as it does for members.
                var worker = _workers[i];
                _workerIds.Add(new Worker(people.IsAlive(worker) ? people.GetId(worker) : EntityId.None, worker));
            }

            _workerIds.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            jobs.CopyTrackedTo(_communities);
            _communities.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            Open(Section.Work, _workerIds.Count);
            Mix(_communities.Count);

            for (var i = 0; i < _workerIds.Count; i++)
            {
                var task = jobs.TaskOf(_workerIds[i].Handle);
                var route = jobs.RouteOf(_workerIds[i].Handle);

                Mix(_workerIds[i].Id);
                Mix(task.Holder);
                Mix((long)task.Job);
                Mix(task.Start.Ticks);
                Mix(task.TravelTicks);
                Mix(task.WorkTicks);
                Mix(task.ReturnTicks);
                Mix(task.Origin);
                Mix(task.Destination);
                Mix(task.Completion);
                MixRoute(route);
            }

            for (var i = 0; i < _communities.Count; i++)
            {
                var band = _communities[i];

                Mix(band.Id);
                Mix(jobs.LivingAtDawn(band));
                Mix(jobs.HearthsAtDawn(band));
                Mix(jobs.SitesFoundFrom(band));

                // Over the enum's own numbering, as supplies are, and only
                // the kinds that are jobs: None has no site.
                for (var kind = 1; kind < DefinedJobKinds.Length; kind++)
                {
                    if (!JobTable.IsJob((JobKind)kind))
                    {
                        continue;
                    }

                    var job = (JobKind)kind;
                    var survey = jobs.SurveyOf(band, job);
                    var siteRoute = jobs.SiteRouteOf(band, job);

                    Mix((long)kind);
                    Mix(jobs.OnDuty(band, job));
                    Mix(survey.Reachable ? 1 : 0);
                    Mix(survey.Destination);
                    Mix(survey.Cost);
                    Mix(survey.ReturnCost);
                    Mix(siteRoute.Count);

                    for (var j = 0; j < siteRoute.Count; j++)
                    {
                        Mix(siteRoute[j]);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Folds in every wandering band's council, ordered by durable id: the
        /// pressure it has built toward settling, its days at camp and since
        /// it last looked for another, and where it has been sent.
        /// </summary>
        /// <remarks>
        /// These decide when a band moves and when it settles, and none of
        /// them reaches the queue until it does (#104).
        /// </remarks>
        public WorldHash AddCouncils(NomadicBands nomads)
        {
            if (nomads is null)
            {
                throw new ArgumentNullException(nameof(nomads));
            }

            nomads.CopyTrackedTo(_communities);
            _communities.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            Open(Section.Councils, _communities.Count);

            for (var i = 0; i < _communities.Count; i++)
            {
                var band = (MobileGroup)_communities[i];
                var booked = nomads.BookedArrival(band);

                Mix(band.Id);
                Mix(nomads.PressureOf(band));
                Mix(nomads.DaysAtCamp(band));
                Mix(nomads.DaysSinceLook(band));
                Mix(booked.HasValue ? 1 : 0);
                Mix(booked ?? default);
            }

            return this;
        }

        /// <summary>
        /// Folds in which communities are in famine, ordered by durable id.
        /// </summary>
        /// <remarks>
        /// The flag decides whether the next meal publishes FamineStarted or
        /// FamineEnded, so two worlds that disagree on it write different
        /// histories later (#104).
        /// </remarks>
        public WorldHash AddFamine(Hunger hunger)
        {
            if (hunger is null)
            {
                throw new ArgumentNullException(nameof(hunger));
            }

            hunger.CopyTrackedTo(_communities);
            _communities.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            Open(Section.Famine, _communities.Count);

            for (var i = 0; i < _communities.Count; i++)
            {
                Mix(_communities[i].Id);
                Mix(hunger.IsInFamine(_communities[i]) ? 1 : 0);
            }

            return this;
        }

        /// <summary>
        /// Folds in the recorded history: how many events, and the digest the
        /// journal keeps of them.
        /// </summary>
        /// <remarks>
        /// No system reads the journal back, but it is what a save keeps and
        /// what the chronicle prints, so two runs whose histories differ have
        /// diverged. The journal folds each event in as it is recorded
        /// (<see cref="EventJournal.Digest"/>), so this costs the same after
        /// two centuries as after a day (#104).
        /// </remarks>
        public WorldHash AddJournal(EventJournal journal)
        {
            if (journal is null)
            {
                throw new ArgumentNullException(nameof(journal));
            }

            Open(Section.Journal, journal.Count);
            Mix(journal.Digest);
            return this;
        }

        /// <summary>
        /// Folds in which communities each of Deaths, Fertility, Warmth and
        /// Matchmaking tracks, each set ordered by durable id.
        /// </summary>
        /// <remarks>
        /// Deaths strikes the dead from its communities and Fertility places
        /// newborns in them, and neither books anything per community, so no
        /// other section reaches their sets (the #108 review). Warmth and
        /// Matchmaking's sets reach the bookings section only while each
        /// community has an event booked, which it does not once its stream
        /// has reached the end of time. Hunger, Jobs and NomadicBands are not
        /// here: their own sections list the communities they track.
        /// </remarks>
        public WorldHash AddTracking(Deaths deaths, Fertility fertility, Warmth warmth, Matchmaking matchmaking)
        {
            if (deaths is null)
            {
                throw new ArgumentNullException(nameof(deaths));
            }

            if (fertility is null)
            {
                throw new ArgumentNullException(nameof(fertility));
            }

            if (warmth is null)
            {
                throw new ArgumentNullException(nameof(warmth));
            }

            if (matchmaking is null)
            {
                throw new ArgumentNullException(nameof(matchmaking));
            }

            Open(Section.Tracked, 4);
            deaths.CopyTrackedTo(_communities);
            MixTracked(_communities);
            fertility.CopyTrackedTo(_communities);
            MixTracked(_communities);
            warmth.CopyTrackedTo(_communities);
            MixTracked(_communities);
            matchmaking.CopyTrackedTo(_communities);
            MixTracked(_communities);
            return this;
        }

        /// <summary>
        /// Folds in every booking a system is holding - the "state names the
        /// event it booked" record each periodic stream keeps (#80) - in
        /// their own canonical order.
        /// </summary>
        /// <remarks>
        /// The pending section already covers the queue's side of this. These
        /// are the other side: what each system believes it has booked. Two
        /// runs that agree on the queue and disagree on who thinks they own
        /// which entry have diverged, and the disagreement surfaces later as a
        /// stream that doubled or stopped.
        ///
        /// Sorted here rather than trusted from the caller, because the
        /// caller gathers them from several systems and the concatenation
        /// order would otherwise be part of the hash.
        /// </remarks>
        public WorldHash AddBookings(IReadOnlyList<PendingBooking> bookings)
        {
            if (bookings is null)
            {
                throw new ArgumentNullException(nameof(bookings));
            }

            _bookings.Clear();

            for (var i = 0; i < bookings.Count; i++)
            {
                _bookings.Add(bookings[i]);
            }

            _bookings.Sort();
            Open(Section.Bookings, _bookings.Count);

            for (var i = 0; i < _bookings.Count; i++)
            {
                Mix(_bookings[i].Owner);
                Mix((long)_bookings[i].Kind);
                Mix(_bookings[i].Booked);
            }

            return this;
        }

        /// <summary>
        /// Folds in the clock's position and every event still due, in
        /// dispatch order.
        /// </summary>
        /// <remarks>
        /// Section 17 makes the same point from the save side: pending events
        /// are state, not something to rebuild from entity fields, because
        /// rebuilding can shift history. A world that agrees on its people and
        /// disagrees on what it has booked has already diverged - it just has
        /// not shown yet. The export this reads is refused off a checkpoint
        /// (<see cref="SimulationClock.AtCheckpoint"/>), so a hash taken from
        /// inside a handler throws rather than folding in a half-dispatched
        /// queue.
        /// </remarks>
        public WorldHash AddPending(SimulationClock clock)
        {
            if (clock is null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            clock.CopyPendingTo(_pending);
            Open(Section.Pending, _pending.Count);
            Mix(clock.Now.Ticks);

            for (var i = 0; i < _pending.Count; i++)
            {
                var scheduled = _pending[i];

                Mix(scheduled.Id);
                Mix(scheduled.Time.Ticks);
                Mix((long)scheduled.Phase);
                Mix((long)scheduled.Kind);
                Mix(scheduled.PrimaryEntity);
                Mix(scheduled.SecondaryEntity);
            }

            return this;
        }

        // Members are stored as handles, which are storage positions rather
        // than identity, so they are resolved to durable ids - in the order
        // the list holds them, not sorted. Every member list's order is part
        // of its container's determinism contract (MobileGroup, Settlement,
        // Household): Hunger feeds, Jobs picks, Matchmaking proposes and
        // Fertility finds a couple in that order, so the same people listed
        // differently is a different world (the #103 review).
        private void MixMembers(IReadOnlyList<PersonHandle> members, PersonStore people)
        {
            Mix(members.Count);

            for (var i = 0; i < members.Count; i++)
            {
                // A member the store no longer holds is corruption, and the
                // validator's job to report. Hashing None keeps the two sides
                // comparable rather than throwing on one of them.
                Mix(people.IsAlive(members[i]) ? people.GetId(members[i]) : EntityId.None);
            }
        }

        // Every kind, in enum order, whether or not it has stock: a ledger
        // that has never seen a resource and one that opened and spent it all
        // are different worlds, and the flow totals are what tell them apart.
        private void MixSupplies(ResourceLedger supplies)
        {
            // Indexed over the mask rather than Enum.GetValues, which is how
            // ResourceLedger.AuditBalances walks the same enum: it allocates
            // nothing, it is explicit that index 0 is None, and - the reason
            // that matters here - the order is the enum's own numbering
            // rather than whatever the runtime's reflection returns. This
            // number is compared across two runtimes; it must not depend on
            // one of them.
            for (var kind = 1; kind < DefinedKinds.Length; kind++)
            {
                if (!DefinedKinds[kind])
                {
                    continue;
                }

                var flows = supplies.Flows((ResourceKind)kind);

                Mix((long)kind);
                Mix(supplies.Available((ResourceKind)kind));
                Mix(supplies.Reserved((ResourceKind)kind));
                Mix(supplies.Carried((ResourceKind)kind));
                Mix(supplies.InProcess((ResourceKind)kind));
                Mix(flows.Opening);
                Mix(flows.Produced);
                Mix(flows.Gathered);
                Mix(flows.Imported);
                Mix(flows.Consumed);
                Mix(flows.Exported);
                Mix(flows.Destroyed);
                Mix(flows.Embodied);
            }
        }

        private void Open(Section section, int count)
        {
            Mix((long)section);
            Mix(count);
        }

        // The folding idiom RandomKey uses, for the same reason: order is part
        // of the result, and every overload funnels to one place so an enum
        // and a bare int of the same value stay interchangeable.
        private void Mix(ulong value) => _state = SplitMix64.Mix(_state ^ value);

        private void Mix(long value) => Mix(unchecked((ulong)value));

        private void Mix(int value) => Mix((long)value);

        private void Mix(WorldPosition position)
        {
            Mix(position.X);
            Mix(position.Y);
        }

        private void Mix(EntityId id)
        {
            Mix((long)id.Kind);
            Mix(id.Value);
        }

        private void Mix(EventId id) => Mix(id.Value);

        // A system's tracked set, sorted by id: which communities it tracks is
        // the world, the order it met them in is not.
        private void MixTracked(List<ICommunity> tracked)
        {
            tracked.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            Mix(tracked.Count);

            for (var i = 0; i < tracked.Count; i++)
            {
                Mix(tracked[i].Id);
            }
        }

        private void MixRoute(ReadOnlySpan<WorldPosition> route)
        {
            Mix(route.Length);

            for (var i = 0; i < route.Length; i++)
            {
                Mix(route[i]);
            }
        }

        // A task's worker as the durable id to sort by, and the handle Jobs
        // is asked about it by.
        private readonly struct Worker
        {
            public Worker(EntityId id, PersonHandle handle)
            {
                Id = id;
                Handle = handle;
            }

            public EntityId Id { get; }

            public PersonHandle Handle { get; }
        }
    }
}
