namespace CityBuilderCore
{
    /// <summary>
    /// a modifier applied to the result of a score, is registered with <see cref="IScoresCalculator"/>
    /// </summary>
    public interface IScoreModifier
    {
        /// <summary>
        /// the score this modifier applies to
        /// </summary>
        Score Score { get; }

        /// <summary>
        /// modifies the score value
        /// </summary>
        /// <param name="value">value of the score</param>
        /// <returns>the modified value</returns>
        int Modify(int value);
    }
}