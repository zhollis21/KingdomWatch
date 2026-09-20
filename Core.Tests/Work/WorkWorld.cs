using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Nomadic;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Settlements;
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
            : this(seed, grid, DemographicSettings.Default)
        {
        }

        // The observer is for the watched-versus-unwatched equivalence and
        // collision tests (#57); it reaches the rng through DemographicWorld,
        // and RandomSite.CampChoice is drawn only from here.
        internal WorkWorld(
            ulong seed, TerrainGrid grid, DemographicSettings settings, IRandomDrawObserver? observer = null)
        {
            Demographics = new DemographicWorld(settings, seed, grid, observer);
            Founding = new Founding(
                Demographics.Bus, Deaths, Demographics.Fertility, Hunger, Jobs, Demographics.Matchmaking, KnownMaps);
            Nomads = new NomadicBands(
                Demographics.Bus, People, Demographics.Base.Pathfinder, Founding, Demographics.Rng, KnownMaps);
            Router.Register(ScheduledEventKind.WorkDayDue, Jobs);
            Router.Register(ScheduledEventKind.TaskCompleted, Jobs);
            Router.Register(ScheduledEventKind.CouncilDue, Nomads);
            Router.Register(ScheduledEventKind.BandArrival, Nomads);
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

        // One map store, the one Jobs was built with: a second instance
        // would leave Founding handing over a map nobody works from.
        internal KnownMaps KnownMaps => Demographics.Base.KnownMaps;

        internal Founding Founding { get; }

        internal NomadicBands Nomads { get; }

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
            var band = NewUnmappedBand(position, food);

            // The map, and what the band can see from where it stands: the
            // same pair NomadicBands.Track gives a wandering band. Section 12
            // leaves a band without one with nowhere to work, so every band
            // meant to do any gets it here.
            KnownMaps.Track(band.Id);
            KnownMaps.Reveal(band.Id, position, Jobs.RevealRadius);

            return band;
        }

        // The same band, with no known map - for the fixtures whose subject is
        // a community nobody has given one, which section 12 makes a real
        // state rather than a broken one. It cannot work: its first dawn pass
        // asks KnownMaps for a map and is refused.
        internal MobileGroup NewUnmappedBand(WorldPosition position, int food)
        {
            var band = new MobileGroup(
                Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, position);
            Deaths.Track(band);
            Demographics.Fertility.Track(band);
            Hunger.Track(band);
            Jobs.Track(band);
            Demographics.Matchmaking.Track(band);

            if (food > 0)
            {
                band.SharedSupplies.Gather(ResourceKind.Food, food);
            }

            return band;
        }

        // A tracked band that also wanders: its first camp is pitched here.
        internal MobileGroup NewWanderingBand(WorldPosition position, int food)
        {
            var band = NewBand(position, food);
            Nomads.Track(band);
            return band;
        }

        // An existing band, put on everything that tracks bands - for a band
        // made by hand at a time of the test's choosing.
        internal void NewBandTrackedEverywhere(MobileGroup band)
        {
            Deaths.Track(band);
            Demographics.Fertility.Track(band);
            Hunger.Track(band);
            Jobs.Track(band);
            Demographics.Matchmaking.Track(band);
            Nomads.Track(band);
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

        // To the first council from now, dispatching it.
        internal void AdvanceToFirstLight()
        {
            var council = Today(NomadicBands.FirstLight);
            AdvanceTo(council > Now ? council : council.Plus(SimulationTime.TicksPerDay));
        }

        // To the first dawn from now, dispatching it.
        internal void AdvanceToDawn()
        {
            var dawn = Today(Jobs.Dawn);
            AdvanceTo(dawn > Now ? dawn : dawn.Plus(SimulationTime.TicksPerDay));
        }

        internal int Count(DomainEventKind kind) => Demographics.Published(kind).Count;
    }
}
