using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests
{
    [TestFixture]
    public sealed class WorldTests
    {
        [Test]
        public void Construction_refuses_a_missing_map_or_table()
        {
            var grid = new TerrainGrid(8, 8, TerrainKind.Plains);

            Assert.Multiple(() =>
            {
                Assert.That(() => new World(1UL, null!, DemographicSettings.Default), Throws.ArgumentNullException);
                Assert.That(() => new World(1UL, grid, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_band_is_refused_off_the_map_or_in_the_river_before_anyone_is_made()
        {
            var grid = new TerrainGrid(8, 8, TerrainKind.Plains);
            grid.Set(new WorldPosition(4, 4), TerrainKind.SmallRiver);
            var world = new World(1UL, grid, DemographicSettings.Default);
            var pending = world.Clock.ScheduledCount;

            Assert.Multiple(() =>
            {
                Assert.That(() => world.AddBand(10, new WorldPosition(99, 99)), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => world.AddBand(10, new WorldPosition(4, 4)), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(world.People.Count, Is.Zero, "nobody was generated");
                Assert.That(world.Clock.ScheduledCount, Is.EqualTo(pending), "no system tracks a half-made band");
                Assert.That(world.Journal.Count, Is.Zero);
            });
        }

        [Test]
        public void A_band_added_stands_where_it_was_put_and_is_tracked_everywhere()
        {
            var world = new World(1UL, new TerrainGrid(8, 8, TerrainKind.Plains), DemographicSettings.Default);
            var at = new WorldPosition(3, 5);

            var band = world.AddBand(12, at);

            var communities = new List<ICommunity>();
            world.CopyCommunitiesTo(communities);
            var tracked = new List<ICommunity>();

            Assert.Multiple(() =>
            {
                Assert.That(band.Members, Has.Count.EqualTo(12));
                Assert.That(communities, Is.EqualTo(new ICommunity[] { band }));

                foreach (var member in band.Members)
                {
                    Assert.That(world.People.GetPosition(member), Is.EqualTo(at));
                }

                world.Hunger.CopyTrackedTo(tracked);
                Assert.That(tracked, Does.Contain(band), "hunger");
                world.Warmth.CopyTrackedTo(tracked);
                Assert.That(tracked, Does.Contain(band), "warmth");
                world.Jobs.CopyTrackedTo(tracked);
                Assert.That(tracked, Does.Contain(band), "jobs");
                world.Fertility.CopyTrackedTo(tracked);
                Assert.That(tracked, Does.Contain(band), "fertility");
                world.Matchmaking.CopyTrackedTo(tracked);
                Assert.That(tracked, Does.Contain(band), "matchmaking");
                world.Nomads.CopyTrackedTo(tracked);
                Assert.That(tracked, Does.Contain(band), "nomads");
            });
        }

        [Test]
        public void Copying_refuses_a_missing_list()
        {
            var world = new World(1UL, new TerrainGrid(8, 8, TerrainKind.Plains), DemographicSettings.Default);

            Assert.Multiple(() =>
            {
                Assert.That(() => world.CopyBookingsTo(null!), Throws.ArgumentNullException);
                Assert.That(() => world.CopyCommunitiesTo(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Copying_bookings_replaces_what_the_list_held()
        {
            var world = new World(1UL, new TerrainGrid(8, 8, TerrainKind.Plains), DemographicSettings.Default);
            world.AddBand(12, new WorldPosition(3, 3));
            var bookings = new List<PendingBooking>();

            world.CopyBookingsTo(bookings);
            var once = bookings.Count;
            world.CopyBookingsTo(bookings);

            Assert.That(bookings, Has.Count.EqualTo(once).And.Count.GreaterThan(0));
        }
    }
}
