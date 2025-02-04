using NUnit.Framework;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.TestTools;

namespace CityBuilderCore.Tests
{
    public class LinkRoadTesting : TestingBase
    {
        public override string ScenePath => @"Assets/SoftLeitner/CityBuilderCore.Tests/City/Movements/LinkRoadDebugging.unity";
        public override float TimeScale => 10f;

        private DebugWalker[] _walkers;

        [UnitySetUp]
        public override IEnumerator SetUp()
        {
            yield return base.SetUp();

            _walkers = Extensions.FindObjects<DebugWalker>();

            int cycles = 0;

            while (cycles < 30)
            {
                if (_walkers.All(w => w.HasFinished))
                    break;
                yield return new WaitForSeconds(1f);
                cycles++;
            }
        }

        [Test]
        public override void Check()
        {
            base.Check();

            foreach (var walker in _walkers)
            {
                string message = $"{walker.transform.parent.parent.name} {walker.transform.parent.name}";
                if (walker.ShouldArrive)
                    Assert.IsTrue(walker.HasArrived, message);
                else
                    Assert.IsFalse(walker.HasArrived, message);
            }
        }
    }
}