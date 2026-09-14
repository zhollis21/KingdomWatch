using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    [TestFixture]
    public sealed class AgeStageTests
    {
        [TestCase(AgeStage.Infant, false, true)]
        [TestCase(AgeStage.Child, false, true)]
        [TestCase(AgeStage.Adolescent, false, true)]
        [TestCase(AgeStage.Adult, true, false)]
        [TestCase(AgeStage.Elder, true, false)]
        public void Every_defined_stage_is_exactly_one_of_adult_or_dependent(AgeStage stage, bool adult, bool dependent)
        {
            Assert.Multiple(() =>
            {
                Assert.That(AgeStages.IsAdult(stage), Is.EqualTo(adult));
                Assert.That(AgeStages.IsDependent(stage), Is.EqualTo(dependent));
            });
        }

        [TestCase(AgeStage.None)]
        [TestCase((AgeStage)6)]
        [TestCase((AgeStage)99)]
        [TestCase((AgeStage)byte.MaxValue)]
        public void An_undefined_stage_is_neither_adult_nor_dependent(AgeStage stage)
        {
            // An enum is a byte with names, and the store's guards are the
            // only thing keeping these out of a record. A value that slips
            // past them must not count as grown by sitting above Elder.
            Assert.Multiple(() =>
            {
                Assert.That(AgeStages.IsAdult(stage), Is.False);
                Assert.That(AgeStages.IsDependent(stage), Is.False);
            });
        }
    }
}
