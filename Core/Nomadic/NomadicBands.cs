using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Settlements;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.Nomadic
{
    /// <summary>
    /// Bands wander, and one day stop. Owns
    /// <see cref="ScheduledEventKind.CouncilDue"/> and
    /// <see cref="ScheduledEventKind.BandArrival"/>: at first light each
    /// tracked band's council sits and decides to settle where it stands,
    /// break camp and walk to better land, or stay; a band that walks
    /// arrives the same day and makes camp.
    /// </summary>
    /// <remarks>
    /// Section 15's nomadic mode: movement, temporary camps, and a settling
    /// trigger driven by band size, land quality and seasonal pressure.
    /// Foraging is <see cref="Jobs"/>'s. Seasons are #53's, so the trigger
    /// here reads size and land only.
    ///
    /// **The council sits at <see cref="FirstLight"/>, an hour before the
    /// work pass.** Nobody is out - every task ends by dusk and none starts
    /// before dawn - so a band can leave, or settle, without stranding a
    /// worker's return leg at a camp that no longer exists. A band that
    /// decides to move sets its destination there and then, and
    /// <see cref="Jobs"/> reads that at dawn and starts nobody: everyone
    /// walks with the band, through daylight, and the next dawn finds sites
    /// from the new camp. The hop is bounded so that it always fits inside
    /// the day.
    ///
    /// **Settling is pressure plus land.** Pressure is member-days: each
    /// council adds the living headcount, so a band of sixty reaches
    /// <see cref="SettlingPressure"/> twice as fast as one of thirty -
    /// section 15's asymmetry, where the largest band settles first. The
    /// land check is whether a forager and a woodcutter would find a site
    /// from here (<see cref="Jobs"/>'s own search, so a camp that passes has
    /// sites the next dawn). A band under pressure at a camp that fails
    /// keeps moving, and moves toward land that passes.
    ///
    /// **Where to go.** Every cell in a box <see cref="HopRadius"/> around
    /// the camp is a candidate: on the map, standable, reachable by a route
    /// that fits between dawn and dusk. Each is scored by how many of the
    /// three jobs would find a site from it; the best score wins, and among
    /// equals one keyed draw (<see cref="RandomDomain.Wandering"/>) decides,
    /// so the band does not always walk the same way. Nothing reachable
    /// means the band stays another day. This is the placeholder cause of
    /// movement: sites are infinite and identical until #26 makes foraging
    /// exhaust them, and a band today moves because nomads move
    /// (<see cref="CampDays"/>), not because it must. The machinery - a
    /// route costed into a travel time, one arrival event - is what #35's
    /// founding parties and M4's armies reuse.
    ///
    /// **A band sees its whole hop box, for now.** Section 12's bounded map
    /// knowledge says a community picks places among the cells it knows;
    /// that map is #81's, and until it exists the council reads the grid
    /// directly, as <see cref="Jobs"/> finds the nearest forest without
    /// anyone looking. #81 replaces the candidate scan and the land check
    /// with reads of the band's known cells; the council, the scoring and
    /// the arrival stay.
    ///
    /// **Camps embody wood.** Making camp takes up to <see cref="CampWood"/>
    /// from the band's stock and embodies it - section 9's temporary camp,
    /// the primitive tier's one consumer of wood. A band with none makes
    /// camp anyway; wood makes it a better camp in no way the sim can see
    /// yet.
    ///
    /// **The state names its events.** A band records the council and the
    /// arrival it booked, and only those run; any other of either kind
    /// throws, the rule <see cref="Jobs"/> and <see cref="Needs.Hunger"/>
    /// apply. Settling hands the band to <see cref="Founding"/>, which
    /// takes it off every other tracker; this class takes it off its own
    /// and books nothing more for it.
    ///
    /// The numbers are placeholders: plausible, not tuned. The chronicle
    /// (#17) is where they get tuned.
    ///
    /// Allocation-free once tracked: the council scans by index and reuses
    /// one route buffer; the site searches are the pathfinder's, which
    /// reuses its own. Settling allocates, by design - a settlement is a
    /// new entity.
    /// </remarks>
    public sealed class NomadicBands : IScheduledEventHandler
    {
        /// <summary>Tick of day the council sits: an hour before <see cref="Jobs.Dawn"/>.</summary>
        public const long FirstLight = 5L * SimulationTime.TicksPerHour;

        /// <summary>Days a band stays at one camp before it looks for the next.</summary>
        public const int CampDays = 20;

        /// <summary>How far, in cells, a band looks for its next camp.</summary>
        public const int HopRadius = 6;

        /// <summary>Wood a camp embodies, if the band has it.</summary>
        public const int CampWood = 5;

        /// <summary>
        /// Member-days of pressure at which a band settles, given land: a
        /// band of sixty in about three years, one of thirty in about six.
        /// </summary>
        public const long SettlingPressure = 60L * SimulationTime.DaysPerYear * 3L;

        /// <summary>A move must fit between dawn and dusk.</summary>
        public const long MaxTravelTicks = Jobs.Dusk - Jobs.Dawn;

        /// <summary>Both kinds are physical: a camp moves and stock is embodied.</summary>
        public const SimulationPhase Phase = SimulationPhase.Physical;

        private readonly DomainEventBus _bus;
        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly Pathfinder _pathfinder;
        private readonly TerrainGrid _grid;
        private readonly Founding _founding;
        private readonly DeterministicRng _rng;

        // A list, scanned by id, for the reason Hunger's is.
        private readonly List<Tracked> _tracked = new List<Tracked>();

        // Scratch for a route or site search. Sized to the map up front: a
        // route never visits a cell twice, so it can never be longer than
        // the map has cells, and a list that grew to fit a long route would
        // allocate inside the tick loop.
        private readonly List<WorldPosition> _scratchRoute;

        public NomadicBands(
            DomainEventBus bus,
            PersonStore people,
            Pathfinder pathfinder,
            Founding founding,
            DeterministicRng rng)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _founding = founding ?? throw new ArgumentNullException(nameof(founding));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _clock = bus.Clock;
            _grid = pathfinder.Grid;
            _scratchRoute = new List<WorldPosition>(_grid.CellCount);
        }

        /// <summary>How many bands are wandering.</summary>
        public int TrackedCount => _tracked.Count;

        /// <summary>
        /// A band makes its first camp where it stands and starts
        /// wandering: wood is embodied, <see cref="DomainEventKind.CampPitched"/>
        /// announced, and its first council booked for the next
        /// <see cref="FirstLight"/>. Refuses a band already tracked, one
        /// that is not a <see cref="MobileGroupPurpose.NomadicBand"/>, or
        /// one standing off the map.
        /// </summary>
        public void Track(MobileGroup band)
        {
            if (band is null)
            {
                throw new ArgumentNullException(nameof(band));
            }

            if (band.Purpose != MobileGroupPurpose.NomadicBand)
            {
                throw new ArgumentException(
                    band.Id + " is a " + band.Purpose + "; only a NomadicBand wanders.", nameof(band));
            }

            if (IndexOf(band.Id) >= 0)
            {
                throw new InvalidOperationException(band.Id + " is already tracked; two councils would decide twice.");
            }

            // A move is decided at a council and booked as an arrival here;
            // a band already on the road decided it somewhere else, and
            // there is no arrival to book for it.
            if (band.Destination is object)
            {
                throw new InvalidOperationException(
                    band.Id + " is on its way to " + band.Destination + "; a band is tracked at rest.");
            }

            // The grid's own off-map contract, up front, as Jobs does.
            _grid.IndexOf(band.Position);

            // Booked before recorded, as Hunger does.
            var council = _clock.Schedule(
                _clock.Now.Plus(TicksUntil(FirstLight, _clock.Now)), Phase, ScheduledEventKind.CouncilDue, band.Id, EntityId.None);
            var tracked = new Tracked(band) { PendingCouncil = council };
            _tracked.Add(tracked);
            PitchCamp(tracked);
        }

        /// <summary>
        /// Stops a band wandering: its pending council or arrival is
        /// cancelled. Throws when it was never tracked.
        /// </summary>
        public void Untrack(MobileGroup band)
        {
            if (band is null)
            {
                throw new ArgumentNullException(nameof(band));
            }

            var index = IndexOf(band.Id);

            if (index < 0)
            {
                throw new InvalidOperationException(band.Id + " is not tracked by NomadicBands.");
            }

            Drop(index);
        }

        /// <summary>Member-days this band has accrued toward settling.</summary>
        public long PressureOf(MobileGroup band) => TrackedFor(band).Pressure;

        /// <summary>Days since this band last made camp.</summary>
        public int DaysAtCamp(MobileGroup band) => TrackedFor(band).DaysAtCamp;

        /// <summary>
        /// How many of the three jobs would find a site from a position:
        /// the land score a council uses. Throws for a position off the map.
        /// </summary>
        public int LandScore(WorldPosition at) => Score(at, out _);

        /// <summary>
        /// Whether a band could settle at a position: a forager and a
        /// woodcutter would both find a site from it.
        /// </summary>
        public bool CanSettleAt(WorldPosition at)
        {
            Score(at, out var viable);
            return viable;
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            if (!ReferenceEquals(clock, _clock))
            {
                throw new InvalidOperationException(
                    "NomadicBands schedules on its bus's clock, but was dispatched by another.");
            }

            switch (scheduled.Kind)
            {
                case ScheduledEventKind.CouncilDue:
                    Council(scheduled);
                    break;
                case ScheduledEventKind.BandArrival:
                    Arrive(scheduled);
                    break;
                default:
                    throw new InvalidOperationException(
                        "NomadicBands owns " + ScheduledEventKind.CouncilDue + " and "
                        + ScheduledEventKind.BandArrival + ", but was handed " + scheduled + ".");
            }
        }

        private void Council(ScheduledEvent due)
        {
            var index = RequireTracked(due);
            var tracked = _tracked[index];

            if (due.Id != tracked.PendingCouncil)
            {
                throw new InvalidOperationException(
                    due + " came due for " + due.PrimaryEntity + ", whose next council is " + tracked.PendingCouncil + ".");
            }

            tracked.PendingCouncil = EventId.None;
            var band = tracked.Band;

            // A council never sits on the road: an arrival is booked for the
            // same day as the departure, and a council for the next.
            if (band.Destination is object)
            {
                throw new InvalidOperationException(
                    band.Id + "'s council sat while it was on its way to " + band.Destination + ".");
            }

            tracked.Pressure += Living(band);
            tracked.DaysAtCamp++;

            if (tracked.Pressure >= SettlingPressure && CanSettleAt(band.Position))
            {
                // Founding takes the band off every other tracker, and
                // refuses before touching anything if it cannot; this is
                // its own, dropped once the handover has happened. No
                // council is booked at this point - the one that sat is
                // spent and the next is booked below - so a refusal leaves
                // the band tracked with nothing pending, and the throw
                // stops the run where the wiring bug is.
                _founding.Found(band, new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable));
                Drop(index);
                return;
            }

            // A move needs a whole day to walk in. On the world's last day
            // there is no dusk to arrive by, so the band stays - and the
            // arrival it would have booked is one the clock could not hold.
            var hasDusk = Jobs.Dusk - FirstLight <= long.MaxValue - _clock.Now.Ticks;

            if (tracked.DaysAtCamp >= CampDays && hasDusk && TryChooseCamp(tracked, out var next, out var travel))
            {
                band.Destination = next;
                var departure = _clock.Now.Plus(Jobs.Dawn - FirstLight);
                tracked.PendingArrival = _clock.Schedule(
                    departure.Plus(travel), Phase, ScheduledEventKind.BandArrival, band.Id, EntityId.None);
            }

            // The stream ends with time itself, as Hunger's does.
            var now = _clock.Now;
            var untilNext = TicksUntil(FirstLight, now);

            if (untilNext <= long.MaxValue - now.Ticks)
            {
                tracked.PendingCouncil = _clock.Schedule(
                    now.Plus(untilNext), Phase, ScheduledEventKind.CouncilDue, band.Id, EntityId.None);
            }
        }

        private void Arrive(ScheduledEvent arrival)
        {
            var tracked = _tracked[RequireTracked(arrival)];

            if (arrival.Id != tracked.PendingArrival)
            {
                throw new InvalidOperationException(
                    arrival + " came due for " + arrival.PrimaryEntity + ", whose arrival is " + tracked.PendingArrival + ".");
            }

            tracked.PendingArrival = EventId.None;
            var band = tracked.Band;

            // Destination is the band's own field, so a caller could have
            // cleared it under a booked arrival; that is the wiring bug this
            // catches.
            if (!(band.Destination is WorldPosition destination))
            {
                throw new InvalidOperationException(band.Id + " arrived with nowhere it was going.");
            }

            band.Position = destination;
            band.Destination = null;

            var members = band.Members;

            // Membership lags death until the cascade strikes the dead from
            // the band; the living walk, the dead have no position to set.
            for (var i = 0; i < members.Count; i++)
            {
                if (_people.IsAlive(members[i]))
                {
                    _people.SetPosition(members[i], destination);
                }
            }

            PitchCamp(tracked);
        }

        // Wood into the camp, the day count reset, and the camp announced.
        private void PitchCamp(Tracked tracked)
        {
            var band = tracked.Band;
            var wood = Math.Min(band.SharedSupplies.Available(ResourceKind.Wood), CampWood);

            if (wood > 0)
            {
                band.SharedSupplies.Embody(ResourceKind.Wood, wood);
            }

            tracked.DaysAtCamp = 0;
            _bus.Publish(DomainEventKind.CampPitched, band.Id, EntityId.None);
        }

        // The best-scoring reachable cell in the hop box, ties broken by one
        // keyed draw. False when nothing but the camp itself is reachable.
        private bool TryChooseCamp(Tracked tracked, out WorldPosition chosen, out long travelTicks)
        {
            var band = tracked.Band;
            var from = band.Position;
            var key = _rng.Key(RandomDomain.Wandering).Mix(band.Id).Mix(_clock.Now.DayNumber);

            chosen = from;
            travelTicks = 0L;
            var bestScore = -1;
            var ties = 0;

            for (var dy = -HopRadius; dy <= HopRadius; dy++)
            {
                for (var dx = -HopRadius; dx <= HopRadius; dx++)
                {
                    var candidate = new WorldPosition(from.X + dx, from.Y + dy);

                    if ((dx == 0 && dy == 0)
                        || !_grid.Contains(candidate)
                        || !_pathfinder.IsPassable(candidate, Jobs.Mover)
                        || !_pathfinder.TryFindRoute(from, candidate, Jobs.Mover, _scratchRoute, out var cost))
                    {
                        continue;
                    }

                    var travel = cost * Jobs.TicksPerCostUnit;

                    if (travel > MaxTravelTicks)
                    {
                        continue;
                    }

                    var score = Score(candidate, out _);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        ties = 1;
                    }
                    else if (score == bestScore)
                    {
                        // The k-th equal candidate replaces the pick with
                        // chance 1/k, so every tie is equally likely and the
                        // scan stays a single pass.
                        ties++;

                        if (!key.Mix(ties).Chance(1, ties))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    chosen = candidate;
                    travelTicks = travel;
                }
            }

            return bestScore >= 0;
        }

        // How many jobs would find a site from here, and whether the two
        // that make a camp settle-able - food and wood - both would.
        private int Score(WorldPosition at, out bool viable)
        {
            var score = 0;
            var food = false;
            var wood = false;

            for (var i = 0; i < Jobs.Priority.Count; i++)
            {
                var job = Jobs.Priority[i];

                if (!_pathfinder.TryFindNearest(at, Jobs.Mover, JobTable.Terrain(job), Jobs.MaxSiteRadius, _scratchRoute, out _))
                {
                    continue;
                }

                score++;
                food |= job == JobKind.Forager;
                wood |= job == JobKind.Woodcutter;
            }

            viable = food && wood;
            return score;
        }

        private int Living(MobileGroup band)
        {
            var living = 0;
            var members = band.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (_people.IsAlive(members[i]))
                {
                    living++;
                }
            }

            return living;
        }

        // Ticks from now to the next occurrence of a tick of day, never zero.
        private static long TicksUntil(long tickOfDay, SimulationTime now)
        {
            var since = now.TickOfDay - tickOfDay;
            return since < 0L ? -since : SimulationTime.TicksPerDay - since;
        }

        // Off the list, with nothing pending and nothing under way: a band
        // dropped mid-move abandons it and stays at the camp it left from,
        // since a destination with no arrival booked would be a road with no
        // end.
        private void Drop(int index)
        {
            var tracked = _tracked[index];

            if (!tracked.PendingCouncil.IsNone)
            {
                _clock.Cancel(tracked.PendingCouncil);
            }

            if (!tracked.PendingArrival.IsNone)
            {
                _clock.Cancel(tracked.PendingArrival);
                tracked.Band.Destination = null;
            }

            _tracked.RemoveAt(index);
        }

        private int RequireTracked(ScheduledEvent scheduled)
        {
            var index = IndexOf(scheduled.PrimaryEntity);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    scheduled + " came due for a band NomadicBands is not tracking.");
            }

            return index;
        }

        private Tracked TrackedFor(MobileGroup band)
        {
            if (band is null)
            {
                throw new ArgumentNullException(nameof(band));
            }

            var index = IndexOf(band.Id);

            if (index < 0)
            {
                throw new InvalidOperationException(band.Id + " is not tracked by NomadicBands.");
            }

            return _tracked[index];
        }

        private int IndexOf(EntityId band)
        {
            for (var i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].Band.Id == band)
                {
                    return i;
                }
            }

            return -1;
        }

        private sealed class Tracked
        {
            public Tracked(MobileGroup band)
            {
                Band = band;
            }

            public MobileGroup Band { get; }

            public long Pressure { get; set; }

            public int DaysAtCamp { get; set; }

            // The council and arrival this band booked, so that no other
            // runs and Untrack can cancel them. None while the event is
            // being handled, when there is none booked, or when the stream
            // has reached the end of time.
            public EventId PendingCouncil { get; set; }

            public EventId PendingArrival { get; set; }
        }
    }
}
