using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Settlements;
using KingdomWatch.Core.Tests.Work;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Settlements
{
    [TestFixture]
    public sealed class FoundingTests
    {
        private static readonly long Day = SimulationTime.TicksPerDay;

        private static readonly Reasons Why = new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable);

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var w = new WorkWorld();
            var d = w.Demographics;

            Assert.Multiple(() =>
            {
                Assert.That(() => new Founding(null!, w.Deaths, d.Fertility, w.Hunger, w.Jobs, d.Matchmaking), Throws.ArgumentNullException);
                Assert.That(() => new Founding(d.Bus, null!, d.Fertility, w.Hunger, w.Jobs, d.Matchmaking), Throws.ArgumentNullException);
                Assert.That(() => new Founding(d.Bus, w.Deaths, null!, w.Hunger, w.Jobs, d.Matchmaking), Throws.ArgumentNullException);
                Assert.That(() => new Founding(d.Bus, w.Deaths, d.Fertility, null!, w.Jobs, d.Matchmaking), Throws.ArgumentNullException);
                Assert.That(() => new Founding(d.Bus, w.Deaths, d.Fertility, w.Hunger, null!, d.Matchmaking), Throws.ArgumentNullException);
                Assert.That(() => new Founding(d.Bus, w.Deaths, d.Fertility, w.Hunger, w.Jobs, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Founding_moves_people_and_stock_whole_and_announces_it()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 30);
            band.SharedSupplies.Gather(ResourceKind.Wood, 7);
            band.SharedSupplies.Gather(ResourceKind.Stone, 2);
            var adults = w.JoinAdults(band, 4);
            var before = w.Demographics.Journal.Count;

            band.Leader = adults[2];

            var settlement = w.Founding.Found(band, Why);

            Assert.Multiple(() =>
            {
                Assert.That(settlement.Id.Kind, Is.EqualTo(EntityKind.Settlement));
                Assert.That(settlement.Position, Is.EqualTo(WorkWorld.Camp));
                Assert.That(settlement.Members, Is.EqualTo(adults), "same people, same order");
                Assert.That(band.Members, Is.Empty);
                Assert.That(band.Leader, Is.EqualTo(PersonHandle.None), "a band with nobody in it leads nobody");
                Assert.That(settlement.SharedSupplies.Available(ResourceKind.Food), Is.EqualTo(30));
                Assert.That(settlement.SharedSupplies.Available(ResourceKind.Wood), Is.EqualTo(7));
                Assert.That(settlement.SharedSupplies.Available(ResourceKind.Stone), Is.EqualTo(2));
                Assert.That(band.SharedSupplies.Stock(ResourceKind.Food), Is.Zero);
                Assert.That(band.SharedSupplies.Stock(ResourceKind.Wood), Is.Zero);
                Assert.That(band.SharedSupplies.AuditBalances(), Is.True);
                Assert.That(settlement.SharedSupplies.AuditBalances(), Is.True);
                Assert.That(settlement.SharedSupplies.Flows(ResourceKind.Food).Imported, Is.EqualTo(30L));
                Assert.That(w.Founding.All, Is.EqualTo(new[] { settlement }));

                Assert.That(w.Demographics.Journal.Count, Is.EqualTo(before + 1));
                var founded = w.Demographics.Journal[before];
                Assert.That(founded.Kind, Is.EqualTo(DomainEventKind.SettlementFounded));
                Assert.That(founded.PrimaryEntity, Is.EqualTo(settlement.Id));
                Assert.That(founded.SecondaryEntity, Is.EqualTo(band.Id));
                Assert.That(founded.Reasons, Is.EqualTo(Why));
            });
        }

        [Test]
        public void Every_tracker_hands_over_from_the_band_to_the_settlement()
        {
            var w = new WorkWorld();
            var d = w.Demographics;
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adults = w.JoinAdults(band, 2);
            var pending = w.Clock.ScheduledCount;

            var settlement = w.Founding.Found(band, Why);

            Assert.Multiple(() =>
            {
                // Each tracker refuses the band now and knows the settlement.
                Assert.That(() => w.Deaths.Untrack(band), Throws.InvalidOperationException);
                Assert.That(() => d.Fertility.Untrack(band), Throws.InvalidOperationException);
                Assert.That(() => w.Hunger.DaysOfFood(band), Throws.InvalidOperationException);
                Assert.That(() => w.Jobs.HasSite(band, JobKind.Forager), Throws.InvalidOperationException);
                Assert.That(() => d.Matchmaking.Untrack(band), Throws.InvalidOperationException);
                Assert.That(w.Hunger.DaysOfFood(settlement), Is.Zero);
                Assert.That(w.Deaths.TrackedCount, Is.EqualTo(1));
                Assert.That(d.Fertility.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Hunger.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Jobs.TrackedCount, Is.EqualTo(1));
                Assert.That(d.Matchmaking.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending), "each cancelled stream was rebooked for the settlement");
            });

            // The settlement's dawn puts people to work and its meal feeds
            // them from its own stores.
            w.AdvanceToDawn();
            Assert.That(w.Jobs.HasTask(adults[0]), Is.True, "the settlement's dawn");
            Assert.That(w.Jobs.TaskOf(adults[0]).Holder, Is.EqualTo(settlement.Id));
            w.AdvanceTo(w.Now.Plus(Day));
            Assert.That(settlement.SharedSupplies.Flows(ResourceKind.Food).Consumed, Is.GreaterThan(0L), "the settlement's meal");

            // A death is struck from the settlement.
            w.Deaths.Die(adults[1], Reasons.None);
            Assert.That(settlement.Members, Is.EqualTo(new[] { adults[0] }));
        }

        [Test]
        public void A_famine_carries_over_to_the_settlement_and_ends_once()
        {
            // A band that settles hungry is a hungry settlement: the famine
            // the chronicle opened under the band's name closes under the
            // settlement's, and is not opened a second time.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            w.Join(band, 8L);
            w.Join(band, 10L);
            w.AdvanceTo(w.Now.Plus(Day));
            Assert.That(w.Hunger.IsInFamine(band), Is.True, "no food, nobody old enough to forage, first meal missed");
            Assert.That(w.Count(DomainEventKind.FamineStarted), Is.EqualTo(1));

            var settlement = w.Founding.Found(band, Why);

            Assert.That(w.Hunger.IsInFamine(settlement), Is.True, "still hungry under the new name");

            settlement.SharedSupplies.Gather(ResourceKind.Food, WorkWorld.PlentifulFood(2));
            w.AdvanceTo(w.Now.Plus(Day));

            Assert.Multiple(() =>
            {
                Assert.That(w.Hunger.IsInFamine(settlement), Is.False);
                Assert.That(w.Count(DomainEventKind.FamineStarted), Is.EqualTo(1), "opened once");
                Assert.That(w.Count(DomainEventKind.FamineEnded), Is.EqualTo(1), "closed once");
                Assert.That(w.Demographics.Published(DomainEventKind.FamineEnded)[0].PrimaryEntity, Is.EqualTo(settlement.Id));
            });
        }

        [Test]
        public void A_settlement_can_be_worked_from_and_found_again_no_more()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            w.JoinAdults(band, 1);
            w.Founding.Found(band, Why);

            Assert.That(() => w.Founding.Found(band, Why), Throws.InvalidOperationException, "the band is nobody's now");
        }

        [Test]
        public void Founding_refuses_null_a_travelling_band_and_one_with_someone_out()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adult = w.Join(band, 30L);

            band.Destination = new WorldPosition(3, 3);
            Assert.That(() => w.Founding.Found(band, Why), Throws.InvalidOperationException, "on the road");
            band.Destination = null;

            w.AdvanceToDawn();
            Assert.That(w.Jobs.HasTask(adult), Is.True);
            Assert.That(() => w.Founding.Found(band, Why), Throws.InvalidOperationException, "someone is out");
            Assert.That(() => w.Founding.Found(null!, Why), Throws.ArgumentNullException);

            Assert.Multiple(() =>
            {
                Assert.That(w.Founding.All, Is.Empty);
                Assert.That(band.Members, Has.Count.EqualTo(1), "nothing moved");
                Assert.That(w.Jobs.TrackedCount, Is.EqualTo(1), "a refusal leaves the band on every tracker");
                Assert.That(w.Deaths.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Demographics.Fertility.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Hunger.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Demographics.Matchmaking.TrackedCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Founding_refuses_a_band_missing_from_any_tracker_before_touching_anything()
        {
            // Five trackers, five ways to be missing from one. Each refusal
            // leaves every other tracker holding the band, so a retry after
            // the wiring is fixed can succeed.
            var w = new WorkWorld();
            var d = w.Demographics;

            var trackers = new Action<MobileGroup>[]
            {
                b => w.Jobs.Track(b),
                b => w.Deaths.Track(b),
                b => d.Fertility.Track(b),
                b => w.Hunger.Track(b),
                b => d.Matchmaking.Track(b),
            };

            for (var missing = 0; missing < trackers.Length; missing++)
            {
                var band = new MobileGroup(
                    d.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
                w.JoinAdults(band, 1);

                for (var t = 0; t < trackers.Length; t++)
                {
                    if (t != missing)
                    {
                        trackers[t](band);
                    }
                }

                var tracked = w.Jobs.TrackedCount + w.Deaths.TrackedCount + d.Fertility.TrackedCount
                    + w.Hunger.TrackedCount + d.Matchmaking.TrackedCount;

                Assert.That(() => w.Founding.Found(band, Why), Throws.InvalidOperationException, "tracker " + missing + " missing");

                Assert.Multiple(() =>
                {
                    Assert.That(
                        w.Jobs.TrackedCount + w.Deaths.TrackedCount + d.Fertility.TrackedCount
                        + w.Hunger.TrackedCount + d.Matchmaking.TrackedCount,
                        Is.EqualTo(tracked), "nothing untracked when tracker " + missing + " was missing");
                    Assert.That(band.Members, Has.Count.EqualTo(1), "nobody moved when tracker " + missing + " was missing");
                    Assert.That(w.Founding.All, Is.Empty);
                });
            }
        }

        [Test]
        public void Founding_refuses_stock_that_is_reserved_carried_or_in_process()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 30);
            w.JoinAdults(band, 1);
            var cooking = new Recipe(
                "Cook", new[] { new ResourceQuantity(ResourceKind.Food, 2) }, new[] { new ResourceQuantity(ResourceKind.Food, 1) }, 10L);

            band.SharedSupplies.Reserve(ResourceKind.Food, 5);
            Assert.That(() => w.Founding.Found(band, Why), Throws.InvalidOperationException, "reserved");
            band.SharedSupplies.Release(ResourceKind.Food, 5);

            band.SharedSupplies.PickUp(ResourceKind.Food, 5);
            Assert.That(() => w.Founding.Found(band, Why), Throws.InvalidOperationException, "carried");
            band.SharedSupplies.SetDown(ResourceKind.Food, 5);

            band.SharedSupplies.BeginRecipe(cooking);
            Assert.That(() => w.Founding.Found(band, Why), Throws.InvalidOperationException, "in process");
            band.SharedSupplies.CancelRecipe(cooking);

            Assert.Multiple(() =>
            {
                Assert.That(w.Founding.All, Is.Empty);
                Assert.That(w.Deaths.TrackedCount, Is.EqualTo(1), "nothing untracked");
                Assert.That(w.Jobs.TrackedCount, Is.EqualTo(1));
                Assert.That(() => w.Founding.Found(band, Why), Throws.Nothing, "at rest again");
            });
        }
    }
}
