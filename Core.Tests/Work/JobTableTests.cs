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
