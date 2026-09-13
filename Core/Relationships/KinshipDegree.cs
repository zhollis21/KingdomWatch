namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// The nearest blood relation between two different people, as far as
    /// <see cref="Genealogy.Kinship"/> looks: two generations up, which
    /// reaches as far as first cousins.
    /// </summary>
    /// <remarks>
    /// This is the vocabulary section 6's family-formation rules are written
    /// in - the hard ban runs through <see cref="Grandparent"/>, and
    /// <see cref="FirstCousin"/> is the culture taboo - so the genealogy
    /// answers in these terms and family formation (#9) decides policy over
    /// them. Half-siblings are <see cref="Sibling"/>: the ban does not
    /// distinguish, and neither does the graph.
    ///
    /// There is no "same person" member. A pair loop never has cause to ask
    /// about someone and themselves, so the genealogy refuses the question
    /// rather than naming an answer to it.
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

        ParentChild = 1,

        /// <summary>At least one parent in common. Half-siblings included.</summary>
        Sibling = 2,

        /// <summary>Grandparent or grandchild.</summary>
        Grandparent = 3,

        /// <summary>A parent's sibling, or a sibling's child.</summary>
        AuntUncle = 4,

        /// <summary>A parent of each is a sibling of a parent of the other.</summary>
        FirstCousin = 5,
    }
}
