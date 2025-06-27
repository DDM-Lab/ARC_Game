using NUnit.Framework;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class ExistencesChecker : CheckerBase
    {
        public GameObject[] Objects;
        public int ExpectedExistance;
        public int ActualExistence => Objects.Count(o => o);

        public override void Check()
        {
            Assert.AreEqual(ExpectedExistance, ActualExistence, name);
        }
    }
}