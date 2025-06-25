namespace CityBuilderCore
{
    /// <summary>
    /// any object that has a tooltip
    /// </summary>
    public interface ITooltipOwner
    {
        /// <summary>
        /// main text in the tooltip
        /// </summary>
        string TooltipName { get; }
        /// <summary>
        /// smaller but potentially longer text below the main text
        /// </summary>
        string TooltipDescription { get; }
    }
}
