namespace CityBuilderCore
{
    /// <summary>
    /// put this interface on any building part that needs to affect the buildings efficiency
    /// </summary>
    public interface IEfficiencyFactor
    {
        /// <summary>
        /// false disrupts the building
        /// </summary>
        bool IsWorking { get; }
        /// <summary>
        /// how efficiency is changed by this component(0-1), a buildings efficiency is calculated by multiplying all of its efficiency factors
        /// </summary>
        float Factor { get; }
    }
}