using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// default calculator for scores which calculates and buffers all scores from <see cref="ObjectRepository.Scores"/><br/>
    /// starts calculations in a checker and spreads them out over multiple frames
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_scores_calculator.html")]
    public class DefaultScoresCalculator : MonoBehaviour, IScoresCalculator
    {
        public event Action Calculated;

        private Dictionary<Score, int> _values = new Dictionary<Score, int>();
        private List<IScoreModifier> _modifiers = new List<IScoreModifier>();

        protected virtual void Awake()
        {
            Dependencies.Register<IScoresCalculator>(this);
        }

        protected virtual void Start()
        {
            foreach (var item in Dependencies.Get<IObjectSet<Score>>().Objects)
            {
                _values.Add(item, item.Calculate());
            }

            StartCoroutine(calculate());
        }

        public int GetValue(Score score) => _values.ContainsKey(score) ? _values[score] : 0;

        public void Register(IScoreModifier modifier) => _modifiers.Add(modifier);
        public void Deregister(IScoreModifier modifier) => _modifiers.Remove(modifier);

        private IEnumerator calculate()
        {
            var settings = Dependencies.GetOptional<IGameSettings>();

            while (true)
            {
                yield return new WaitForSecondsRealtime(settings?.CheckInterval ?? 1f);

                foreach (var score in _values.Keys.ToList())
                {
                    var value = score.Calculate();

                    foreach (var modifier in _modifiers.Where(m => m.Score == score))
                    {
                        value = modifier.Modify(value);
                    }

                    _values[score] = value;
                    yield return null;
                }

                Calculated?.Invoke();
            }
        }
    }
}