namespace CityBuilderCore
{
    /// <summary>
    /// something influencing difficulty, multiple factors are multiplied
    /// </summary>
    public interface IDifficultyFactor
    {
        /// <summary>
        /// influences the speed at which risks increase
        /// </summary>
        float RiskMultiplier { get; }
        /// <summary>
        /// influences the speed at which services deplete
        /// </summary>
        float ServiceMultiplier { get; }
        /// <summary>
        /// influences the speed at which items deplete
        /// </summary>
        float ItemsMultiplier { get; }
    }
}