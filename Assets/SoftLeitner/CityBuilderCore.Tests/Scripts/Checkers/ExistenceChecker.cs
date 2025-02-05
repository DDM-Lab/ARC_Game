using NUnit.Framework;
using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class ExistenceChecker : CheckerBase
    {
        public GameObject Object;
        public bool ExpectedExistance;
        public bool ActualExistence => Object;

        public override void Check()
        {
            Assert.AreEqual(ExpectedExistance, ActualExistence, name);
        }
    }
}