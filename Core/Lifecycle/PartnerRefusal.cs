namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// Why two people may not partner, or <see cref="None"/> when they may.
    /// The answer <see cref="FamilyFormation.Evaluate"/> gives, in the order
    /// the checks run: the first refusal found is the one reported.
    /// </summary>
    /// <remarks>
    /// A reason rather than a bool so the social decision system (#38) can
    /// tell a pairing that is wrong from one that is merely early - a couple
    /// refused for <see cref="Mourning"/> is worth asking about again next
    /// year; one refused for <see cref="KinshipBanned"/> never is.
    /// </remarks>
    public enum PartnerRefusal
    {
        /// <summary>Eligible.</summary>
        None = 0,

        /// <summary>The same person twice.</summary>
        SamePerson = 1,

        /// <summary>One of them is not yet an adult.</summary>
        NotAdult = 2,

        /// <summary>Both the same sex.</summary>
        SameSex = 3,

        /// <summary>One of them has an active partnership.</summary>
        AlreadyPartnered = 4,

        /// <summary>One of them lost a partner too recently.</summary>
        Mourning = 5,

        /// <summary>
        /// Too closely related: parent and child, siblings, grandparent and
        /// grandchild, or aunt or uncle and niece or nephew. A hard ban.
        /// </summary>
        KinshipBanned = 6,

        /// <summary>First cousins, and the settings do not permit it.</summary>
        CousinTaboo = 7,

        /// <summary>No home for the household they would form.</summary>
        NoHomeAvailable = 8,
    }
}
