using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper used to specify when a happening occurs<br/>
    /// condition of the same unit are ORed, conditions of different units are ANDed<br/>
    /// eg Day:2|Day:3|Month:4 will occur on the 2nd and 3rd day of every 4th month
    /// </summary>
    [Serializable]
    public class TimingHappeningOccurence
    {
        [Tooltip("the happening that will be active when the conditions are met")]
        public TimingHappening Happening;
        [Tooltip("condition for the happening occuring(Day:2|Day:3|Month:4 will occur on the 2nd and 3rd day of every 4th month)")]
        public TimingCondition[] Conditions;

        public bool GetIsOccuring(float playtime, int seed)
        {
            return Conditions.GroupBy(c => c.Unit).All(g => g.Any(c => c.Check(playtime, seed)));
        }
    }
}