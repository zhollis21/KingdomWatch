using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// Owns person storage. Systems never touch the layout directly - they go
    /// through the accessors here for scattered access, or over
    /// <see cref="RecordSpan"/> for bulk loops.
    /// </summary>
    /// <remarks>
    /// The point of the indirection is that changing the layout later stays
    /// confined to this one class instead of becoming a refactor across every
    /// system - and refactors that size are where determinism bugs get
    /// introduced, which is the worst place to have them given that the whole
    /// debugging strategy rests on determinism. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 5.
    ///
    /// This class also allocates person slots, which is what makes
    /// <see cref="PersonHandle.Generation"/> mean anything: a slot freed by
    /// <see cref="Remove"/> can be handed to a later person, and the generation
    /// is what tells a handle taken before that apart from one taken after.
    ///
    /// Removal tombstones a slot in place rather than compacting the array.
    /// Swapping the last record into the freed slot would be denser, but it
    /// would move a live person to a different index while other code still
    /// holds handles pointing at the old one - and those handles would still
    /// carry a matching generation, so they would resolve silently to the wrong
    /// person. That is the exact corruption the handle/id split exists to
    /// prevent, so density loses. The cost is that the bulk span can contain
    /// unoccupied slots; defragmenting is an M2 question if profiling ever
    /// raises it, not a guess to make now.
    ///
    /// An unoccupied slot is one whose <see cref="PersonRecord.Id"/> is
    /// <see cref="EntityId.None"/>. No parallel occupancy array: a durable id
    /// is required for every real person, so its absence already says the slot
    /// holds nobody.
    ///
    /// A handle is only meaningful to the store that issued it. Handles carry
    /// no store identity, so one taken from a different PersonStore with the
    /// same slot history is indistinguishable from a local one and will
    /// resolve - and remove. A world has exactly one person store, which is
    /// what makes that acceptable; adding a second one means adding store
    /// identity to the handle first. There is a test recording this.
    ///
    /// Two invariants here cannot be enforced by the type system, because the
    /// bulk span hands out records by reference: <see cref="Count"/> equals the
    /// number of occupied slots, and every occupied slot's
    /// <see cref="PersonRecord.Handle"/> matches its own index and current
    /// generation. Writing identity fields through the span breaks both, and
    /// a third with them: the id-to-handle index answers
    /// <see cref="TryGetHandle"/> for exactly the occupied slots. A fourth is
    /// kept by another class: <see cref="PersonRecord.Household"/> agrees with
    /// the member list <see cref="Lifecycle.Households"/> maintains, and a
    /// write through the span bypasses it. Checking all of them belongs to the
    /// WorldValidator (#13), where world-state invariants live.
    ///
    /// Nothing here is Unity-aware, and nothing here may become so.
    /// NativeArray, Jobs and Burst are Unity dependencies and cannot enter
    /// Core; if profiling ever demands them the seam is a separate
    /// optimization backend, not an in-place change to this class.
    /// </remarks>
    public sealed class PersonStore
    {
        private const int InitialCapacity = 16;

        /// <summary>
        /// The earliest birth <see cref="Add"/> accepts: half the clock's range
        /// before it started. A birth further back - some 146 billion years -
        /// is not a founder, it is a corrupted field. Within the bound, age
        /// arithmetic is exact until the clock has run the same distance
        /// forward and saturates after that; see <see cref="GetTicksLived"/>.
        /// </summary>
        public const long EarliestBornTick = -(long.MaxValue / 2L);

        // Slots [0, _slotCount) have been allocated at some point; the array
        // may be longer. A slot is occupied unless its record's Id is None.
        private PersonRecord[] _people = Array.Empty<PersonRecord>();

        // Freed slots waiting to be reused, taken from the end. Used as a LIFO
        // stack rather than a queue only because that is cheaper on a List;
        // either is deterministic, which is what matters, since the order
        // people are removed in is itself deterministic.
        private readonly List<int> _freeSlots = new List<int>();

        // Durable id back to the live handle. Relationships (section 6) are
        // keyed by EntityId because they outlive the people in them, so
        // anything that acts on kin - adoption, for one - gets ids back and
        // needs handles to do anything with them. Kept in step with Add and
        // Remove; a dead person's id is simply absent.
        private readonly Dictionary<EntityId, PersonHandle> _handlesById =
            new Dictionary<EntityId, PersonHandle>();

        private static readonly bool[] DefinedAgeStages = EnumGuard.BuildMask(typeof(AgeStage));
        private static readonly bool[] DefinedSexes = EnumGuard.BuildMask(typeof(Sex));

        private int _slotCount;
        private int _count;

        /// <summary>How many people the store currently holds.</summary>
        public int Count => _count;

        /// <summary>
        /// Stores a new person and returns the handle that addresses them.
        /// </summary>
        /// <remarks>
        /// The durable id is allocated by the caller, through
        /// <see cref="IdAllocator"/>, and passed in - this class hands out
        /// storage slots, not identities.
        ///
        /// <paramref name="lastFedAt"/> is required rather than defaulted so
        /// that nobody can be added at year fifty with a last meal at the
        /// start of the world and starve at their first one. Whoever adds a
        /// person - the band generator, a birth - knows when they last ate;
        /// this class does not. <paramref name="bornTick"/> is required for
        /// the same reason: a defaulted birth would make every founder a
        /// newborn at tick zero, and the age is what ageing and mortality
        /// run on. It may be negative - see
        /// <see cref="PersonRecord.BornTick"/>. Whether it lies in the past
        /// is the caller's to know, as with the last meal: this class has no
        /// clock.
        /// </remarks>
        public PersonHandle Add(
            EntityId id,
            WorldPosition position,
            short health,
            AgeStage ageStage,
            Sex sex,
            byte birthCulture,
            byte assimilation,
            SimulationTime lastFedAt,
            long bornTick)
        {
            // Checking the kind covers EntityId.None as well: None is the only
            // id with no kind, and EntityId's constructor already refuses a
            // Person-kind id with a zero value.
            if (id.Kind != EntityKind.Person)
            {
                throw new ArgumentException(
                    "A person needs an EntityId of kind Person, not " + id.Kind + ".",
                    nameof(id));
            }

            if (_handlesById.ContainsKey(id))
            {
                throw new ArgumentException(
                    id + " is already stored; a durable id names one person.", nameof(id));
            }

            if (!EnumGuard.IsDefined(DefinedAgeStages, (int)ageStage) || ageStage == AgeStage.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ageStage), ageStage, "Not a defined AgeStage, or None.");
            }

            if (!EnumGuard.IsDefined(DefinedSexes, (int)sex) || sex == Sex.None)
            {
                throw new ArgumentOutOfRangeException(nameof(sex), sex, "Not a defined Sex, or None.");
            }

            if (bornTick < EarliestBornTick)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bornTick), bornTick, "Born before the clock can measure an age from; see EarliestBornTick.");
            }

            var handle = ClaimSlot();

            _people[handle.Index] = new PersonRecord
            {
                Handle = handle,
                Id = id,
                Position = position,
                Health = health,
                AgeStage = ageStage,
                Sex = sex,
                BirthCulture = birthCulture,
                Assimilation = assimilation,
                LastFedAt = lastFedAt,
                LastWarmedAt = lastFedAt,
                BornTick = bornTick,
                PregnancyDue = EventId.None,
                PendingMortalityCheck = EventId.None,
                PendingAgeStage = EventId.None,
                Household = EntityId.None,
            };

            _handlesById.Add(id, handle);
            _count++;
            return handle;
        }

        /// <summary>
        /// Removes a person and frees their slot for reuse. Throws when the
        /// handle does not address a live person, rather than reporting it, so
        /// that a caller holding a stale handle finds out at the point the bug
        /// is instead of much later. Also refuses someone still in a household,
        /// for the reason in the body.
        /// </summary>
        public void Remove(PersonHandle handle)
        {
            var slot = SlotFor(handle);

            // The record knows it is in a household even though this class
            // knows nothing about households: removing it now would leave the
            // handle in that household's member list, naming a slot that is
            // about to be someone else's. The death cascade leaves first.
            if (!_people[slot].Household.IsNone)
            {
                throw new InvalidOperationException(
                    handle + " is in " + _people[slot].Household + "; they leave it before their slot is freed.");
            }

            // Everything except the handle is wiped. The handle stays so the
            // slot remembers the generation it reached - clearing it would send
            // the next occupant back to generation 1, and a handle kept from
            // the first occupant would then match the second exactly, which is
            // precisely the silent mis-resolution the generation prevents.
            _handlesById.Remove(_people[slot].Id);
            _people[slot] = new PersonRecord { Handle = handle };
            _count--;
            _freeSlots.Add(slot);
        }

        /// <summary>
        /// Finds the live person a durable id names. False for the dead, the
        /// never-born and <see cref="EntityId.None"/> - the non-throwing
        /// question, because the ids that arrive here come from genealogy and
        /// partnerships, which keep naming people after they have died.
        /// </summary>
        public bool TryGetHandle(EntityId id, out PersonHandle handle) =>
            _handlesById.TryGetValue(id, out handle);

        /// <summary>
        /// Whether the handle still addresses a live person. The non-throwing
        /// question, for callers that legitimately hold handles which may have
        /// died since - group membership, for one.
        /// </summary>
        public bool IsAlive(PersonHandle handle) =>
            !handle.IsNone
            && handle.Index < _slotCount
            && _people[handle.Index].Handle == handle
            && !_people[handle.Index].Id.IsNone;

        public EntityId GetId(PersonHandle handle) => _people[SlotFor(handle)].Id;

        public WorldPosition GetPosition(PersonHandle handle) => _people[SlotFor(handle)].Position;

        public void SetPosition(PersonHandle handle, WorldPosition value) =>
            _people[SlotFor(handle)].Position = value;

        public short GetHealth(PersonHandle handle) => _people[SlotFor(handle)].Health;

        public void SetHealth(PersonHandle handle, short value) =>
            _people[SlotFor(handle)].Health = value;

        public AgeStage GetAgeStage(PersonHandle handle) => _people[SlotFor(handle)].AgeStage;

        /// <summary>
        /// Moves a person to a stage. Refuses an undefined stage and
        /// <see cref="AgeStage.None"/>, for the same reason <see cref="Add"/>
        /// does: a person is always in some stage.
        /// </summary>
        public void SetAgeStage(PersonHandle handle, AgeStage value)
        {
            if (!EnumGuard.IsDefined(DefinedAgeStages, (int)value) || value == AgeStage.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "Not a defined AgeStage, or None.");
            }

            _people[SlotFor(handle)].AgeStage = value;
        }

        public Sex GetSex(PersonHandle handle) => _people[SlotFor(handle)].Sex;

        public byte GetBirthCulture(PersonHandle handle) => _people[SlotFor(handle)].BirthCulture;

        public void SetBirthCulture(PersonHandle handle, byte value) =>
            _people[SlotFor(handle)].BirthCulture = value;

        public byte GetAssimilation(PersonHandle handle) => _people[SlotFor(handle)].Assimilation;

        public void SetAssimilation(PersonHandle handle, byte value) =>
            _people[SlotFor(handle)].Assimilation = value;

        public SimulationTime GetLastFedAt(PersonHandle handle) => _people[SlotFor(handle)].LastFedAt;

        public void SetLastFedAt(PersonHandle handle, SimulationTime value) =>
            _people[SlotFor(handle)].LastFedAt = value;

        public SimulationTime GetLastWarmedAt(PersonHandle handle) => _people[SlotFor(handle)].LastWarmedAt;

        public void SetLastWarmedAt(PersonHandle handle, SimulationTime value) =>
            _people[SlotFor(handle)].LastWarmedAt = value;

        public long GetBornTick(PersonHandle handle) => _people[SlotFor(handle)].BornTick;

        /// <summary>
        /// Ticks this person has lived at <paramref name="now"/>: the distance
        /// from their birth, never stored, computed when asked. Saturates at
        /// <see cref="long.MaxValue"/> rather than wrapping.
        /// </summary>
        /// <remarks>
        /// A birth may lie up to half the clock's range before the start and
        /// the clock may run to its end, so the true distance can exceed what
        /// a long holds. The subtraction can only wrap when the birth is
        /// negative and the true value is past the maximum, so a negative
        /// result from a negative birth is that case exactly, and the answer
        /// is "as old as can be measured". Callers then see an elder past
        /// every lifespan, which is what such a person is, rather than an
        /// infant. A birth after <paramref name="now"/> is not saturated -
        /// that is a negative distance, and the validator's (#13) to name.
        /// </remarks>
        public long GetTicksLived(PersonHandle handle, SimulationTime now)
        {
            var born = _people[SlotFor(handle)].BornTick;
            var lived = now.Ticks - born;
            return born < 0L && lived < 0L ? long.MaxValue : lived;
        }

        /// <summary>
        /// Whole years this person has lived at <paramref name="now"/>. See
        /// <see cref="GetTicksLived"/>.
        /// </summary>
        public long GetAgeYears(PersonHandle handle, SimulationTime now) =>
            GetTicksLived(handle, now) / SimulationTime.TicksPerYear;

        public EventId GetPregnancyDue(PersonHandle handle) => _people[SlotFor(handle)].PregnancyDue;

        public void SetPregnancyDue(PersonHandle handle, EventId value) =>
            _people[SlotFor(handle)].PregnancyDue = value;

        /// <summary>The MortalityCheck this person's next roll is booked as.</summary>
        public EventId GetPendingMortalityCheck(PersonHandle handle) =>
            _people[SlotFor(handle)].PendingMortalityCheck;

        public void SetPendingMortalityCheck(PersonHandle handle, EventId value) =>
            _people[SlotFor(handle)].PendingMortalityCheck = value;

        /// <summary>The AgeStageDue this person's next boundary is booked as.</summary>
        public EventId GetPendingAgeStage(PersonHandle handle) =>
            _people[SlotFor(handle)].PendingAgeStage;

        public void SetPendingAgeStage(PersonHandle handle, EventId value) =>
            _people[SlotFor(handle)].PendingAgeStage = value;

        public EntityId GetHousehold(PersonHandle handle) => _people[SlotFor(handle)].Household;

        /// <summary>
        /// Records which household a person belongs to. Internal, and called
        /// by <see cref="Lifecycle.Households"/> alone: the registry is what
        /// keeps this field and the household's member list agreeing, and a
        /// write from anywhere else is the one thing that can make them
        /// disagree.
        /// </summary>
        internal void SetHousehold(PersonHandle handle, EntityId value) =>
            _people[SlotFor(handle)].Household = value;

        public JobKind GetJob(PersonHandle handle) => _people[SlotFor(handle)].Job;

        /// <summary>
        /// Records the job a person is on. Internal, and called by
        /// <see cref="Work.Jobs"/> alone, for the reason
        /// <see cref="SetHousehold"/> is: the field says "on a task", and only
        /// the system that holds the task can keep that true. Jobs writes
        /// only kinds from its table and <see cref="JobKind.None"/>, which is
        /// what idleness is, so there is no guard here for a test to reach.
        /// </summary>
        internal void SetJob(PersonHandle handle, JobKind value) =>
            _people[SlotFor(handle)].Job = value;

        /// <summary>
        /// The bulk path: every allocated slot in slot order, which is stable
        /// and so safe to iterate in the simulation.
        /// </summary>
        /// <remarks>
        /// Named for what it returns rather than section 5's AliveSpan sketch.
        /// Removal leaves unoccupied slots in place (see the type's remarks),
        /// so the span is not alive-only and a name promising otherwise would
        /// eventually be believed. <b>Skip slots whose Id is
        /// <see cref="EntityId.None"/>.</b>
        ///
        /// Writes through the span go straight to storage, which is the point -
        /// it keeps tight loops vectorizable and allocation-free. Do not hold
        /// it across an <see cref="Add"/> or <see cref="Remove"/>: growing the
        /// array replaces it, and the old span would then be a view onto
        /// storage no longer in use.
        ///
        /// Write the simulation fields, never <see cref="PersonRecord.Id"/> or
        /// <see cref="PersonRecord.Handle"/>. Those are what identifies the
        /// slot's occupant, so changing them through the span would leave
        /// <see cref="Count"/> and the free list describing a population that
        /// is no longer there. Births and deaths go through
        /// <see cref="Add"/> and <see cref="Remove"/>.
        /// </remarks>
        public Span<PersonRecord> RecordSpan() => new Span<PersonRecord>(_people, 0, _slotCount);

        /// <summary>
        /// The scattered path: a handle for each live person, in slot order.
        /// </summary>
        /// <remarks>
        /// Allocates one iterator per enumeration, so it is not the tick-loop
        /// path - <see cref="RecordSpan"/> is. Section 5's claim that the tick
        /// loop is allocation-free either way is tracked by #59, which exists
        /// because nothing measures it yet.
        ///
        /// Lazy, so do not add or remove people part-way through enumerating
        /// it: the walk would then be reading storage that has moved under it.
        /// Collect the handles first when the loop needs to change the
        /// population.
        /// </remarks>
        public IEnumerable<PersonHandle> Alive()
        {
            for (var slot = 0; slot < _slotCount; slot++)
            {
                if (!_people[slot].Id.IsNone)
                {
                    yield return _people[slot].Handle;
                }
            }
        }

        /// <summary>
        /// Picks the slot the next person will occupy and the handle that will
        /// address them, then commits that choice.
        /// </summary>
        /// <remarks>
        /// The handle is built before any bookkeeping moves, and deliberately
        /// so. It is the only thing in an <see cref="Add"/> that can be
        /// rejected, and taking the slot first would leave a refused handle
        /// having already consumed one - dropped from the free list, occupied
        /// by nobody, and unreachable from then on. Ordering it this way makes
        /// a half-applied Add impossible rather than merely unlikely, and
        /// keeps that true for whatever gets added here later.
        /// </remarks>
        private PersonHandle ClaimSlot()
        {
            var reusable = _freeSlots.Count - 1;

            if (reusable >= 0)
            {
                var recycled = _freeSlots[reusable];

                // A recycled slot kept the generation its last occupant had,
                // so the next one gets that plus one and every handle from
                // before the removal is detectably stale.
                var reused = new PersonHandle(
                    recycled, _people[recycled].Handle.Generation + 1);

                _freeSlots.RemoveAt(reusable);
                return reused;
            }

            if (_slotCount == _people.Length)
            {
                // Doubling from a small start rather than pre-sizing to the
                // ~1,650 of section 5: M1 runs two prototype bands, and a
                // capacity guess is not worth making before anything measures
                // one.
                Array.Resize(
                    ref _people, _people.Length == 0 ? InitialCapacity : _people.Length * 2);
            }

            // A slot nobody has used starts its first occupant at generation 1.
            var fresh = new PersonHandle(_slotCount, 1);
            _slotCount++;
            return fresh;
        }

        private int SlotFor(PersonHandle handle)
        {
            if (handle.IsNone)
            {
                throw new ArgumentException(
                    "PersonHandle.None addresses no person.", nameof(handle));
            }

            // Index cannot be negative - PersonHandle's constructor refuses
            // that - and _slotCount only ever grows, so anything at or past it
            // was never issued here.
            if (handle.Index >= _slotCount)
            {
                throw new ArgumentException(
                    handle + " was never issued by this store.", nameof(handle));
            }

            var current = _people[handle.Index].Handle;

            if (current != handle)
            {
                throw new ArgumentException(
                    handle + " is stale: that slot is now on generation "
                    + current.Generation + ".",
                    nameof(handle));
            }

            if (_people[handle.Index].Id.IsNone)
            {
                throw new ArgumentException(
                    handle + " refers to a person who has been removed.", nameof(handle));
            }

            return handle.Index;
        }
    }
}
