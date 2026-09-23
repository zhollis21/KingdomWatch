using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Tests.Lifecycle;
using KingdomWatch.Core.Tests.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Needs
{
    [TestFixture]
    public sealed class WarmthTests
    {
        private static readonly long Day = SimulationTime.TicksPerDay;

        // The first day of winter, and the first winter night.
        private static readonly SimulationTime FirstWinterDay = SimulationTime.FromDays(3L * SimulationTime.DaysPerSeason);
        private static readonly SimulationTime FirstWinterNight = FirstWinterDay.Plus(Warmth.Nightfall);

        // Stands in for Mortality, as HungerTests' does.
        private sealed class Crossings : IScheduledEventHandler
        {
            internal List<ScheduledEvent> Raised { get; } = new List<ScheduledEvent>();

            public void Handle(ScheduledEvent scheduled, SimulationClock clock) => Raised.Add(scheduled);
        }

        // Households, so there is something to keep a hearth; Warmth owns
        // WarmthDue and a recorder answers the crossing Mortality would.
        private sealed class World
        {
            internal World()
            {
                Base = new HouseholdWorld();
                Warmth = new Warmth(Base.Clock, Base.People);
                Exposure = new Crossings();
                Router = new ScheduledEventRouter();
                Router.Register(ScheduledEventKind.WarmthDue, Warmth);
                Router.Register(ScheduledEventKind.ExposureCritical, Exposure);
            }

            internal HouseholdWorld Base { get; }

            internal Warmth Warmth { get; }

            internal Crossings Exposure { get; }

            internal ScheduledEventRouter Router { get; }

            internal SimulationClock Clock => Base.Clock;

            internal PersonStore People => Base.People;

            internal MobileGroup NewBand(int wood)
            {
                var band = Base.NewBand();

                if (wood > 0)
                {
                    band.SharedSupplies.Gather(ResourceKind.Wood, wood);
                }

                return band;
            }

            // A couple housed together, in the band; with a child when asked.
            internal Household NewFamily(MobileGroup band, bool withChild, out PersonHandle wife, out PersonHandle husband)
            {
                var household = Base.NewCouple(out wife, out husband);
                band.AddMember(wife);
                band.AddMember(husband);

                if (withChild)
                {
                    band.AddMember(Base.NewChildOf(household, wife, husband, AgeStage.Child));
                }

                return household;
            }

            // Someone in no household: the communal fire's.
            internal PersonHandle NewLoner(MobileGroup band)
            {
                var loner = Base.NewPerson(AgeStage.Adult, Sex.Male);
                band.AddMember(loner);
                return loner;
            }

            internal void AdvanceTo(SimulationTime time) => Clock.AdvanceTo(time, Router);

            internal int Wood(MobileGroup band) => band.SharedSupplies.Available(ResourceKind.Wood);
        }

        private static SimulationTime NextTime(SimulationClock clock)
        {
            Assert.That(clock.TryPeekNext(out var next), Is.True);
            return next.Time;
        }

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var world = new World();

            Assert.Multiple(() =>
            {
                Assert.That(() => new Warmth(null!, world.People), Throws.ArgumentNullException);
                Assert.That(() => new Warmth(world.Clock, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Tracking_books_the_next_nightfall_and_refuses_the_same_community_twice()
        {
            var world = new World();
            var band = world.NewBand(0);

            world.Warmth.Track(band);

            var bookings = new List<PendingBooking>();
            world.Warmth.CopyBookingsTo(bookings);

            Assert.Multiple(() =>
            {
                Assert.That(world.Warmth.TrackedCount, Is.EqualTo(1));
                Assert.That(world.Warmth.IsTracked(band), Is.True);
                Assert.That(bookings, Has.Count.EqualTo(1));
                Assert.That(bookings[0].Kind, Is.EqualTo(ScheduledEventKind.WarmthDue));
                Assert.That(NextTime(world.Clock), Is.EqualTo(SimulationTime.Zero.Plus(Warmth.Nightfall)));
                Assert.That(() => world.Warmth.Track(band), Throws.InvalidOperationException);
                Assert.That(() => world.Warmth.Track(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Tracked_at_nightfall_it_burns_tomorrow_not_twice_today()
        {
            var world = new World();
            var band = world.NewBand(0);
            world.Base.Advance(Warmth.Nightfall);

            world.Warmth.Track(band);

            Assert.That(NextTime(world.Clock), Is.EqualTo(SimulationTime.FromDays(1L).Plus(Warmth.Nightfall)));
        }

        [Test]
        public void Untracking_cancels_the_evening_and_refuses_a_stranger()
        {
            var world = new World();
            var band = world.NewBand(0);
            world.Warmth.Track(band);

            world.Warmth.Untrack(band);

            var bookings = new List<PendingBooking>();
            world.Warmth.CopyBookingsTo(bookings);

            Assert.Multiple(() =>
            {
                Assert.That(world.Warmth.TrackedCount, Is.Zero);
                Assert.That(bookings, Is.Empty);
                Assert.That(world.Clock.ScheduledCount, Is.Zero);
                Assert.That(() => world.Warmth.Untrack(band), Throws.InvalidOperationException);
                Assert.That(() => world.Warmth.Untrack(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Outside_winter_nothing_burns_and_everyone_sleeps_warm()
        {
            var world = new World();
            var band = world.NewBand(50);
            world.NewFamily(band, withChild: true, out var wife, out _);
            var loner = world.NewLoner(band);
            world.Warmth.Track(band);

            var lastAutumnNight = FirstWinterNight.Plus(-Day);
            world.AdvanceTo(lastAutumnNight);

            Assert.Multiple(() =>
            {
                Assert.That(world.Wood(band), Is.EqualTo(50), "ninety evenings, none of them winter");
                Assert.That(world.People.GetLastWarmedAt(wife), Is.EqualTo(lastAutumnNight));
                Assert.That(world.People.GetLastWarmedAt(loner), Is.EqualTo(lastAutumnNight));
            });
        }

        [Test]
        public void A_winter_night_burns_one_fire_per_household_and_one_for_those_in_none()
        {
            var world = new World();
            var band = world.NewBand(50);
            world.NewFamily(band, withChild: true, out _, out _);
            world.NewFamily(band, withChild: false, out _, out _);
            world.NewLoner(band);
            world.NewLoner(band);
            world.Warmth.Track(band);

            world.AdvanceTo(FirstWinterNight.Plus(-1L));
            var before = world.Wood(band);
            world.AdvanceTo(FirstWinterNight);

            Assert.Multiple(() =>
            {
                Assert.That(before, Is.EqualTo(50));
                Assert.That(world.Wood(band), Is.EqualTo(50 - (3 * Warmth.FuelPerFire)), "two hearths and one communal fire");
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Wood).Consumed, Is.EqualTo(3L * Warmth.FuelPerFire));
                Assert.That(
                    Warmth.CountHearths(band.Members, world.People, new List<EntityId>()), Is.EqualTo(3));
            });
        }

        [Test]
        public void Short_of_wood_the_homes_with_children_are_lit_first_and_the_communal_fire_last()
        {
            var world = new World();
            var band = world.NewBand(0);

            // Formed first, childless; then a family; then someone in no
            // household. One fire's worth of wood goes to the family, though
            // the childless couple's household is older; two reaches the
            // couple too, and the loner is cold either way.
            world.NewFamily(band, withChild: false, out var elderWife, out _);
            world.NewFamily(band, withChild: true, out var mother, out _);
            var loner = world.NewLoner(band);
            world.Warmth.Track(band);

            world.AdvanceTo(FirstWinterNight.Plus(-1L));
            band.SharedSupplies.Gather(ResourceKind.Wood, Warmth.FuelPerFire);
            world.AdvanceTo(FirstWinterNight);

            var oneFire = (
                Mother: world.People.GetLastWarmedAt(mother),
                Couple: world.People.GetLastWarmedAt(elderWife),
                Loner: world.People.GetLastWarmedAt(loner));

            band.SharedSupplies.Gather(ResourceKind.Wood, 2 * Warmth.FuelPerFire);
            world.AdvanceTo(FirstWinterNight.Plus(Day));

            Assert.Multiple(() =>
            {
                Assert.That(oneFire.Mother, Is.EqualTo(FirstWinterNight), "the family's hearth first");
                Assert.That(oneFire.Couple, Is.LessThan(FirstWinterNight), "the older household waits");
                Assert.That(oneFire.Loner, Is.LessThan(FirstWinterNight));
                Assert.That(world.People.GetLastWarmedAt(elderWife), Is.EqualTo(FirstWinterNight.Plus(Day)), "two fires reach the couple");
                Assert.That(world.People.GetLastWarmedAt(loner), Is.LessThan(FirstWinterNight), "the communal fire last");
                Assert.That(world.Wood(band), Is.Zero);
            });
        }

        [Test]
        public void Among_households_alike_the_older_is_lit_first()
        {
            var world = new World();
            var band = world.NewBand(0);
            world.NewFamily(band, withChild: false, out var older, out _);
            world.NewFamily(band, withChild: false, out var younger, out _);
            world.Warmth.Track(band);

            world.AdvanceTo(FirstWinterNight.Plus(-1L));
            band.SharedSupplies.Gather(ResourceKind.Wood, Warmth.FuelPerFire);
            world.AdvanceTo(FirstWinterNight);

            Assert.Multiple(() =>
            {
                Assert.That(world.People.GetLastWarmedAt(older), Is.EqualTo(FirstWinterNight));
                Assert.That(world.People.GetLastWarmedAt(younger), Is.LessThan(FirstWinterNight));
            });
        }

        [Test]
        public void The_lighting_order_does_not_depend_on_the_order_members_are_listed()
        {
            // Four households formed oldest first, their members then listed
            // youngest household first and interleaved, with the two families
            // with a child in the middle. Hearths still count once each, and
            // light children's homes first, then the rest oldest first.
            var world = new World();
            var staging = world.NewBand(0);
            var oldCouple = world.NewFamily(staging, withChild: false, out var oldWife, out var oldHusband);
            var firstFamily = world.NewFamily(staging, withChild: true, out var firstMother, out _);
            var youngCouple = world.NewFamily(staging, withChild: false, out var youngWife, out var youngHusband);
            var secondFamily = world.NewFamily(staging, withChild: true, out var secondMother, out _);

            var band = world.NewBand(0);

            for (var i = staging.Members.Count - 1; i >= 0; i--)
            {
                band.AddMember(staging.Members[i]);
            }

            band.RemoveMember(youngHusband);
            band.AddMember(youngHusband);
            band.RemoveMember(oldHusband);
            band.AddMember(oldHusband);
            world.Warmth.Track(band);

            var scratch = new List<EntityId>();
            var hearths = Warmth.CountHearths(band.Members, world.People, scratch);

            world.AdvanceTo(FirstWinterNight.Plus(-1L));
            band.SharedSupplies.Gather(ResourceKind.Wood, 3 * Warmth.FuelPerFire);
            world.AdvanceTo(FirstWinterNight);

            Assert.Multiple(() =>
            {
                Assert.That(hearths, Is.EqualTo(4));
                Assert.That(scratch, Is.EqualTo(new[] { oldCouple.Id, firstFamily.Id, youngCouple.Id, secondFamily.Id }), "distinct, in id order");
                Assert.That(world.People.GetLastWarmedAt(firstMother), Is.EqualTo(FirstWinterNight));
                Assert.That(world.People.GetLastWarmedAt(secondMother), Is.EqualTo(FirstWinterNight));
                Assert.That(world.People.GetLastWarmedAt(oldWife), Is.EqualTo(FirstWinterNight), "the older childless household next");
                Assert.That(world.People.GetLastWarmedAt(youngWife), Is.LessThan(FirstWinterNight), "the younger one is out of wood");
                Assert.That(world.People.GetLastWarmedAt(youngHusband), Is.LessThan(FirstWinterNight));
            });
        }

        [Test]
        public void Cold_costs_health_only_past_the_grace_period()
        {
            var world = new World();
            var band = world.NewBand(0);
            var loner = world.NewLoner(band);
            world.Warmth.Track(band);

            // Warm on the last autumn night; winter nights one and two are
            // within grace, and the third costs health.
            world.AdvanceTo(FirstWinterNight.Plus(Day));
            var afterTwo = world.People.GetHealth(loner);
            world.AdvanceTo(FirstWinterNight.Plus(2L * Day));

            Assert.Multiple(() =>
            {
                Assert.That(afterTwo, Is.EqualTo(100));
                Assert.That(world.People.GetHealth(loner), Is.EqualTo(100 - Warmth.ExposureDamagePerNight));
                Assert.That(world.People.GetLastWarmedAt(loner), Is.EqualTo(FirstWinterNight.Plus(-Day)));
                Assert.That(world.Exposure.Raised, Is.Empty);
            });
        }

        [Test]
        public void The_night_that_reaches_zero_raises_the_crossing_once_for_the_same_instant()
        {
            var world = new World();
            var band = world.NewBand(0);
            var loner = world.NewLoner(band);
            world.Warmth.Track(band);
            world.People.SetHealth(loner, Warmth.ExposureDamagePerNight);

            var fatal = FirstWinterNight.Plus(2L * Day);
            world.AdvanceTo(fatal.Plus(Day));

            Assert.Multiple(() =>
            {
                Assert.That(world.People.GetHealth(loner), Is.Zero, "never below zero");
                Assert.That(world.Exposure.Raised, Has.Count.EqualTo(1), "raised once, not every night after");
                Assert.That(world.Exposure.Raised[0].Time, Is.EqualTo(fatal));
                Assert.That(world.Exposure.Raised[0].Phase, Is.EqualTo(Mortality.Phase));
                Assert.That(world.Exposure.Raised[0].PrimaryEntity, Is.EqualTo(world.Base.IdOf(loner)));
            });
        }

        [Test]
        public void A_winter_with_no_wood_anywhere_kills_and_the_death_reads_froze()
        {
            // The whole chain in a world that works: plains only, so nobody
            // can cut wood, and food enough that hunger is not the cause.
            // Two nights of grace, then ten nights at ten health each: the
            // twelfth winter night is the last.
            var w = new WorkWorld(1UL, WorkWorld.PlainsOnly());
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1) * 2);
            var person = w.Join(band, 30L);
            var id = w.People.GetId(person);
            var fatal = FirstWinterNight.Plus(11L * Day);

            w.AdvanceTo(fatal.Plus(-1L));
            var justBefore = w.People.GetHealth(person);
            w.AdvanceTo(fatal);

            var deaths = w.Demographics.Published(DomainEventKind.PersonDied);

            Assert.Multiple(() =>
            {
                Assert.That(justBefore, Is.EqualTo(Warmth.ExposureDamagePerNight));
                Assert.That(w.People.IsAlive(person), Is.False);
                Assert.That(deaths, Has.Count.EqualTo(1));
                Assert.That(deaths[0].PrimaryEntity, Is.EqualTo(id));
                Assert.That(deaths[0].Time, Is.EqualTo(fatal));
                Assert.That(deaths[0].Reasons.Contains(ReasonCode.Froze), Is.True);
                Assert.That(w.Count(DomainEventKind.FamineStarted), Is.Zero, "not hunger");
            });
        }

        [Test]
        public void Spring_ends_the_cold()
        {
            var world = new World();
            var band = world.NewBand(0);
            var loner = world.NewLoner(band);
            world.Warmth.Track(band);

            var firstSpringNight = SimulationTime.FromYears(1L).Plus(Warmth.Nightfall);
            world.AdvanceTo(firstSpringNight);
            var health = world.People.GetHealth(loner);
            world.AdvanceTo(firstSpringNight.Plus(5L * Day));

            Assert.Multiple(() =>
            {
                Assert.That(world.People.GetLastWarmedAt(loner), Is.EqualTo(firstSpringNight.Plus(5L * Day)));
                Assert.That(world.People.GetHealth(loner), Is.EqualTo(health), "no damage once winter is over");
            });
        }

        [Test]
        public void The_dead_are_neither_warmed_nor_counted()
        {
            var world = new World();
            var band = world.NewBand(10);
            world.NewFamily(band, withChild: false, out _, out _);
            var loner = world.NewLoner(band);
            world.Warmth.Track(band);

            // Dead but still listed: membership lags death until the cascade
            // strikes them from the group. Their fire is not lit.
            world.People.Remove(loner);
            world.AdvanceTo(FirstWinterNight);

            Assert.That(world.Wood(band), Is.EqualTo(10 - Warmth.FuelPerFire), "the couple's hearth only");
        }

        [Test]
        public void An_evening_that_is_not_the_communitys_own_is_a_wiring_bug()
        {
            var world = new World();
            var band = world.NewBand(0);
            world.Warmth.Track(band);

            world.Clock.Schedule(
                SimulationTime.Zero.Plus(1L), Warmth.Phase, ScheduledEventKind.WarmthDue, band.Id, EntityId.None);

            Assert.That(() => world.AdvanceTo(SimulationTime.Zero.Plus(1L)), Throws.InvalidOperationException);
        }

        [Test]
        public void Handling_refuses_another_kind_another_clock_and_a_stranger()
        {
            var world = new World();
            var band = world.NewBand(0);
            var meal = new ScheduledEvent(
                new EventId(1UL), SimulationTime.Zero, Warmth.Phase, ScheduledEventKind.MealDue, band.Id, EntityId.None);
            var evening = new ScheduledEvent(
                new EventId(2UL), SimulationTime.Zero, Warmth.Phase, ScheduledEventKind.WarmthDue, band.Id, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => world.Warmth.Handle(meal, world.Clock), Throws.InvalidOperationException);
                Assert.That(
                    () => world.Warmth.Handle(evening, new SimulationClock(new IdAllocator())), Throws.InvalidOperationException);
                Assert.That(() => world.Warmth.Handle(evening, world.Clock), Throws.InvalidOperationException, "not tracked");
            });
        }

        [Test]
        public void Counting_hearths_refuses_missing_arguments_and_clears_the_scratch()
        {
            var world = new World();
            var band = world.NewBand(0);
            world.NewFamily(band, withChild: true, out _, out _);
            var scratch = new List<EntityId> { new EntityId(EntityKind.Household, 999UL) };

            Assert.Multiple(() =>
            {
                Assert.That(Warmth.CountHearths(band.Members, world.People, scratch), Is.EqualTo(1));
                Assert.That(scratch, Has.Count.EqualTo(1), "the stale entry is gone");
                Assert.That(() => Warmth.CountHearths(null!, world.People, scratch), Throws.ArgumentNullException);
                Assert.That(() => Warmth.CountHearths(band.Members, null!, scratch), Throws.ArgumentNullException);
                Assert.That(() => Warmth.CountHearths(band.Members, world.People, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void The_tracked_list_is_every_community_in_tracking_order_and_is_refilled_not_appended()
        {
            var world = new World();
            var first = world.NewBand(0);
            var second = world.NewBand(0);
            world.Warmth.Track(first);
            world.Warmth.Track(second);
            var into = new List<ICommunity> { second, second };

            world.Warmth.CopyTrackedTo(into);

            Assert.That(into, Is.EqualTo(new ICommunity[] { first, second }));
        }

        [Test]
        public void Copying_refuses_null()
        {
            var world = new World();

            Assert.Multiple(() =>
            {
                Assert.That(() => world.Warmth.CopyTrackedTo(null!), Throws.ArgumentNullException);
                Assert.That(() => world.Warmth.CopyBookingsTo(null!), Throws.ArgumentNullException);
                Assert.That(() => world.Warmth.IsTracked(null!), Throws.ArgumentNullException);
            });
        }
    }
}
