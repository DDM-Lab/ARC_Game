using System;

namespace CityBuilderCore
{
    /// <summary>
    /// central repository that calculates and buffers score values<br/>
    /// this is expecially important for performance heavy scores that should not be calculated multiple times every frame<br/>
    /// </summary>
    public interface IScoresCalculator
    {
        /// <summary>
        /// fires after every round of score calculations<br/>
        /// can be used to refresh score visualizations
        /// </summary>
        event Action Calculated;
        /// <summary>
        /// gets the last calculated value for the score
        /// </summary>
        /// <param name="score">the score for which to retrieve tha value</param>
        /// <returns>the last value calculated</returns>
        int GetValue(Score score);

        /// <summary>
        /// adds a modifier that will be applied to score values after calculating them
        /// </summary>
        /// <param name="modifier">the modifier that will be applied next time score calculation happens</param>
        void Register(IScoreModifier modifier);
        /// <summary>
        /// removes a modifier that has previously been registered
        /// </summary>
        /// <param name="modifier">the modifier that will no longer be applied, the change will not happen until the next round of calculations</param>
        void Deregister(IScoreModifier modifier);
    }
}