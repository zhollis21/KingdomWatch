using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Tests.Construction;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Work
{
    /// <summary>
    /// A settlement's woodcutters look further when nothing stands near
    /// (#149): a village that cleared its first stand had nothing to fell for
    /// the five years its stumps take to stand again.
    /// </summary>
    [TestFixture]
    public sealed class WoodReachTests
    {
        // The settlement by the west edge, and one tree due east of it:
        // further than MaxSiteRadius, within EmergencyWoodRadius.
        private static readonly WorldPosition Camp = new WorldPosition(6, 20);
        private static readonly WorldPosition FarTree = new WorldPosition(Camp.X + Jobs.MaxSiteRadius + 8, Camp.Y);

        [Test]
        public void A_settlement_with_no_tree_in_reach_sends_its_woodcutters_further()
        {
            var w = new BuildingsWorld(paint: grid => grid.Set(FarTree, TerrainKind.Forest), camp: Camp);
            w.World.Jobs.Track(w.Settlement);
            w.World.Jobs.RefreshSites(w.Settlement);

            Assert.Multiple(() =>
            {
                Assert.That(FarTree.X - Camp.X, Is.GreaterThan(Jobs.MaxSiteRadius).And.LessThanOrEqualTo(Jobs.EmergencyWoodRadius));
                Assert.That(w.World.Jobs.HasSite(w.Settlement, JobKind.Woodcutter), Is.True);
                Assert.That(w.World.Jobs.SiteFor(w.Settlement, JobKind.Woodcutter), Is.EqualTo(FarTree));
            });
        }

        [Test]
        public void A_band_with_no_tree_in_reach_does_not_look_further()
        {
            // A band moves on instead, and never pays for the wider search.
            var w = new BuildingsWorld(paint: grid => grid.Set(FarTree, TerrainKind.Forest), camp: Camp);
            var band = w.World.AddBand(4, Camp);
            w.World.Jobs.RefreshSites(band);

            Assert.That(w.World.Jobs.HasSite(band, JobKind.Woodcutter), Is.False);
        }

        [Test]
        public void A_tree_in_reach_is_found_first_however_many_stand_further_out()
        {
            var near = new WorldPosition(Camp.X + 5, Camp.Y);
            var w = new BuildingsWorld(
                paint: grid =>
                {
                    grid.Set(FarTree, TerrainKind.Forest);
                    grid.Set(near, TerrainKind.Forest);
                },
                camp: Camp);
            w.World.Jobs.Track(w.Settlement);
            w.World.Jobs.RefreshSites(w.Settlement);

            Assert.That(w.World.Jobs.SiteFor(w.Settlement, JobKind.Woodcutter), Is.EqualTo(near));
        }
    }
}
