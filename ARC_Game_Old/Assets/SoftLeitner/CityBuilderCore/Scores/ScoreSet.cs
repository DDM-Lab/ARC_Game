using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some collection of scores<br/>
    /// a set of all scores in the game is needed by <see cref="ObjectRepository"/> so the <see cref="IScoresCalculator"/> can calculate them
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_score_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(ScoreSet))]
    public class ScoreSet : ObjectSet<Score> { }
}