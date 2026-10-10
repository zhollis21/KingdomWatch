using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Construction
{
    /// <summary>Where a field is in its year: being tended toward a crop, or being harvested.</summary>
    public enum FieldStage : byte
    {
        /// <summary>Not a field.</summary>
        None = 0,

        /// <summary>Sown, and worked toward <see cref="Buildings.TendingDays"/> tended days.</summary>
        Tending = 1,

        /// <summary>Ripe, and brought in over <see cref="Buildings.HarvestDays"/> days.</summary>
        Harvesting = 2,
    }

    /// <summary>
    /// One building: a house, a barn or a field, standing or going up, on a
    /// rectangle of cells near its settlement (#100).
    /// </summary>
    /// <remarks>
    /// One record per building rather than a count per settlement, because
    /// a home is something a household holds by id - what
    /// <see cref="Lifecycle.IHousing.Claim"/> hands out - and section 12's
    /// destruction and ownership need a thing to destroy and own.
    ///
    /// A plain class for the reason <see cref="Settlements.Settlement"/> is
    /// one: a village has dozens, not thousands. State is changed only by
    /// <see cref="Buildings"/>, hence the internal setters.
    ///
    /// **Work is hours.** Clearing and building are worker-ticks put in by
    /// Builders, claimed when a trip sets out (<see cref="Claimed"/>) and
    /// counted when it comes home (<see cref="Worked"/>), the way a berry
    /// picker claims a bush. The ground is cleared once
    /// <see cref="ClearTicks"/> have been worked, and the building stands
    /// once all of <see cref="LabourTicks"/> have. A field then counts its
    /// own days of tending and harvest.
    /// </remarks>
    public sealed class Building
    {
        internal Building(
            EntityId id,
            BuildingKind kind,
            EntityId settlement,
            EntityId barn,
            WorldPosition anchor,
            WorldPosition[] lane,
            WorldPosition[] square,
            long clearTicks,
            int clearCuts,
            int clearRocks,
            long labourTicks)
        {
            Id = id;
            Kind = kind;
            Tier = 1;
            Settlement = settlement;
            Barn = barn;
            Anchor = anchor;
            // Wrapped once: a bare array behind IReadOnlyList can be downcast
            // and rewritten, and reservations, clearing and the hash read these.
            Lane = Array.AsReadOnly(lane);
            Square = Array.AsReadOnly(square);
            ClearTicks = clearTicks;
            ClearCuts = clearCuts;
            ClearRocks = clearRocks;
            LabourTicks = labourTicks;
            Cleared = clearTicks == 0L;
            Stage = kind == BuildingKind.Field ? FieldStage.Tending : FieldStage.None;
        }

        /// <summary>Durable identity, kind <see cref="EntityKind.Building"/>.</summary>
        public EntityId Id { get; }

        public BuildingKind Kind { get; }

        /// <summary>
        /// Which rung of its kind it stands at, from 1. Nothing tiers up yet:
        /// the logic arrives with the first kind that does (#144).
        /// </summary>
        public byte Tier { get; }

        /// <summary>The settlement it belongs to.</summary>
        public EntityId Settlement { get; }

        /// <summary>The barn a field is laid out around; <see cref="EntityId.None"/> for anything else.</summary>
        public EntityId Barn { get; }

        /// <summary>The footprint's corner with the least x and y.</summary>
        public WorldPosition Anchor { get; }

        /// <summary>
        /// The cells of the lane the town planner joined it to the roads by,
        /// laid as road when its ground is cleared (#23): from its door, or
        /// for a field from beside the middle of one of its sides. Empty when
        /// the door, or one of a field's edges, is already on the network.
        /// </summary>
        public IReadOnlyList<WorldPosition> Lane { get; }

        /// <summary>
        /// Scrub, forest and rocks in the settlement's camp yard that this building
        /// clears with its own ground, and paves as the square once cleared
        /// (#23): whatever was still uncleared and unclaimed there when it
        /// was approved. Usually the village's first building takes them all.
        /// </summary>
        public IReadOnlyList<WorldPosition> Square { get; }

        /// <summary>Cells across, from <see cref="BuildingTable"/>.</summary>
        public int Width => BuildingTable.Of(Kind).Width;

        /// <summary>Cells down, from <see cref="BuildingTable"/>.</summary>
        public int Height => BuildingTable.Of(Kind).Height;

        /// <summary>Worker-ticks of clearing scrub and forest off the footprint, its lane and its share of the square before building starts.</summary>
        public long ClearTicks { get; }

        /// <summary>
        /// The cuts of standing timber <see cref="ClearTicks"/> was priced at,
        /// whose Wood clearing pays - the quote, not whatever stands when the
        /// ground is cleared, so a claim given back since cannot pay a cut
        /// nobody was charged for (the #148 review).
        /// </summary>
        public int ClearCuts { get; }

        /// <summary>
        /// The rock cells of <see cref="Square"/> its clearing quarries away,
        /// as priced at approval: each pays <see cref="Buildings.StonePerRock"/>
        /// Stone when the ground is cleared (#23).
        /// </summary>
        public int ClearRocks { get; }

        /// <summary>Worker-ticks in all, clearing included.</summary>
        public long LabourTicks { get; }

        /// <summary>Worker-ticks claimed by Builders on their way or at work.</summary>
        public long Claimed { get; internal set; }

        /// <summary>Worker-ticks done.</summary>
        public long Worked { get; internal set; }

        /// <summary>
        /// Whether its clearing is done: the footprint cleared to plains, and
        /// its <see cref="Lane"/> and <see cref="Square"/> cleared and laid as
        /// road (#23).
        /// </summary>
        public bool Cleared { get; internal set; }

        /// <summary>Whether all its work is done.</summary>
        public bool IsComplete => Worked == LabourTicks;

        /// <summary>The household living in a house; <see cref="EntityId.None"/> when empty or not a house.</summary>
        public EntityId Occupant { get; internal set; }

        /// <summary>A field's stage; <see cref="FieldStage.None"/> for anything else.</summary>
        public FieldStage Stage { get; internal set; }

        /// <summary>Days of the current stage done.</summary>
        public int DaysDone { get; internal set; }

        /// <summary>The day <see cref="ClaimedToday"/> and <see cref="WorkedToday"/> count; stale counts belong to a day that is over.</summary>
        public long CountsDay { get; internal set; }

        /// <summary>Worker-ticks of today's field work claimed by Farmers out on it.</summary>
        public long ClaimedToday { get; internal set; }

        /// <summary>Worker-ticks of today's field work done.</summary>
        public long WorkedToday { get; internal set; }

        /// <summary>Whether a cell lies inside the footprint.</summary>
        public bool Covers(WorldPosition at) =>
            at.X >= Anchor.X && at.X < Anchor.X + Width && at.Y >= Anchor.Y && at.Y < Anchor.Y + Height;

        public override string ToString() => Kind + " " + Id + " at " + Anchor;
    }
}
