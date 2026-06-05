namespace RevitPlugins.Common
{
    /// <summary>How the user supplies the slab outline to the perimeter-rebar tool.</summary>
    public enum SelectionMethod
    {
        /// <summary>Pick floor element(s); the outer perimeter is derived from their geometry.</summary>
        ContinuousFloor,

        /// <summary>Pick an existing filled region and use its boundary directly.</summary>
        FilledRegion
    }
}
