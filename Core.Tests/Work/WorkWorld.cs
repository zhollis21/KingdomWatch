using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Tests.Lifecycle;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.Tests.Work
{
    // A DemographicWorld on a map worth walking across, with Jobs on the
    // router: what a band needs to feed itself by working. Shared by the
    // fixtures that put people to work.
    internal sealed class WorkWorld
    {
        // Sixteen by sixteen plains, with a forest cell and a hills cell a
        // known walk from Camp, and a river down column 12 cutting the far
        // strip off. Cheap to reason about: from Camp, the forest is four
        // straight steps east (three plains entered at 100 ticks, one forest
        // at 200) and the hills four straight steps south.
        internal const int Width = 16;
        internal const int Height = 16;
        internal static readonly WorldPosition Camp = new WorldPosition(2, 2);
        internal static readonly WorldPosition ForestCell = new WorldPosition(6, 2);
        internal static readonly WorldPosition HillsCell = new WorldPosition(2, 6);
        internal const int RiverColumn = 12;
        internal const long TicksToForest = 3L * 100L + 200L;
        internal const long TicksToHills = 3L * 100L + 300L;

        // Home enters three plains and the plains camp cell, whichever the site.
        internal const long TicksBack = 4L * 100L;

        internal WorkWorld()
            : this(1UL, DefaultMap())
        {
        }

        internal WorkWorld(ulong seed, TerrainGrid grid)
        {
            Demographics = new DemographicWorld(DemographicSettings.Default, seed, grid);
            Router.Register(ScheduledEventKind.WorkDayDue, Jobs);
            Router.Register(ScheduledEventKind.TaskCompleted, Jobs);
        }

        internal static TerrainGrid DefaultMap()
        {
            var grid = new TerrainGrid(Width, Height, TerrainKind.Plains);
            grid.Set(ForestCell, TerrainKind.Forest);
            grid.Set(HillsCell, TerrainKind.Hills);

            for (var y = 0; y < Height; y++)
            {
                grid.Set(new WorldPosition(RiverColumn, y), TerrainKind.SmallRiver);
            }

            return grid;
        }

        // Plains only, as far as the eye can see: foraging works underfoot
        // and nothing else works anywhere.
        internal static TerrainGrid PlainsOnly() => new TerrainGrid(Width, Height, TerrainKind.Plains);

        internal DemographicWorld Demographics { get; }

        internal TerrainGrid Grid => Demographics.Base.Grid;

        internal Jobs Jobs => Demographics.Base.Jobs;

        internal SimulationClock Clock => Demographics.Clock;

        internal ScheduledEventRouter Router => Demographics.Router;

        internal PersonStore People => Demographics.People;

        internal Deaths Deaths => Demographics.Deaths;

        internal Hunger Hunger => Demographics.Hunger;

        internal SimulationTime Now => Clock.Now;

        // A band standing at a position, tracked by everything that tracks
        // bands, with the given food and nothing else. Members are added by
        // the caller.
        internal MobileGroup NewBand(WorldPosition position, int food)
        {
            var band = new MobileGroup(
                Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, position);
            Deaths.Track(band);
            Demographics.Fertility.Track(band);
            Hunger.Track(band);
            Jobs.Track(band);

            if (food > 0)
            {
                band.SharedSupplies.Gather(ResourceKind.Food, food);
            }

            return band;
        }

        internal PersonHandle Join(MobileGroup band, long ageYears, Sex sex = Sex.Male)
        {
            var person = Demographics.NewPerson(ageYears, sex);
            People.SetPosition(person, band.Position);
            band.AddMember(person);
            return person;
        }

        internal List<PersonHandle> JoinAdults(MobileGroup band, int count)
        {
            var adults = new List<PersonHandle>(count);

            for (var i = 0; i < count; i++)
            {
                adults.Add(Join(band, 25L + (i % 20), i % 2 == 0 ? Sex.Male : Sex.Female));
            }

            return adults;
        }

        // Enough food that nobody forages for the length of any test here.
        internal static int PlentifulFood(int members) => members * Hunger.DailyRation * (Jobs.FoodTargetDays + 100);

        internal SimulationTime Today(long tickOfDay) => new SimulationTime(Now.Ticks - Now.TickOfDay + tickOfDay);

        internal void AdvanceTo(SimulationTime time) => Clock.AdvanceTo(time, Router);

        internal void Advance(long ticks) => AdvanceTo(Now.Plus(ticks));

        // To the first dawn from now, dispatching it.
        internal void AdvanceToDawn()
        {
            var dawn = Today(Jobs.Dawn);
            AdvanceTo(dawn > Now ? dawn : dawn.Plus(SimulationTime.TicksPerDay));
        }

        internal int Count(DomainEventKind kind) => Demographics.Published(kind).Count;
    }
}
