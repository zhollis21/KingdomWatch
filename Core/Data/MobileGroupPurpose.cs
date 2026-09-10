namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// What a <see cref="MobileGroup"/> is for.
    /// </summary>
    /// <remarks>
    /// Three uses at launch. SupplyConvoy is the likely fourth - warfare
    /// promises visible carts of grain on roads - so the enum stays open rather
    /// than being treated as closed. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 3.
    ///
    /// Values are explicit and must never be renumbered: they are written into
    /// saves. Append new purposes at the end.
    /// </remarks>
    public enum MobileGroupPurpose
    {
        /// <summary>Not a valid purpose. Guards against a defaulted field.</summary>
        None = 0,

        /// <summary>A wandering band. The starting state of the whole game.</summary>
        NomadicBand = 1,

        /// <summary>People travelling to found a settlement.</summary>
        FoundingParty = 2,

        /// <summary>People travelling to fight.</summary>
        Army = 3,
    }
}
