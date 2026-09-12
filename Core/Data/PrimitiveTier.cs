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
    /// source of anything until crafting has a consumer. Section 9 also lists
    /// stone tools and hide clothing at this tier; both wait, for the reasons
    /// on <see cref="ResourceKind"/>. Temporary camps embody wood rather than
    /// producing anything, so they are #54's, not a recipe here.
    ///
    /// The quantities and durations are placeholders chosen to be plausible
    /// for a day's work, not tuned. Tuning is the harness's job once #17 can
    /// run a chronicle; nothing here should be read as a balance decision.
    /// </remarks>
    public static class PrimitiveTier
    {
        /// <summary>Half a day's foraging feeds a person for about a day.</summary>
        public static readonly Recipe Forage = new Recipe(
            "Forage",
            Array.Empty<ResourceQuantity>(),
            new[] { new ResourceQuantity(ResourceKind.Food, 3) },
            4L * SimulationTime.TicksPerHour);

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
        /// Every tier-zero recipe, in a fixed order. Job assignment iterates
        /// this, so the order is part of the determinism contract.
        /// </summary>
        public static readonly IReadOnlyList<Recipe> Recipes =
            new ReadOnlyCollection<Recipe>(new[] { Forage, GatherWood, GatherStone });
    }
}
