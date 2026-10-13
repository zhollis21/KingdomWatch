using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Land;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Land
{
    [TestFixture]
    public sealed class LandCoverTests
    {
        private static readonly WorldPosition Bush = new WorldPosition(1, 0);
        private static readonly WorldPosition Tree = new WorldPosition(2, 0);
        private static readonly WorldPosition Rock = new WorldPosition(3, 0);
        private static readonly WorldPosition Open = new WorldPosition(0, 0);

        private SimulationClock _clock = null!;
        private TerrainGrid _grid = null!;
        private LandCover _land = null!;

        [SetUp]
        public void SetUp()
        {
            _grid = new TerrainGrid(4, 1, TerrainKind.Plains);
            _grid.Set(Bush, TerrainKind.Scrub);
            _grid.Set(Tree, TerrainKind.Forest);
            _grid.Set(Rock, TerrainKind.Rocks);
            _clock = new SimulationClock(new IdAllocator());
            _land = new LandCover(_grid, _clock);
        }

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new LandCover(null!, _clock), Throws.ArgumentNullException);
                Assert.That(() => new LandCover(_grid, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_bush_gives_its_picks_and_is_then_bare_for_the_rest_of_the_season()
        {
            for (var i = 0; i < _land.BushPicks; i++)
            {
                Assert.That(_land.IsWorkable(Bush), Is.True, "pick " + i);
                _land.Take(Bush);
            }

            Assert.That(_land.IsWorkable(Bush), Is.False);
            Assert.That(_land.HasFruit(Bush), Is.False);
            Assert.That(() => _land.Take(Bush), Throws.InvalidOperationException);
            Assert.That(_land.Ripe.Accepts(_grid.IndexOf(Bush)), Is.False);
        }

        [Test]
        public void A_stripped_bush_fruits_again_when_the_next_season_starts()
        {
            Strip(Bush);
            AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerSeason - 1L));
            Assert.That(_land.IsWorkable(Bush), Is.False, "last day of spring");

            AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerSeason));
            Assert.That(_land.IsWorkable(Bush), Is.True, "first day of summer");
        }

        [Test]
        public void A_partly_picked_bush_refills_at_the_next_season()
        {
            _land.Take(Bush);
            _land.Take(Bush);
            AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerSeason));

            for (var i = 0; i < _land.BushPicks; i++)
            {
                _land.Take(Bush);
            }

            Assert.That(_land.IsWorkable(Bush), Is.False);
        }

        [Test]
        public void Every_bush_is_bare_in_winter_but_still_promising()
        {
            AdvanceTo(SimulationTime.FromDays(3L * SimulationTime.DaysPerSeason));
            var cell = _grid.IndexOf(Bush);

            Assert.Multiple(() =>
            {
                Assert.That(_land.IsWorkable(Bush), Is.False);
                Assert.That(_land.Ripe.Accepts(cell), Is.False);
                Assert.That(_land.Promising.Accepts(cell), Is.True, "it fruits in spring");
            });

            AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerYear));
            Assert.That(_land.IsWorkable(Bush), Is.True, "spring");
        }

        [Test]
        public void A_stripped_bush_is_not_promising_outside_winter()
        {
            Strip(Bush);
            Assert.That(_land.Promising.Accepts(_grid.IndexOf(Bush)), Is.False);
        }

        [Test]
        public void A_tree_falls_after_its_cuts_and_stands_again_after_regrowing()
        {
            for (var i = 0; i < _land.TreeCuts; i++)
            {
                Assert.That(_land.StageOf(Tree), Is.EqualTo(TreeStage.Standing), "cut " + i);
                _land.Take(Tree);
            }

            Assert.That(_land.IsWorkable(Tree), Is.False);
            Assert.That(_land.StageOf(Tree), Is.EqualTo(TreeStage.Stump));
            Assert.That(_land.Promising.Accepts(_grid.IndexOf(Tree)), Is.False, "a stump is no wood, in any season");

            AdvanceTo(SimulationTime.FromDays(LandCover.RegrowDays / 2L));
            Assert.That(_land.StageOf(Tree), Is.EqualTo(TreeStage.Sapling));
            Assert.That(_land.IsWorkable(Tree), Is.False);

            AdvanceTo(SimulationTime.FromDays(LandCover.RegrowDays));
            Assert.That(_land.StageOf(Tree), Is.EqualTo(TreeStage.Standing));
            Assert.That(_land.IsWorkable(Tree), Is.True);

            // Grown back, it takes a full set of cuts again.
            for (var i = 0; i < _land.TreeCuts; i++)
            {
                _land.Take(Tree);
            }

            Assert.That(_land.StageOf(Tree), Is.EqualTo(TreeStage.Stump));
        }

        [Test]
        public void Rocks_and_plains_give_without_limit_and_record_nothing()
        {
            for (var i = 0; i < 100; i++)
            {
                _land.Take(Rock);
                _land.Take(new WorldPosition(0, 0));
            }

            Assert.That(_land.IsWorkable(Rock), Is.True);
            Assert.That(_land.StateAt(_grid.IndexOf(Rock)), Is.Zero);
            Assert.That(() => _land.Return(Rock), Throws.Nothing);
        }

        [Test]
        public void A_returned_claim_puts_the_fruit_back_and_stands_the_tree_up()
        {
            Strip(Bush);
            _land.Return(Bush);
            Assert.That(_land.IsWorkable(Bush), Is.True);

            Strip(Tree);
            _land.Return(Tree);
            Assert.That(_land.IsWorkable(Tree), Is.True);
            Assert.That(_land.StageOf(Tree), Is.EqualTo(TreeStage.Standing));
        }

        [Test]
        public void A_bush_given_back_its_only_claim_is_untouched_again()
        {
            _land.Take(Bush);
            _land.Return(Bush);

            Assert.That(_land.StateAt(_grid.IndexOf(Bush)), Is.Zero);
            Assert.That(() => _land.Return(Bush), Throws.InvalidOperationException);
        }

        [Test]
        public void A_tree_given_back_its_only_claim_is_untouched_again()
        {
            // As a bush is: a touched cell always holds a claim, so a cell
            // left with none cannot outlive a terrain rewrite unnoticed (the
            // #141 review).
            _land.Take(Tree);
            _land.Return(Tree);

            Assert.That(_land.StateAt(_grid.IndexOf(Tree)), Is.Zero);
        }

        [Test]
        public void Every_claim_given_back_is_counted_and_a_rock_gives_none_back()
        {
            Assert.That(_land.Returns, Is.Zero);
            _land.Take(Bush);
            _land.Take(Tree);
            _land.Return(Bush);
            _land.Return(Tree);
            _land.Return(Rock);

            Assert.That(_land.Returns, Is.EqualTo(2L));
        }

        [Test]
        public void A_tree_with_no_claim_has_none_to_give_back()
        {
            Assert.That(() => _land.Return(Tree), Throws.InvalidOperationException);
        }

        [Test]
        public void Claims_read_back_and_a_new_season_counts_afresh()
        {
            var cell = _grid.IndexOf(Bush);
            _land.Take(Bush);
            _land.Take(Bush);
            Assert.That(_land.ClaimsAt(cell), Is.EqualTo(2));

            AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerSeason + 2L));
            _land.Take(Bush);

            Assert.That(_land.ClaimsAt(cell), Is.EqualTo(1), "spring's two picks are not summer's");
        }

        [Test]
        public void Picks_and_cuts_are_settable_from_one_to_the_most_a_cell_counts()
        {
            Assert.Multiple(() =>
            {
                Assert.That(_land.BushPicks, Is.EqualTo(LandCover.DefaultBushPicks));
                Assert.That(_land.TreeCuts, Is.EqualTo(LandCover.DefaultTreeCuts));
                Assert.That(() => _land.BushPicks = 0, Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => _land.TreeCuts = 0, Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => _land.BushPicks = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => _land.BushPicks = LandCover.MaxClaims + 1, Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => _land.TreeCuts = LandCover.MaxClaims + 1, Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(_land.BushPicks, Is.EqualTo(LandCover.DefaultBushPicks), "a refused value changes nothing");
            });
        }

        [Test]
        public void A_bush_counts_up_to_the_most_a_cell_holds()
        {
            // The count's top value round-trips: one short is still ripe, the
            // last pick strips it, and a pick past it is refused rather than
            // wrapping the count into the period's bits.
            _land.BushPicks = LandCover.MaxClaims;

            for (var i = 0; i < LandCover.MaxClaims - 1; i++)
            {
                _land.Take(Bush);
            }

            Assert.That(_land.IsWorkable(Bush), Is.True);
            _land.Take(Bush);

            Assert.Multiple(() =>
            {
                Assert.That(_land.ClaimsAt(_grid.IndexOf(Bush)), Is.EqualTo(LandCover.MaxClaims));
                Assert.That(_land.IsWorkable(Bush), Is.False);
                Assert.That(() => _land.Take(Bush), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void Asking_after_a_bush_or_tree_where_there_is_none_is_refused()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => _land.HasFruit(Tree), Throws.ArgumentException);
                Assert.That(() => _land.HasFruit(Rock), Throws.ArgumentException);
                Assert.That(() => _land.StageOf(Bush), Throws.ArgumentException);
                Assert.That(() => _land.StageOf(Rock), Throws.ArgumentException);
            });
        }

        [Test]
        public void Every_position_off_the_map_is_refused_reads_and_writes_alike()
        {
            var off = new WorldPosition(4, 0);

            Assert.Multiple(() =>
            {
                Assert.That(() => _land.IsWorkable(off), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => _land.HasFruit(off), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => _land.StageOf(off), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => _land.Take(off), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => _land.Return(off), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void The_filters_accept_cells_that_are_neither_scrub_nor_forest()
        {
            var plains = _grid.IndexOf(new WorldPosition(0, 0));
            Assert.That(_land.Ripe.Accepts(plains) && _land.Promising.Accepts(plains), Is.True);
        }

        [Test]
        public void A_tree_planted_on_plains_is_forest_growing_from_a_stump()
        {
            _land.Plant(Open);
            var cell = _grid.IndexOf(Open);

            Assert.Multiple(() =>
            {
                Assert.That(_grid[Open], Is.EqualTo(TerrainKind.Forest));
                Assert.That(_land.StageOf(Open), Is.EqualTo(TreeStage.Stump));
                Assert.That(_land.IsGrowing(cell), Is.True);
                Assert.That(_land.IsWorkable(Open), Is.False, "nothing to cut yet");
                Assert.That(_land.CutsLeft(Open), Is.Zero);
                Assert.That(_land.Grown.Accepts(cell), Is.False);
                Assert.That(_land.TryGetPlanted(cell, out var day), Is.True);
                Assert.That(day, Is.Zero);
            });
        }

        [Test]
        public void A_planted_tree_stands_after_the_regrowth_and_is_then_an_ordinary_tree()
        {
            _land.Plant(Open);
            var cell = _grid.IndexOf(Open);

            AdvanceTo(SimulationTime.FromDays(LandCover.RegrowDays / 2));
            Assert.That(_land.StageOf(Open), Is.EqualTo(TreeStage.Sapling));
            Assert.That(_land.IsGrowing(cell), Is.True);

            AdvanceTo(SimulationTime.FromDays(LandCover.RegrowDays));

            Assert.Multiple(() =>
            {
                Assert.That(_land.StageOf(Open), Is.EqualTo(TreeStage.Standing));
                Assert.That(_land.IsGrowing(cell), Is.False);
                Assert.That(_land.Grown.Accepts(cell), Is.True);
                Assert.That(_land.TryGetPlanted(cell, out _), Is.False);
                Assert.That(_land.CutsLeft(Open), Is.EqualTo(_land.TreeCuts));
            });

            _land.Take(Open);
            Assert.That(_land.CutsLeft(Open), Is.EqualTo(_land.TreeCuts - 1));
        }

        [Test]
        public void Planting_refuses_anything_but_plains()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => _land.Plant(Bush), Throws.ArgumentException);
                Assert.That(() => _land.Plant(Tree), Throws.ArgumentException);
                Assert.That(() => _land.Plant(Rock), Throws.ArgumentException);
            });
        }

        [Test]
        public void Unplanting_leaves_untouched_plains()
        {
            _land.Plant(Open);
            _land.Unplant(Open);
            var cell = _grid.IndexOf(Open);

            Assert.Multiple(() =>
            {
                Assert.That(_grid[Open], Is.EqualTo(TerrainKind.Plains));
                Assert.That(_land.StateAt(cell), Is.Zero);
                Assert.That(_land.IsGrowing(cell), Is.False);
            });
        }

        [Test]
        public void Unplanting_ground_cleared_since_does_nothing()
        {
            _land.Plant(Open);
            _land.Clear(Open);

            _land.Unplant(Open);

            Assert.That(_grid[Open], Is.EqualTo(TerrainKind.Plains));
            Assert.That(_land.IsGrowing(_grid.IndexOf(Open)), Is.False);
        }

        [Test]
        public void Unplanting_refuses_a_tree_nobody_planted()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => _land.Unplant(Tree), Throws.InvalidOperationException);
                Assert.That(() => _land.Unplant(Bush), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void Clearing_a_planted_tree_forgets_it()
        {
            _land.Plant(Open);

            Assert.That(_land.Clear(Open), Is.Zero, "a sapling has no wood");
            Assert.That(_land.IsGrowing(_grid.IndexOf(Open)), Is.False);
            Assert.That(_land.NextGrowth(), Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void A_claim_given_back_on_ground_cleared_and_planted_since_leaves_the_sapling_alone()
        {
            // A woodcutter set out for the tree; the ground was cleared under
            // the trip and a Forester planted it; then the woodcutter died.
            _land.Take(Tree);
            _land.Clear(Tree);
            _land.Plant(Tree);

            _land.Return(Tree);

            Assert.That(_land.StageOf(Tree), Is.EqualTo(TreeStage.Stump));
            Assert.That(_land.IsGrowing(_grid.IndexOf(Tree)), Is.True);
            Assert.That(_land.IsWorkable(Tree), Is.False);
        }

        [Test]
        public void The_next_growth_is_the_earliest_planted_tree_to_stand()
        {
            Assert.That(_land.NextGrowth(), Is.EqualTo(long.MaxValue), "nothing planted");

            _land.Plant(Open);
            AdvanceTo(SimulationTime.FromDays(10));
            _land.Clear(Tree);
            _land.Plant(Tree);

            Assert.That(_land.NextGrowth(), Is.EqualTo(LandCover.RegrowDays));

            AdvanceTo(SimulationTime.FromDays(LandCover.RegrowDays));
            Assert.That(_land.NextGrowth(), Is.EqualTo(10 + LandCover.RegrowDays), "the first has stood");

            AdvanceTo(SimulationTime.FromDays(10 + LandCover.RegrowDays));
            Assert.That(_land.NextGrowth(), Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void IsGrowing_refuses_a_cell_off_the_grid()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => _land.IsGrowing(-1), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => _land.IsGrowing(_grid.CellCount), Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_bush_has_its_picks_left_while_it_has_fruit_and_none_when_bare()
        {
            Assert.That(_land.PicksLeft(Bush), Is.EqualTo(_land.BushPicks));

            _land.Take(Bush);
            _land.Take(Bush);
            Assert.That(_land.PicksLeft(Bush), Is.EqualTo(_land.BushPicks - 2));

            while (_land.IsWorkable(Bush))
            {
                _land.Take(Bush);
            }

            Assert.That(_land.PicksLeft(Bush), Is.Zero, "stripped");

            AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerSeason));
            Assert.That(_land.PicksLeft(Bush), Is.EqualTo(_land.BushPicks), "next season");

            AdvanceTo(SimulationTime.FromDays(3L * SimulationTime.DaysPerSeason));
            Assert.That(_land.PicksLeft(Bush), Is.Zero, "winter");
            Assert.That(() => _land.PicksLeft(Tree), Throws.ArgumentException);
        }

        private void Strip(WorldPosition at)
        {
            var count = _grid[at] == TerrainKind.Scrub ? _land.BushPicks : _land.TreeCuts;

            for (var i = 0; i < count; i++)
            {
                _land.Take(at);
            }
        }

        private void AdvanceTo(SimulationTime time) => _clock.AdvanceTo(time, NoEvents.Instance);

        private sealed class NoEvents : IScheduledEventHandler
        {
            public static readonly NoEvents Instance = new NoEvents();

            public void Handle(ScheduledEvent scheduled, SimulationClock clock) =>
                throw new InvalidOperationException("Nothing is booked here.");
        }
    }
}
