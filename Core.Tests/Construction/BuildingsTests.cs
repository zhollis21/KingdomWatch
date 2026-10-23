using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Land;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    [TestFixture]
    public sealed class BuildingsTests
    {
        private const long Hour = SimulationTime.TicksPerHour;

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var w = new BuildingsWorld().World;
            var skills = new NoviceSkills();

            Assert.Multiple(() =>
            {
                Assert.That(() => new Buildings(null!, w.People, w.Households, w.Partnerships, w.Pathfinder, w.KnownMaps, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, null!, w.Households, w.Partnerships, w.Pathfinder, w.KnownMaps, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, null!, w.Partnerships, w.Pathfinder, w.KnownMaps, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, w.Households, null!, w.Pathfinder, w.KnownMaps, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, w.Households, w.Partnerships, null!, w.KnownMaps, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, w.Households, w.Partnerships, w.Pathfinder, null!, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, w.Households, w.Partnerships, w.Pathfinder, w.KnownMaps, null!, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, w.Households, w.Partnerships, w.Pathfinder, w.KnownMaps, w.Land, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_band_is_not_a_settlement_and_builds_nothing()
        {
            var w = new BuildingsWorld().World;
            var band = w.AddBand(4, new WorldPosition(5, 5));

            Assert.That(() => w.Buildings.AtDawn(band, 1, 4), Throws.ArgumentException);
        }

        [Test]
        public void Households_without_homes_get_a_house_when_hands_and_wood_are_spare()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            var woodBefore = w.Stores.Available(ResourceKind.Wood);

            w.Dawn();

            var house = w.Buildings.All.Single();
            var spec = BuildingTable.Of(BuildingKind.House);

            Assert.Multiple(() =>
            {
                Assert.That(house.Kind, Is.EqualTo(BuildingKind.House));
                Assert.That(house.Settlement, Is.EqualTo(w.Settlement.Id));
                Assert.That(house.Id.Kind, Is.EqualTo(EntityKind.Building));
                Assert.That(house.IsComplete, Is.False);
                Assert.That(house.ClearTicks, Is.Zero, "open plains first: nothing to clear");
                Assert.That(house.LabourTicks, Is.EqualTo(spec.BuildTicks));
                Assert.That(house.Covers(BuildingsWorld.Centre), Is.False, "the camp cell is kept");
                Assert.That(w.Stores.Available(ResourceKind.Wood), Is.EqualTo(woodBefore - spec.Wood));
                Assert.That(w.Stores.Flows(ResourceKind.Wood).Embodied, Is.EqualTo(spec.Wood));
            });
        }

        [Test]
        public void No_spare_hands_no_building()
        {
            var w = new BuildingsWorld();
            w.Wood(100);

            w.Buildings.AtDawn(w.Settlement, 0, w.Living);

            Assert.That(w.Buildings.All, Is.Empty);
        }

        [Test]
        public void A_house_waits_for_its_stone_and_takes_it_with_the_wood()
        {
            // #166: wood to spare and one stone short, nothing is approved;
            // one more and the house is, built of both.
            var w = new BuildingsWorld();
            var spec = BuildingTable.Of(BuildingKind.House);
            w.Wood(100);
            w.StoneLeft(spec.Stone - 1);

            w.DawnAsIs();
            Assert.That(w.Buildings.All, Is.Empty, "one stone short");

            w.Stone(1);
            w.DawnAsIs();

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.All.Single().Kind, Is.EqualTo(BuildingKind.House));
                Assert.That(w.Stores.Available(ResourceKind.Stone), Is.Zero);
                Assert.That(w.Stores.Flows(ResourceKind.Stone).Embodied, Is.EqualTo(spec.Stone));
            });
        }

        [Test]
        public void Houses_barns_and_stores_need_stone_and_fields_do_not()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BuildingTable.Of(BuildingKind.House).Stone, Is.GreaterThan(0));
                Assert.That(BuildingTable.Of(BuildingKind.Barn).Stone, Is.GreaterThan(0));
                Assert.That(BuildingTable.Of(BuildingKind.Granary).Stone, Is.GreaterThan(0));
                Assert.That(BuildingTable.Of(BuildingKind.Woodshed).Stone, Is.GreaterThan(0));
                Assert.That(BuildingTable.Of(BuildingKind.Field).Stone, Is.Zero);
            });
        }

        [Test]
        public void The_stone_reserve_covers_every_building_and_fits_the_store()
        {
            // StoneGatherers stop at the reserve, so a building dearer than it
            // could never be afforded; and the reserve past the base store
            // would want a Stoneyard before there is a house to build one by
            // (#178).
            Assert.Multiple(() =>
            {
                foreach (BuildingKind kind in System.Enum.GetValues(typeof(BuildingKind)))
                {
                    if (kind != BuildingKind.None)
                    {
                        Assert.That(BuildingTable.Of(kind).Stone, Is.LessThanOrEqualTo(KingdomWatch.Core.Work.Jobs.StoneReserve), kind.ToString());
                    }
                }

                Assert.That(KingdomWatch.Core.Work.Jobs.StoneReserve, Is.LessThanOrEqualTo(Storage.BaseStone));
            });
        }

        [Test]
        public void The_stone_wanted_grows_by_each_finished_house()
        {
            // #166: fifty with no house (#178), ten more for each finished one; a
            // house still going up does not count.
            var w = new BuildingsWorld();
            w.Wood(100);
            var house = w.BuildNext();
            w.Dawn();

            Assert.Multiple(() =>
            {
                Assert.That(KingdomWatch.Core.Work.Jobs.StoneWant(0), Is.EqualTo(KingdomWatch.Core.Work.Jobs.StoneReserve));
                Assert.That(KingdomWatch.Core.Work.Jobs.StoneWant(3), Is.EqualTo(KingdomWatch.Core.Work.Jobs.StoneReserve + (3 * KingdomWatch.Core.Work.Jobs.StonePerHouse)));
                Assert.That(() => KingdomWatch.Core.Work.Jobs.StoneWant(-1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(house.Kind, Is.EqualTo(BuildingKind.House));
                Assert.That(w.Latest, Is.Not.SameAs(house), "a second building going up");
                Assert.That(w.Buildings.FinishedHouses(w.Settlement.Id), Is.EqualTo(1));
                Assert.That(w.Buildings.FinishedHouses(w.World.AddBand(2, new WorldPosition(2, 2)).Id), Is.Zero, "a band builds nothing");
            });
        }

        [Test]
        public void A_field_is_laid_out_with_no_stone_in_store()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            var barn = w.BuildNext();
            w.StoneLeft(0);

            w.DawnAsIs();

            Assert.Multiple(() =>
            {
                Assert.That(barn.Kind, Is.EqualTo(BuildingKind.Barn));
                Assert.That(w.Latest?.Kind, Is.EqualTo(BuildingKind.Field));
            });
        }

        [Test]
        public void No_skill_no_building()
        {
            var w = new BuildingsWorld();
            var unskilled = new Buildings(
                w.World.Bus, w.World.People, w.World.Households, w.World.Partnerships, w.World.Pathfinder, w.World.KnownMaps, w.World.Land, new NoSkills());
            w.Wood(100);

            unskilled.AtDawn(w.Settlement, 1, w.Living);

            Assert.That(unskilled.All, Is.Empty);
        }

        [Test]
        public void Wood_for_the_coming_winter_is_not_built_with()
        {
            var w = new BuildingsWorld();
            // Fed through the wait, since nobody here works.
            w.Stores.Gather(ResourceKind.Food, w.Living * 200);

            // Summer: a whole winter of fires is spoken for.
            w.World.AdvanceTo(new SimulationTime(SimulationTime.TicksPerDay * SimulationTime.DaysPerSeason));
            var hearths = KingdomWatch.Core.Needs.Warmth.CountHearths(w.Settlement.Members, w.World.People, new System.Collections.Generic.List<EntityId>());
            var reserve = hearths * KingdomWatch.Core.Needs.Warmth.FuelPerFire * (int)SimulationTime.DaysPerSeason;
            var have = w.Stores.Available(ResourceKind.Wood);
            var spec = BuildingTable.Of(BuildingKind.House);
            var count = w.Buildings.All.Count;

            if (have >= reserve + spec.Wood)
            {
                w.Stores.Consume(ResourceKind.Wood, have - (reserve + spec.Wood - 1));
            }
            else
            {
                w.Wood(reserve + spec.Wood - 1 - have);
            }

            w.Dawn();
            Assert.That(w.Buildings.All.Count, Is.EqualTo(count), "one short of the reserve plus the house");

            w.Wood(1);
            w.Dawn();
            Assert.That(w.Buildings.All.Count, Is.EqualTo(count + 1));
        }

        [Test]
        public void One_building_goes_up_at_a_time()
        {
            var w = new BuildingsWorld();
            w.Wood(500);

            w.Dawn();
            w.Dawn();

            Assert.That(w.Buildings.All, Has.Count.EqualTo(1));
        }

        [Test]
        public void A_finished_house_is_announced_and_goes_to_the_first_household_without_one()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            var house = w.BuildNext();

            Assert.That(w.Heard.Count(e => e.Kind == DomainEventKind.BuildingCompleted && e.PrimaryEntity == house.Id && e.SecondaryEntity == w.Settlement.Id), Is.EqualTo(1));
            Assert.That(house.Occupant.IsNone, Is.True, "homes are handed out at dawn");

            w.Buildings.AtDawn(w.Settlement, 0, w.Living);

            var first = w.Settlement.Members.Select(m => w.World.Households.Of(m)).OfType<Household>().First();
            Assert.Multiple(() =>
            {
                Assert.That(house.Occupant, Is.EqualTo(first.Id));
                Assert.That(w.Buildings.HomeOf(first.Id), Is.SameAs(house));
            });
        }

        [Test]
        public void A_household_that_dissolves_leaves_its_house_empty()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            var house = w.BuildNext();
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            var household = house.Occupant;

            w.World.Bus.Publish(DomainEventKind.HouseholdDissolved, household, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(house.Occupant.IsNone, Is.True);
                Assert.That(w.Buildings.HomeOf(household), Is.Null);
            });
        }

        [Test]
        public void A_hungry_village_builds_a_barn_after_its_first_house_then_fields_around_it()
        {
            // All plains: no bushes in reach, so food is short once the
            // stores hold less than a year.
            var w = new BuildingsWorld();
            w.Wood(500);

            var house = w.BuildNext();
            var barn = w.BuildNext();
            var field = w.BuildNext();

            Assert.Multiple(() =>
            {
                Assert.That(house.Kind, Is.EqualTo(BuildingKind.House));
                Assert.That(barn.Kind, Is.EqualTo(BuildingKind.Barn));
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.Barn, Is.EqualTo(barn.Id));
                Assert.That(field.Stage, Is.EqualTo(FieldStage.Tending));
                Assert.That(System.Math.Abs(field.Anchor.X - barn.Anchor.X), Is.LessThanOrEqualTo(Buildings.FieldRadius));
                Assert.That(System.Math.Abs(field.Anchor.Y - barn.Anchor.Y), Is.LessThanOrEqualTo(Buildings.FieldRadius));
            });
        }

        [Test]
        public void A_barn_holds_only_so_many_fields()
        {
            // Forty people: short of food until some ten fields stand. On a
            // map with room round the barn on every side: against the edge of
            // the usual one, with the streets taking their share (#171), a
            // barn has room for fewer fields than it may hold.
            var w = new BuildingsWorld(people: 40, size: 80, camp: new WorldPosition(40, 40));
            w.Wood(1000);
            w.BuildNext();
            var barn = w.BuildNext();

            for (var i = 0; i < Buildings.FieldsPerBarn; i++)
            {
                w.BuildNext();
            }

            var second = w.BuildNext();

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.All.Count(b => b.Barn == barn.Id), Is.EqualTo(Buildings.FieldsPerBarn));
                Assert.That(second.Kind, Is.EqualTo(BuildingKind.Barn));
            });
        }

        [Test]
        public void A_barn_with_no_ground_left_for_a_field_does_not_hold_off_the_next_barn()
        {
            // #163: a barn with room on paper but rocks all round it was
            // still the barn every field waited for, so no second barn was
            // ever built and the village stayed short of food for good.
            var w = new BuildingsWorld(people: 40, size: 80);
            w.Wood(1000);
            w.BuildNext();
            var barn = w.BuildNext();
            w.BuildNext();

            BoxIn(w, barn, Buildings.FieldRadius + 5);

            var next = w.BuildNext();

            // And the boxed-in barn, still first in building order with room
            // on paper, does not take the next field from the new one.
            var after = w.BuildNext();

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.All.Count(b => b.Barn == barn.Id), Is.LessThan(Buildings.FieldsPerBarn), "the first barn still has room on paper");
                Assert.That(next.Kind, Is.EqualTo(BuildingKind.Barn));
                Assert.That(after.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(after.Barn, Is.EqualTo(next.Id), "the field goes to the barn with ground for it");
            });
        }

        [Test]
        public void A_boxed_in_barn_waits_for_the_wood_a_new_barn_takes()
        {
            // A field's wood is in store but a barn's is not: the boxed-in
            // barn gets no field and no barn is built yet. One more piece of
            // wood and the barn is approved. Spring, so no winter's fuel is
            // spoken for.
            var w = new BuildingsWorld(people: 40, size: 80);
            w.Wood(1000);
            w.BuildNext();
            var barn = w.BuildNext();
            BoxIn(w, barn, Buildings.FieldRadius + 5);
            var barnWood = BuildingTable.Of(BuildingKind.Barn).Wood;
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood) - (barnWood - 1));
            var count = w.Buildings.All.Count;

            w.Dawn();

            Assert.Multiple(() =>
            {
                Assert.That(barnWood - 1, Is.GreaterThanOrEqualTo(BuildingTable.Of(BuildingKind.Field).Wood), "a field could be paid for");
                Assert.That(w.Buildings.All.Count, Is.EqualTo(count), "but there is nowhere to put one, and no barn yet");
            });

            w.Wood(1);
            w.Dawn();

            Assert.That(w.Latest?.Kind, Is.EqualTo(BuildingKind.Barn));
        }

        [Test]
        public void A_village_with_no_ground_left_for_a_field_or_a_barn_builds_nothing()
        {
            // Nowhere for a field and nowhere for a barn: the dawn approves
            // nothing rather than throwing, and the village waits.
            var w = new BuildingsWorld(people: 40, size: 80);
            w.Wood(1000);
            w.BuildNext();
            var barn = w.BuildNext();
            BoxIn(w, barn, 80);
            var count = w.Buildings.All.Count;

            Assert.That(() => w.Dawn(), Throws.Nothing);
            Assert.That(w.Buildings.All.Count, Is.EqualTo(count));
        }

        [Test]
        public void A_field_still_being_sown_feeds_nobody_yet()
        {
            // All plains, so nothing in reach but the field; and nothing in
            // store. One person, whom one standing field feeds.
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            w.Dawn();
            var field = w.Latest;
            Assume.That(field?.Kind, Is.EqualTo(BuildingKind.Field));
            w.Stores.Consume(ResourceKind.Food, w.Stores.Available(ResourceKind.Food));
            Assume.That(w.Stores.Available(ResourceKind.Grain), Is.Zero, "no harvest yet");

            Assert.That(w.Buildings.IsFoodShort(w.Settlement, 1), Is.True, "a field going up grows nothing");

            w.Finish(w.Buildings.All[w.Buildings.All.Count - 1]);

            Assert.That(w.Buildings.IsFoodShort(w.Settlement, 1), Is.False, "a field standing feeds one");
        }

        [Test]
        public void A_village_with_a_year_in_store_is_not_short_of_food()
        {
            var w = new BuildingsWorld();
            var need = Buildings.YearlyNeed(w.Living);
            Assert.That(w.Buildings.IsFoodShort(w.Settlement, w.Living), Is.True, "no bushes, no fields");

            w.Stores.Gather(ResourceKind.Grain, (int)(need / PrimitiveTier.MealsPerGrain));

            Assert.That(w.Buildings.IsFoodShort(w.Settlement, w.Living), Is.False);
        }

        [Test]
        public void Bushes_in_reach_are_counted_as_a_years_picking()
        {
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var x = 0; x < 5; x++)
                {
                    grid.Set(new WorldPosition(24 + x, 20), TerrainKind.Scrub);
                }
            });

            var perBush = (long)w.World.Land.BushPicks
                * (PrimitiveTier.Forage.Outputs[0].Quantity
                    + PrimitiveTier.ForageIn(Season.Summer).Outputs[0].Quantity
                    + PrimitiveTier.ForageIn(Season.Autumn).Outputs[0].Quantity);

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.BushYearlyFood, Is.EqualTo(perBush));
                Assert.That(w.Buildings.ForageInReach(w.Settlement.Position, w.Settlement.Id), Is.EqualTo(5 * perBush));
            });
        }

        [Test]
        public void Land_a_trip_a_day_away_counts_half_for_wood_and_not_at_all_for_food()
        {
            // Due east across plains: ten cells out a worker still makes two
            // trips a day; forty out, one; eighty, none before dusk. A village
            // counts on a bush only near, and on a tree forty out at half.
            var near = new WorldPosition(BuildingsWorld.Centre.X + 10, BuildingsWorld.Centre.Y);
            var far = new WorldPosition(BuildingsWorld.Centre.X + 40, BuildingsWorld.Centre.Y);
            var tooFar = new WorldPosition(BuildingsWorld.Centre.X + 80, BuildingsWorld.Centre.Y);
            var w = new BuildingsWorld(size: 110, paint: grid =>
            {
                foreach (var at in new[] { near, far, tooFar })
                {
                    grid.Set(at, TerrainKind.Scrub);
                    grid.Set(new WorldPosition(at.X, at.Y + 2), TerrainKind.Forest);
                }
            });
            w.World.KnownMaps.Reveal(w.Settlement.Id, tooFar, 2);

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.ForageInReach(w.Settlement.Position, w.Settlement.Id), Is.EqualTo(w.Buildings.BushYearlyFood), "the near bush alone");
                Assert.That(w.Buildings.WoodInReach(w.Settlement), Is.EqualTo(w.Buildings.WoodFromTrees(3) / 2), "the near tree whole, the far one half");
            });

            // Counted again once the ground changes: the near bush cleared.
            w.World.Grid.Set(near, TerrainKind.Plains);
            Assert.That(w.Buildings.ForageInReach(w.Settlement.Position, w.Settlement.Id), Is.Zero);

            // And once the map does: a far tree nobody knew of, now seen.
            var unseen = new WorldPosition(BuildingsWorld.Centre.X, BuildingsWorld.Centre.Y + 60);
            w.World.Grid.Set(unseen, TerrainKind.Forest);
            Assert.That(w.Buildings.WoodInReach(w.Settlement), Is.EqualTo(w.Buildings.WoodFromTrees(3) / 2), "not seen yet");
            w.World.KnownMaps.Reveal(w.Settlement.Id, unseen, 0);
            Assert.That(w.Buildings.WoodInReach(w.Settlement), Is.EqualTo(w.Buildings.WoodFromTrees(4) / 2));
        }

        [Test]
        public void Builders_claim_shares_and_clearing_fells_trees_for_their_wood()
        {
            // No plains anywhere a house could go: a checkerboard of forest
            // and scrub, so any footprint has standing trees and bushes on it.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        grid.Set(new WorldPosition(x, y), (x + y) % 2 == 0 ? TerrainKind.Forest : TerrainKind.Scrub);
                    }
                }

                // The yard open, so the house's clearing is its own (#23).
                PaintYard(grid, TerrainKind.Plains);
            });
            w.Wood(100);
            w.Dawn();

            var house = w.Buildings.All.Single();
            var cells = house.Width * house.Height;
            var trees = 0;

            for (var dy = 0; dy < house.Height; dy++)
            {
                for (var dx = 0; dx < house.Width; dx++)
                {
                    trees += w.World.Grid[new WorldPosition(house.Anchor.X + dx, house.Anchor.Y + dy)] == TerrainKind.Forest ? 1 : 0;
                }
            }

            Assert.That(trees, Is.GreaterThan(0));
            var expected = (trees * w.World.Land.TreeCuts * Buildings.TicksPerCut) + ((cells - trees) * Buildings.ClearTicksPerCell);

            Assert.That(house.ClearTicks, Is.EqualTo(expected));
            Assert.That(w.Buildings.ShareOf(house, JobKind.Builder), Is.EqualTo(Buildings.ShareTicks));

            var woodBefore = w.Stores.Available(ResourceKind.Wood);
            var foodBefore = w.Stores.Available(ResourceKind.Food);
            w.Finish(house);

            // The bushes pay their spring fruit and a little brushwood (#155).
            var bushes = cells - trees;

            Assert.Multiple(() =>
            {
                Assert.That(house.Cleared, Is.True);
                Assert.That(house.IsComplete, Is.True);
                Assert.That(
                    w.Stores.Available(ResourceKind.Wood) - woodBefore,
                    Is.EqualTo((trees * w.World.Land.TreeCuts * Buildings.WoodPerCut) + (bushes * Buildings.WoodPerBush)));
                Assert.That(
                    w.Stores.Available(ResourceKind.Food) - foodBefore,
                    Is.EqualTo(bushes * w.World.Land.BushPicks * Buildings.FoodPerPick(Season.Spring)));

                for (var dy = 0; dy < house.Height; dy++)
                {
                    for (var dx = 0; dx < house.Width; dx++)
                    {
                        var at = new WorldPosition(house.Anchor.X + dx, house.Anchor.Y + dy);
                        Assert.That(w.World.Grid[at], Is.EqualTo(TerrainKind.Plains), at.ToString());
                        Assert.That(w.World.Land.StateAt(w.World.Grid.IndexOf(at)), Is.Zero, at.ToString());
                    }
                }
            });
        }

        [Test]
        public void Clearing_a_tree_costs_and_gives_only_the_cuts_it_has_left()
        {
            // The #148 review: a tree four cuts down had already given eight
            // of its ten Wood, and clearing paid all ten again.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Forest);
                    }
                }

                // The yard open, so the house's clearing is its own (#23).
                PaintYard(grid, TerrainKind.Plains);
            });
            var land = w.World.Land;

            for (var y = 12; y <= 28; y++)
            {
                for (var x = 12; x <= 28; x++)
                {
                    var at = new WorldPosition(x, y);

                    for (var cut = 0; cut < LandCover.DefaultTreeCuts - 1 && w.World.Grid[at] == TerrainKind.Forest; cut++)
                    {
                        land.Take(at);
                    }
                }
            }

            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var cells = house.Width * house.Height;
            var perCut = PrimitiveTier.GatherWood.Duration;

            Assert.That(house.ClearTicks, Is.EqualTo(cells * perCut), "one cut left on every tree");

            var wood = w.Stores.Available(ResourceKind.Wood);
            w.Finish(house);

            Assert.That(w.Stores.Available(ResourceKind.Wood) - wood, Is.EqualTo(cells * PrimitiveTier.GatherWood.Outputs[0].Quantity));
        }

        [Test]
        public void Clearing_pays_the_cuts_it_was_priced_at_even_when_a_claim_comes_back()
        {
            // The #148 review: a woodcutter out at approval holds a cut that
            // is priced as done; dying on the way gives it back, and the tree
            // then had a cut more than clearing charged for.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Forest);
                    }
                }

                // The yard open, so the house's clearing is its own (#23).
                PaintYard(grid, TerrainKind.Plains);
            });
            var land = w.World.Land;
            var claimed = new System.Collections.Generic.List<WorldPosition>();

            // Every tree a house beside the camp could stand on.
            for (var y = 12; y <= 28; y++)
            {
                for (var x = 12; x <= 28; x++)
                {
                    var at = new WorldPosition(x, y);

                    if (w.World.Grid[at] == TerrainKind.Forest)
                    {
                        land.Take(at);
                        claimed.Add(at);
                    }
                }
            }

            // Little enough that the clearing's wood all fits (#98).
            w.Wood(BuildingTable.Of(BuildingKind.House).Wood);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var cells = house.Width * house.Height;
            Assert.That(house.ClearTicks, Is.EqualTo(cells * (LandCover.DefaultTreeCuts - 1) * Buildings.TicksPerCut), "priced with the claimed cut done");

            // Every trip out comes back empty-handed: the claims go back.
            foreach (var at in claimed)
            {
                land.Return(at);
            }

            var wood = w.Stores.Available(ResourceKind.Wood);
            w.Finish(house);

            Assert.That(
                w.Stores.Available(ResourceKind.Wood) - wood,
                Is.EqualTo(cells * (LandCover.DefaultTreeCuts - 1) * Buildings.WoodPerCut),
                "the Wood of the cuts it charged for, not the cut given back");
        }

        [Test]
        public void Nothing_is_built_in_the_camp_yard()
        {
            var w = BuildVillage();
            var radius = Buildings.CampYardRadius;

            foreach (var building in w.Buildings.All)
            {
                var clearance = BuildingTable.Of(building.Kind).Clearance;
                for (var y = building.Anchor.Y - clearance; y < building.Anchor.Y + building.Height; y++)
                {
                    for (var x = building.Anchor.X; x < building.Anchor.X + building.Width; x++)
                    {
                        var inYard = System.Math.Abs(x - BuildingsWorld.Centre.X) <= radius && System.Math.Abs(y - BuildingsWorld.Centre.Y) <= radius;
                        Assert.That(inYard, Is.False, building + " or its roof takes (" + x + ", " + y + ") in the camp yard");
                    }
                }
            }
        }

        [Test]
        public void Nothing_is_built_within_the_gap_of_a_neighbouring_settlement()
        {
            // The #153 review kept a neighbour's yard clear; VillageGap keeps
            // the ground between two villages clear too. The neighbour stands
            // thirty-eight cells east, so the gap leaves this village barely
            // room past its own yard to build toward it.
            var w = new BuildingsWorld(people: 40, size: 100);
            var neighbour = w.World.Founding.Found(
                w.World.AddBand(4, new WorldPosition(58, 20)),
                new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable));
            w.Wood(5000);
            for (var i = 0; i < 16; i++)
            {
                var before = w.Buildings.All.Count;
                w.Dawn();
                if (w.Buildings.All.Count == before)
                {
                    break;
                }

                w.Finish(w.Buildings.All[before]);
            }

            Assert.That(w.Buildings.All.Count(b => b.Settlement == w.Settlement.Id), Is.GreaterThan(8), "the village grew");

            var yardEdge = neighbour.Position.X - Buildings.CampYardRadius;
            foreach (var building in w.Buildings.All)
            {
                Assert.That(yardEdge - (building.Anchor.X + building.Width - 1), Is.GreaterThan(Buildings.VillageGap), building + " stands within the gap of " + neighbour.Id);
            }
        }

        [Test]
        public void A_band_does_not_settle_within_the_gap_of_a_settlement()
        {
            // #154: nothing kept two camps apart, and the #153 review's
            // sibling - a camp founded over a standing house. The yard of a
            // camp asked about due south of the village must keep VillageGap
            // from the village's own. Bushes in a row within an hour of it.
            var bushes = (int)(((Buildings.YearlyNeed(12) * 3 / 2 / SimulationTime.SeasonsPerYear) / 42) + 1);
            var w = new BuildingsWorld(size: 100, paint: grid =>
            {
                for (var i = 0; i < bushes; i++)
                {
                    grid.Set(new WorldPosition(12 + (i % 15), 68 + (i / 15)), TerrainKind.Scrub);
                }

                grid.Set(new WorldPosition(11, 68), TerrainKind.Rocks);
            });
            w.Wood(100);
            var house = w.BuildNext();
            var radius = Buildings.CampYardRadius;
            var map = w.Settlement.Id;
            var closest = BuildingsWorld.Centre.Y + radius + Buildings.VillageGap + radius + 1;

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.CanSettle(new WorldPosition(BuildingsWorld.Centre.X, closest - 1), map, 12), Is.False, "the yard's edge is within the gap of the village's");
                Assert.That(w.Buildings.CanSettle(new WorldPosition(BuildingsWorld.Centre.X, closest), map, 12), Is.True, "one row further, clear of it");
                Assert.That(w.Buildings.CanSettle(w.Settlement.Position, map, 12), Is.False, "nor on its camp");
                Assert.That(w.Buildings.CanSettle(house.Anchor, map, 12), Is.False, "nor on its house");
            });
        }

        [Test]
        public void The_gap_is_kept_from_a_roof_as_from_its_house()
        {
            // A roof stands Clearance rows over the ground north of its house,
            // and counts as the house does. The camp south on a tall map, so
            // its first house goes up north of it with room north of that.
            var camp = new WorldPosition(20, 60);
            var bushes = (int)(((Buildings.YearlyNeed(12) * 3 / 2 / SimulationTime.SeasonsPerYear) / 42) + 1);
            var w = new BuildingsWorld(size: 100, camp: camp, paint: grid =>
            {
                for (var i = 0; i < bushes; i++)
                {
                    grid.Set(new WorldPosition(12 + (i % 15), 10 + (i / 15)), TerrainKind.Scrub);
                }

                grid.Set(new WorldPosition(11, 10), TerrainKind.Rocks);
            });
            w.Wood(100);
            var house = w.BuildNext();
            var roofTop = house.Anchor.Y - BuildingTable.Of(BuildingKind.House).Clearance;
            var radius = Buildings.CampYardRadius;
            var map = w.Settlement.Id;
            var furthest = roofTop - Buildings.VillageGap - radius - 1;

            Assert.Multiple(() =>
            {
                Assert.That(furthest + 1 + radius, Is.LessThan(camp.Y - radius - Buildings.VillageGap), "the camp's own yard is not what binds here");
                Assert.That(w.Buildings.CanSettle(new WorldPosition(house.Anchor.X, furthest + 1), map, 12), Is.False, "the yard's edge is within the gap of the roof");
                Assert.That(w.Buildings.CanSettle(new WorldPosition(house.Anchor.X, furthest), map, 12), Is.True, "one row further, clear of it");
            });
        }

        [Test]
        public void No_roof_is_built_within_the_gap_of_a_neighbouring_settlement()
        {
            // A neighbour forty cells north: the gap reaches down to just
            // north of this village's own yard, where a roof over a house
            // beside the yard would overhang into it.
            var camp = new WorldPosition(30, 64);
            var w = new BuildingsWorld(people: 40, size: 100, camp: camp);
            var neighbour = w.World.Founding.Found(
                w.World.AddBand(4, new WorldPosition(camp.X, camp.Y - 40)),
                new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable));
            w.Wood(5000);
            for (var i = 0; i < 16; i++)
            {
                var before = w.Buildings.All.Count;
                w.Dawn();
                if (w.Buildings.All.Count == before)
                {
                    break;
                }

                w.Finish(w.Buildings.All[before]);
            }

            var gapEnds = neighbour.Position.Y + Buildings.CampYardRadius + Buildings.VillageGap;
            Assert.That(w.Buildings.All.Where(b => b.Settlement == w.Settlement.Id).Count(), Is.GreaterThan(8), "the village grew");

            foreach (var building in w.Buildings.All.Where(b => b.Settlement == w.Settlement.Id))
            {
                var roofTop = building.Anchor.Y - BuildingTable.Of(building.Kind).Clearance;
                Assert.That(roofTop, Is.GreaterThan(gapEnds), building + "'s roof is within the gap of " + neighbour.Id);
            }
        }

        // Every cell of the camp yard at the fixture's centre, set to one kind.
        private static void PaintYard(TerrainGrid grid, TerrainKind kind)
        {
            var radius = Buildings.CampYardRadius;

            for (var y = BuildingsWorld.Centre.Y - radius; y <= BuildingsWorld.Centre.Y + radius; y++)
            {
                for (var x = BuildingsWorld.Centre.X - radius; x <= BuildingsWorld.Centre.X + radius; x++)
                {
                    grid.Set(new WorldPosition(x, y), kind);
                }
            }
        }

        [Test]
        public void The_camps_to_keep_clear_refuse_null()
        {
            var w = new BuildingsWorld();

            Assert.That(() => w.Buildings.Camps = null!, Throws.ArgumentNullException);
        }

        // Forty people with wood to spare, built up as far as sixteen
        // buildings: houses, barns and fields enough that buildings go up on
        // every side of the camp and both north and south of each other.
        private static BuildingsWorld BuildVillage()
        {
            var w = new BuildingsWorld(people: 40);
            w.Wood(5000);
            for (var i = 0; i < 16; i++)
            {
                var before = w.Buildings.All.Count;
                w.Dawn();
                if (w.Buildings.All.Count == before)
                {
                    break;
                }

                w.Finish(w.Buildings.All[before]);
            }

            Assume.That(w.Buildings.All.Count(b => BuildingTable.Of(b.Kind).Clearance > 0), Is.GreaterThanOrEqualTo(4), "enough roofs to crowd");
            return w;
        }

        [Test]
        public void No_roof_stands_over_another_building_or_the_camp()
        {
            var w = BuildVillage();

            foreach (var building in w.Buildings.All)
            {
                var clearance = BuildingTable.Of(building.Kind).Clearance;
                for (var dy = 1; dy <= clearance; dy++)
                {
                    for (var dx = 0; dx < building.Width; dx++)
                    {
                        var under = new WorldPosition(building.Anchor.X + dx, building.Anchor.Y - dy);
                        if (under.Y < 0)
                        {
                            continue;
                        }

                        Assert.That(w.Buildings.At(under), Is.Null, building + "'s roof stands over " + w.Buildings.At(under));
                        Assert.That(under, Is.Not.EqualTo(BuildingsWorld.Centre), building + "'s roof stands over the camp");
                    }
                }
            }
        }

        [Test]
        public void A_roof_may_hang_off_the_north_edge_of_the_map()
        {
            // Rocks everywhere but the camp, a lane north from the corner of
            // its yard, and the top rows: the only room for a house is against
            // the north edge, where its roof stands off the map. The lane
            // keeps off the street north from the camp, which nothing is
            // built on (#171).
            var house = BuildingTable.Of(BuildingKind.House);
            var camp = new WorldPosition(20, 8);
            var lane = camp.X + Buildings.CampYardRadius;
            var w = new BuildingsWorld(
                paint: grid =>
                {
                    for (var y = 0; y < BuildingsWorld.Size; y++)
                    {
                        for (var x = 0; x < BuildingsWorld.Size; x++)
                        {
                            var open = y < house.Height || (x == lane && y <= camp.Y);
                            grid.Set(new WorldPosition(x, y), open ? TerrainKind.Plains : TerrainKind.Rocks);
                        }
                    }
                },
                camp: camp);
            w.Wood(100);

            w.Dawn();

            Assert.That(w.Buildings.All.Single().Anchor.Y, Is.Zero);
        }

        [Test]
        public void Ground_to_clear_beside_the_yard_is_not_passed_over_for_plains_further_out()
        {
            // Scrub right up to the camp, plains a little further out. Until
            // #155 the house went out to the plains; now the ground scores
            // the same, so it goes beside the yard and clears the bushes.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 14; y <= 26; y++)
                {
                    for (var x = 14; x <= 26; x++)
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Scrub);
                    }
                }

                grid.Set(BuildingsWorld.Centre, TerrainKind.Plains);
            });
            w.Wood(100);
            w.Dawn();

            var house = w.Buildings.All.Single();
            var footprint = new List<WorldPosition>();

            for (var dy = 0; dy < house.Height; dy++)
            {
                for (var dx = 0; dx < house.Width; dx++)
                {
                    footprint.Add(new WorldPosition(house.Anchor.X + dx, house.Anchor.Y + dy));
                }
            }

            // Its footprint, its lane and the scrub in the yard it clears as
            // the square (#23) are scrub still waiting to be cleared.
            var scrub = footprint.Concat(house.Lane).Concat(house.Square).Distinct().Count(at => w.World.Grid[at] == TerrainKind.Scrub);

            Assert.Multiple(() =>
            {
                Assert.That(footprint.Count(at => w.World.Grid[at] == TerrainKind.Scrub), Is.GreaterThan(footprint.Count / 2), "mostly on the scrub, not out on the plains: " + house.Anchor);
                Assert.That(house.Square, Is.Not.Empty);
                Assert.That(house.Cleared, Is.False);
                Assert.That(house.ClearTicks, Is.EqualTo(scrub * Buildings.ClearTicksPerCell));
            });
        }

        [Test]
        public void An_unclaimed_trip_gives_its_hours_back()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();

            w.Buildings.Claim(house, JobKind.Builder, Buildings.ShareTicks);
            Assert.That(w.Buildings.TryBuildWork(w.Settlement.Id, out _, out var shortBy, out var target), Is.True);
            Assert.That(shortBy, Is.EqualTo(house.LabourTicks - Buildings.ShareTicks));
            Assert.That(target, Is.EqualTo(house.LabourTicks * Buildings.NeedScale));

            w.Buildings.Unclaim(house.Anchor, JobKind.Builder, Buildings.ShareTicks);
            Assert.That(house.Claimed, Is.Zero);
        }

        [Test]
        public void A_field_counts_one_day_a_day_then_harvests_grain_and_is_sown_again()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            var field = w.BuildNext();
            var day = SimulationTime.TicksPerDay;

            void WorkTheDay()
            {
                for (var share = 0; share < Buildings.FieldDayTicks / Buildings.ShareTicks; share++)
                {
                    Assert.That(w.Buildings.TryFieldWork(w.Settlement.Id, out var f, out _, out _), Is.True);
                    Assert.That(f, Is.SameAs(field));
                    var ticks = w.Buildings.ShareOf(field, JobKind.Farmer);
                    w.Buildings.Claim(field, JobKind.Farmer, ticks);
                    w.Buildings.Credit(field.Anchor, JobKind.Farmer, ticks, w.Stores);
                }

                Assert.That(w.Buildings.TryFieldWork(w.Settlement.Id, out _, out _, out _), Is.False, "a day's hours are all done");
            }

            for (var i = 0; i < Buildings.TendingDays; i++)
            {
                WorkTheDay();
                w.World.Clock.AdvanceTo(w.World.Now.Plus(day), w.World.Router);
            }

            Assert.That(field.Stage, Is.EqualTo(FieldStage.Harvesting));
            var grain = w.Stores.Available(ResourceKind.Grain);

            for (var i = 0; i < Buildings.HarvestDays; i++)
            {
                WorkTheDay();
                w.World.Clock.AdvanceTo(w.World.Now.Plus(day), w.World.Router);
            }

            Assert.Multiple(() =>
            {
                Assert.That(field.Stage, Is.EqualTo(FieldStage.Tending));
                Assert.That(field.DaysDone, Is.Zero);
                Assert.That(w.Stores.Flows(ResourceKind.Grain).Gathered - grain, Is.EqualTo(Buildings.HarvestDays * Buildings.GrainPerHarvestDay));
                Assert.That(w.Heard.Count(e => e.Kind == DomainEventKind.FieldHarvested && e.PrimaryEntity == field.Id), Is.EqualTo(1));
            });
        }

        [Test]
        public void Winter_kills_a_growing_crop_and_the_field_is_sown_fresh()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            var field = w.BuildNext();
            WorkFieldDays(w, field, 4);

            // The last dawn of autumn: the crop is still growing.
            AdvanceToDay(w, (3L * SimulationTime.DaysPerSeason) - 1L);
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            Assert.That(field.DaysDone, Is.EqualTo(4), "autumn takes nothing");

            // Winter, and first another settlement's dawn: its frost is its own.
            AdvanceToDay(w, 3L * SimulationTime.DaysPerSeason);
            var other = w.World.Founding.Found(
                w.World.AddBand(4, new WorldPosition(3, 3)),
                new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable));
            w.Buildings.AtDawn(other, 0, other.Members.Count);
            Assert.That(field.DaysDone, Is.EqualTo(4), "another village's winter");

            w.Buildings.AtDawn(w.Settlement, 0, w.Living);

            Assert.Multiple(() =>
            {
                Assert.That(field.Stage, Is.EqualTo(FieldStage.Tending));
                Assert.That(field.DaysDone, Is.Zero);
            });
        }

        [Test]
        public void Winter_takes_the_unharvested_rest_but_not_the_grain_already_in()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            var field = w.BuildNext();
            WorkFieldDays(w, field, Buildings.TendingDays + 2);
            Assume.That(field.Stage, Is.EqualTo(FieldStage.Harvesting));
            var grain = w.Stores.Flows(ResourceKind.Grain).Gathered;
            Assume.That(grain, Is.EqualTo(2 * Buildings.GrainPerHarvestDay));

            AdvanceToDay(w, 3L * SimulationTime.DaysPerSeason);
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);

            Assert.Multiple(() =>
            {
                Assert.That(field.Stage, Is.EqualTo(FieldStage.Tending));
                Assert.That(field.DaysDone, Is.Zero);
                Assert.That(w.Stores.Flows(ResourceKind.Grain).Gathered, Is.EqualTo(grain));
                Assert.That(w.Heard.Any(e => e.Kind == DomainEventKind.FieldHarvested), Is.False, "a harvest cut short is not a harvest");
            });
        }

        // Whole days of field work, one a day from now.
        private static void WorkFieldDays(BuildingsWorld w, Building field, int days)
        {
            for (var i = 0; i < days; i++)
            {
                w.Buildings.Claim(field, JobKind.Farmer, Buildings.FieldDayTicks);
                w.Buildings.Credit(field.Anchor, JobKind.Farmer, Buildings.FieldDayTicks, w.Stores);
                w.World.Clock.AdvanceTo(w.World.Now.Plus(SimulationTime.TicksPerDay), w.World.Router);
            }
        }

        private static void AdvanceToDay(BuildingsWorld w, long day) =>
            w.World.Clock.AdvanceTo(new SimulationTime(day * SimulationTime.TicksPerDay), w.World.Router);

        [Test]
        public void A_harvest_day_left_unfinished_brings_in_nothing()
        {
            // The #148 review: Grain paid per share, with unfinished days
            // forgotten at midnight, let one share a day harvest forever.
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            var field = w.BuildNext();
            var day = SimulationTime.TicksPerDay;

            for (var i = 0; i < Buildings.TendingDays; i++)
            {
                w.Buildings.Claim(field, JobKind.Farmer, Buildings.FieldDayTicks);
                w.Buildings.Credit(field.Anchor, JobKind.Farmer, Buildings.FieldDayTicks, w.Stores);
                w.World.Clock.AdvanceTo(w.World.Now.Plus(day), w.World.Router);
            }

            Assert.That(field.Stage, Is.EqualTo(FieldStage.Harvesting));
            var grain = w.Stores.Flows(ResourceKind.Grain).Gathered;

            // One share a day, never a whole day's work.
            for (var i = 0; i < 2 * Buildings.HarvestDays; i++)
            {
                w.Buildings.Claim(field, JobKind.Farmer, Buildings.ShareTicks);
                w.Buildings.Credit(field.Anchor, JobKind.Farmer, Buildings.ShareTicks, w.Stores);
                w.World.Clock.AdvanceTo(w.World.Now.Plus(day), w.World.Router);
            }

            Assert.Multiple(() =>
            {
                Assert.That(w.Stores.Flows(ResourceKind.Grain).Gathered, Is.EqualTo(grain), "no harvest day was finished");
                Assert.That(field.Stage, Is.EqualTo(FieldStage.Harvesting));
                Assert.That(field.DaysDone, Is.Zero);
            });

            // A whole day's work brings in that day's Grain.
            w.Buildings.Claim(field, JobKind.Farmer, Buildings.ShareTicks);
            w.Buildings.Credit(field.Anchor, JobKind.Farmer, Buildings.ShareTicks, w.Stores);
            Assert.That(w.Stores.Flows(ResourceKind.Grain).Gathered, Is.EqualTo(grain), "half a day");
            w.Buildings.Claim(field, JobKind.Farmer, Buildings.ShareTicks);
            w.Buildings.Credit(field.Anchor, JobKind.Farmer, Buildings.ShareTicks, w.Stores);
            Assert.That(w.Stores.Flows(ResourceKind.Grain).Gathered, Is.EqualTo(grain + Buildings.GrainPerHarvestDay));
        }

        [Test]
        public void Nobody_works_a_field_in_winter()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            w.BuildNext();

            w.World.Clock.AdvanceTo(new SimulationTime(3L * SimulationTime.DaysPerSeason * SimulationTime.TicksPerDay), w.World.Router);

            Assert.That(w.World.Now.Season, Is.EqualTo(Season.Winter));
            Assert.That(w.Buildings.TryFieldWork(w.Settlement.Id, out _, out _, out _), Is.False);
        }

        [Test]
        public void A_band_settles_only_in_early_spring_with_a_season_of_bushes_and_room_for_a_barn()
        {
            // Enough bushes in reach for a season and a half of twelve people,
            // at 42 Food a bush a year (7 picks of 2 in each fruiting season).
            var bushes = (int)(((Buildings.YearlyNeed(12) * 3 / 2 / SimulationTime.SeasonsPerYear) / 42) + 1);
            var w = new BuildingsWorld(size: 100, paint: grid =>
            {
                for (var i = 0; i < bushes; i++)
                {
                    grid.Set(new WorldPosition(SettleAt.X - 5 + (i % 10), SettleAt.Y + 5 + (i / 10)), TerrainKind.Scrub);
                }

                grid.Set(SettleRock, TerrainKind.Rocks);
            });
            var at = SettleAt;
            var map = w.Settlement.Id;

            Assert.That(w.Buildings.CanSettle(at, map, 12), Is.True, "spring day 0");
            Assert.That(w.Buildings.CanSettle(at, map, 1000), Is.False, "too many mouths");

            w.World.Clock.AdvanceTo(new SimulationTime(((SimulationTime.DaysPerSeason / 2) - 1) * SimulationTime.TicksPerDay), w.World.Router);
            Assert.That(w.Buildings.CanSettle(at, map, 12), Is.True, "the last day of early spring");

            w.World.Clock.AdvanceTo(new SimulationTime((SimulationTime.DaysPerSeason / 2) * SimulationTime.TicksPerDay), w.World.Router);
            Assert.That(w.Buildings.CanSettle(at, map, 12), Is.False, "late spring");

            w.World.Clock.AdvanceTo(new SimulationTime(SimulationTime.DaysPerSeason * SimulationTime.TicksPerDay), w.World.Router);
            Assert.That(w.Buildings.CanSettle(at, map, 12), Is.False, "the first day of summer");
        }

        [Test]
        public void A_band_settles_only_with_a_rock_in_reach()
        {
            // #166: every house, barn and store needs stone, and a rock is the
            // only source until a quarry, so a village with none in reach
            // could never build. Bushes and barn ground as the season test.
            var bushes = (int)(((Buildings.YearlyNeed(12) * 3 / 2 / SimulationTime.SeasonsPerYear) / 42) + 1);
            System.Action<TerrainGrid> paintBushes = grid =>
            {
                for (var i = 0; i < bushes; i++)
                {
                    grid.Set(new WorldPosition(SettleAt.X - 5 + (i % 10), SettleAt.Y + 5 + (i / 10)), TerrainKind.Scrub);
                }
            };
            var bare = new BuildingsWorld(size: 100, paint: paintBushes);
            var rocky = new BuildingsWorld(size: 100, paint: grid =>
            {
                paintBushes(grid);
                grid.Set(SettleRock, TerrainKind.Rocks);
            });

            Assert.Multiple(() =>
            {
                Assert.That(bare.Buildings.HasRocksInReach(SettleAt, System.ReadOnlySpan<bool>.Empty), Is.False);
                Assert.That(bare.Buildings.CanSettle(SettleAt, bare.Settlement.Id, 12), Is.False, "food and barn ground, but no stone");
                Assert.That(rocky.Buildings.HasRocksInReach(SettleAt, System.ReadOnlySpan<bool>.Empty), Is.True);
                Assert.That(rocky.Buildings.CanSettle(SettleAt, rocky.Settlement.Id, 12), Is.True, "one rock is stone for good");
            });
        }

        // Where the settling fixtures ask about, on a map a hundred across:
        // VillageGap clear of the fixture's own settlement, and inside what
        // its band saw.
        private static readonly WorldPosition SettleAt = new WorldPosition(66, 20);

        // A rock in reach of SettleAt, beside its bushes (#166).
        private static readonly WorldPosition SettleRock = new WorldPosition(SettleAt.X - 6, SettleAt.Y + 5);

        [Test]
        public void Skills_and_food_questions_refuse_what_is_not_a_question()
        {
            // The #148 review: a capability of None, or one cast from nowhere,
            // must not quietly pass a build gate.
            var w = new BuildingsWorld();
            var skills = new NoviceSkills();

            Assert.Multiple(() =>
            {
                Assert.That(skills.BestIn(w.Settlement, Capability.Farming), Is.EqualTo(SkillTier.Novice));
                Assert.That(() => skills.BestIn(null!, Capability.Farming), Throws.ArgumentNullException);
                Assert.That(() => skills.BestIn(w.Settlement, Capability.None), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => skills.BestIn(w.Settlement, (Capability)99), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.IsFoodShort(null!, 1), Throws.ArgumentNullException);
                Assert.That(() => Buildings.YearlyNeed(-1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(Buildings.YearlyNeed(0), Is.Zero);
            });
        }

        [Test]
        public void A_dawn_needs_a_settlement()
        {
            var w = new BuildingsWorld();

            Assert.That(() => w.Buildings.AtDawn(null!, 1, 1), Throws.ArgumentNullException);
        }

        [Test]
        public void Settling_refuses_what_it_cannot_mean_in_any_season()
        {
            // Found sweeping for the #148 review's class: the season was
            // checked first, so outside early spring a bad count, an
            // off-map position or a map nobody keeps came back as "no".
            var w = new BuildingsWorld();
            w.World.Clock.AdvanceTo(new SimulationTime(SimulationTime.DaysPerSeason * SimulationTime.TicksPerDay), w.World.Router);
            var map = w.Settlement.Id;

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.CanSettle(BuildingsWorld.Centre, map, 1), Is.False, "summer");
                Assert.That(() => w.Buildings.CanSettle(BuildingsWorld.Centre, map, -1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.CanSettle(new WorldPosition(-1, 0), map, 1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.CanSettle(BuildingsWorld.Centre, new EntityId(EntityKind.MobileGroup, 999UL), 1), Throws.InvalidOperationException);
                Assert.That(() => w.Buildings.CanSettle(BuildingsWorld.Centre, new bool[1], 1), Throws.ArgumentException, "a known mask short of the grid");
                Assert.That(() => w.Buildings.CanSettle(BuildingsWorld.Centre, System.ReadOnlySpan<bool>.Empty, -1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.CanSettle(new WorldPosition(-1, 0), System.ReadOnlySpan<bool>.Empty, 1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.ForageInReach(BuildingsWorld.Centre, new bool[1]), Throws.ArgumentException);
                Assert.That(() => w.Buildings.WoodInReach(BuildingsWorld.Centre, new bool[1]), Throws.ArgumentException);
            });
        }

        [Test]
        public void A_map_nobody_holds_reads_an_empty_mask_as_the_whole_map()
        {
            // What a world picks its starting villages with (#146): no holder
            // exists yet, so the land is read as if all of it were known.
            var bushes = (int)(((Buildings.YearlyNeed(12) * 3 / 2 / SimulationTime.SeasonsPerYear) / 42) + 1);
            var w = new BuildingsWorld(size: 100, paint: grid =>
            {
                for (var i = 0; i < bushes; i++)
                {
                    grid.Set(new WorldPosition(SettleAt.X - 5 + (i % 10), SettleAt.Y + 5 + (i / 10)), TerrainKind.Scrub);
                }

                grid.Set(SettleRock, TerrainKind.Rocks);

                for (var x = SettleAt.X + 10; x < SettleAt.X + 16; x++)
                {
                    grid.Set(new WorldPosition(x, SettleAt.Y + 10), TerrainKind.Forest);
                }
            });
            var at = SettleAt;
            var all = new bool[w.World.Grid.CellCount];
            System.Array.Fill(all, true);
            var none = new bool[w.World.Grid.CellCount];

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.ForageInReach(at, System.ReadOnlySpan<bool>.Empty), Is.EqualTo(w.Buildings.ForageInReach(at, all)).And.GreaterThan(0));
                Assert.That(w.Buildings.WoodInReach(at, System.ReadOnlySpan<bool>.Empty), Is.EqualTo(w.Buildings.WoodInReach(at, all)).And.GreaterThan(0));
                Assert.That(w.Buildings.CanSettle(at, System.ReadOnlySpan<bool>.Empty, 12), Is.True);
                Assert.That(w.Buildings.ForageInReach(at, none), Is.Zero, "a mask that knows nothing finds nothing");
                Assert.That(w.Buildings.WoodInReach(at, none), Is.Zero);
                Assert.That(w.Buildings.CanSettle(at, none, 12), Is.False);
            });
        }

        [Test]
        public void A_dawn_refuses_counts_that_cannot_be()
        {
            // The #148 review: more hands idle than people alive passed the
            // spare-hands gate, and a negative count quietly skipped it.
            var w = new BuildingsWorld();
            w.Wood(100);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Buildings.AtDawn(w.Settlement, -1, w.Living), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.AtDawn(w.Settlement, 0, -1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.AtDawn(w.Settlement, 2, 1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(w.Buildings.All, Is.Empty, "a refused dawn approved nothing");
                Assert.That(() => w.Buildings.AtDawn(w.Settlement, 0, 0), Throws.Nothing, "nobody idle among nobody");
                Assert.That(() => w.Buildings.IsReserved(-1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.IsReserved(w.World.Grid.CellCount), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(w.Buildings.IsReserved(0), Is.False);
                Assert.That(() => KingdomWatch.Core.Needs.Hunger.MealsInStore(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void No_demand_no_building()
        {
            // Everyone housed, a year in store and room for the winter's
            // wood: nothing is wanted.
            var w = new BuildingsWorld();
            w.Wood(1000);
            w.Stores.Gather(ResourceKind.Grain, (int)(Buildings.YearlyNeed(w.Living) / PrimitiveTier.MealsPerGrain));

            while (w.Settlement.Members.Any(m => w.World.Households.Of(m) is Household h && w.Buildings.HomeOf(h.Id) is null))
            {
                w.BuildNext();
                w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            }

            var count = w.Buildings.All.Count;
            w.Dawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.All.Count, Is.EqualTo(count));
                Assert.That(
                    w.Buildings.All.Where(b => b.Kind != BuildingKind.House).Select(b => b.Kind),
                    Is.EqualTo(new[] { BuildingKind.Woodshed, BuildingKind.Stoneyard }),
                    "the full woodpile's Woodshed (#98) and the full stone store's Stoneyard (#178), before the houses after the first");
            });
        }

        [Test]
        public void No_room_no_building_and_no_settling()
        {
            // Water every third column, scrub between, but the camps: no
            // footprint fits anywhere, and the bushes are still walked to.
            // Water rather than rocks, which since #155 are ground to clear.
            var w = new BuildingsWorld(size: 100, paint: grid =>
            {
                for (var y = 0; y < grid.Height; y++)
                {
                    for (var x = 0; x < grid.Width; x++)
                    {
                        grid.Set(new WorldPosition(x, y), x % 3 == 0 ? TerrainKind.DeepWater : TerrainKind.Scrub);
                    }
                }

                grid.Set(BuildingsWorld.Centre, TerrainKind.Plains);
                grid.Set(SettleAt, TerrainKind.Plains);
            });
            w.Wood(100);
            var wood = w.Stores.Available(ResourceKind.Wood);

            w.Dawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.All, Is.Empty);
                Assert.That(w.Stores.Available(ResourceKind.Wood), Is.EqualTo(wood), "nothing embodied");
                Assert.That(w.Buildings.ForageInReach(BuildingsWorld.Centre, w.Settlement.Id), Is.GreaterThan(Buildings.YearlyNeed(1)), "bushes aplenty");
                Assert.That(w.Buildings.ForageInReach(SettleAt, w.Settlement.Id), Is.GreaterThan(Buildings.YearlyNeed(1)), "bushes aplenty there too");
                Assert.That(w.Buildings.CanSettle(SettleAt, w.Settlement.Id, 1), Is.False, "but no ground for a barn");
            });
        }

        [Test]
        public void No_building_under_way_is_no_building_work()
        {
            var w = new BuildingsWorld();

            Assert.That(w.Buildings.TryBuildWork(w.Settlement.Id, out var project, out _, out _), Is.False);
            Assert.That(project, Is.Null);
        }

        [Test]
        public void The_last_share_is_what_is_left()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var allButAnHour = house.LabourTicks - Hour;

            w.Buildings.Claim(house, JobKind.Builder, allButAnHour);

            Assert.That(w.Buildings.ShareOf(house, JobKind.Builder), Is.EqualTo(Hour));
        }

        [Test]
        public void Claims_and_credits_refuse_work_that_is_not_there()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            var house = w.BuildNext();
            w.BuildNext();
            w.Dawn();
            var field = w.Buildings.All.Last();
            var b = w.Buildings;

            Assert.Multiple(() =>
            {
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.IsComplete, Is.False);

                // The wrong job for the building, or no building job at all.
                Assert.That(() => b.Claim(house, JobKind.Builder, Hour), Throws.ArgumentException, "a finished house");
                Assert.That(() => b.Claim(house, JobKind.Farmer, Hour), Throws.ArgumentException, "a house is no field");
                Assert.That(() => b.Claim(field, JobKind.Farmer, Hour), Throws.ArgumentException, "a field still going up");
                Assert.That(() => b.Claim(field, JobKind.Forager, Hour), Throws.ArgumentException);
                Assert.That(() => b.Claim(field, (JobKind)99, Hour), Throws.ArgumentException);
                Assert.That(() => b.ShareOf(house, JobKind.Builder), Throws.ArgumentException);
                Assert.That(() => b.Claim(null!, JobKind.Builder, Hour), Throws.ArgumentNullException);

                // Out of range.
                Assert.That(() => b.Claim(field, JobKind.Builder, 0L), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => b.Claim(field, JobKind.Builder, field.LabourTicks + 1L), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => b.Credit(field.Anchor, JobKind.Builder, Hour, w.Stores), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "nothing claimed");
                Assert.That(() => b.Unclaim(field.Anchor, JobKind.Builder, Hour), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "nothing claimed");

                // Nowhere.
                Assert.That(() => b.Credit(new WorldPosition(0, 0), JobKind.Builder, Hour, w.Stores), Throws.InvalidOperationException);
                Assert.That(() => b.Unclaim(new WorldPosition(0, 0), JobKind.Builder, Hour), Throws.InvalidOperationException);
                Assert.That(() => b.Credit(field.Anchor, JobKind.Builder, Hour, null!), Throws.ArgumentNullException);
            });

            // A refused write changed nothing.
            Assert.That((field.Claimed, field.Worked), Is.EqualTo((0L, 0L)));

            b.Claim(field, JobKind.Builder, Hour);
            Assert.That(() => b.Credit(field.Anchor, JobKind.Builder, Hour + 1L, w.Stores), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "more than was claimed");
            Assert.That((field.Claimed, field.Worked), Is.EqualTo((Hour, 0L)));
        }

        [Test]
        public void A_farmer_who_never_came_home_gives_back_today_s_claim_but_not_yesterday_s()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            var field = w.BuildNext();

            w.Buildings.Claim(field, JobKind.Farmer, Buildings.ShareTicks);
            w.Buildings.Unclaim(field.Anchor, JobKind.Farmer, Buildings.ShareTicks);
            Assert.That(field.ClaimedToday, Is.Zero);

            w.Buildings.Claim(field, JobKind.Farmer, Buildings.ShareTicks);
            w.World.Clock.AdvanceTo(w.World.Now.Plus(SimulationTime.TicksPerDay), w.World.Router);
            w.Buildings.Unclaim(field.Anchor, JobKind.Farmer, Buildings.ShareTicks);

            Assert.Multiple(() =>
            {
                Assert.That(field.ClaimedToday, Is.EqualTo(Buildings.ShareTicks), "yesterday's count, left as it was");
            });

            // Asking about today turns the count over.
            w.Buildings.ShareOf(field, JobKind.Farmer);

            Assert.Multiple(() =>
            {
                Assert.That(field.CountsDay, Is.EqualTo(w.World.Now.DayNumber));
                Assert.That(field.ClaimedToday, Is.Zero, "today starts afresh");
            });
        }

        [Test]
        public void Grain_in_store_is_food()
        {
            var w = new BuildingsWorld();
            var before = w.World.Hunger.DaysOfFood(w.Settlement);
            w.Stores.Gather(ResourceKind.Grain, w.Living);

            Assert.That(w.World.Hunger.DaysOfFood(w.Settlement), Is.EqualTo(before + PrimitiveTier.MealsPerGrain));
        }

        [Test]
        public void Days_of_food_saturate_rather_than_wrap()
        {
            // The #148 review: Grain counts five meals, so a full ledger of it
            // is more days than an int holds, and the cast wrapped negative.
            var w = new BuildingsWorld(people: 3);
            w.Stores.Gather(ResourceKind.Grain, int.MaxValue - w.Stores.Available(ResourceKind.Grain));

            Assert.That(w.World.Hunger.DaysOfFood(w.Settlement), Is.EqualTo(int.MaxValue));
        }

        // Nobody has any skill at all.
        private sealed class NoSkills : ISkillSource
        {
            public SkillTier BestIn(ICommunity community, Capability capability) => SkillTier.None;
        }

        // Rocks on every cell within `reach` of the barn that is not a
        // building, a road or the camp: walkable, but ground nothing is
        // built on (#163).
        // Water, not rocks: since #155 rocks are ground a building clears.
        private static void BoxIn(BuildingsWorld w, Building barn, int reach)
        {
            var grid = w.World.Grid;

            for (var y = barn.Anchor.Y - reach; y <= barn.Anchor.Y + reach + barn.Height; y++)
            {
                for (var x = barn.Anchor.X - reach; x <= barn.Anchor.X + reach + barn.Width; x++)
                {
                    var at = new WorldPosition(x, y);
                    var nearCamp = System.Math.Abs(x - BuildingsWorld.Centre.X) <= 4 && System.Math.Abs(y - BuildingsWorld.Centre.Y) <= 4;

                    if (grid.Contains(at) && !nearCamp && w.Buildings.At(at) is null && grid.RoadAt(at) == RoadGrade.None)
                    {
                        grid.Set(at, TerrainKind.DeepWater);
                    }
                }
            }
        }
    }
}
