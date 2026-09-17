using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Tests.Lifecycle;
using KingdomWatch.Core.Work;
using KingdomWatch.Core.WorldGen;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.WorldGen
{
    [TestFixture]
    public sealed class BandGeneratorTests
    {
        private static readonly WorldPosition Here = new WorldPosition(3, 4);

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var w = new DemographicWorld();
            var s = DemographicSettings.Default;

            Assert.Multiple(() =>
            {
                Assert.That(() => new BandGenerator(null!, w.People, w.Genealogy, w.Family, w.Households, s, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new BandGenerator(w.Bus, null!, w.Genealogy, w.Family, w.Households, s, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new BandGenerator(w.Bus, w.People, null!, w.Family, w.Households, s, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new BandGenerator(w.Bus, w.People, w.Genealogy, null!, w.Households, s, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new BandGenerator(w.Bus, w.People, w.Genealogy, w.Family, null!, s, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new BandGenerator(w.Bus, w.People, w.Genealogy, w.Family, w.Households, null!, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new BandGenerator(w.Bus, w.People, w.Genealogy, w.Family, w.Households, s, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Lineages_scale_with_size_between_the_bounds_and_never_past_the_pairs()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BandGenerator.LineagesFor(30), Is.EqualTo(BandGenerator.MinLineages), "the smallest starting band");
                Assert.That(BandGenerator.LineagesFor(45), Is.EqualTo(9));
                Assert.That(BandGenerator.LineagesFor(60), Is.EqualTo(BandGenerator.MaxLineages), "the largest");
                Assert.That(BandGenerator.LineagesFor(200), Is.EqualTo(BandGenerator.MaxLineages));
                Assert.That(BandGenerator.LineagesFor(10), Is.EqualTo(5), "no more couples than pairs");
                Assert.That(BandGenerator.LineagesFor(2), Is.EqualTo(1));
            });
        }

        [TestCase(30)]
        [TestCase(45)]
        [TestCase(60)]
        public void A_band_is_the_size_asked_for_with_unrelated_founding_couples(int size)
        {
            var w = new DemographicWorld();

            var band = w.Generator.Generate(size, Here);

            var couples = BandGenerator.LineagesFor(size);
            var founders = 0;
            var children = 0;
            var stages = new HashSet<AgeStage>();

            foreach (var member in band.Members)
            {
                stages.Add(w.People.GetAgeStage(member));

                if (w.Genealogy.Parents(w.IdOf(member)).Mother.IsNone)
                {
                    founders++;
                }
                else
                {
                    children++;
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(band.Purpose, Is.EqualTo(MobileGroupPurpose.NomadicBand));
                Assert.That(band.Position, Is.EqualTo(Here));
                Assert.That(band.Destination, Is.Null);
                Assert.That(band.Members, Has.Count.EqualTo(size));
                Assert.That(w.People.Count, Is.EqualTo(size));
                Assert.That(founders, Is.GreaterThanOrEqualTo(2 * couples), "every couple is two founders");
                Assert.That(children, Is.GreaterThan(0));
                Assert.That(w.Households.Count, Is.EqualTo(couples), "one household per couple; singles are unhoused");
                Assert.That(w.Published(DomainEventKind.MarriageFormed), Has.Count.EqualTo(couples));
                Assert.That(w.Published(DomainEventKind.PersonBorn), Has.Count.EqualTo(size), "everyone announced");
                Assert.That(stages, Does.Contain(AgeStage.Adult));
                Assert.That(stages.Count, Is.GreaterThanOrEqualTo(3), "not everyone is the same age");
                w.Base.AssertHouseholdsConsistent();
            });

            // No founder is kin to any other founder: the lineages are
            // genuinely unrelated, so the mating pool starts wide.
            var founderHandles = new List<PersonHandle>();

            foreach (var member in band.Members)
            {
                if (w.Genealogy.Parents(w.IdOf(member)).Mother.IsNone)
                {
                    founderHandles.Add(member);
                }
            }

            for (var i = 0; i < founderHandles.Count; i++)
            {
                for (var j = i + 1; j < founderHandles.Count; j++)
                {
                    Assert.That(
                        w.Genealogy.Kinship(w.IdOf(founderHandles[i]), w.IdOf(founderHandles[j])),
                        Is.EqualTo(KinshipDegree.None));
                }
            }
        }

        [Test]
        public void Children_belong_to_their_parents_household_and_are_younger_than_the_parents_allow()
        {
            var w = new DemographicWorld();
            var band = w.Generator.Generate(60, Here);
            var now = w.Clock.Now;

            foreach (var member in band.Members)
            {
                var parents = w.Genealogy.Parents(w.IdOf(member));

                if (parents.Mother.IsNone)
                {
                    continue;
                }

                Assert.That(w.People.TryGetHandle(parents.Mother, out var mother), Is.True);
                Assert.That(w.People.TryGetHandle(parents.Father, out var father), Is.True);
                var age = w.People.GetAgeYears(member, now);

                Assert.Multiple(() =>
                {
                    Assert.That(age, Is.LessThanOrEqualTo(BandGenerator.MaxChildYears));
                    Assert.That(w.People.GetAgeYears(mother, now) - age, Is.GreaterThanOrEqualTo(BandGenerator.MinParentYears));
                    Assert.That(w.People.GetAgeYears(father, now) - age, Is.GreaterThanOrEqualTo(BandGenerator.MinParentYears));
                    Assert.That(w.Households.Of(member), Is.SameAs(w.Households.Of(mother)), "housed with the parents");
                    Assert.That(w.Partnerships.ActivePartnerOf(parents.Mother), Is.EqualTo(parents.Father), "the parents are a couple");
                });
            }
        }

        [Test]
        public void Everyone_is_announced_once_the_whole_band_exists()
        {
            // A subscriber to PersonBorn sees a person who fully exists, the
            // way Fertility announces a birth after placing the child: in the
            // genealogy, and in the household the band was generated with -
            // thirty is eight couples and fourteen children, no singles.
            var w = new DemographicWorld();
            var witness = new Witness(w);
            w.Bus.Subscribe(witness);

            var band = w.Generator.Generate(30, Here);

            Assert.Multiple(() =>
            {
                Assert.That(witness.Seen, Is.EqualTo(30));
                Assert.That(witness.NotRecorded, Is.Zero, "announced before the genealogy knew them");
                Assert.That(witness.Unhoused, Is.Zero, "announced before their household stood");
                Assert.That(witness.WholeAt, Is.EqualTo(30), "announced before the band was whole");
            });
        }

        private sealed class Witness : IDomainEventSubscriber
        {
            private readonly DemographicWorld _w;

            internal Witness(DemographicWorld w)
            {
                _w = w;
            }

            internal int Seen { get; private set; }

            internal int NotRecorded { get; private set; }

            internal int Unhoused { get; private set; }

            private int BandSize { get; set; } = int.MaxValue;

            // People.Count is the band: nothing else exists in this world.
            // Its lowest value at an announcement is how whole the band was
            // when the first person was announced.
            internal int WholeAt => BandSize;

            public void On(in DomainEvent published)
            {
                if (published.Kind != DomainEventKind.PersonBorn)
                {
                    return;
                }

                Seen++;
                Assert.That(_w.People.TryGetHandle(published.PrimaryEntity, out var person), Is.True);

                NotRecorded += _w.Genealogy.IsRecorded(published.PrimaryEntity) ? 0 : 1;
                Unhoused += _w.Households.Of(person) is null ? 1 : 0;
                BandSize = System.Math.Min(BandSize, _w.People.Count);
            }
        }

        [Test]
        public void Everyone_stands_with_the_band_fed_and_whole()
        {
            var w = new DemographicWorld();
            var band = w.Generator.Generate(30, Here);

            foreach (var member in band.Members)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(w.People.GetPosition(member), Is.EqualTo(Here));
                    Assert.That(w.People.GetLastFedAt(member), Is.EqualTo(w.Clock.Now));
                    Assert.That(w.People.GetHealth(member), Is.EqualTo(100));
                    Assert.That(w.People.GetAgeStage(member), Is.EqualTo(w.Settings.StageAt(w.People.GetAgeYears(member, w.Clock.Now))));
                });
            }
        }

        [Test]
        public void The_oldest_adult_leads_and_the_band_is_stocked()
        {
            var w = new DemographicWorld();
            var band = w.Generator.Generate(45, Here);
            var now = w.Clock.Now;
            var oldest = -1L;

            foreach (var member in band.Members)
            {
                if (AgeStages.IsAdult(w.People.GetAgeStage(member)))
                {
                    oldest = System.Math.Max(oldest, w.People.GetAgeYears(member, now));
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(band.Leader.IsNone, Is.False);
                Assert.That(band.Members, Does.Contain(band.Leader));
                Assert.That(w.People.GetAgeYears(band.Leader, now), Is.EqualTo(oldest));
                Assert.That(band.SharedSupplies.Available(ResourceKind.Food), Is.EqualTo(45 * Hunger.DailyRation * Jobs.FoodTargetDays));
                Assert.That(band.SharedSupplies.Available(ResourceKind.Wood), Is.EqualTo(BandGenerator.StartingWood));
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Opening, Is.EqualTo(45L * Hunger.DailyRation * Jobs.FoodTargetDays), "opening stock, not production");
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Wood).Opening, Is.EqualTo((long)BandGenerator.StartingWood));
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Gathered, Is.Zero, "nobody has worked yet");
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Wood).Gathered, Is.Zero);
                Assert.That(band.SharedSupplies.Available(ResourceKind.Stone), Is.Zero);
                Assert.That(band.SharedSupplies.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void The_same_seed_makes_the_same_band_and_another_seed_a_different_one()
        {
            var first = Ages(1UL);
            var again = Ages(1UL);
            var other = Ages(2UL);

            Assert.Multiple(() =>
            {
                Assert.That(again, Is.EqualTo(first));
                Assert.That(other, Is.Not.EqualTo(first));
            });
        }

        [Test]
        public void Two_bands_from_one_world_do_not_share_people_or_draws()
        {
            var w = new DemographicWorld();

            var first = w.Generator.Generate(30, Here);
            var second = w.Generator.Generate(30, new WorldPosition(7, 7));

            Assert.Multiple(() =>
            {
                Assert.That(first.Id, Is.Not.EqualTo(second.Id));
                Assert.That(w.People.Count, Is.EqualTo(60));
                Assert.That(first.Members, Has.None.AnyOf(second.Members));
                Assert.That(AgesOf(w, first), Is.Not.EqualTo(AgesOf(w, second)), "keyed by band, not by index alone");
            });
        }

        [TestCase(2, 1, 0)]
        [TestCase(3, 1, 0)]
        [TestCase(7, 3, 0)]
        [TestCase(100, 12, 28)]
        public void Small_bands_are_couples_and_large_ones_have_singles(int size, int couples, int singles)
        {
            // Under sixteen there are fewer couples than the minimum wants;
            // past what twelve couples and four children each can hold, the
            // rest are single adults, each a lineage of their own.
            var w = new DemographicWorld();

            var band = w.Generator.Generate(size, Here);

            var founders = 0;

            foreach (var member in band.Members)
            {
                if (w.Genealogy.Parents(w.IdOf(member)).Mother.IsNone)
                {
                    founders++;
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(band.Members, Has.Count.EqualTo(size));
                Assert.That(BandGenerator.LineagesFor(size), Is.EqualTo(couples));
                Assert.That(founders, Is.EqualTo(2 * couples + singles));
                Assert.That(w.Households.Count, Is.EqualTo(couples));
                Assert.That(band.Leader.IsNone, Is.False);
                w.Base.AssertHouseholdsConsistent();
            });
        }

        [Test]
        public void Generation_refuses_fewer_than_two()
        {
            var w = new DemographicWorld();

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Generator.Generate(1, Here), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Generator.Generate(0, Here), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(w.People.Count, Is.Zero);
            });
        }

        private static List<long> Ages(ulong seed)
        {
            var w = new DemographicWorld(DemographicSettings.Default, seed);
            return AgesOf(w, w.Generator.Generate(30, Here));
        }

        private static List<long> AgesOf(DemographicWorld w, MobileGroup band)
        {
            var ages = new List<long>();

            foreach (var member in band.Members)
            {
                ages.Add(w.People.GetAgeYears(member, w.Clock.Now));
            }

            return ages;
        }
    }
}
