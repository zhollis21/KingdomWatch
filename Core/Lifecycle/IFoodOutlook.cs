using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// Whether a community is outgrowing its food: what holds back courtship
    /// (<see cref="Matchmaking"/>) and conception (<see cref="Fertility"/>)
    /// in a village that cannot feed more mouths (#69). Section 6 names food
    /// availability beside housing among the regulators of a population.
    /// </summary>
    /// <remarks>
    /// A seam because the answer is the settlement's buildings and land
    /// (<see cref="Construction.Buildings.IsFoodShort"/>), which lifecycle
    /// does not otherwise know about. <see cref="Never"/> until a world sets
    /// one, so fixtures about something else are untouched.
    /// </remarks>
    public interface IFoodOutlook
    {
        /// <summary>Whether this community's food is short now. False for any community it does not judge.</summary>
        bool IsFoodShort(EntityId community);
    }

    /// <summary>Food outlooks that need nothing behind them.</summary>
    public static class FoodOutlook
    {
        /// <summary>An outlook under which no community is ever short.</summary>
        public static IFoodOutlook Never { get; } = new NeverShort();

        private sealed class NeverShort : IFoodOutlook
        {
            public bool IsFoodShort(EntityId community) => false;
        }
    }
}
