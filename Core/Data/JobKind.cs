namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// The standing role a person holds: what they are for, as opposed to the
    /// task they are on right now. Section 12's work manager posts these and
    /// people take them; section 6's death cascade vacates them.
    /// </summary>
    /// <remarks>
    /// A role rather than a recipe index, although every M1 job is exactly
    /// "run this gathering recipe": later jobs - builder, hauler, priest
    /// (section 11) - are not recipes, so a recipe would be the wrong key.
    /// <see cref="Work.JobTable"/> maps the jobs that are recipes to theirs.
    /// A person's job is written on their record and set by
    /// <see cref="Work.Jobs"/> alone; as built (#52) a person holds a job
    /// exactly while they have a task, so the field reads as "on duty as".
    ///
    /// Stored on every person, so append-only: renumbering repoints every
    /// saved record. A byte because there is one per person and nothing
    /// reads it in bulk.
    /// </remarks>
    public enum JobKind : byte
    {
        /// <summary>No job. The default, and what the dead and the idle hold.</summary>
        None = 0,

        /// <summary>Runs <see cref="PrimitiveTier.Forage"/>: food from plains or forest.</summary>
        Forager = 1,

        /// <summary>Runs <see cref="PrimitiveTier.GatherWood"/>: wood from forest.</summary>
        Woodcutter = 2,

        /// <summary>Runs <see cref="PrimitiveTier.GatherStone"/>: stone from hills.</summary>
        StoneGatherer = 3,
    }
}
