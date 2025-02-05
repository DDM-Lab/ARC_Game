using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for scores used for win conditions and statistics<br/>
    /// a score is an asset that provides the method of calculating an int value<br/>
    /// the value can be gotten directly using <see cref="Calculate"/> but it is recommended to use <see cref="GetCalculatorValue"/><br/>
    /// because the value is buffered there, just make sure there is a <see cref="IScoresCalculator"/> in the scene<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_score.html")]
    public abstract class Score : ScriptableObject
    {
        [Tooltip("name of the score for use in UI")]
        public string Name;

        /// <summary>
        /// freshly calculates the score value<br/>
        /// consider using <see cref="GetCalculatorValue"/> instead to get a buffered value
        /// </summary>
        /// <returns>the current score value</returns>
        public abstract int Calculate();

        /// <summary>
        /// gets the buffered score value from the <see cref="IScoresCalculator"/>
        /// </summary>
        /// <returns>the last value calcualted for the score</returns>
        public int GetCalculatorValue() => Dependencies.Get<IScoresCalculator>().GetValue(this);
    }
}