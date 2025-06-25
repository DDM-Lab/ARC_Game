namespace CityBuilderCore
{
    public interface IGameSettings
    {
        /// <summary>
        /// when false efficiency is disabled and always 1, mainly useful for debugging
        /// </summary>
        bool HasEfficiency { get; }
        /// <summary>
        /// when false employment is disabled and behaves as if always fully employed, mainly useful for debugging
        /// </summary>
        bool HasEmployment { get; }

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

        /// <summary>
        /// how often various checkers(this.StartChecker) in the game run(calculating scores, check immigration, check employment, ...)
        /// </summary>
        float CheckInterval { get; }

        /// <summary>
        /// register something influencing difficulty(for example increase risks faster during times of peril)
        /// </summary>
        /// <param name="difficultyFactor"></param>
        void RegisterDifficultyFactor(IDifficultyFactor difficultyFactor);
        /// <summary>
        /// deregister something influencing difficulty
        /// </summary>
        /// <param name="difficultyFactor"></param>
        void DeregisterDifficultyFactor(IDifficultyFactor difficultyFactor);
    }
}