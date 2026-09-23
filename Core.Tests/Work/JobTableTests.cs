using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Work
{
    [TestFixture]
    public sealed class JobTableTests
    {
        [Test]
        public void Every_job_runs_a_primitive_gathering_recipe()
        {
            Assert.Multiple(() =>
            {
                Assert.That(JobTable.Recipe(JobKind.Forager), Is.SameAs(PrimitiveTier.Forage));
                Assert.That(JobTable.Recipe(JobKind.Woodcutter), Is.SameAs(PrimitiveTier.GatherWood));
                Assert.That(JobTable.Recipe(JobKind.StoneGatherer), Is.SameAs(PrimitiveTier.GatherStone));
            });
        }

        [Test]
        public void Only_foraging_changes_with_the_season()
        {
            Assert.Multiple(() =>
            {
                foreach (Season season in System.Enum.GetValues(typeof(Season)))
                {
                    Assert.That(JobTable.Recipe(JobKind.Forager, season), Is.SameAs(PrimitiveTier.ForageIn(season)), season.ToString());
                    Assert.That(JobTable.Recipe(JobKind.Woodcutter, season), Is.SameAs(PrimitiveTier.GatherWood), season.ToString());
                    Assert.That(JobTable.Recipe(JobKind.StoneGatherer, season), Is.SameAs(PrimitiveTier.GatherStone), season.ToString());
                }

                Assert.That(JobTable.Recipe(JobKind.Forager), Is.SameAs(JobTable.Recipe(JobKind.Forager, Season.Spring)));
                Assert.That(
                    () => JobTable.Recipe(JobKind.Woodcutter, (Season)4), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(
                    () => JobTable.Recipe(JobKind.None, Season.Winter), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void None_and_undefined_kinds_are_not_jobs()
        {
            Assert.Multiple(() =>
            {
                Assert.That(JobTable.IsJob(JobKind.None), Is.False);
                Assert.That(JobTable.IsJob((JobKind)200), Is.False);
                Assert.That(JobTable.IsJob(JobKind.Forager), Is.True);
                Assert.That(() => JobTable.Recipe(JobKind.None), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => JobTable.Recipe((JobKind)200), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => JobTable.WorksOn(JobKind.None, TerrainKind.Plains), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => JobTable.WorksOn((JobKind)200, TerrainKind.Plains), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void None_and_undefined_terrain_are_refused_rather_than_unworkable()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => JobTable.WorksOn(JobKind.Forager, TerrainKind.None), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => JobTable.WorksOn(JobKind.Forager, (TerrainKind)255), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => JobTable.WorksOn(JobKind.StoneGatherer, (TerrainKind)6), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "one past the last");
            });
        }

        [Test]
        public void The_terrain_mask_agrees_with_WorksOn_for_every_defined_kind()
        {
            foreach (var job in new[] { JobKind.Forager, JobKind.Woodcutter, JobKind.StoneGatherer })
            {
                var mask = JobTable.Terrain(job);
                Assert.That(mask.Length, Is.EqualTo((int)TerrainKind.DeepWater + 1), job.ToString());
                Assert.That(mask[(int)TerrainKind.None], Is.False, job + " on None");

                for (var kind = TerrainKind.Plains; kind <= TerrainKind.DeepWater; kind++)
                {
                    Assert.That(mask[(int)kind], Is.EqualTo(JobTable.WorksOn(job, kind)), job + " on " + kind);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(() => JobTable.Terrain(JobKind.None).Length, Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => JobTable.Terrain((JobKind)200).Length, Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [TestCase(JobKind.Forager, TerrainKind.Plains, true)]
        [TestCase(JobKind.Forager, TerrainKind.Forest, true)]
        [TestCase(JobKind.Forager, TerrainKind.Hills, false)]
        [TestCase(JobKind.Forager, TerrainKind.SmallRiver, false)]
        [TestCase(JobKind.Woodcutter, TerrainKind.Forest, true)]
        [TestCase(JobKind.Woodcutter, TerrainKind.Plains, false)]
        [TestCase(JobKind.StoneGatherer, TerrainKind.Hills, true)]
        [TestCase(JobKind.StoneGatherer, TerrainKind.Forest, false)]
        [TestCase(JobKind.StoneGatherer, TerrainKind.DeepWater, false)]
        public void Each_job_works_on_the_terrain_that_has_the_thing(JobKind job, TerrainKind terrain, bool works) =>
            Assert.That(JobTable.WorksOn(job, terrain), Is.EqualTo(works));
    }
}
