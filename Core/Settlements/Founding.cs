using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
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
    /// <see cref="ResourceLedger.TransferTo"/>, so both ledgers still audit;
    /// stock that is reserved, carried or in process has no available pile
    /// to move from, so a band with any is refused. So is a band with
    /// anyone out on a task, by <see cref="Jobs.Untrack"/> - the caller
    /// founds at first light or after dusk, when nobody is, rather than
    /// strand a return leg to a camp that is now a town.
    ///
    /// Section 12's founding - a trigger, a scored site, a party that walks
    /// there - is #35's, and this is the half it will call once the party
    /// arrives. What the band gets as a settlement is exactly what it had as
    /// a band: <see cref="Lifecycle.CampSpace"/> still houses its
    /// households, so nothing but food bounds its growth until #69.
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

        private readonly List<Settlement> _settlements = new List<Settlement>();
        private readonly ReadOnlyCollection<Settlement> _settlementsView;

        public Founding(
            DomainEventBus bus,
            Deaths deaths,
            Fertility fertility,
            Hunger hunger,
            Jobs jobs,
            Matchmaking matchmaking)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
            _fertility = fertility ?? throw new ArgumentNullException(nameof(fertility));
            _hunger = hunger ?? throw new ArgumentNullException(nameof(hunger));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _matchmaking = matchmaking ?? throw new ArgumentNullException(nameof(matchmaking));
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

            // The band leaves every tracker before its members leave it:
            // Jobs refuses a band with anyone out on a task, and can only
            // tell while the members are still listed. Jobs goes first
            // because it is the one that can refuse, and a refusal must
            // leave the band on every tracker it was on.
            _jobs.Untrack(band);
            _deaths.Untrack(band);
            _fertility.Untrack(band);
            _hunger.Untrack(band);
            _matchmaking.Untrack(band);

            var settlement = new Settlement(_clock.Ids.Next(EntityKind.Settlement), band.Position);

            // Members, then stock, then the trackers: the first meal or dawn
            // the settlement books should find its people and food already
            // there.
            while (band.Members.Count > 0)
            {
                var member = band.Members[0];
                band.RemoveMember(member);
                settlement.AddMember(member);
            }

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

            _deaths.Track(settlement);
            _fertility.Track(settlement);
            _hunger.Track(settlement);
            _jobs.Track(settlement);
            _matchmaking.Track(settlement);

            // Announced before recorded, as Households.Form does: a refused
            // publish is a wiring bug the bus throws for.
            _bus.Publish(DomainEventKind.SettlementFounded, settlement.Id, band.Id, reasons);
            _settlements.Add(settlement);
            return settlement;
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
