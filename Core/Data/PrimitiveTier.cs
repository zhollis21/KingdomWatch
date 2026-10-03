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
    /// <see cref="ForageIn"/> returns the recipe for any season. Since #26
    /// the lean part is winter alone, when the bushes are bare and nobody
    /// forages; spring, summer and autumn yield alike, the whole-number
    /// rounding of what were richer summers and autumns. Deadfall and
    /// surface stone are there all year.
    /// A season's recipe differs from the others only in its output, so a
    /// task's duration never depends on when it starts.
    ///
    /// The quantities and durations are placeholders chosen to be plausible
    /// for a day's work. Foraging's four yields are the first numbers the
    /// harness tuned (#17): at 3/4/4/1 about one homeland in seven starved
    /// within forty years, because a settlement with more dependents than
    /// workers could not store a winter; 4/6/6/1 is the lowest tried where
    /// none did across 64 seeds. #26 made one food a person's day (it was a
    /// third of one), so those are 1⅓/2/2 now. Spring rounds up to 2, the
    /// same as summer and autumn: rounding it down to 1 starved every
    /// homeland within two years once bushes ran out (#26's sweeps).
    /// Stores read a third as large. Nothing
    /// here is a balance decision.
    /// </remarks>
    public static class PrimitiveTier
    {
        /// <summary>
        /// A trip feeds two people for a day. The spring yield, and the base
        /// the other seasons are measured against.
        /// </summary>
        public static readonly Recipe Forage = ForageYielding("Forage", 2);

        /// <summary>Summer's foraging: as spring's since #26.</summary>
        public static readonly Recipe ForageSummer = ForageYielding("Forage (summer)", 2);

        /// <summary>Autumn's foraging: nuts and late fruit, as spring's since #26.</summary>
        public static readonly Recipe ForageAutumn = ForageYielding("Forage (autumn)", 2);

        /// <summary>
        /// Winter's foraging. Until #26 it was a third of a forager's own day,
        /// so a band that did not store food in autumn could not forage its
        /// way through. Since #26 berry bushes are bare all winter, so nothing
        /// forages then and this recipe is not run; it stays as the season's entry until a
        /// winter food source (#114's game) gives foraging something to find.
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
