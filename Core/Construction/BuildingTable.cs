using System;
using KingdomWatch.Core.Clock;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// What one kind of building needs and takes up: section 4 of the
    /// economy ladder as data.
    /// </summary>
    public readonly struct BuildingSpec
    {
        internal BuildingSpec(Capability skill, SkillTier minimumTier, int wood, int width, int height, int clearance, long buildTicks)
        {
            Skill = skill;
            MinimumTier = minimumTier;
            Wood = wood;
            Width = width;
            Height = height;
            Clearance = clearance;
            BuildTicks = buildTicks;
        }

        /// <summary>The capability the first build condition reads.</summary>
        public Capability Skill { get; }

        /// <summary>The least tier of <see cref="Skill"/> someone must hold.</summary>
        public SkillTier MinimumTier { get; }

        /// <summary>Wood built into it, taken from stock when work is approved.</summary>
        public int Wood { get; }

        /// <summary>Cells across.</summary>
        public int Width { get; }

        /// <summary>Cells down.</summary>
        public int Height { get; }

        /// <summary>
        /// Rows north of the footprint kept free of other buildings and of
        /// the camp yard (<see cref="Buildings.CampYardRadius"/>): the game's 3/4 view draws the roof standing over them,
        /// so a building there would be hidden under it (#150). They may lie
        /// off the map, and under another building's roof.
        /// </summary>
        public int Clearance { get; }

        /// <summary>Worker-ticks of building once the ground is clear.</summary>
        public long BuildTicks { get; }
    }

    /// <summary>
    /// The gates and footprint of every <see cref="BuildingKind"/>.
    /// </summary>
    /// <remarks>
    /// Prerequisites and demand are not here: they read the settlement, so
    /// they are rules in <see cref="Buildings"/>. Every number is a
    /// placeholder in the <see cref="Data.PrimitiveTier"/> sense; footprints
    /// are in 1.5 m cells (#123), sized to the ground the art's house and
    /// barn stand on, with the clearance their roofs need (#150).
    /// </remarks>
    public static class BuildingTable
    {
        private const long Hour = SimulationTime.TicksPerHour;

        private static readonly BuildingSpec House = new BuildingSpec(Capability.Construction, SkillTier.Novice, 20, 5, 5, 2, 24 * Hour);
        private static readonly BuildingSpec Barn = new BuildingSpec(Capability.Farming, SkillTier.Novice, 30, 7, 5, 3, 32 * Hour);
        private static readonly BuildingSpec Field = new BuildingSpec(Capability.Farming, SkillTier.Novice, 5, 5, 5, 0, 8 * Hour);

        /// <summary>The spec of a kind. Throws for <see cref="BuildingKind.None"/> or an undefined kind.</summary>
        public static BuildingSpec Of(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.House:
                    return House;
                case BuildingKind.Barn:
                    return Barn;
                case BuildingKind.Field:
                    return Field;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a defined BuildingKind, or None.");
            }
        }
    }
}
