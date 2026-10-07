using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// Where households get homes. Section 6 makes this the demographic
    /// regulator - forming a household requires an available home, homes
    /// require labour and materials, so housing supply throttles fertility -
    /// which is why it is a seam rather than a detail of household formation.
    /// </summary>
    /// <remarks>
    /// **Asked per community** (#69). A band's people live in camp space
    /// (<see cref="CampSpace"/>), and a settlement's in the houses it has
    /// built (<see cref="Construction.SettlementHousing"/>), so whether there
    /// is a home depends on where the couple is. The community is
    /// <see cref="EntityId.None"/> where there is none to name - world
    /// generation's founding couples, and fixtures - which camp space takes
    /// like any band's.
    ///
    /// Two calls rather than one TryClaim, so that eligibility can be asked
    /// without changing anything: <see cref="FamilyFormation.Evaluate"/> reads
    /// <see cref="HasVacancy"/>, and only <see cref="FamilyFormation.Partner"/>
    /// claims.
    /// </remarks>
    public interface IHousing
    {
        /// <summary>Whether <see cref="Claim"/> would succeed right now for this community.</summary>
        bool HasVacancy(EntityId community);

        /// <summary>
        /// Takes a home in this community for a new household and returns its
        /// id - <see cref="EntityId.None"/> when the housing has no identity
        /// to give, as camp space does not. Throws when there is no vacancy.
        /// </summary>
        EntityId Claim(EntityId community);

        /// <summary>
        /// Returns a home to the stock when its household dissolves. Takes
        /// whatever <see cref="Claim"/> handed out, <see cref="EntityId.None"/>
        /// included.
        /// </summary>
        void Release(EntityId home);
    }
}
