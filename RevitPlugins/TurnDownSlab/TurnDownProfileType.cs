namespace RevitPlugins.TurnDownSlab
{
    /// <summary>Which turn-down profile family the user is building a type from.</summary>
    public enum TurnDownProfileType
    {
        /// <summary>SlabEdgeProfile — trapezoid driven by Width + Depth (+ slope → Extension).</summary>
        Simple,

        /// <summary>SlabEdgeProfileBrick — stepped, driven by Width + D1 + D2 (+ slope → Extension).</summary>
        Brick
    }
}
