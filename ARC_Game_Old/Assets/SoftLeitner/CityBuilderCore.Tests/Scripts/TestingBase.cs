using NUnit.Framework;
using System.Collections;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CityBuilderCore.Tests
{
    public abstract class TestingBase
    {
        public virtual string ScenePath => null;
        public virtual float TimeScale => 1f;
        public virtual float Delay => 0f;

        private CheckerBase[] _checkers;

        [UnitySetUp]
        public virtual IEnumerator SetUp()
        {
            Time.timeScale = TimeScale;

            if (!string.IsNullOrWhiteSpace(ScenePath))
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));

            var gameManager = Extensions.FindObject<DefaultGameManager>();
            if (gameManager)
                gameManager.Speed = TimeScale;

            if (Delay > 0f)
                yield return new WaitForSeconds(Delay);

            _checkers = Extensions.FindObjects<CheckerBase>();
        }

        [UnityTearDown]
        public virtual IEnumerator TearDown()
        {
            Time.timeScale = 1f;

            if (!string.IsNullOrWhiteSpace(ScenePath))
                yield return SceneManager.UnloadSceneAsync(ScenePath);
        }

        [Test]
        public virtual void Check()
        {
            foreach (var checker in _checkers)
            {
                checker.Check();
            }
        }
    }
}