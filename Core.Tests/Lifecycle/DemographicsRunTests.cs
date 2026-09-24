using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    /// <summary>
    /// The whole loop, run: people are born, grow up, pair off, have
    /// children of their own and die, for a century, on the default table.
    /// A smoke test that the mechanism holds together over time - not the
    /// population-stability test (#17's <c>WorldRunTests</c>), and not a statement about the
    /// numbers, which are placeholders.
    /// </summary>
    [TestFixture]
    public sealed class DemographicsRunTests
    {
        [Test]
        public void A_band_of_six_couples_carries_on_for_a_century()
        {
            var w = new DemographicWorld(DemographicSettings.Default, 42UL);
            var band = w.NewBand();

            for (var i = 0; i < 6; i++)
            {
                w.NewCouple(out var wife, out var husband);
                band.AddMember(wife);
                band.AddMember(husband);
            }

            var peak = w.People.Count;
            var secondGeneration = false;

            // The social decision system (#38) is M7's; until then the
            // placeholder Matchmaking pairs people off once a year.
            w.Matchmaking.Track(band);

            for (var year = 0; year < 100; year++)
            {
                w.AdvanceYears(1L);
                peak = Math.Max(peak, w.People.Count);
                secondGeneration |= AnyoneWithAGrandparent(w);
                w.Base.AssertHouseholdsConsistent();
            }

            var births = w.Published(DomainEventKind.PersonBorn).Count - 12;
            var deaths = w.Published(DomainEventKind.PersonDied).Count;

            Assert.Multiple(() =>
            {
                Assert.That(births, Is.GreaterThan(0));
                Assert.That(deaths, Is.GreaterThan(0));
                Assert.That(w.People.Count, Is.EqualTo(12 + births - deaths), "the store agrees with the chronicle");
                Assert.That(w.People.Count, Is.GreaterThan(0), "the band did not die out");
                Assert.That(secondGeneration, Is.True, "children of children were born");
                Assert.That(band.Members, Has.Count.EqualTo(w.People.Count), "everyone alive is in the band");
                Assert.That(w.Published(DomainEventKind.MarriageFormed), Has.Count.GreaterThan(6), "the next generation married");
                Assert.That(w.Published(DomainEventKind.FamineStarted), Is.Empty, "the band was fed throughout");
            });

            TestContext.Out.WriteLine(
                "century: " + births + " births, " + deaths + " deaths, peak " + peak + ", alive " + w.People.Count);
        }

        /// <summary>
        /// Section 6's mating-pool viability run, moved here from #9 with the
        /// generator it tests: a generated band of thirty - the smallest
        /// starting size, and so the tightest pool - breeds for two
        /// centuries on plentiful food, and in every generation someone
        /// still finds someone to marry. Only the human table exists (#34);
        /// elves, the constraining case, are checked when it does.
        /// </summary>
        /// <remarks>
        /// Section 6 asks for five centuries. That is out of reach until
        /// something regulates growth: with camp space for housing (#69) and
        /// food that never runs short, thirty people are 923 by year 200 and
        /// past 5,000 by 250, at which point a year costs seconds and the
        /// question - can the founders' descendants still find partners - was
        /// answered a hundred years earlier. The pinch is early and
        /// temporary, as section 6 says; two centuries covers it.
        /// </remarks>
        [Test]
        public void A_generated_band_of_thirty_keeps_finding_partners_for_two_centuries()
        {
            var w = new DemographicWorld(DemographicSettings.Default, 11UL);
            var band = w.Generator.Generate(30, default);
            w.Deaths.Track(band);
            w.Fertility.Track(band);
            w.Hunger.Track(band);
            w.Matchmaking.Track(band);
            band.SharedSupplies.Gather(ResourceKind.Food, DemographicWorld.PlentifulFood);

            const int Years = 200;
            const int Generation = 25;
            var marriagesByGeneration = new int[Years / Generation];
            var peak = 0;
            var low = int.MaxValue;
            var seen = 0;

            for (var year = 0; year < Years; year++)
            {
                w.AdvanceYears(1L);
                var marriages = w.Published(DomainEventKind.MarriageFormed).Count;
                marriagesByGeneration[year / Generation] += marriages - seen;
                seen = marriages;
                peak = Math.Max(peak, w.People.Count);
                low = Math.Min(low, w.People.Count);
                w.Base.AssertHouseholdsConsistent();
            }

            TestContext.Out.WriteLine(
                "two centuries: peak " + peak + ", low " + low + ", alive " + w.People.Count
                + ", famines " + w.Published(DomainEventKind.FamineStarted).Count
                + ", marriages by generation " + string.Join(" ", marriagesByGeneration));

            Assert.Multiple(() =>
            {
                Assert.That(w.People.Count, Is.GreaterThan(0), "the band did not die out");
                Assert.That(low, Is.GreaterThanOrEqualTo(30 / 2), "the early pinch did not halve the band");
                Assert.That(marriagesByGeneration, Has.All.GreaterThan(0), "every generation found partners");
                Assert.That(band.Members, Has.Count.EqualTo(w.People.Count), "everyone alive is in the band");
            });
        }

        private static bool AnyoneWithAGrandparent(DemographicWorld w)
        {
            foreach (var person in w.People.Alive())
            {
                var mother = w.Genealogy.Parents(w.IdOf(person)).Mother;

                if (!mother.IsNone && !w.Genealogy.Parents(mother).Mother.IsNone)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
