using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.Settlements
{
    /// <summary>
    /// A band becomes a settlement. Every settlement there is, and the one
    /// place that makes one: the band's people and stock move to a new
    /// <see cref="Settlement"/> where the band stands, every system that
    /// tracked the band tracks the settlement instead, and
    /// <see cref="DomainEventKind.SettlementFounded"/> is announced.
    /// </summary>
    /// <remarks>
    /// The handover is the whole job. <see cref="Deaths"/>,
    /// <see cref="Fertility"/>, <see cref="Hunger"/>, <see cref="Jobs"/> and
    /// <see cref="Matchmaking"/> each see an <see cref="ICommunity"/>, so
    /// founding is an <c>Untrack</c> and a <c>Track</c> on each rather than
    /// any of them learning what a settlement is. Order matters only in that
    /// the settlement is fully populated and stocked before anything is
    /// told about it, so the first meal or dawn it books finds people there.
    ///
    /// **Nothing is lost or doubled.** Members move in band order, which
    /// becomes the settlement's order. Stock moves through
    /// <see cref="ResourceLedger.TransferTo"/>, so both ledgers still audit.
    /// Flows do not move: they are a holder's history, and the band's says
    /// it built camps while the settlement's says it has built nothing yet.
    /// The wood the band embodied in its last camp is in that history and
    /// nowhere else - there is no camp to hand over until #23 and #69 give a
    /// settlement things it can own. Stock that is reserved, carried or in
    /// process has no available pile to move from, so a band with any is
    /// refused. So is a band with
    /// anyone out on a task, by <see cref="Jobs.Untrack"/> - the caller
    /// founds at first light or after dusk, when nobody is, rather than
    /// strand a return leg to a camp that is now a town.
    ///
    /// Section 12's founding - a trigger, a scored site, a party that walks
    /// there - is #35's, and this is the half it will call once the party
    /// arrives. What the band gets as a settlement is exactly what it had as
    /// a band: <see cref="Lifecycle.CampSpace"/> still houses its
    /// households, so nothing but food bounds its growth until #69. A
    /// famine carries over too: a band that settles hungry is a hungry
    /// settlement, and the chronicle closes the famine under the new name
    /// rather than opening a second one.
    ///
    /// Founding allocates - a settlement is a new entity, like a person -
    /// and happens a handful of times a game.
    /// </remarks>
    public sealed class Founding
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(ResourceKind));

        private readonly DomainEventBus _bus;
        private readonly SimulationClock _clock;
        private readonly Deaths _deaths;
        private readonly Fertility _fertility;
        private readonly Hunger _hunger;
        private readonly Jobs _jobs;
        private readonly Matchmaking _matchmaking;
        private readonly KnownMaps _knownMaps;

        private readonly List<Settlement> _settlements = new List<Settlement>();
        private readonly ReadOnlyCollection<Settlement> _settlementsView;

        public Founding(
            DomainEventBus bus,
            Deaths deaths,
            Fertility fertility,
            Hunger hunger,
            Jobs jobs,
            Matchmaking matchmaking,
            KnownMaps knownMaps)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
            _fertility = fertility ?? throw new ArgumentNullException(nameof(fertility));
            _hunger = hunger ?? throw new ArgumentNullException(nameof(hunger));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _matchmaking = matchmaking ?? throw new ArgumentNullException(nameof(matchmaking));
            _knownMaps = knownMaps ?? throw new ArgumentNullException(nameof(knownMaps));
            _clock = bus.Clock;
            _settlementsView = _settlements.AsReadOnly();
        }

        /// <summary>Every settlement, oldest first. The order to iterate in.</summary>
        public IReadOnlyList<Settlement> All => _settlementsView;

        /// <summary>
        /// Settles a band where it stands. Its members and stock become the
        /// new settlement's, every tracker hands over, and the founding is
        /// announced with the band as its origin and the reasons the caller
        /// decided on. The band is left empty and untracked; its id lives on
        /// in history. Refuses a band that is travelling, or has stock in use.
        /// </summary>
        public Settlement Found(MobileGroup band, Reasons reasons)
        {
            if (band is null)
            {
                throw new ArgumentNullException(nameof(band));
            }

            if (band.Destination is object)
            {
                throw new InvalidOperationException(
                    band.Id + " is on its way to " + band.Destination + "; a band settles where it stands.");
            }

            RequireStockAtRest(band);
            RequireTrackedEverywhere(band);

            // Jobs refuses to track a community standing where nobody can,
            // and a band's position is its own to set after tracking - so
            // the settlement's ground is checked here, before the band has
            // left any tracker, rather than found wanting at the last step.
            if (!_jobs.CanStandAt(band.Position))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(band), band.Position, "Nobody can stand on that cell; a settlement there could not be worked from.");
            }

            // Every stream the settlement will book has to fit before the
            // end of time, or Track would throw after the band had already
            // been emptied. Checked here, before anything moves, for the
            // same reason as the two above: a refusal leaves the band whole.
            if (!HasRoomForStreams(_clock.Now))
            {
                throw new InvalidOperationException(
                    "The world ends before " + band.Id + "'s settlement could hold its first courtship; a band settles with a tomorrow to settle into.");
            }

            // The band leaves every tracker before its members leave it:
            // Jobs refuses a band with anyone out on a task, and can only
            // tell while the members are still listed. Jobs goes first
            // because it is the one refusal the preflight above does not
            // cover, and a refusal must leave the band on every tracker.
            var inFamine = _hunger.IsInFamine(band);
            _jobs.Untrack(band);
            _deaths.Untrack(band);
            _fertility.Untrack(band);
            _hunger.Untrack(band);
            _matchmaking.Untrack(band);

            var settlement = new Settlement(_clock.Ids.Next(EntityKind.Settlement), band.Position);

            // Members, then stock, then the trackers: the first meal or dawn
            // the settlement books should find its people and food already
            // there. A band with nobody in it leads nobody.
            while (band.Members.Count > 0)
            {
                var member = band.Members[0];
                band.RemoveMember(member);
                settlement.AddMember(member);
            }

            band.Leader = PersonHandle.None;

            for (var kind = 1; kind < DefinedKinds.Length; kind++)
            {
                if (!DefinedKinds[kind])
                {
                    continue;
                }

                var available = band.SharedSupplies.Available((ResourceKind)kind);

                if (available > 0)
                {
                    band.SharedSupplies.TransferTo(settlement.SharedSupplies, (ResourceKind)kind, available);
                }
            }

            // What the band knew, the settlement knows - the same handover as
            // members and stock, and section 12's rule that at founding the
            // settlement takes the map over. A band that never wandered has no
            // map to give, and its settlement starts knowing nothing rather
            // than knowing everything.
            if (_knownMaps.IsTracked(band.Id))
            {
                _knownMaps.HandOver(band.Id, settlement.Id);
            }
            else
            {
                _knownMaps.Track(settlement.Id);
            }

            _deaths.Track(settlement);
            _fertility.Track(settlement);
            _hunger.Track(settlement, inFamine);
            _jobs.Track(settlement);
            _matchmaking.Track(settlement);

            // Announced before recorded, as Households.Form does: a refused
            // publish is a wiring bug the bus throws for.
            _bus.Publish(DomainEventKind.SettlementFounded, settlement.Id, band.Id, reasons);
            _settlements.Add(settlement);
            return settlement;
        }

        /// <summary>
        /// Whether a settlement founded now could book every stream it needs:
        /// the longest is <see cref="Matchmaking.Interval"/>, a year out. The
        /// council asks this before deciding to settle on the world's last
        /// days, so that it stays a band rather than be refused.
        /// </summary>
        public static bool HasRoomForStreams(SimulationTime now) =>
            Matchmaking.Interval <= long.MaxValue - now.Ticks;

        // Every tracker must know the band before any of them lets it go,
        // or a "not tracked" from the third would leave the first two
        // already done with it and a retry unable to succeed.
        private void RequireTrackedEverywhere(MobileGroup band)
        {
            if (!_jobs.IsTracked(band) || !_deaths.IsTracked(band) || !_fertility.IsTracked(band)
                || !_hunger.IsTracked(band) || !_matchmaking.IsTracked(band))
            {
                throw new InvalidOperationException(
                    band.Id + " is not on every tracker a settlement takes over from; founding hands over all five or none.");
            }
        }

        private static void RequireStockAtRest(MobileGroup band)
        {
            for (var kind = 1; kind < DefinedKinds.Length; kind++)
            {
                if (!DefinedKinds[kind])
                {
                    continue;
                }

                var ledger = band.SharedSupplies;
                var resource = (ResourceKind)kind;

                if (ledger.Reserved(resource) != 0 || ledger.Carried(resource) != 0 || ledger.InProcess(resource) != 0)
                {
                    throw new InvalidOperationException(
                        band.Id + " has " + resource + " reserved, carried or in process; found when nobody is mid-task.");
                }
            }
        }
    }
}
