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
    /// else. A settled band needs the real stock (#69); until then a fresh
    /// settlement keeps these semantics, and its population is bounded by
    /// food alone.
    /// </remarks>
    public sealed class CampSpace : IHousing
    {
        public bool HasVacancy => true;

        public EntityId Claim() => EntityId.None;

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
