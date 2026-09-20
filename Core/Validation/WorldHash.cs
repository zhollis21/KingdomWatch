using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Settlements;

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
    /// So nothing here reads a container's own order: people and households
    /// are sorted by <see cref="EntityId"/>, pending events by
    /// <see cref="ScheduledEvent.CompareTo"/>, and the fields of each record
    /// are folded in a fixed order written out longhand rather than reflected
    /// over.
    ///
    /// **Storage handles are deliberately not hashed.** A
    /// <see cref="PersonHandle"/> is a slot index and a generation - the
    /// representation section 5 warns about, not the logical world. A
    /// household's members are hashed as the durable ids they resolve to.
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
        }

        // Kept between calls: a hash taken once per simulated day over a long
        // run would otherwise allocate these on every take.
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(ResourceKind));

        private readonly List<PersonRecord> _people = new List<PersonRecord>();
        private readonly List<EntityId> _ids = new List<EntityId>();
        private readonly List<Household> _households = new List<Household>();
        private readonly List<Settlement> _settlements = new List<Settlement>();
        private readonly List<ScheduledEvent> _pending = new List<ScheduledEvent>();
        private readonly List<PendingBooking> _bookings = new List<PendingBooking>();

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
        // than identity, so they are resolved to durable ids and sorted. A
        // membership list that differs only in order is the same world.
        private void MixMembers(IReadOnlyList<PersonHandle> members, PersonStore people)
        {
            _ids.Clear();

            for (var i = 0; i < members.Count; i++)
            {
                // A member the store no longer holds is corruption, and the
                // validator's job to report. Hashing None keeps the two sides
                // comparable rather than throwing on one of them.
                _ids.Add(people.IsAlive(members[i]) ? people.GetId(members[i]) : EntityId.None);
            }

            _ids.Sort();
            Mix(_ids.Count);

            for (var i = 0; i < _ids.Count; i++)
            {
                Mix(_ids[i]);
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
    }
}
