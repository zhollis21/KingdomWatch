using System;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Core.Work
{
    /// <summary>
    /// What each <see cref="JobKind"/> does: the recipe it runs and the
    /// terrain it runs it on. Data, in the section 9 sense - a job is a row
    /// here, and adding one is adding a row, not a branch in
    /// <see cref="Jobs"/>.
    /// </summary>
    /// <remarks>
    /// Every M1 job is a gathering recipe from <see cref="PrimitiveTier"/>,
    /// worked at the nearest cell of a terrain that has the thing. Foraging
    /// takes plains or forest, so it is never out of reach of a band standing
    /// on land; wood needs forest and stone needs hills, and a band that can
    /// reach neither simply does not gather them. The mapping is placeholder
    /// in the <see cref="PrimitiveTier"/> sense: a table for a later resource
    /// model (#26) to make finite and per-cell.
    /// </remarks>
    public static class JobTable
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(JobKind));

        /// <summary>The recipe one task of this job runs.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Not a defined job, or <see cref="JobKind.None"/>, which runs nothing.
        /// </exception>
        public static Recipe Recipe(JobKind job)
        {
            switch (job)
            {
                case JobKind.Forager:
                    return PrimitiveTier.Forage;
                case JobKind.Woodcutter:
                    return PrimitiveTier.GatherWood;
                case JobKind.StoneGatherer:
                    return PrimitiveTier.GatherStone;
                default:
                    throw NotAJob(job);
            }
        }

        /// <summary>Whether this job can be worked on a cell of this terrain.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Not a defined job, or <see cref="JobKind.None"/>.
        /// </exception>
        public static bool WorksOn(JobKind job, TerrainKind terrain)
        {
            switch (job)
            {
                case JobKind.Forager:
                    return terrain == TerrainKind.Plains || terrain == TerrainKind.Forest;
                case JobKind.Woodcutter:
                    return terrain == TerrainKind.Forest;
                case JobKind.StoneGatherer:
                    return terrain == TerrainKind.Hills;
                default:
                    throw NotAJob(job);
            }
        }

        /// <summary>Whether this is a job a person can hold: defined, and not None.</summary>
        public static bool IsJob(JobKind job) =>
            EnumGuard.IsDefined(DefinedKinds, (int)job) && job != JobKind.None;

        private static ArgumentOutOfRangeException NotAJob(JobKind job) =>
            new ArgumentOutOfRangeException(nameof(job), job, "Not a defined JobKind, or None.");
    }
}
