using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.History;
using KingdomWatch.Core.Needs;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Needs
{
    [TestFixture]
    public sealed class HungerTests
    {
        private const short StartingHealth = 100;

        private static readonly long Day = SimulationTime.TicksPerDay;

        // Stands in for Mortality: remembers each starvation crossing Hunger
        // raises, so the tests here can see it without the death cascade.
        private sealed class Crossings : IScheduledEventHandler
        {
            internal List<ScheduledEvent> Raised { get; } = new List<ScheduledEvent>();

            public void Handle(ScheduledEvent scheduled, SimulationClock clock) => Raised.Add(scheduled);
        }

        // Everything a meal touches, wired the way a world will wire it: the
        // router drives the clock, hunger owns MealDue, the journal remembers
        // what the bus publishes, and a recorder answers the crossing that
        // Mortality would.
        private sealed class World
        {
            internal World()
            {
                Ids = new IdAllocator();
                Clock = new SimulationClock(Ids);
                Bus = new DomainEventBus(Clock);
                Journal = new EventJournal(16);
                Bus.Subscribe(Journal);
                People = new PersonStore();
                Router = new ScheduledEventRouter();
                Hunger = new Hunger(Bus, People);
                Starvation = new Crossings();
                Router.Register(ScheduledEventKind.MealDue, Hunger);
                Router.Register(ScheduledEventKind.StarvationCritical, Starvation);
            }

            internal Crossings Starvation { get; }

            internal IdAllocator Ids { get; }

            internal SimulationClock Clock { get; }

            internal DomainEventBus Bus { get; }

            internal EventJournal Journal { get; }

            internal PersonStore People { get; }

            internal ScheduledEventRouter Router { get; }

            internal Hunger Hunger { get; }

            internal MobileGroup NewBand(int members, int food)
            {
                var band = new MobileGroup(
                    Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, default);

                for (var i = 0; i < members; i++)
                {
                    band.AddMember(People.Add(
                        Ids.Next(EntityKind.Person), default, StartingHealth, AgeStage.Adult, Sex.Female, 0, 0, Clock.Now, 0L));
                }

                if (food > 0)
                {
                    band.SharedSupplies.Gather(ResourceKind.Food, food);
                }

                return band;
            }

            internal PersonHandle NewMember(MobileGroup band, AgeStage stage)
            {
                var member = People.Add(
                    Ids.Next(EntityKind.Person), default, StartingHealth, stage, Sex.Female, 0, 0, Clock.Now, 0L);
                band.AddMember(member);
                return member;
            }

            internal void RunDays(long days)
            {
                var start = Clock.Now;

                for (var day = 1L; day <= days; day++)
                {
                    Clock.AdvanceTo(start.Plus(day * Day), Router);
                }
            }

            internal List<DomainEventKind> Published()
            {
                var kinds = new List<DomainEventKind>();
                var events = Journal.AsSpan();

                for (var i = 0; i < events.Length; i++)
                {
                    kinds.Add(events[i].Kind);
                }

                return kinds;
            }
        }

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var world = new World();

            Assert.Multiple(() =>
            {
                Assert.That(() => new Hunger(null!, world.People), Throws.ArgumentNullException);
                Assert.That(() => new Hunger(world.Bus, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Tracking_books_the_first_meal_a_day_out()
        {
            var world = new World();
            var band = world.NewBand(2, 30);

            world.Hunger.Track(band);

            Assert.Multiple(() =>
            {
                Assert.That(world.Hunger.TrackedCount, Is.EqualTo(1));
                Assert.That(world.Clock.TryPeekNext(out var next), Is.True);
                Assert.That(next.Kind, Is.EqualTo(ScheduledEventKind.MealDue));
                Assert.That(next.Time, Is.EqualTo(SimulationTime.FromDays(1L)));
                Assert.That(next.Phase, Is.EqualTo(Hunger.MealPhase));
                Assert.That(next.PrimaryEntity, Is.EqualTo(band.Id));
                Assert.That(next.SecondaryEntity, Is.EqualTo(EntityId.None));
            });
        }

        [Test]
        public void Tracking_refuses_null_and_a_second_stream_on_one_holder()
        {
            var world = new World();
            var band = world.NewBand(1, 3);
            world.Hunger.Track(band);

            Assert.Multiple(() =>
            {
                Assert.That(() => world.Hunger.Track(null!), Throws.ArgumentNullException);
                Assert.That(() => world.Hunger.Track(band), Throws.InvalidOperationException);
                Assert.That(world.Clock.ScheduledCount, Is.EqualTo(1), "the refused Track booked nothing");
            });
        }

        [Test]
        public void A_holder_whose_first_meal_cannot_be_booked_is_not_left_half_tracked()
        {
            // The only way the first booking fails is the clock standing within
            // a meal of the end of time. If tracking recorded the holder before
            // booking, it would be tracked with no meal stream, and refuse to
            // be tracked again - fed never, silently.
            var world = new World();
            var band = world.NewBand(1, 3);
            world.Clock.AdvanceTo(new SimulationTime(long.MaxValue - 1L), world.Router);

            Assert.Multiple(() =>
            {
                Assert.That(() => world.Hunger.Track(band), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(world.Hunger.TrackedCount, Is.Zero);
                Assert.That(world.Clock.ScheduledCount, Is.Zero);
                Assert.That(() => world.Hunger.IsInFamine(band), Throws.InvalidOperationException, "not tracked");
            });
        }

        [Test]
        public void The_last_meal_the_world_can_hold_is_served_and_books_nothing_after_it()
        {
            // Booked for the last representable instant, a meal is still a
            // meal. The stream ends there because there is no tomorrow to
            // book into - not by throwing after the ledger has already moved.
            var world = new World();
            var band = world.NewBand(1, 3);
            var lastMeal = new SimulationTime(long.MaxValue);
            world.Clock.AdvanceTo(lastMeal.Plus(-Hunger.MealInterval), world.Router);
            world.Hunger.Track(band);

            var dispatched = world.Clock.AdvanceTo(lastMeal, world.Router);

            Assert.Multiple(() =>
            {
                Assert.That(dispatched, Is.EqualTo(1));
                Assert.That(band.SharedSupplies.Available(ResourceKind.Food), Is.Zero);
                Assert.That(world.People.GetLastFedAt(band.Members[0]), Is.EqualTo(lastMeal));
                Assert.That(world.Clock.ScheduledCount, Is.Zero);
                Assert.That(world.Hunger.TrackedCount, Is.EqualTo(1), "still tracked; time ran out, the holder did not");
            });
        }

        [Test]
        public void A_meal_draws_one_ration_per_living_member_from_the_ledger()
        {
            var world = new World();
            var band = world.NewBand(4, 100);
            world.Hunger.Track(band);

            world.RunDays(1L);

            Assert.Multiple(() =>
            {
                Assert.That(band.SharedSupplies.Available(ResourceKind.Food), Is.EqualTo(100 - 4 * Hunger.DailyRation));
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Consumed, Is.EqualTo(4 * Hunger.DailyRation));
                Assert.That(band.SharedSupplies.AuditBalances(), Is.True);

                foreach (var member in band.Members)
                {
                    Assert.That(world.People.GetLastFedAt(member), Is.EqualTo(SimulationTime.FromDays(1L)));
                    Assert.That(world.People.GetHealth(member), Is.EqualTo(StartingHealth));
                }

                Assert.That(world.Published(), Is.Empty, "a full meal is not news");
            });
        }

        [Test]
        public void Each_meal_books_the_next_so_a_long_jump_serves_every_day()
        {
            var world = new World();
            var band = world.NewBand(1, 1000);
            world.Hunger.Track(band);

            var dispatched = world.Clock.AdvanceTo(SimulationTime.FromDays(30L), world.Router);

            Assert.Multiple(() =>
            {
                Assert.That(dispatched, Is.EqualTo(30));
                Assert.That(band.SharedSupplies.Available(ResourceKind.Food), Is.EqualTo(1000 - 30 * Hunger.DailyRation));
                Assert.That(world.Clock.TryPeekNext(out var next), Is.True);
                Assert.That(next.Time, Is.EqualTo(SimulationTime.FromDays(31L)));
            });
        }

        [Test]
        public void The_dead_neither_eat_nor_starve()
        {
            var world = new World();
            var band = world.NewBand(3, 100);
            world.Hunger.Track(band);
            var dead = band.Members[1];
            world.People.Remove(dead);

            world.RunDays(1L);

            Assert.Multiple(() =>
            {
                Assert.That(band.Members, Has.Count.EqualTo(3), "membership lags the death cascade (#9)");
                Assert.That(band.SharedSupplies.Available(ResourceKind.Food), Is.EqualTo(100 - 2 * Hunger.DailyRation));
                Assert.That(world.People.IsAlive(dead), Is.False);
                Assert.That(world.Published(), Is.Empty);
            });
        }

        [Test]
        public void Shortfall_within_one_sitting_feeds_in_member_order_and_the_tail_goes_without()
        {
            var world = new World();
            // Three adults - one sitting - with enough for two; the third is
            // short by one.
            var band = world.NewBand(3, 2 * Hunger.DailyRation + Hunger.DailyRation - 1);
            world.Hunger.Track(band);

            world.RunDays(1L);

            var mealTime = SimulationTime.FromDays(1L);

            Assert.Multiple(() =>
            {
                Assert.That(world.People.GetLastFedAt(band.Members[0]), Is.EqualTo(mealTime));
                Assert.That(world.People.GetLastFedAt(band.Members[1]), Is.EqualTo(mealTime));
                Assert.That(world.People.GetLastFedAt(band.Members[2]), Is.EqualTo(SimulationTime.Zero), "unfed, so untouched");
                Assert.That(
                    band.SharedSupplies.Available(ResourceKind.Food),
                    Is.EqualTo(Hunger.DailyRation - 1),
                    "a partial ration is not a meal: the remainder stays in the ledger");
                Assert.That(band.SharedSupplies.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Shortfall_feeds_dependents_then_adults_then_elders_whatever_the_member_order()
        {
            var world = new World();
            // Inserted oldest first, so insertion order and feeding order
            // disagree on every pair.
            var band = world.NewBand(0, 3 * Hunger.DailyRation);
            var elder = world.NewMember(band, AgeStage.Elder);
            var adult = world.NewMember(band, AgeStage.Adult);
            var adolescent = world.NewMember(band, AgeStage.Adolescent);
            var child = world.NewMember(band, AgeStage.Child);
            var infant = world.NewMember(band, AgeStage.Infant);
            world.Hunger.Track(band);

            world.RunDays(1L);

            var mealTime = SimulationTime.FromDays(1L);

            Assert.Multiple(() =>
            {
                Assert.That(world.People.GetLastFedAt(adolescent), Is.EqualTo(mealTime), "dependents eat first");
                Assert.That(world.People.GetLastFedAt(child), Is.EqualTo(mealTime));
                Assert.That(world.People.GetLastFedAt(infant), Is.EqualTo(mealTime));
                Assert.That(world.People.GetLastFedAt(adult), Is.EqualTo(SimulationTime.Zero), "then adults, and there was none left");
                Assert.That(world.People.GetLastFedAt(elder), Is.EqualTo(SimulationTime.Zero), "elders last");
                Assert.That(band.SharedSupplies.Available(ResourceKind.Food), Is.Zero);
            });
        }

        [Test]
        public void Shortfall_feeds_adults_before_elders()
        {
            var world = new World();
            var band = world.NewBand(0, Hunger.DailyRation);
            var elder = world.NewMember(band, AgeStage.Elder);
            var adult = world.NewMember(band, AgeStage.Adult);
            world.Hunger.Track(band);

            world.RunDays(1L);

            Assert.Multiple(() =>
            {
                Assert.That(world.People.GetLastFedAt(adult), Is.EqualTo(SimulationTime.FromDays(1L)));
                Assert.That(world.People.GetLastFedAt(elder), Is.EqualTo(SimulationTime.Zero));
            });
        }

        [Test]
        public void A_missed_meal_inside_the_grace_period_costs_nothing()
        {
            var world = new World();
            var band = world.NewBand(1, 0);
            world.Hunger.Track(band);

            // Ate at time zero; first meal is at day 1, which is within a
            // two-day grace.
            world.RunDays(1L);

            Assert.Multiple(() =>
            {
                Assert.That(world.People.GetHealth(band.Members[0]), Is.EqualTo(StartingHealth));
                Assert.That(world.Hunger.IsInFamine(band), Is.True, "hungry is still a famine, even before it hurts");
            });
        }

        [Test]
        public void Past_the_grace_period_each_missed_meal_costs_health()
        {
            var world = new World();
            var band = world.NewBand(1, 0);
            world.Hunger.Track(band);
            var person = band.Members[0];

            // Day 1 and day 2 are within a two-day grace; day 3 is the first
            // meal strictly past it.
            world.RunDays(2L);
            var afterGrace = world.People.GetHealth(person);
            world.RunDays(1L);
            var afterOneMissed = world.People.GetHealth(person);
            world.RunDays(1L);
            var afterTwoMissed = world.People.GetHealth(person);

            Assert.Multiple(() =>
            {
                Assert.That(afterGrace, Is.EqualTo(StartingHealth));
                Assert.That(afterOneMissed, Is.EqualTo(StartingHealth - Hunger.StarvationDamagePerMeal));
                Assert.That(afterTwoMissed, Is.EqualTo(StartingHealth - 2 * Hunger.StarvationDamagePerMeal));
            });
        }

        [Test]
        public void Starvation_floors_health_at_zero_and_leaves_a_lower_value_alone()
        {
            var world = new World();
            var band = world.NewBand(2, 0);
            world.Hunger.Track(band);
            var nearlyDead = band.Members[0];
            var belowZero = band.Members[1];
            world.People.SetHealth(nearlyDead, (short)(Hunger.StarvationDamagePerMeal / 2));
            world.People.SetHealth(belowZero, -7);

            world.RunDays(3L);

            Assert.Multiple(() =>
            {
                Assert.That(world.People.GetHealth(nearlyDead), Is.Zero);
                Assert.That(world.People.GetHealth(belowZero), Is.EqualTo(-7), "not this system's to push further down");
            });
        }

        [Test]
        public void The_meal_that_reaches_zero_raises_the_starvation_crossing_once_for_the_same_instant()
        {
            var world = new World();
            var band = world.NewBand(2, 0);
            world.Hunger.Track(band);
            var starving = band.Members[0];
            var alreadyGone = band.Members[1];
            world.People.SetHealth(starving, (short)(2 * Hunger.StarvationDamagePerMeal));
            world.People.SetHealth(alreadyGone, 0);

            // Day 3 is the first meal past the grace period; day 4 takes the
            // starving member to zero; day 5 finds them there and raises
            // nothing more. The member already at zero never crosses.
            world.RunDays(5L);

            Assert.Multiple(() =>
            {
                Assert.That(world.Starvation.Raised, Has.Count.EqualTo(1));
                var crossing = world.Starvation.Raised[0];
                Assert.That(crossing.Kind, Is.EqualTo(ScheduledEventKind.StarvationCritical));
                Assert.That(crossing.PrimaryEntity, Is.EqualTo(world.People.GetId(starving)));
                Assert.That(crossing.Time, Is.EqualTo(SimulationTime.FromDays(4L)), "the instant of the meal, not later");
                Assert.That(crossing.Phase, Is.EqualTo(SimulationPhase.Lifecycle), "after the meal's own phase");
                Assert.That(world.People.GetHealth(starving), Is.Zero);
            });
        }

        [Test]
        public void Eating_again_resets_the_grace_period()
        {
            var world = new World();
            var band = world.NewBand(1, 0);
            world.Hunger.Track(band);
            var person = band.Members[0];

            world.RunDays(3L);
            var starved = world.People.GetHealth(person);
            band.SharedSupplies.Gather(ResourceKind.Food, Hunger.DailyRation);
            world.RunDays(1L);
            var fed = world.People.GetHealth(person);
            world.RunDays(2L);
            var withinGrace = world.People.GetHealth(person);
            world.RunDays(1L);
            var pastGrace = world.People.GetHealth(person);

            Assert.Multiple(() =>
            {
                Assert.That(starved, Is.EqualTo(StartingHealth - Hunger.StarvationDamagePerMeal));
                Assert.That(fed, Is.EqualTo(starved), "the meal itself heals nothing; nothing does yet");
                Assert.That(withinGrace, Is.EqualTo(starved));
                Assert.That(pastGrace, Is.EqualTo(starved - Hunger.StarvationDamagePerMeal));
            });
        }

        [Test]
        public void Famine_starts_once_on_the_first_shortfall_and_ends_when_everyone_eats_again()
        {
            var world = new World();
            var band = world.NewBand(2, 2 * Hunger.DailyRation);
            world.Hunger.Track(band);

            world.RunDays(1L); // full meal, ledger now empty
            var beforeShortfall = world.Published();
            world.RunDays(1L); // first shortfall
            var afterShortfall = world.Published();
            world.RunDays(1L); // second shortfall
            var afterSecondShortfall = world.Published();
            band.SharedSupplies.Gather(ResourceKind.Food, 2 * Hunger.DailyRation);
            world.RunDays(1L); // everyone eats
            var afterRelief = world.Published();

            Assert.Multiple(() =>
            {
                Assert.That(beforeShortfall, Is.Empty);
                Assert.That(afterShortfall, Is.EqualTo(new[] { DomainEventKind.FamineStarted }));
                Assert.That(afterSecondShortfall, Is.EqualTo(afterShortfall), "still the same famine");
                Assert.That(
                    afterRelief,
                    Is.EqualTo(new[] { DomainEventKind.FamineStarted, DomainEventKind.FamineEnded }));
                Assert.That(world.Hunger.IsInFamine(band), Is.False);

                var started = world.Journal[0];
                Assert.That(started.PrimaryEntity, Is.EqualTo(band.Id));
                Assert.That(started.SecondaryEntity, Is.EqualTo(EntityId.None));
                Assert.That(started.Reasons, Is.EqualTo(Reasons.None), "a famine is not a decision");
                Assert.That(started.Time, Is.EqualTo(SimulationTime.FromDays(2L)));
                Assert.That(world.Journal[1].Time, Is.EqualTo(SimulationTime.FromDays(4L)));
            });
        }

        [Test]
        public void A_partial_meal_keeps_the_famine_open()
        {
            var world = new World();
            var band = world.NewBand(2, 0);
            world.Hunger.Track(band);

            world.RunDays(1L);
            band.SharedSupplies.Gather(ResourceKind.Food, Hunger.DailyRation);
            world.RunDays(1L);

            Assert.Multiple(() =>
            {
                Assert.That(world.Published(), Is.EqualTo(new[] { DomainEventKind.FamineStarted }));
                Assert.That(world.Hunger.IsInFamine(band), Is.True);
            });
        }

        [Test]
        public void A_meal_with_nobody_living_changes_nothing()
        {
            var world = new World();
            var band = world.NewBand(1, 0);
            world.Hunger.Track(band);
            world.RunDays(1L); // famine opens
            world.People.Remove(band.Members[0]);

            world.RunDays(1L);

            Assert.Multiple(() =>
            {
                Assert.That(world.Published(), Is.EqualTo(new[] { DomainEventKind.FamineStarted }));
                Assert.That(world.Hunger.IsInFamine(band), Is.True, "no one ate, so nothing ended");
                Assert.That(world.Clock.ScheduledCount, Is.EqualTo(1), "meals keep coming for whoever joins");
            });
        }

        [Test]
        public void A_long_jump_finds_the_famine_on_the_day_it_starts()
        {
            var world = new World();
            // Section 4's example with the ration scaled in: ten people, ten
            // days of food, a thirty-day jump. Day eleven is the first
            // shortfall, and the famine must be dated there, not at day 30.
            var band = world.NewBand(10, 10 * 10 * Hunger.DailyRation);
            world.Hunger.Track(band);

            world.Clock.AdvanceTo(SimulationTime.FromDays(30L), world.Router);

            Assert.Multiple(() =>
            {
                Assert.That(world.Journal.Count, Is.EqualTo(1));
                Assert.That(world.Journal[0].Kind, Is.EqualTo(DomainEventKind.FamineStarted));
                Assert.That(world.Journal[0].Time, Is.EqualTo(SimulationTime.FromDays(11L)));
                Assert.That(band.SharedSupplies.Available(ResourceKind.Food), Is.Zero);
            });
        }

        [Test]
        public void Holders_are_fed_independently()
        {
            var world = new World();
            var fed = world.NewBand(2, 100);
            var starving = world.NewBand(2, 0);
            world.Hunger.Track(fed);
            world.Hunger.Track(starving);

            world.RunDays(1L);

            Assert.Multiple(() =>
            {
                Assert.That(world.Hunger.TrackedCount, Is.EqualTo(2));
                Assert.That(world.Hunger.IsInFamine(fed), Is.False);
                Assert.That(world.Hunger.IsInFamine(starving), Is.True);
                Assert.That(fed.SharedSupplies.Available(ResourceKind.Food), Is.EqualTo(100 - 2 * Hunger.DailyRation));
                Assert.That(world.Journal[0].PrimaryEntity, Is.EqualTo(starving.Id));
            });
        }

        [Test]
        public void Days_of_food_is_stock_over_the_living_draw()
        {
            var world = new World();
            var band = world.NewBand(3, 10 * 3 * Hunger.DailyRation + 2);
            world.Hunger.Track(band);
            var empty = world.NewBand(0, 50);
            world.Hunger.Track(empty);

            var tenDays = world.Hunger.DaysOfFood(band);
            world.People.Remove(band.Members[2]);
            var fifteenDays = world.Hunger.DaysOfFood(band);

            Assert.Multiple(() =>
            {
                Assert.That(tenDays, Is.EqualTo(10), "whole days; the remainder does not count");
                Assert.That(fifteenDays, Is.EqualTo(15), "the dead do not draw");
                Assert.That(world.Hunger.DaysOfFood(empty), Is.EqualTo(int.MaxValue));
            });
        }

        [Test]
        public void Queries_refuse_null_and_an_untracked_holder()
        {
            var world = new World();
            var untracked = world.NewBand(1, 3);

            Assert.Multiple(() =>
            {
                Assert.That(() => world.Hunger.DaysOfFood(null!), Throws.ArgumentNullException);
                Assert.That(() => world.Hunger.IsInFamine(null!), Throws.ArgumentNullException);
                Assert.That(() => world.Hunger.DaysOfFood(untracked), Throws.InvalidOperationException);
                Assert.That(() => world.Hunger.IsInFamine(untracked), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void Handle_refuses_a_kind_it_does_not_own()
        {
            var world = new World();
            var band = world.NewBand(1, 3);
            world.Hunger.Track(band);
            var foreign = new ScheduledEvent(
                new EventId(99UL), SimulationTime.Zero, SimulationPhase.Physical,
                ScheduledEventKind.BirthCheck, band.Id, EntityId.None);

            Assert.That(() => world.Hunger.Handle(foreign, world.Clock), Throws.InvalidOperationException);
        }

        [Test]
        public void Handle_refuses_a_holder_it_is_not_tracking()
        {
            var world = new World();
            var untracked = world.NewBand(1, 3);
            world.Clock.Schedule(
                SimulationTime.FromDays(1L), Hunger.MealPhase, ScheduledEventKind.MealDue, untracked.Id, EntityId.None);

            Assert.That(() => world.RunDays(1L), Throws.InvalidOperationException);
        }

        [Test]
        public void Handle_refuses_a_clock_other_than_its_bus_s()
        {
            // The clock is derived from the bus, so the only way to reach
            // Hunger with another one is to dispatch from it directly.
            var world = new World();
            var band = world.NewBand(1, 3);
            world.Hunger.Track(band);
            var other = new SimulationClock(new IdAllocator());
            other.Schedule(SimulationTime.FromDays(1L), Hunger.MealPhase, ScheduledEventKind.MealDue, band.Id, EntityId.None);

            Assert.That(
                () => other.AdvanceTo(SimulationTime.FromDays(1L), world.Router),
                Throws.InvalidOperationException);
        }
    }
}
