using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Tests.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Section 18's zero-allocation tick loop, pointed at the demographic
    /// model: a span of years in which every household is checked and
    /// nobody conceives, everyone is rolled and nobody dies, and people
    /// cross age boundaries, allocates nothing.
    /// </summary>
    /// <remarks>
    /// A birth allocates and is deliberately not measured - see
    /// <see cref="Fertility"/> - and a death is measured by
    /// <see cref="DeathsAllocationTests"/>. What is measured here is the
    /// steady state between them: the checks that come to nothing, which is
    /// most of them, and the ageing that always happens.
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    public sealed class DemographicsAllocationTests
    {
        [Test]
        public void Checks_that_come_to_nothing_and_age_boundaries_allocate_nothing()
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

            // Ten couples, and children of every age below adulthood so that
            // each boundary is crossed during the measured span.
            for (var i = 0; i < 10; i++)
            {
                w.NewCouple(out var wife, out var husband);
                band.AddMember(wife);
                band.AddMember(husband);
            }

            for (var age = 0L; age < settings.AdultAtYears; age++)
            {
                band.AddMember(w.NewPerson(age, age % 2 == 0 ? Sex.Female : Sex.Male));
            }

            // Two years of warm-up: every handler path the measured span
            // will take - meal, check, roll, boundary - has run and been
            // JIT-compiled, and the queue has reached its steady size.
            w.AdvanceYears(2L);

            var allocated = Allocations.Measure(() => w.AdvanceYears(3L));

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across three simulated years");
                Assert.That(w.People.Count, Is.EqualTo(36), "nobody born, nobody died");
                Assert.That(w.Clock.Now, Is.EqualTo(SimulationTime.FromYears(5L)));
            });
        }
    }
}
