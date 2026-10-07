using System;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// The world's housing (#69): a settlement's households live in the
    /// houses it has built (<see cref="Buildings"/>), and everyone else -
    /// bands, and couples formed with no community named - in camp space
    /// (<see cref="CampSpace"/>).
    /// </summary>
    /// <remarks>
    /// **Housing is what limits a village's growth.** Section 6 makes it the
    /// regulator: a couple marries only into a free house, and a house is
    /// built only when the land can fuel its hearth
    /// (<see cref="Buildings.CanFuelAnotherHearth"/>). Without it a village
    /// grew until a winter it could not fuel or feed killed it whole (#147,
    /// #149).
    ///
    /// The stock is set after construction because <see cref="Buildings"/>
    /// needs the <see cref="Households"/> this is handed to; a settlement
    /// asking before it is set is a wiring bug and throws.
    /// </remarks>
    public sealed class SettlementHousing : IHousing
    {
        private readonly CampSpace _camp = new CampSpace();
        private Buildings? _stock;

        /// <summary>The settlements' houses. Set once, by <see cref="World"/>.</summary>
        public Buildings Stock
        {
            get => _stock ?? throw new InvalidOperationException("No buildings have been given to the settlements' housing.");
            set => _stock = value ?? throw new ArgumentNullException(nameof(value));
        }

        public bool HasVacancy(EntityId community) =>
            community.Kind == EntityKind.Settlement ? Stock.HasVacancy(community) : _camp.HasVacancy(community);

        public EntityId Claim(EntityId community) =>
            community.Kind == EntityKind.Settlement ? Stock.Claim(community) : _camp.Claim(community);

        public void Release(EntityId home)
        {
            if (home.IsNone)
            {
                _camp.Release(home);
            }
            else
            {
                Stock.Release(home);
            }
        }
    }
}
