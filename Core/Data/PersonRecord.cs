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
    /// Section 5 also lists Job and Household fields here. Both are absent for
    /// now: JobId does not exist until #52 and HouseholdHandle until #9, and
    /// neither type's shape is settled - #52 describes recipes as data rather
    /// than code, which may make a job reference a data-table lookup rather
    /// than a handle at all. A placeholder guessed now would have dependent
    /// code written against it before either issue makes its own design
    /// decision. Same reasoning as MobileGroup deferring SharedSupplies to #12.
    /// Adding them later is a field plus an accessor pair, which is the whole
    /// point of storage living behind PersonStore.
    ///
    /// Skills are deliberately not a field either - section 5 indexes them
    /// separately as [personIndex * skillCount + skillId], and they arrive with
    /// #22 in M3.
    ///
    /// AgeStage, BirthCulture and Assimilation are raw bytes rather than enums
    /// because the enums that would give them meaning belong to systems not
    /// built yet. Section 5 declares them the same way.
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

        public byte AgeStage;

        public byte BirthCulture;

        public byte Assimilation;
    }
}
