using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// used to describe a timeframe in game time using a timing unit<br/>
    /// for example a condition with Unit:Day and Number:3 will be active on the 3rd day every month<br/>
    /// can be combined in <see cref="TimingHappeningOccurence"/> to define when a happening is active
    /// </summary>
    [Serializable]
    public class TimingCondition
    {
        [Tooltip("timing unit to check against")]
        public TimingUnit Unit;
        [Tooltip("exact number the unit has to match(optional 0 for any)")]
        public int Number = 1;
        [Tooltip("inclusive number the condition starts from(optional 0 for any)")]
        public int NumberFrom;
        [Tooltip("inclusive number the condition goes to(optional 0 for any)")]
        public int NumberTill;
        [Tooltip("chance the condition will occur(0-1)")]
        public double Chance = 1;

        /// <summary>
        /// checks if the condition is currently met
        /// </summary>
        /// <param name="playtime">game time to check</param>
        /// <param name="seed">seed used for conditions that have a chance < 1</param>
        /// <returns>true if the condition is met</returns>
        public bool Check(float playtime, int seed)
        {
            var number = Unit.GetNumber(playtime);

            if (Number > 0 && number != Number)
                return false;

            if (NumberFrom > 0 && NumberFrom > number)
                return false;

            if (NumberTill > 0 && NumberTill < number)
                return false;

            if (Chance == 1)
                return true;
            else
                return new System.Random((seed.ToString() + Unit.GetIteration(playtime).ToString()).GetHashCode()).NextDouble() < Chance;
        }
    }
}