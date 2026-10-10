using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    // A food outlook under which one community is short until told not:
    // for the tests of what a hungry village holds back (#69).
    internal sealed class ShortOf : IFoodOutlook
    {
        private readonly EntityId _community;

        public ShortOf(EntityId community) => _community = community;

        public bool Short { get; set; } = true;

        public bool IsFoodShort(EntityId community) => Short && community == _community;
    }
}
