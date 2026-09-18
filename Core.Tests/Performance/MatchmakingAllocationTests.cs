using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Tests.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Section 18's zero-allocation tick loop, pointed at
    /// <see cref="Matchmaking"/>: a courtship that considers every eligible
    /// pair and marries nobody allocates nothing. A wedding allocates - a
    /// household is a new entity - and <see cref="Households"/> says so.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class MatchmakingAllocationTests
    {
        [Test]
        public void Courtships_that_come_to_nothing_allocate_nothing()
        {
            var settings = new DemographicSettings
            {
                ConceptionPerMille = 0,
                InfantMortalityPerMille = 0,
                ChildMortalityPerMille = 0,
                AdolescentMortalityPerMille = 0,
                AdultMortalityPerMille = 0,
                ElderMortalityPerMille = 0,
                SoftLifespanYears = 1_000L,
                MaxLifespanYears = 2_000L,
            };
            var w = new DemographicWorld(settings, 1UL);
            var band = w.NewBand();

            // Siblings only: every pair is considered and the kinship ban
            // refuses each, so the whole scan runs and nothing is formed.
            w.NewCouple(out var mother, out var father);
            band.AddMember(mother);
            band.AddMember(father);

            for (var i = 0; i < 20; i++)
            {
                band.AddMember(w.NewChild(20L + (i % 5), i % 2 == 0 ? Sex.Female : Sex.Male, mother, father));
            }

            w.Matchmaking.Track(band);
            w.AdvanceYears(2L);
            var households = w.Households.Count;

            var allocated = Allocations.Measure(() => w.AdvanceYears(3L));

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across three courtships");
                Assert.That(w.Households.Count, Is.EqualTo(households), "nobody married a sibling");
            });
        }
    }
}
