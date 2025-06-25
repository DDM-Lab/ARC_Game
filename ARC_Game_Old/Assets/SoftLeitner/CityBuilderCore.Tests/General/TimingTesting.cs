using NUnit.Framework;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class TimingTesting
    {
        [Test]
        public virtual void CheckTimingChance()
        {
            //make sure 

            var unit = ScriptableObject.CreateInstance<TimingUnit>();
            unit.Amount = 0;
            unit.Duration = 1;

            var occurence = new TimingHappeningOccurence()
            {
                Conditions = new[] {
                    new TimingCondition()
                    {
                        Unit = unit,
                        Number = 0,
                        Chance = 0.5
                    }
                }
            };

            var resultsA = Enumerable.Range(0, 10).Select(i => occurence.GetIsOccuring(i, 1)).ToArray();
            var resultsB = Enumerable.Range(0, 10).Select(i => occurence.GetIsOccuring(i, 7)).ToArray();
            var resultsC = Enumerable.Range(0, 10).Select(i => occurence.GetIsOccuring(i, 1)).ToArray();

            Debug.Assert(checkResults(resultsA, resultsB) == false);
            Debug.Assert(checkResults(resultsA, resultsC) == true);
        }

        private bool checkResults(bool[] a, bool[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
    }
}
