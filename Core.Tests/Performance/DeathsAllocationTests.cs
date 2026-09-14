using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Tests.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Section 18's zero-allocation tick loop, pointed at <see cref="Deaths"/>:
    /// deaths run under AdvanceTo once the mortality model (#11) rolls them,
    /// so the whole cascade - the publish, the partnership ending, witness
    /// pruning, adoption through the genealogy, a dissolution, the band
    /// roster and the storage slot - allocates nothing.
    /// </summary>
    /// <remarks>
    /// Forming a household allocates, like adding a person, and is not
    /// measured. What is: a first death that widows, and a second that
    /// orphans, adopts out, and dissolves. The lists the cascade adds to -
    /// the adoptive household's members and the store's free slots - are
    /// arranged to have capacity, so a growth there would be the scenario's
    /// allocation rather than the cascade's.
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    public sealed class DeathsAllocationTests
    {
        [Test]
        public void A_death_with_a_full_cascade_allocates_nothing()
        {
            var w = new HouseholdWorld();
            var band = w.NewBand();

            // Two families of identical shape: grandparents housed together, a
            // daughter married out with one child. The first family dies in
            // the warm-up so every path is JIT-compiled; the second dies in
            // the measured span.
            var first = NewFamily(w, band);
            var second = NewFamily(w, band);
            var holder = w.IdOf(first.Dad);
            var witness = w.Bus.Publish(DomainEventKind.DivineActWitnessed, holder, EntityId.None);
            w.Memories.Record(
                holder, witness, EntityId.None, 1, new[] { w.IdOf(second.Dad), w.IdOf(second.Mum) }, w.Clock.Now);

            w.Deaths.Die(first.Dad, Reasons.None);
            w.Deaths.Die(first.Mum, new Reasons(ReasonCode.FoodShortage));

            var allocated = Allocations.Measure(() =>
            {
                w.Deaths.Die(second.Dad, Reasons.None);
                w.Deaths.Die(second.Mum, new Reasons(ReasonCode.FoodShortage));
            });

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across two deaths");
                Assert.That(second.Grandparents.Members, Does.Contain(second.Child), "the child was adopted");
                Assert.That(w.Households.TryGet(second.Home.Id, out _), Is.False, "and the household dissolved");
                Assert.That(band.Members, Does.Not.Contain(second.Dad).And.Not.Contain(second.Mum));
                Assert.That(w.Memories.Witnesses(holder, witness).Length, Is.Zero, "both witnesses struck");
            });
        }

        private static Family NewFamily(HouseholdWorld w, MobileGroup band)
        {
            var grandparents = w.NewCouple(out var grandmother, out var grandfather);
            var mum = w.NewPerson(AgeStage.Adult, Sex.Female, grandmother, grandfather);
            var dad = w.NewPerson(AgeStage.Adult, Sex.Male);
            var home = w.Family.Partner(mum, dad, Reasons.None);
            var child = w.NewChildOf(home, mum, dad, AgeStage.Child);

            band.AddMember(grandmother);
            band.AddMember(grandfather);
            band.AddMember(mum);
            band.AddMember(dad);
            band.AddMember(child);

            return new Family(grandparents, home, mum, dad, child);
        }

        private readonly struct Family
        {
            public Family(Household grandparents, Household home, PersonHandle mum, PersonHandle dad, PersonHandle child)
            {
                Grandparents = grandparents;
                Home = home;
                Mum = mum;
                Dad = dad;
                Child = child;
            }

            public Household Grandparents { get; }

            public Household Home { get; }

            public PersonHandle Mum { get; }

            public PersonHandle Dad { get; }

            public PersonHandle Child { get; }
        }
    }
}
