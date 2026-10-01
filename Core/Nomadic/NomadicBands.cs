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
    /// Foraging is <see cref="Jobs"/>'s. Seasons exist (#53), but feeding
    /// them to the council is #96, so the trigger here reads size and land
    /// only.
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
    /// **Where to go.** The candidates are a lattice of
    /// <see cref="CandidatesPerSide"/> by <see cref="CandidatesPerSide"/>
    /// cells spread evenly over the part of the box <see cref="HopRadius"/>
    /// around the camp that lies on the map, each on the map, standable, and
    /// reachable by a route that fits between dawn and dusk. A lattice rather
    /// than every cell because every cell is too many: a hop of a quarter
    /// day's walk is a box over 100 cells across, and each candidate costs a
    /// site search per job (#123). A box no wider than the lattice is every
    /// cell of it, as it always was. Each is ranked by the walk from it to
    /// food, then by how many of the three jobs would find a site from it,
    /// then by the walks to work altogether (#137): on a map of patches the
    /// walk is what a forager's day is spent on. Among equals keyed draws
    /// (<see cref="RandomDomain.Wandering"/>) decide, so the band does not
    /// always walk the same way. Only candidates in the running are routed,
    /// best first, until one is reachable in a day (#130). Nothing reachable
    /// means the band stays another day. A band moves after
    /// <see cref="CampDays"/>, or at the next council if its camp has no
    /// food in reach; sites are infinite until #26 makes foraging exhaust
    /// them. The machinery - a
    /// route costed into a travel time, one arrival event - is what #35's
    /// founding parties and M4's armies reuse.
    ///
    /// **A band scores only what it knows** (#81). Section 12's bounded map
    /// knowledge: a site counts toward a cell's score only if the band has
    /// seen it, so a council rates the land it has walked rather than the
    /// world. The band reveals <see cref="RevealRadius"/> around wherever it
    /// stands and around every cell of a hop it walks.
    ///
    /// **Every candidate is one the band has already seen**, so nothing gates
    /// the candidate itself. <see cref="RevealRadius"/> matches
    /// <see cref="HopRadius"/>, which makes the square a band reveals from
    /// where it stands exactly the square it picks its next camp from: an
    /// unknown candidate does not arise. What the fog changes is the
    /// *scoring* - a site counts toward a cell's score only if the band has
    /// seen the site - which is how a band can stand a few cells from good
    /// land and still have no idea it is there.
    ///
    /// Should <see cref="RevealRadius"/> ever drop below
    /// <see cref="HopRadius"/> - #85, something that goes looking on purpose,
    /// is what would buy that - unknown candidates become reachable, and one
    /// scored by known sites near it would beat its unseen neighbours on
    /// knowledge the band does not have. The test that pins this invariant is
    /// what will say so.
    ///
    /// <see cref="Jobs"/> works only sites the community has seen (#84), and
    /// with <see cref="RevealRadius"/> past every work trip, a band's sight
    /// is all the map a settlement it founds will have until #85 (#123).
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

        /// <summary>
        /// How far, in cells, a band looks for its next camp: about a quarter
        /// of a day's walk at <see cref="Jobs.TicksPerCostUnit"/>, a straight
        /// plains hop to the edge taking three hours (#123, #130).
        /// </summary>
        public const int HopRadius = 54;

        /// <summary>
        /// Candidates along each side of the hop box: twelve even gaps, so a
        /// box of <see cref="HopRadius"/> 54 puts one every nine cells.
        /// </summary>
        public const int CandidatesPerSide = 13;

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
        public const int RevealRadius = HopRadius;

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

        // A council's candidate camps, their scores, and the keyed order they
        // are tried in, sized for the whole lattice so choosing a camp never
        // allocates.
        private readonly List<WorldPosition> _candidates = new List<WorldPosition>(CandidatesPerSide * CandidatesPerSide);
        private readonly int[] _candidateScores = new int[CandidatesPerSide * CandidatesPerSide];
        private readonly bool[] _candidateViable = new bool[CandidatesPerSide * CandidatesPerSide];
        private readonly long[] _candidateFoodCosts = new long[CandidatesPerSide * CandidatesPerSide];
        private readonly long[] _candidateCosts = new long[CandidatesPerSide * CandidatesPerSide];
        private readonly List<int> _order = new List<int>(CandidatesPerSide * CandidatesPerSide);
        private readonly List<int> _ranked = new List<int>(CandidatesPerSide * CandidatesPerSide);

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
        /// Fills <paramref name="into"/> with every wandering band, in the order they were
        /// tracked. Clears the list first.
        /// </summary>
        /// <remarks>
        /// For the validator (issue 13), which cannot otherwise tell that a
        /// tracked community still exists, or that the people it holds are
        /// alive. The list is the caller's so a check taken once per
        /// simulated day reuses one buffer.
        ///
        /// Read-only in the list sense only: the entries are the live
        /// communities, and <see cref="ICommunity"/> can add and remove
        /// members. Same as <see cref="Lifecycle.Households.All"/>. It is
        /// handed out for reading, and writing through it is a caller bug
        /// rather than something this can prevent.
        /// </remarks>
        public void CopyTrackedTo(List<ICommunity> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            for (var i = 0; i < _tracked.Count; i++)
            {
                into.Add(_tracked[i].Band);
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every council or arrival this system has
        /// booked and not yet seen come due (#80). Clears the list first.
        /// </summary>
        /// <remarks>
        /// The validator confirms each one is still in the queue, and the
        /// world hash folds them in: a world that agrees on its people and
        /// disagrees on what it has booked for them has already diverged, it
        /// has just not shown yet.
        /// </remarks>
        public void CopyBookingsTo(List<PendingBooking> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            for (var i = 0; i < _tracked.Count; i++)
            {
                var council = _tracked[i].PendingCouncil;
                var arrival = _tracked[i].PendingArrival;

                if (!council.IsNone)
                {
                    into.Add(new PendingBooking(
                        _tracked[i].Band.Id, ScheduledEventKind.CouncilDue, council));
                }

                // A band on the move has both: the council that sent it and
                // the arrival that ends the journey.
                if (!arrival.IsNone)
                {
                    into.Add(new PendingBooking(
                        _tracked[i].Band.Id, ScheduledEventKind.BandArrival, arrival));
                }
            }
        }

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

        /// <summary>Days since this band last looked for a new camp, or last made one.</summary>
        public int DaysSinceLook(MobileGroup band) => TrackedFor(band).DaysSinceLook;

        /// <summary>
        /// The cell the council sent this band to, or null when no arrival is
        /// booked. The arrival is refused unless the band's destination still
        /// names it.
        /// </summary>
        public WorldPosition? BookedArrival(MobileGroup band)
        {
            var tracked = TrackedFor(band);
            return tracked.PendingArrival.IsNone ? (WorldPosition?)null : tracked.Booked;
        }

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

            // A camp with no food in reach is left at the next council rather
            // than after CampDays (#137): on a map of patches a band can pitch
            // where nothing grows, and waiting there eats its stores.
            if (!hasDusk || (tracked.DaysSinceLook < CampDays && HasFoodInReach(band)))
            {
                return;
            }

            // The look is the expensive part of a council - a route and
            // three site searches per candidate - so a camp that has nowhere
            // to go looks again in another CampDays, not tomorrow, unless it
            // has no food.
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
            // DETOUR: what a straight hop sees along the way lies inside the
            // squares revealed from the two camps, so the Reveal calls either
            // side would cover it. A band walking around a river sees, from
            // the far end of the detour, cells outside both, and those are
            // known only because the route was read. The route stays inside
            // the hop box, as the council's did, so this is that same route.
            // The council costed a route to this very cell when it booked the
            // arrival, so one exists unless the ground changed underneath it.
            // Nothing changes terrain mid-run today - bridges (#35) will be the
            // first - and a silent skip here would leave the band's map quietly
            // missing the walk instead of saying so.
            if (!_pathfinder.TryFindRoute(band.Position, destination, Jobs.Mover, HopRadius, _scratchRoute, out _))
            {
                throw new InvalidOperationException(
                    band.Id + " has no route from " + band.Position + " to " + destination
                    + ", which its council costed one to; the ground changed under a booked arrival.");
            }

            _knownMaps.RevealAlong(band.Id, _scratchRoute, RevealRadius);
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

        // The best-scoring reachable cell in the hop box, equals taken in a
        // keyed order. False when nothing but the camp itself is reachable.
        //
        // Candidates are tried in an order drawn from the key, so each
        // reachable one is equally likely among those it ties with. Scoring is
        // three short site searches and a route a search across the box, so a
        // council scores until it meets a perfect, reachable candidate, and
        // routes only candidates that could win (#130).
        private bool TryChooseCamp(Tracked tracked, out WorldPosition chosen, out long travelTicks)
        {
            var band = tracked.Band;
            var from = band.Position;
            var key = _rng.Key(RandomDomain.Wandering, RandomSite.CampChoice).Mix(band.Id).Mix(_clock.Now.DayNumber);

            // Fetched once for the whole scan rather than per cell: the look
            // scores every candidate in the hop box, each with a site search
            // per job, so a lookup inside Score would be the hot path.
            var known = _knownMaps.For(band.Id);

            chosen = from;
            travelTicks = 0L;

            // The lattice spans the part of the box on the map, so a camp by
            // the edge still spreads its candidates over the land it has.
            var minX = Math.Max(0, from.X - HopRadius);
            var maxX = Math.Min(_grid.Width - 1, from.X + HopRadius);
            var minY = Math.Max(0, from.Y - HopRadius);
            var maxY = Math.Min(_grid.Height - 1, from.Y + HopRadius);
            var previousY = -1;
            _candidates.Clear();

            for (var row = 0; row < CandidatesPerSide; row++)
            {
                var y = LatticeLine(minY, maxY, row);

                // A span narrower than the lattice lands two lines on one
                // cell; each cell is scored once, as when every cell was.
                if (y == previousY)
                {
                    continue;
                }

                previousY = y;
                var previousX = -1;

                for (var column = 0; column < CandidatesPerSide; column++)
                {
                    var x = LatticeLine(minX, maxX, column);

                    if (x == previousX)
                    {
                        continue;
                    }

                    previousX = x;
                    var candidate = new WorldPosition(x, y);

                    if (!candidate.Equals(from) && _pathfinder.IsPassable(candidate, Jobs.Mover))
                    {
                        _candidates.Add(candidate);
                    }
                }
            }

            // One keyed order for the whole council: each candidate is
            // equally likely to come first among any it ties with.
            _order.Clear();
            for (var i = 0; i < _candidates.Count; i++)
            {
                var swap = (int)key.Mix(i).Below((ulong)(i + 1));
                _order.Add(i);
                _order[i] = _order[swap];
                _order[swap] = i;
            }

            // Every candidate is scored, then ranked (RanksBefore): food and
            // wood both in reach first, then the shortest walk to food, then
            // more jobs with a site, then the shortest walks to work (#137),
            // so a band camps beside its berries, in reach of firewood, rather
            // than merely within reach of them. Equals keep the
            // keyed order. Only the ranking is routed, best first, until one
            // is reachable.
            _ranked.Clear();
            for (var n = 0; n < _order.Count; n++)
            {
                var i = _order[n];
                _candidateScores[i] = Score(known, _candidates[i], out _candidateViable[i], out _candidateFoodCosts[i], out _candidateCosts[i]);

                var at = _ranked.Count;
                while (at > 0 && RanksBefore(i, _ranked[at - 1]))
                {
                    at--;
                }

                _ranked.Insert(at, i);
            }

            for (var n = 0; n < _ranked.Count; n++)
            {
                if (TryWalk(from, _candidates[_ranked[n]], out chosen, out travelTicks))
                {
                    return true;
                }
            }

            chosen = from;
            travelTicks = 0L;
            return false;
        }

        // Whether a candidate is reachable inside the hop box between dawn
        // and dusk, and how long the walk takes.
        private bool TryWalk(WorldPosition from, WorldPosition candidate, out WorldPosition chosen, out long travelTicks)
        {
            chosen = candidate;
            travelTicks = 0L;
            if (!_pathfinder.TryFindRoute(from, candidate, Jobs.Mover, HopRadius, _scratchRoute, out var cost))
            {
                return false;
            }

            travelTicks = cost * Jobs.TicksPerCostUnit;
            return travelTicks <= MaxTravelTicks;
        }

        // The index-th of CandidatesPerSide lines spread evenly from min to
        // max, both ends included; rounded down, so lines only ever repeat,
        // never go backwards.
        private static int LatticeLine(int min, int max, int index) =>
            min + (index * (max - min) / (CandidatesPerSide - 1));

        // Strictly better: food and wood both in reach, the two a band dies
        // without (starving, freezing); then a shorter walk to food; then a
        // higher score; then shorter walks to work altogether.
        private bool RanksBefore(int candidate, int other)
        {
            if (_candidateViable[candidate] != _candidateViable[other])
            {
                return _candidateViable[candidate];
            }

            if (_candidateFoodCosts[candidate] != _candidateFoodCosts[other])
            {
                return _candidateFoodCosts[candidate] < _candidateFoodCosts[other];
            }

            if (_candidateScores[candidate] != _candidateScores[other])
            {
                return _candidateScores[candidate] > _candidateScores[other];
            }

            return _candidateCosts[candidate] < _candidateCosts[other];
        }

        private int Score(ReadOnlySpan<bool> known, WorldPosition at, out bool viable) =>
            Score(known, at, out viable, out _, out _);

        private bool HasFoodInReach(MobileGroup band) =>
            _pathfinder.TryFindNearest(
                band.Position, Jobs.Mover, JobTable.Terrain(JobKind.Forager), _knownMaps.For(band.Id), Jobs.MaxSiteRadius, _scratchRoute, out _);

        // How many jobs would find a site from here; the path cost out to the
        // food site (long.MaxValue with none) and summed over every site
        // found; and whether the two that make a camp settle-able - food and
        // wood - both would.
        private int Score(ReadOnlySpan<bool> known, WorldPosition at, out bool viable, out long foodCost, out long siteCost)
        {
            var score = 0;
            var food = false;
            var wood = false;
            foodCost = long.MaxValue;
            siteCost = 0L;

            for (var i = 0; i < Jobs.Priority.Count; i++)
            {
                var job = Jobs.Priority[i];

                if (!_pathfinder.TryFindNearest(at, Jobs.Mover, JobTable.Terrain(job), known, Jobs.MaxSiteRadius, _scratchRoute, out var cost))
                {
                    continue;
                }

                score++;
                siteCost += cost;
                foodCost = job == JobKind.Forager ? cost : foodCost;
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
