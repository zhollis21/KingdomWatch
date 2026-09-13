namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// The nearest blood relation between two people, as far as
    /// <see cref="Genealogy.Kinship"/> looks: two generations up, and the
    /// cousins that gives.
    /// </summary>
    /// <remarks>
    /// This is the vocabulary section 6's family-formation rules are written
    /// in - the hard ban runs through <see cref="Grandparent"/>, and
    /// <see cref="FirstCousin"/> is the culture taboo - so the genealogy
    /// answers in these terms and family formation (#9) decides policy over
    /// them. Half-siblings are <see cref="Sibling"/>: the ban does not
    /// distinguish, and neither does the graph.
    ///
    /// Ordered nearest first. When two relations both hold - which inbreeding
    /// makes possible - the nearer one is reported. A query result rather
    /// than a stored value, so renumbering would be safe; append anyway, so
    /// that a later "closer than" comparison can rely on the order.
    /// </remarks>
    public enum KinshipDegree
    {
        /// <summary>No relation within two generations.</summary>
        None = 0,

        /// <summary>The same person.</summary>
        Self = 1,

        ParentChild = 2,

        /// <summary>At least one parent in common. Half-siblings included.</summary>
        Sibling = 3,

        /// <summary>Grandparent or grandchild.</summary>
        Grandparent = 4,

        /// <summary>A parent's sibling, or a sibling's child.</summary>
        AuntUncle = 5,

        /// <summary>A parent of each is a sibling of a parent of the other.</summary>
        FirstCousin = 6,
    }
}
