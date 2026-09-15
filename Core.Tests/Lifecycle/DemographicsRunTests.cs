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
    /// population-stability test #17 owns, and not a statement about the
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

            // Nothing in M1 pairs people off (#38), so the run does it here
            // in the plainest way: each year, every unpartnered adult woman
            // marries the first unpartnered adult man the rules allow.
            for (var year = 0; year < 100; year++)
            {
                w.AdvanceYears(1L);
                PairOff(w);
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

        private static void PairOff(DemographicWorld w)
        {
            var women = new List<PersonHandle>();
            var men = new List<PersonHandle>();

            foreach (var person in w.People.Alive())
            {
                if (!AgeStages.IsAdult(w.People.GetAgeStage(person))
                    || !w.Partnerships.ActivePartnerOf(w.IdOf(person)).IsNone)
                {
                    continue;
                }

                (w.People.GetSex(person) == Sex.Female ? women : men).Add(person);
            }

            foreach (var woman in women)
            {
                for (var i = 0; i < men.Count; i++)
                {
                    if (w.Family.Evaluate(woman, men[i]) == PartnerRefusal.None)
                    {
                        w.Family.Partner(woman, men[i], Reasons.None);
                        men.RemoveAt(i);
                        break;
                    }
                }
            }
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
