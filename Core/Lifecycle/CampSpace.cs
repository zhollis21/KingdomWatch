using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// Housing for a nomadic band: there is always room for another tent.
    /// Section 15 - temporary dwellings satisfy the housing requirement in
    /// nomadic mode - made concrete.
    /// </summary>
    /// <remarks>
    /// Homes here have no identity, so <see cref="Claim"/> hands out
    /// <see cref="EntityId.None"/> and <see cref="Release"/> accepts nothing
    /// else. Every community gets the same answer: a settlement's houses are
    /// <see cref="Construction.SettlementHousing"/>'s (#69), which hands
    /// everyone else here.
    /// </remarks>
    public sealed class CampSpace : IHousing
    {
        public bool HasVacancy(EntityId community) => true;

        public EntityId Claim(EntityId community) => EntityId.None;

        public void Release(EntityId home)
        {
            if (!home.IsNone)
            {
                throw new ArgumentException(
                    "Camp space never handed out " + home + "; a home with an id belongs to a housing stock.",
                    nameof(home));
            }
        }
    }
}
