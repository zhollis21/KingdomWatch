using System.Collections.Generic;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Settlements;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Core.Tests.Construction
{
    // A whole World on a small hand-drawn map, with one band founded as a
    // settlement at tick zero, before its first dawn: what the building
    // tests drive Buildings through directly, without advancing the clock
    // unless they mean to.
    internal sealed class BuildingsWorld : IDomainEventSubscriber
    {
        // Forty by forty plains, the settlement in the middle. Fixtures paint
        // scrub or forest onto it before founding.
        internal const int Size = 40;
        internal static readonly WorldPosition Centre = new WorldPosition(20, 20);

        // `camp` moves the settlement off the middle, for the fixtures that
        // need it by an edge.
        internal BuildingsWorld(int people = 12, System.Action<TerrainGrid>? paint = null, WorldPosition? camp = null)
        {
            var grid = new TerrainGrid(Size, Size, TerrainKind.Plains);
            paint?.Invoke(grid);
            World = new World(1UL, grid, DemographicSettings.Default);
            World.Bus.Subscribe(this);
            World.Nomads.Settles = false;

            var band = World.AddBand(people, camp ?? Centre);
            Settlement = World.Founding.Found(band, new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable));

            // Nobody works on their own: the tests are the only builders and
            // farmers, so a day advanced is a day nothing else was approved.
            World.Jobs.Untrack(Settlement);
        }

        internal World World { get; }

        internal Settlement Settlement { get; }

        internal Buildings Buildings => World.Buildings;

        internal ResourceLedger Stores => Settlement.SharedSupplies;

        internal List<DomainEvent> Heard { get; } = new List<DomainEvent>();

        public void On(in DomainEvent published) => Heard.Add(published);

        internal int Living => Settlement.Members.Count;

        // A dawn with hands to spare.
        internal void Dawn() => Buildings.AtDawn(Settlement, 1, Living);

        // Every hour a building has left, claimed and done in one go.
        internal void Finish(Building building)
        {
            var left = building.LabourTicks - building.Worked - building.Claimed;
            Buildings.Claim(building, JobKind.Builder, left);
            Buildings.Credit(building.Anchor, JobKind.Builder, left, Stores);
        }

        // The building approved last, or null.
        internal Building? Latest => Buildings.All.Count == 0 ? null : Buildings.All[Buildings.All.Count - 1];

        // Approves and finishes the next building the settlement wants.
        internal Building BuildNext()
        {
            var before = Buildings.All.Count;
            Dawn();

            if (Buildings.All.Count == before)
            {
                throw new System.InvalidOperationException("Nothing was approved.");
            }

            var building = Buildings.All[Buildings.All.Count - 1];
            Finish(building);
            return building;
        }

        internal void Wood(int amount) => Stores.Gather(ResourceKind.Wood, amount);
    }
}
