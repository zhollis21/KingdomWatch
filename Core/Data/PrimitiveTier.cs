using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Clock;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// Tier zero of the capability graph: what people can do with no
    /// buildings, no tools and no settlement. Every capability must have a
    /// reachable path from here, or the smithing chicken-and-egg has no
    /// answer. See docs/design/kingdom-watch-plan-v7.1.md section 9.
    /// </summary>
    /// <remarks>
    /// M1's three recipes are all gathering (#12): the world is the only
    /// source of anything until crafting has a consumer. Section 9's own
    /// "stone tools and hide clothing" example predates the economy ladder
    /// (#78), which settled the actual tier-zero forms - see
    /// <see cref="ResourceKind"/> and the ladder's capability graph.
    /// Temporary camps embody wood rather than producing anything, so they
    /// are #54's, not a recipe here.
    ///
    /// **Foraging follows the seasons; nothing else does** (#53). Section 9's
    /// "famine is a timing problem" needs the one food source to be lean for
    /// part of the year. <see cref="Forage"/> is the spring yield, and
    /// <see cref="ForageIn"/> returns the recipe for any season: richer in
    /// summer and autumn, thin in winter. Deadfall and surface stone are there all year.
    /// A season's recipe differs from the others only in its output, so a
    /// task's duration never depends on when it starts.
    ///
    /// The quantities and durations are placeholders chosen to be plausible
    /// for a day's work, not tuned. Tuning is the harness's job once #17 can
    /// run a chronicle; nothing here should be read as a balance decision.
    /// </remarks>
    public static class PrimitiveTier
    {
        /// <summary>
        /// Half a day's foraging feeds a person for about a day. The spring
        /// yield, and the base the other seasons are measured against.
        /// </summary>
        public static readonly Recipe Forage = ForageYielding("Forage", 3);

        /// <summary>Summer's foraging: the land at its most generous.</summary>
        public static readonly Recipe ForageSummer = ForageYielding("Forage (summer)", 4);

        /// <summary>Autumn's foraging: nuts, late fruit, the last of the year's plenty.</summary>
        public static readonly Recipe ForageAutumn = ForageYielding("Forage (autumn)", 4);

        /// <summary>
        /// Winter's foraging: not enough to feed the forager, so a band that
        /// did not store food in autumn cannot forage its way through.
        /// </summary>
        public static readonly Recipe ForageWinter = ForageYielding("Forage (winter)", 1);

        /// <summary>Deadfall and small timber, no axe required.</summary>
        public static readonly Recipe GatherWood = new Recipe(
            "Gather wood",
            Array.Empty<ResourceQuantity>(),
            new[] { new ResourceQuantity(ResourceKind.Wood, 2) },
            4L * SimulationTime.TicksPerHour);

        /// <summary>Loose surface stone. Slower: it is heavy and scattered.</summary>
        public static readonly Recipe GatherStone = new Recipe(
            "Gather stone",
            Array.Empty<ResourceQuantity>(),
            new[] { new ResourceQuantity(ResourceKind.Stone, 1) },
            6L * SimulationTime.TicksPerHour);

        /// <summary>
        /// Every tier-zero recipe at its base yield, in a fixed order - one
        /// per thing people can do, so the seasonal forms of
        /// <see cref="Forage"/> are not listed again. The order is part of
        /// the determinism contract for anything that iterates it.
        /// </summary>
        public static readonly IReadOnlyList<Recipe> Recipes =
            new ReadOnlyCollection<Recipe>(new[] { Forage, GatherWood, GatherStone });

        // Indexed by Season.
        private static readonly Recipe[] ForageBySeason = { Forage, ForageSummer, ForageAutumn, ForageWinter };

        private static readonly bool[] DefinedSeasons = EnumGuard.BuildMask(typeof(Season));

        /// <summary>The foraging recipe for a season.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Not a defined season.</exception>
        public static Recipe ForageIn(Season season)
        {
            if (!EnumGuard.IsDefined(DefinedSeasons, (int)season))
            {
                throw new ArgumentOutOfRangeException(nameof(season), season, "Not a defined Season.");
            }

            return ForageBySeason[(int)season];
        }

        private static Recipe ForageYielding(string name, int food) => new Recipe(
            name,
            Array.Empty<ResourceQuantity>(),
            new[] { new ResourceQuantity(ResourceKind.Food, food) },
            4L * SimulationTime.TicksPerHour);
    }
}
