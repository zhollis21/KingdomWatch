namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// One person's stored state, in two halves. The identity fields
    /// (<see cref="Handle"/> and <see cref="Id"/>) belong to
    /// <see cref="PersonStore"/> and only it may write them. The simulation
    /// fields below them are the caller's to change, either through the
    /// store's accessors or in place through
    /// <see cref="PersonStore.RecordSpan"/>.
    /// </summary>
    /// <remarks>
    /// A dense record rather than parallel arrays. At roughly 1,650 people the
    /// whole population is about 105 KB and fits in L2 cache, so struct-of-
    /// arrays would be solving a cache-miss problem that does not exist yet.
    /// Records also suit the three things already committed to - the
    /// WorldValidator, the canonical world hash (sort by durable id, serialize
    /// in a defined order) and versioned save migration - all of which are
    /// fiddly over index-correlated arrays. Split hot fields out only if M2
    /// profiling says so. See docs/design/kingdom-watch-plan-v7.1.md section 5.
    ///
    /// Public mutable fields, not properties: <see cref="PersonStore"/> mutates
    /// records in place through the array indexer, and the accessors that do it
    /// are what systems see instead of this type.
    ///
    /// Section 5 also lists a Job field here. It is absent for now: JobId does
    /// not exist until #52 and its shape is not settled - #52 describes recipes
    /// as data rather than code, which may make a job reference a data-table
    /// lookup rather than a handle at all. A placeholder guessed now would have
    /// dependent code written against it before that issue makes its own
    /// design decision. Same reasoning as MobileGroup deferring SharedSupplies
    /// to #12. Adding it later is a field plus an accessor pair, which is the
    /// whole point of storage living behind PersonStore.
    ///
    /// <see cref="Household"/> is an <see cref="EntityId"/> rather than the
    /// HouseholdHandle section 5 sketched. Households are a few hundred plain
    /// objects in a registry (<see cref="Lifecycle.Households"/>), not a
    /// recycled-slot store, so there is no generation to check - and a durable
    /// id that is never reused already makes a reference to a dissolved
    /// household fail loudly on lookup.
    ///
    /// Skills are deliberately not a field either - section 5 indexes them
    /// separately as [personIndex * skillCount + skillId], and they arrive with
    /// #22 in M3.
    ///
    /// BirthCulture and Assimilation are raw bytes rather than enums because
    /// the enums that would give them meaning belong to systems not built yet.
    /// Section 5 declares them the same way. AgeStage was one too, until #9
    /// needed to tell adults from children.
    /// </remarks>
    public struct PersonRecord
    {
        /// <summary>
        /// Store-owned. The handle that currently addresses this slot, present
        /// so a caller iterating <see cref="PersonStore.RecordSpan"/> can tell
        /// which person a record belongs to - a span alone carries no handles.
        /// Read it; writing it forges an identity the store knows nothing
        /// about.
        /// </summary>
        public PersonHandle Handle;

        /// <summary>
        /// Store-owned. Durable identity, safe to reference from history and
        /// saves. <see cref="EntityId.None"/> marks an unoccupied slot, so
        /// writing this field is what silently turns a tombstone into an
        /// apparent person, or a person into a leak. Births and deaths go
        /// through <see cref="PersonStore.Add"/> and
        /// <see cref="PersonStore.Remove"/>.
        /// </summary>
        public EntityId Id;

        // Simulation fields: the caller's to write, through the store's
        // accessors or in place through the bulk span.

        public WorldPosition Position;

        public short Health;

        /// <summary>
        /// Where this person is in life. A reading of <see cref="BornTick"/>
        /// rather than a fact of its own: <see cref="Lifecycle.Aging"/> moves
        /// it at each boundary the age crosses, and where the two disagree
        /// the age wins. Kept as a field because every system that branches
        /// on it - eligibility, adoption, seating - reads it far more often
        /// than anyone crosses a boundary.
        /// </summary>
        public AgeStage AgeStage;

        /// <summary>
        /// Set at birth. The store has no setter, so nothing changes it
        /// through scattered access; the bulk span can, like every simulation
        /// field, and is trusted not to.
        /// </summary>
        public Sex Sex;

        public byte BirthCulture;

        public byte Assimilation;

        /// <summary>
        /// When this person last ate. Written by <see cref="Needs.Hunger"/>
        /// at each meal they draw; how long ago it was is what starvation
        /// integrates over, and what <see cref="Lifecycle.Mortality"/> and
        /// <see cref="Lifecycle.Fertility"/> read as the nutrition modifier.
        /// </summary>
        public Clock.SimulationTime LastFedAt;

        /// <summary>
        /// When this person was born, in ticks since the start of the world -
        /// negative for a founder born before it. Set at birth and never
        /// changed; age is the distance from here to now, computed when
        /// something asks and never ticked. The demographic model
        /// (<see cref="Lifecycle.Aging"/>, <see cref="Lifecycle.Mortality"/>,
        /// <see cref="Lifecycle.Fertility"/>) reads it.
        /// </summary>
        /// <remarks>
        /// A raw long rather than a <see cref="Clock.SimulationTime"/> because
        /// that type refuses to be negative - time never runs backwards - and
        /// worldgen seeds a band of people who were forty before tick zero.
        /// Offsetting the world clock so that founders fit would put the
        /// start of history at some arbitrary year; a signed duration is the
        /// honest representation.
        /// </remarks>
        public long BornTick;

        /// <summary>
        /// The scheduled <see cref="Clock.ScheduledEventKind.BirthDue"/> this
        /// person is carrying, or <see cref="EventId.None"/> when not
        /// pregnant. The pregnancy IS the pending event - section 17 names
        /// birth due dates as the future commitments a save serializes - so
        /// the record points at it rather than keeping a due date of its own
        /// that could disagree with the queue. Written by
        /// <see cref="Lifecycle.Fertility"/> at conception and birth, and
        /// cleared by the death cascade, which cancels the event.
        /// </summary>
        public EventId PregnancyDue;

        /// <summary>
        /// The household this person belongs to, or <see cref="EntityId.None"/>
        /// for someone in none. Written by <see cref="Lifecycle.Households"/>,
        /// which keeps this and the household's member list saying the same
        /// thing - and by nothing else. The bulk span could; a write there
        /// breaks that agreement the way writing <see cref="Id"/> breaks the
        /// store's, and the validator (#13) is what catches both.
        /// </summary>
        public EntityId Household;
    }
}
