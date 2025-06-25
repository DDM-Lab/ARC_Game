using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, during its activity, modifies a score<br/>
    /// might be used for weather, seasons(summer-happiness, fertility, ...) or to decrease the player score over time
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_score_modifier_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(ScoreModifierHappening))]
    public class ScoreModifierHappening : TimingHappening, IScoreModifier
    {
        [Tooltip("the score that will be modified during the happenings activity")]
        public Score Score;
        [Tooltip("score value will first be multiplied with this value")]
        public float Multiplier = 1f;
        [Tooltip("after multiplication this value is added to the score value")]
        public int Addend;

        Score IScoreModifier.Score => Score;

#pragma warning disable 0067
        public event Action<ILayerModifier> Changed;
#pragma warning restore 0067


        public override void Activate()
        {
            base.Activate();

            Dependencies.Get<IScoresCalculator>().Register(this);
        }

        public override void Deactivate()
        {
            base.Deactivate();

            Dependencies.Get<IScoresCalculator>().Deregister(this);
        }

        public int Modify(int value)
        {
            return Mathf.RoundToInt(value * Multiplier) + Addend;
        }
    }
}
