using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    [TestFixture]
    public sealed class MatchmakingTests
    {
        private static readonly long Year = SimulationTime.TicksPerYear;

        // A table nobody dies to and nobody conceives on, so that a run
        // measures the matchmaker and nothing else.
        private static readonly DemographicSettings Immortal = new DemographicSettings
        {
            InfantMortalityPerMille = 0,
            ChildMortalityPerMille = 0,
            AdolescentMortalityPerMille = 0,
            AdultMortalityPerMille = 0,
            ElderMortalityPerMille = 0,
            SoftLifespanYears = 1_000L,
            MaxLifespanYears = 2_000L,
            ConceptionPerMille = 0,
        };

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var w = new DemographicWorld();

            Assert.Multiple(() =>
            {
                Assert.That(() => new Matchmaking(null!, w.People, w.Family, w.Partnerships, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new Matchmaking(w.Bus, null!, w.Family, w.Partnerships, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new Matchmaking(w.Bus, w.People, null!, w.Partnerships, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new Matchmaking(w.Bus, w.People, w.Family, null!, w.Rng), Throws.ArgumentNullException);
                Assert.That(() => new Matchmaking(w.Bus, w.People, w.Family, w.Partnerships, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void The_chance_falls_with_the_age_gap_to_a_floor()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Matchmaking.ChancePerMille(0L), Is.EqualTo(Matchmaking.BaseChancePerMille));
                Assert.That(Matchmaking.ChancePerMille(1L), Is.EqualTo(Matchmaking.BaseChancePerMille - Matchmaking.PerYearOfGapPerMille));
                Assert.That(Matchmaking.ChancePerMille(-1L), Is.EqualTo(Matchmaking.ChancePerMille(1L)), "a gap has no sign");
                Assert.That(Matchmaking.ChancePerMille(8L), Is.EqualTo(Matchmaking.FloorChancePerMille));
                Assert.That(Matchmaking.ChancePerMille(40L), Is.EqualTo(Matchmaking.FloorChancePerMille));
                Assert.That(Matchmaking.ChancePerMille(long.MaxValue), Is.EqualTo(Matchmaking.FloorChancePerMille), "no overflow");
                Assert.That(Matchmaking.ChancePerMille(long.MinValue + 1L), Is.EqualTo(Matchmaking.FloorChancePerMille));
            });
        }

        [Test]
        public void Tracking_books_a_courtship_a_year_out_and_refuses_a_second_stream()
        {
            var w = new DemographicWorld();
            var band = w.NewBand();
            var pending = w.Clock.ScheduledCount;

            w.Matchmaking.Track(band);

            Assert.Multiple(() =>
            {
                Assert.That(w.Matchmaking.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending + 1));
                Assert.That(() => w.Matchmaking.Track(band), Throws.InvalidOperationException);
                Assert.That(() => w.Matchmaking.Track(null!), Throws.ArgumentNullException);
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending + 1), "the refusal booked nothing");
            });

            w.Matchmaking.Untrack(band);

            Assert.Multiple(() =>
            {
                Assert.That(w.Matchmaking.TrackedCount, Is.Zero);
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending), "the courtship is cancelled");
                Assert.That(() => w.Matchmaking.Untrack(band), Throws.InvalidOperationException);
                Assert.That(() => w.Matchmaking.Untrack(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Only_pairs_the_rules_allow_and_not_everyone_in_the_first_year()
        {
            // Ten women and ten men of one age, all eligible: at a quarter
            // chance per pair each woman is likely matched within the year,
            // but with ten candidates each the odds are not certainty, and
            // across seeds some year-one pools stay partly single. What is
            // certain: nobody marries a sibling, a child, or twice.
            var w = new DemographicWorld(Immortal, 3UL);
            var band = w.NewBand();
            var women = new List<PersonHandle>();
            var men = new List<PersonHandle>();

            for (var i = 0; i < 10; i++)
            {
                women.Add(Join(w, band, 25L, Sex.Female));
                men.Add(Join(w, band, 25L, Sex.Male));
            }

            var child = w.NewChild(5L, Sex.Female, women[0], men[0]);
            band.AddMember(child);
            w.Matchmaking.Track(band);

            w.Advance(Year);

            var marriages = w.Published(DomainEventKind.MarriageFormed);

            Assert.Multiple(() =>
            {
                Assert.That(marriages, Is.Not.Empty, "someone married");
                Assert.That(marriages, Has.Count.LessThanOrEqualTo(10));
                Assert.That(w.Partnerships.ActivePartnerOf(w.IdOf(child)).IsNone, Is.True, "a child does not marry");

                foreach (var marriage in marriages)
                {
                    Assert.That(marriage.PrimaryEntity, Is.Not.EqualTo(marriage.SecondaryEntity));
                    Assert.That(w.People.GetSex(Handle(w, marriage.PrimaryEntity)), Is.EqualTo(Sex.Female));
                    Assert.That(w.People.GetSex(Handle(w, marriage.SecondaryEntity)), Is.EqualTo(Sex.Male));
                }

                w.Base.AssertHouseholdsConsistent();
            });
        }

        [Test]
        public void A_pair_the_rules_refuse_never_marries_however_long_they_wait()
        {
            // A brother and sister with nobody else to consider: the draw
            // would accept them within a few years, and the rulebook refuses
            // every time. If the matchmaker ever skipped the rulebook,
            // Partner would throw here rather than marry them.
            var w = new DemographicWorld(Immortal, 4UL);
            var band = w.NewBand();
            var mother = w.NewPerson(60L, Sex.Female);
            var father = w.NewPerson(60L, Sex.Male);
            band.AddMember(w.NewChild(25L, Sex.Female, mother, father));
            band.AddMember(w.NewChild(24L, Sex.Male, mother, father));
            w.Matchmaking.Track(band);

            Assert.That(() => w.Advance(30L * Year), Throws.Nothing);
            Assert.That(w.Published(DomainEventKind.MarriageFormed), Is.Empty);
        }

        [Test]
        public void Marriages_spread_over_years_rather_than_all_at_once()
        {
            // One woman and one man, both eligible from the start: they marry
            // in some year, but at a quarter chance a year, not necessarily
            // the first. Across a handful of seeds the year varies.
            var years = new List<long>();

            for (var seed = 1UL; seed <= 12UL; seed++)
            {
                var w = new DemographicWorld(Immortal, seed);
                var band = w.NewBand();
                Join(w, band, 25L, Sex.Female);
                Join(w, band, 25L, Sex.Male);
                w.Matchmaking.Track(band);

                var married = -1L;

                for (var year = 1L; year <= 30L && married < 0L; year++)
                {
                    w.Advance(Year);

                    if (w.Published(DomainEventKind.MarriageFormed).Count > 0)
                    {
                        married = year;
                    }
                }

                years.Add(married);
            }

            Assert.Multiple(() =>
            {
                Assert.That(years, Has.None.EqualTo(-1L), "everyone married within thirty years");
                Assert.That(years, Is.Not.All.EqualTo(years[0]), "not all in the same year");
            });
        }

        [Test]
        public void A_smaller_age_gap_marries_sooner_on_average()
        {
            var close = AverageYearsToMarry(25L, 26L);
            var far = AverageYearsToMarry(25L, 44L);

            TestContext.Out.WriteLine("close: " + close + " years; far: " + far + " years");
            Assert.That(close, Is.LessThan(far));
        }

        [Test]
        public void The_same_seed_gives_the_same_weddings()
        {
            var first = WeddingsFor(5UL);
            var again = WeddingsFor(5UL);

            Assert.That(again, Is.EqualTo(first));
        }

        [Test]
        public void Only_the_courtship_the_community_booked_runs()
        {
            var w = new DemographicWorld();
            var band = w.NewBand();
            w.Matchmaking.Track(band);
            w.Clock.Schedule(
                w.Clock.Now.Plus(1L), Matchmaking.Phase, ScheduledEventKind.CourtshipDue, band.Id, EntityId.None);

            Assert.That(() => w.Advance(1L), Throws.InvalidOperationException);

            var stranger = new EntityId(EntityKind.MobileGroup, 999UL);
            w.Clock.Schedule(
                w.Clock.Now.Plus(1L), Matchmaking.Phase, ScheduledEventKind.CourtshipDue, stranger, EntityId.None);

            Assert.That(() => w.Advance(1L), Throws.InvalidOperationException);
        }

        [Test]
        public void Handling_refuses_another_kind_or_another_clock()
        {
            var w = new DemographicWorld();
            var other = new SimulationClock(new IdAllocator());
            var meal = new ScheduledEvent(
                new EventId(1UL), w.Clock.Now, Matchmaking.Phase, ScheduledEventKind.MealDue, EntityId.None, EntityId.None);
            var courtship = new ScheduledEvent(
                new EventId(1UL), w.Clock.Now, Matchmaking.Phase, ScheduledEventKind.CourtshipDue, EntityId.None, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Matchmaking.Handle(meal, w.Clock), Throws.InvalidOperationException);
                Assert.That(() => w.Matchmaking.Handle(courtship, other), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void The_stream_ends_with_time_itself()
        {
            var w = new DemographicWorld();
            var band = w.Base.NewBand();
            var end = new SimulationTime(long.MaxValue);
            w.Clock.AdvanceTo(end.Plus(-Year), w.Router);
            w.Matchmaking.Track(band);

            Assert.That(() => w.AdvanceTo(end), Throws.Nothing);
            Assert.That(w.Clock.ScheduledCount, Is.Zero);
        }

        private static PersonHandle Join(DemographicWorld w, MobileGroup band, long ageYears, Sex sex)
        {
            var person = w.NewPerson(ageYears, sex);
            band.AddMember(person);
            return person;
        }

        private static PersonHandle Handle(DemographicWorld w, EntityId id)
        {
            Assert.That(w.People.TryGetHandle(id, out var handle), Is.True);
            return handle;
        }

        private static double AverageYearsToMarry(long herAge, long hisAge)
        {
            var total = 0L;
            const int Seeds = 40;

            for (var seed = 1UL; seed <= Seeds; seed++)
            {
                var w = new DemographicWorld(Immortal, seed);
                var band = w.NewBand();
                Join(w, band, herAge, Sex.Female);
                Join(w, band, hisAge, Sex.Male);
                w.Matchmaking.Track(band);
                var year = 0L;

                while (w.Published(DomainEventKind.MarriageFormed).Count == 0 && year < 60L)
                {
                    w.Advance(Year);
                    year++;
                }

                total += year;
            }

            return total / (double)Seeds;
        }

        private static List<(EntityId, EntityId)> WeddingsFor(ulong seed)
        {
            var w = new DemographicWorld(Immortal, seed);
            var band = w.NewBand();

            for (var i = 0; i < 6; i++)
            {
                Join(w, band, 20L + i, Sex.Female);
                Join(w, band, 22L + i, Sex.Male);
            }

            w.Matchmaking.Track(band);
            w.Advance(5L * Year);

            var weddings = new List<(EntityId, EntityId)>();

            foreach (var marriage in w.Published(DomainEventKind.MarriageFormed))
            {
                weddings.Add((marriage.PrimaryEntity, marriage.SecondaryEntity));
            }

            return weddings;
        }
    }
}
