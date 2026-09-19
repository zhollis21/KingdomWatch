using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Knowledge
{
    [TestFixture]
    public sealed class KnownMapsTests
    {
        private const int Width = 12;
        private const int Height = 10;

        private static readonly EntityId Band = new EntityId(EntityKind.MobileGroup, 1UL);
        private static readonly EntityId Other = new EntityId(EntityKind.MobileGroup, 2UL);
        private static readonly EntityId Town = new EntityId(EntityKind.Settlement, 1UL);

        private static KnownMaps Fresh(out TerrainGrid grid)
        {
            grid = new TerrainGrid(Width, Height, TerrainKind.Plains);
            return new KnownMaps(grid);
        }

        private static KnownMaps Tracking(EntityId holder)
        {
            var maps = Fresh(out _);
            maps.Track(holder);
            return maps;
        }

        [Test]
        public void A_grid_is_required()
        {
            Assert.That(() => new KnownMaps(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void A_holder_knows_nothing_until_something_reveals()
        {
            var maps = Tracking(Band);

            Assert.Multiple(() =>
            {
                Assert.That(maps.IsTracked(Band), Is.True);
                Assert.That(maps.Knows(Band, new WorldPosition(0, 0)), Is.False);
                Assert.That(maps.Knows(Band, new WorldPosition(Width - 1, Height - 1)), Is.False);
                Assert.That(maps.For(Band).Length, Is.EqualTo(Width * Height), "one entry per cell");
            });
        }

        [Test]
        public void An_untracked_holder_is_not_a_holder()
        {
            var maps = Fresh(out _);

            Assert.Multiple(() =>
            {
                Assert.That(maps.IsTracked(Band), Is.False);
                Assert.That(() => maps.Knows(Band, new WorldPosition(1, 1)), Throws.InvalidOperationException);
                Assert.That(() => maps.Reveal(Band, new WorldPosition(1, 1), 1), Throws.InvalidOperationException);
                Assert.That(() => maps.Untrack(Band), Throws.InvalidOperationException);
                Assert.That(() => maps.HandOver(Band, Town), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void For_an_untracked_holder_throws_rather_than_answering_empty()
        {
            // An empty span means "omniscient" to the pathfinder, so handing
            // one back for a holder with no map would quietly restore exactly
            // the world-reading this class exists to stop.
            var maps = Fresh(out _);
            Assert.That(() => maps.For(Band).Length, Throws.InvalidOperationException);
        }

        [Test]
        public void None_is_not_a_holder()
        {
            var maps = Fresh(out _);

            Assert.Multiple(() =>
            {
                Assert.That(() => maps.Track(EntityId.None), Throws.ArgumentException);
                Assert.That(() => maps.HandOver(Band, EntityId.None), Throws.ArgumentException);
            });
        }

        [Test]
        public void Tracking_twice_refuses_rather_than_discarding_what_was_learned()
        {
            var maps = Tracking(Band);
            maps.Reveal(Band, new WorldPosition(3, 3), 1);

            Assert.That(() => maps.Track(Band), Throws.InvalidOperationException);
            Assert.That(maps.Knows(Band, new WorldPosition(3, 3)), Is.True, "and keeps it");
        }

        [Test]
        public void Reveal_covers_a_square_and_counts_only_what_was_new()
        {
            var maps = Tracking(Band);
            var centre = new WorldPosition(5, 5);

            Assert.Multiple(() =>
            {
                Assert.That(maps.Reveal(Band, centre, 1), Is.EqualTo(9), "three by three");
                Assert.That(maps.Knows(Band, new WorldPosition(4, 4)), Is.True, "corner: the radius is Chebyshev");
                Assert.That(maps.Knows(Band, new WorldPosition(6, 6)), Is.True);
                Assert.That(maps.Knows(Band, new WorldPosition(7, 5)), Is.False, "two east is outside");
                Assert.That(maps.Reveal(Band, centre, 1), Is.EqualTo(0), "nothing new the second time");
            });
        }

        [Test]
        public void Reveal_of_radius_zero_is_the_cell_itself()
        {
            var maps = Tracking(Band);

            Assert.Multiple(() =>
            {
                Assert.That(maps.Reveal(Band, new WorldPosition(2, 2), 0), Is.EqualTo(1));
                Assert.That(maps.Knows(Band, new WorldPosition(2, 2)), Is.True);
                Assert.That(maps.Knows(Band, new WorldPosition(2, 3)), Is.False);
            });
        }

        [Test]
        public void Reveal_clips_at_the_edges_rather_than_refusing()
        {
            // A band walking the edge of the world reveals what there is.
            var maps = Tracking(Band);

            Assert.Multiple(() =>
            {
                Assert.That(maps.Reveal(Band, new WorldPosition(0, 0), 1), Is.EqualTo(4), "a quarter of the square is on the map");
                Assert.That(maps.Knows(Band, new WorldPosition(1, 1)), Is.True);
            });
        }

        [Test]
        public void Reveal_from_off_the_map_still_reveals_what_falls_on_it()
        {
            // Nothing stands off the map today, but a radius is a box and the
            // centre is not special: refusing here would be a guard nothing
            // reaches, and clipping is the same rule as the edge case above.
            var maps = Tracking(Band);

            Assert.That(maps.Reveal(Band, new WorldPosition(-1, -1), 1), Is.EqualTo(1));
            Assert.That(maps.Knows(Band, new WorldPosition(0, 0)), Is.True);
        }

        [Test]
        public void A_negative_radius_is_refused()
        {
            var maps = Tracking(Band);
            Assert.That(() => maps.Reveal(Band, new WorldPosition(1, 1), -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_radius_past_the_map_reveals_all_of_it_and_no_more()
        {
            var maps = Tracking(Band);
            var revealed = maps.Reveal(Band, new WorldPosition(0, 0), Width + Height);

            Assert.Multiple(() =>
            {
                Assert.That(revealed, Is.EqualTo(Width * Height));
                Assert.That(maps.Knows(Band, new WorldPosition(Width - 1, Height - 1)), Is.True);
            });
        }

        [Test]
        public void Knows_is_false_off_the_map_rather_than_throwing()
        {
            var maps = Tracking(Band);
            maps.Reveal(Band, new WorldPosition(1, 1), Width + Height);

            Assert.That(maps.Knows(Band, new WorldPosition(-1, 0)), Is.False, "nothing is known off the map");
            Assert.That(maps.Knows(Band, new WorldPosition(Width, 0)), Is.False);
        }

        [Test]
        public void RevealAlong_reveals_around_every_cell_of_a_route()
        {
            var maps = Tracking(Band);
            var route = new List<WorldPosition>
            {
                new WorldPosition(1, 1),
                new WorldPosition(2, 1),
                new WorldPosition(3, 1),
            };

            maps.RevealAlong(Band, route, 0);

            Assert.Multiple(() =>
            {
                Assert.That(maps.Knows(Band, new WorldPosition(1, 1)), Is.True);
                Assert.That(maps.Knows(Band, new WorldPosition(2, 1)), Is.True);
                Assert.That(maps.Knows(Band, new WorldPosition(3, 1)), Is.True);
                Assert.That(maps.Knows(Band, new WorldPosition(4, 1)), Is.False, "the route stopped");
            });
        }

        [Test]
        public void RevealAlong_an_empty_route_reveals_nothing_and_a_null_one_refuses()
        {
            var maps = Tracking(Band);

            Assert.Multiple(() =>
            {
                Assert.That(maps.RevealAlong(Band, new List<WorldPosition>(), 3), Is.EqualTo(0));
                Assert.That(() => maps.RevealAlong(Band, null!, 3), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Holders_do_not_share_what_they_learn()
        {
            var maps = Tracking(Band);
            maps.Track(Other);
            maps.Reveal(Band, new WorldPosition(4, 4), 1);

            Assert.Multiple(() =>
            {
                Assert.That(maps.Knows(Band, new WorldPosition(4, 4)), Is.True);
                Assert.That(maps.Knows(Other, new WorldPosition(4, 4)), Is.False);
            });
        }

        [Test]
        public void Handing_over_moves_the_map_whole_and_leaves_nothing_behind()
        {
            var maps = Tracking(Band);
            maps.Reveal(Band, new WorldPosition(4, 4), 2);

            maps.HandOver(Band, Town);

            Assert.Multiple(() =>
            {
                Assert.That(maps.IsTracked(Band), Is.False, "the band is becoming the town, not splitting from it");
                Assert.That(maps.IsTracked(Town), Is.True);
                Assert.That(maps.Knows(Town, new WorldPosition(4, 4)), Is.True);
                Assert.That(maps.Knows(Town, new WorldPosition(6, 6)), Is.True, "the whole square, not a sample");
                Assert.That(maps.Knows(Town, new WorldPosition(7, 7)), Is.False);
            });
        }

        [Test]
        public void Handing_over_onto_an_existing_map_refuses_rather_than_merging()
        {
            // Merging is #39's union, and doing it here by accident would be a
            // silent one - the receiving holder would gain cells nobody walked.
            var maps = Tracking(Band);
            maps.Track(Town);

            Assert.That(() => maps.HandOver(Band, Town), Throws.InvalidOperationException);
        }

        [Test]
        public void Forgetting_a_holder_removes_its_map()
        {
            var maps = Tracking(Band);
            maps.Reveal(Band, new WorldPosition(4, 4), 1);

            maps.Untrack(Band);

            Assert.Multiple(() =>
            {
                Assert.That(maps.IsTracked(Band), Is.False);
                Assert.That(() => maps.Knows(Band, new WorldPosition(4, 4)), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void The_mask_is_indexed_by_cell_index_so_the_pathfinder_can_read_it()
        {
            // The contract Pathfinder.TryFindNearest relies on: position i of
            // the span is the cell the grid calls i. A mask indexed any other
            // way would restrict searches to the wrong cells entirely, and
            // still look plausible.
            var maps = Fresh(out var grid);
            maps.Track(Band);
            var cell = new WorldPosition(7, 3);
            maps.Reveal(Band, cell, 0);

            // Read out before asserting: a span cannot cross into a lambda.
            var atCell = maps.For(Band)[grid.IndexOf(cell)];
            var atTranspose = maps.For(Band)[grid.IndexOf(new WorldPosition(3, 7))];

            Assert.Multiple(() =>
            {
                Assert.That(atCell, Is.True);
                Assert.That(atTranspose, Is.False, "not the transpose");
            });
        }

        [Test]
        public void RevealAlong_checks_the_holder_even_when_the_route_is_empty()
        {
            // The one path that could report success for a holder with no map.
            var maps = Fresh(out _);

            Assert.Multiple(() =>
            {
                Assert.That(() => maps.RevealAlong(Band, new List<WorldPosition>(), 3), Throws.InvalidOperationException);
                Assert.That(() => maps.RevealAlong(Band, new List<WorldPosition> { new WorldPosition(1, 1) }, 3), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void RevealAlong_refuses_a_negative_radius()
        {
            var maps = Tracking(Band);
            var route = new List<WorldPosition> { new WorldPosition(1, 1) };

            Assert.That(() => maps.RevealAlong(Band, route, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_handed_over_map_is_the_same_map_not_a_copy_of_it()
        {
            // The band is becoming the settlement. If HandOver copied, a later
            // reveal for the settlement would leave two maps disagreeing, and
            // #39's secession - which does copy - would have nothing to mean.
            var maps = Tracking(Band);
            maps.Reveal(Band, new WorldPosition(2, 2), 0);
            maps.HandOver(Band, Town);
            maps.Reveal(Town, new WorldPosition(8, 8), 0);

            Assert.Multiple(() =>
            {
                Assert.That(maps.Knows(Town, new WorldPosition(2, 2)), Is.True, "what the band had");
                Assert.That(maps.Knows(Town, new WorldPosition(8, 8)), Is.True, "and what it has learned since");
                Assert.That(maps.IsTracked(Band), Is.False);
            });
        }
    }
}
