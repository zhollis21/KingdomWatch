using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Core.Work
{
    /// <summary>
    /// What each <see cref="JobKind"/> does: the recipe it runs and the
    /// terrain it runs on. Data, in the section 9 sense: adding a job is a
    /// case here for what it does and where, and a need rule in
    /// <see cref="Jobs"/> for when the band wants it - the two things a job
    /// is, kept apart.
    /// </summary>
    /// <remarks>
    /// Builders and Farmers (#100) are jobs with neither: their work is
    /// hours put into a building, which <see cref="Construction.Buildings"/>
    /// owns, so everything here but <see cref="IsJob"/> refuses them.
    ///
    /// Every gathering job is a recipe from <see cref="PrimitiveTier"/>,
    /// worked at the nearest cell of a terrain that has the thing. Foraging
    /// takes berry scrub, wood needs forest and stone needs rocks; open
    /// plains have none of them (#137), so a band that can reach none
    /// of a kind simply does not gather it. Whether a given bush or tree has
    /// anything left is <see cref="Land.LandCover"/>'s (#26); this says only
    /// which terrain a job looks at.
    /// </remarks>
    public static class JobTable
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(JobKind));
        private static readonly bool[] DefinedTerrain = EnumGuard.BuildMask(typeof(TerrainKind));
        private static readonly bool[] DefinedSeasons = EnumGuard.BuildMask(typeof(Season));

        // WorksOn as a mask per job, built once, for the pathfinder's nearest
        // search. Indexed by JobKind; None's slot is an empty mask nobody asks for.
        private static readonly bool[][] TerrainByJob = BuildTerrainMasks();

        /// <summary>
        /// The recipe one task of this job runs in spring - its base yield.
        /// <see cref="Jobs"/> asks <see cref="Recipe(JobKind, Season)"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Not a gathering job (<see cref="IsGathering"/>): undefined, <see cref="JobKind.None"/>, or a Builder or Farmer, which run no recipe.
        /// </exception>
        public static Recipe Recipe(JobKind job) => Recipe(job, Season.Spring);

        /// <summary>
        /// The recipe one task of this job runs in a season. Only foraging
        /// changes with it (<see cref="PrimitiveTier.ForageIn"/>, #53).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Not a gathering job (<see cref="IsGathering"/>), which runs no recipe;
        /// or not a defined season.
        /// </exception>
        public static Recipe Recipe(JobKind job, Season season)
        {
            if (!EnumGuard.IsDefined(DefinedSeasons, (int)season))
            {
                throw new ArgumentOutOfRangeException(nameof(season), season, "Not a defined Season.");
            }

            switch (job)
            {
                case JobKind.Forager:
                    return PrimitiveTier.ForageIn(season);
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
        /// Not a gathering job (<see cref="IsGathering"/>); or not a defined
        /// terrain, or <see cref="TerrainKind.None"/> - the same contract as
        /// <see cref="TerrainRules"/>, so a corrupted cell is named rather
        /// than treated as one nothing works on.
        /// </exception>
        public static bool WorksOn(JobKind job, TerrainKind terrain)
        {
            if (!EnumGuard.IsDefined(DefinedTerrain, (int)terrain) || terrain == TerrainKind.None)
            {
                throw new ArgumentOutOfRangeException(nameof(terrain), terrain, "Not a defined TerrainKind, or None.");
            }

            switch (job)
            {
                case JobKind.Forager:
                    return terrain == TerrainKind.Scrub;
                case JobKind.Woodcutter:
                    return terrain == TerrainKind.Forest;
                case JobKind.StoneGatherer:
                    return terrain == TerrainKind.Rocks;
                default:
                    throw NotAJob(job);
            }
        }

        /// <summary>
        /// The terrain this job works on, as a mask indexed by
        /// <see cref="TerrainKind"/>: what <see cref="WorksOn"/> answers, in
        /// the shape <see cref="Traversal.Pathfinder.TryFindNearest"/> takes.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Not a gathering job (<see cref="IsGathering"/>).
        /// </exception>
        public static ReadOnlySpan<bool> Terrain(JobKind job)
        {
            if (!IsGathering(job))
            {
                throw NotAJob(job);
            }

            return TerrainByJob[(int)job];
        }

        /// <summary>Whether this is a job a person can hold: defined, and not None.</summary>
        public static bool IsJob(JobKind job) =>
            EnumGuard.IsDefined(DefinedKinds, (int)job) && job != JobKind.None;

        /// <summary>
        /// Whether this job runs a gathering recipe on a terrain - every job
        /// but <see cref="JobKind.Builder"/> and <see cref="JobKind.Farmer"/>,
        /// whose work is hours put into a building (#100) and who have
        /// neither a recipe nor a terrain here.
        /// </summary>
        public static bool IsGathering(JobKind job) =>
            IsJob(job) && job != JobKind.Builder && job != JobKind.Farmer;

        private static bool[][] BuildTerrainMasks()
        {
            var masks = new bool[DefinedKinds.Length][];

            for (var job = 0; job < masks.Length; job++)
            {
                masks[job] = new bool[DefinedTerrain.Length];

                if (!IsGathering((JobKind)job))
                {
                    continue;
                }

                for (var terrain = 1; terrain < DefinedTerrain.Length; terrain++)
                {
                    masks[job][terrain] = DefinedTerrain[terrain] && WorksOn((JobKind)job, (TerrainKind)terrain);
                }
            }

            return masks;
        }

        private static ArgumentOutOfRangeException NotAJob(JobKind job) =>
            new ArgumentOutOfRangeException(nameof(job), job, "Not a defined gathering JobKind.");
    }
}
