using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Knowledge;
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
    /// **A band scores only what it knows** (#81). Section 12's bounded map
    /// knowledge: a site counts toward a cell's score only if the band has
    /// seen it, so a council rates the land it has walked rather than the
    /// world. The band reveals <see cref="RevealRadius"/> around wherever it
    /// stands and around every cell of a hop it walks.
    ///
    /// The *candidates* are deliberately not restricted. A band may hop onto
    /// ground it knows nothing about - an unknown cell simply scores zero,
    /// ties with every other unknown cell, and the keyed draw picks among
    /// them. That is what keeps a band exploring; restricting candidates to
    /// known cells would trap it inside the disc it started in, because
    /// nothing else in M1 widens a map. <see cref="Jobs"/> still finds work
    /// sites by reading the grid: whether section 12's rule reaches work
    /// sites at all is #84, and binding them naively would stop a settled
    /// community's map ever growing.
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
    /// takes it off every other tracker and announces the settlement; this
    /// class hears that and takes the band off its own list, whoever
    /// called Found, and books nothing more for it.
    ///
    /// The numbers are placeholders: plausible, not tuned. The chronicle
    /// (#17) is where they get tuned.
    ///
    /// Allocation-free once tracked: the council scans by index and reuses
    /// one route buffer; the site searches are the pathfinder's, which
    /// reuses its own. Settling allocates, by design - a settlement is a
    /// new entity.
    /// </remarks>
    public sealed class NomadicBands : IScheduledEventHandler, IDomainEventSubscriber
    {
        /// <summary>Tick of day the council sits: an hour before <see cref="Jobs.Dawn"/>.</summary>
        public const long FirstLight = 5L * SimulationTime.TicksPerHour;

        /// <summary>Days a band stays at one camp before it looks for the next.</summary>
        public const int CampDays = 20;

        /// <summary>How far, in cells, a band looks for its next camp.</summary>
        public const int HopRadius = 6;

        /// <summary>
        /// How far, in cells, a band sees around wherever it stands or walks.
        /// Section 12: reveal is passive, so this is the only thing widening a
        /// band's map.
        /// </summary>
        /// <remarks>
        /// Matched to <see cref="HopRadius"/> so a band always knows the ground
        /// it could hop to next. It cannot go much tighter while reveal stays
        /// passive: a band only settles once it knows a forager's site and a
        /// woodcutter's, each within <see cref="Jobs.MaxSiteRadius"/> of the
        /// camp, so a smaller radius trades directly against whether a band
        /// ever settles at all. #85 - something that goes looking on purpose -
        /// is what would buy a tighter one.
        /// </remarks>
        public const int RevealRadius = 6;

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
        private readonly KnownMaps _knownMaps;

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
            DeterministicRng rng,
            KnownMaps knownMaps)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _founding = founding ?? throw new ArgumentNullException(nameof(founding));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _knownMaps = knownMaps ?? throw new ArgumentNullException(nameof(knownMaps));
            _clock = bus.Clock;
            _grid = pathfinder.Grid;
            _scratchRoute = new List<WorldPosition>(_grid.CellCount);

            // Listen for the handover rather than be told: Founding is
            // public, and whoever calls it, the band it emptied stops
            // wandering. Subscribing here rather than Founding taking this
            // class avoids a construction cycle - the council calls Founding.
            bus.Subscribe(this);
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

            // Off the map is the grid's refusal; a cell the band's feet cannot
            // stand on is this one. Every route and site search rejects such
            // an origin, so a band tracked there could never move or settle
            // and its councils would sit forever with nothing to decide.
            RequireStandable(band.Position);

            // Booked before recorded, as Hunger does.
            var council = _clock.Schedule(
                _clock.Now.Plus(TicksUntil(FirstLight, _clock.Now)), Phase, ScheduledEventKind.CouncilDue, band.Id, EntityId.None);
            var tracked = new Tracked(band) { PendingCouncil = council };
            _tracked.Add(tracked);

            // A band is its own holder until there are polities (#39). Only on
            // the first Track: a band untracked and tracked again keeps what it
            // learned, since it did not stop existing in between.
            if (!_knownMaps.IsTracked(band.Id))
            {
                _knownMaps.Track(band.Id);
            }

            _knownMaps.Reveal(band.Id, band.Position, RevealRadius);
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
        /// How many of the three jobs this band would find a site for from a
        /// position, counting only cells the band knows. Throws for a position
        /// off the map, or a band with no map.
        /// </summary>
        /// <remarks>
        /// Holder-relative rather than a property of the land, because under
        /// section 12 there is no such thing as what a cell is worth to
        /// nobody - two bands standing on the same cell score it differently
        /// when one has walked further.
        /// </remarks>
        public int LandScore(MobileGroup band, WorldPosition at) =>
            Score(_knownMaps.For(BandId(band)), at, out _);

        /// <summary>
        /// Whether this band could settle at a position: it knows a forager's
        /// site and a woodcutter's within reach of it.
        /// </summary>
        public bool CanSettleAt(MobileGroup band, WorldPosition at)
        {
            Score(_knownMaps.For(BandId(band)), at, out var viable);
            return viable;
        }

        private static EntityId BandId(MobileGroup band) =>
            band is null ? throw new ArgumentNullException(nameof(band)) : band.Id;

        /// <summary>
        /// A settlement founded from a tracked band ends its wandering: the
        /// council it had booked is cancelled, and a move under way is
        /// abandoned - though Founding refuses a band on the road, so there
        /// is none. Other events are not this class's business.
        /// </summary>
        public void On(in DomainEvent published)
        {
            if (published.Kind != DomainEventKind.SettlementFounded)
            {
                return;
            }

            var index = IndexOf(published.SecondaryEntity);

            if (index >= 0)
            {
                Drop(index);
            }
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
            tracked.DaysSinceLook++;

            // Tomorrow's council is booked before anything is decided, so
            // that a refused founding leaves a band still wandering rather
            // than one on the list with nothing pending. When the founding
            // goes through, SettlementFounded arrives and On cancels it.
            // The stream ends with time itself, as Hunger's does.
            var now = _clock.Now;
            var untilNext = TicksUntil(FirstLight, now);

            if (untilNext <= long.MaxValue - now.Ticks)
            {
                tracked.PendingCouncil = _clock.Schedule(
                    now.Plus(untilNext), Phase, ScheduledEventKind.CouncilDue, band.Id, EntityId.None);
            }

            // Settling needs a tomorrow for the settlement's streams to book
            // into; on the world's last days the band stays a band.
            if (tracked.Pressure >= SettlingPressure
                && Founding.HasRoomForStreams(now)
                && CanSettleAt(band, band.Position))
            {
                // Founding takes the band off every other tracker and
                // refuses before touching anything if it cannot; this class
                // takes the band off its own list when it hears
                // SettlementFounded (see On), whoever called Found. A refusal
                // is a wiring bug and throws; the band it leaves behind is
                // whole and has its next council.
                _founding.Found(band, new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable));
                return;
            }

            // A move needs a whole day to walk in. On the world's last day
            // there is no dusk to arrive by, so the band stays - and the
            // arrival it would have booked is one the clock could not hold.
            var hasDusk = Jobs.Dusk - FirstLight <= long.MaxValue - now.Ticks;

            if (tracked.DaysSinceLook < CampDays || !hasDusk)
            {
                return;
            }

            // The look is the expensive part of a council - a route and
            // three site searches per candidate - so a camp that has nowhere
            // to go looks again in another CampDays, not tomorrow.
            tracked.DaysSinceLook = 0;

            if (TryChooseCamp(tracked, out var next, out var travel))
            {
                band.Destination = next;
                tracked.Booked = next;
                var departure = now.Plus(Jobs.Dawn - FirstLight);
                tracked.PendingArrival = _clock.Schedule(
                    departure.Plus(travel), Phase, ScheduledEventKind.BandArrival, band.Id, EntityId.None);
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
            // cleared or changed it under a booked arrival. The route and the
            // travel time were costed to the cell the council chose, and a
            // band landing anywhere else walked a road nobody priced: the
            // state names its destination as it names its event.
            if (!(band.Destination is WorldPosition destination) || destination != tracked.Booked)
            {
                throw new InvalidOperationException(
                    band.Id + " arrived at " + (band.Destination?.ToString() ?? "nowhere") + ", but the council sent it to " + tracked.Booked + ".");
            }

            // Revealed here rather than at departure, and recomputed rather
            // than kept: TryChooseCamp pathfinds every candidate into one
            // shared buffer, so once it returns the buffer holds whichever
            // candidate the scan ended on, not the one it picked. The same
            // inputs through the same deterministic search give the route the
            // band actually walked. Arrival rather than departure because a
            // band cannot decide anything while travelling - a council refuses
            // to sit - so the whole path landing at once is indistinguishable
            // from revealing it stride by stride, which is what section 4's
            // LOD equivalence asks for.
            //
            // While RevealRadius equals HopRadius this only earns its keep on a
            // DETOUR: a straight hop never leaves the square already revealed
            // from the old camp, so the two Reveal calls either side would
            // cover it. A band walking around a river does leave it, and those
            // cells are known only because the route was read.
            if (_pathfinder.TryFindRoute(band.Position, destination, Jobs.Mover, _scratchRoute, out _))
            {
                _knownMaps.RevealAlong(band.Id, _scratchRoute, RevealRadius);
            }

            _knownMaps.Reveal(band.Id, destination, RevealRadius);

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
            tracked.DaysSinceLook = 0;
            _bus.Publish(DomainEventKind.CampPitched, band.Id, EntityId.None);
        }

        // The best-scoring reachable cell in the hop box, ties broken by one
        // keyed draw. False when nothing but the camp itself is reachable.
        private bool TryChooseCamp(Tracked tracked, out WorldPosition chosen, out long travelTicks)
        {
            var band = tracked.Band;
            var from = band.Position;
            var key = _rng.Key(RandomDomain.Wandering).Mix(band.Id).Mix(_clock.Now.DayNumber);

            // Fetched once for the whole scan rather than per cell: the look
            // scores every candidate in the hop box, each with a site search
            // per job, so a lookup inside Score would be the hot path.
            var known = _knownMaps.For(band.Id);

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

                    var score = Score(known, candidate, out _);

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
        private int Score(ReadOnlySpan<bool> known, WorldPosition at, out bool viable)
        {
            var score = 0;
            var food = false;
            var wood = false;

            for (var i = 0; i < Jobs.Priority.Count; i++)
            {
                var job = Jobs.Priority[i];

                if (!_pathfinder.TryFindNearest(at, Jobs.Mover, JobTable.Terrain(job), known, Jobs.MaxSiteRadius, _scratchRoute, out _))
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

        private void RequireStandable(WorldPosition at)
        {
            if (!_pathfinder.IsPassable(at, Jobs.Mover))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(at), at, "Nobody can stand on that cell; no route or site would ever be found from it.");
            }
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

            // Days since the last hop scan, or the last camp: the scan runs
            // every CampDays whether or not the last one found anywhere to go.
            public int DaysSinceLook { get; set; }

            // The council and arrival this band booked, so that no other
            // runs and Untrack can cancel them. None while the event is
            // being handled, when there is none booked, or when the stream
            // has reached the end of time.
            public EventId PendingCouncil { get; set; }

            public EventId PendingArrival { get; set; }

            // Where the pending arrival was booked to. Meaningful only while
            // one is pending.
            public WorldPosition Booked { get; set; }
        }
    }
}
