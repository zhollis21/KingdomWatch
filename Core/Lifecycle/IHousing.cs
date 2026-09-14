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
    /// One implementation today, <see cref="CampSpace"/>. The settlement
    /// housing stock that actually runs short is #69, and it plugs in here.
    ///
    /// Two calls rather than one TryClaim, so that eligibility can be asked
    /// without changing anything: <see cref="FamilyFormation.Evaluate"/> reads
    /// <see cref="HasVacancy"/>, and only <see cref="FamilyFormation.Partner"/>
    /// claims.
    /// </remarks>
    public interface IHousing
    {
        /// <summary>Whether <see cref="Claim"/> would succeed right now.</summary>
        bool HasVacancy { get; }

        /// <summary>
        /// Takes a home for a new household and returns its id -
        /// <see cref="EntityId.None"/> when the housing has no identity to
        /// give, as camp space does not. Throws when there is no vacancy.
        /// </summary>
        EntityId Claim();

        /// <summary>
        /// Returns a home to the stock when its household dissolves. Takes
        /// whatever <see cref="Claim"/> handed out, <see cref="EntityId.None"/>
        /// included.
        /// </summary>
        void Release(EntityId home);
    }
}
