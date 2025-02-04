using CityBuilderCore;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// task that makes walkers go to it and progress it while playing the work animation<br/>
    /// used by the <see cref="TownConstructionComponent"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_build_task.html")]
    public class TownBuildTask : TownTask
    {
        [Tooltip("maximum number of walkers that can progress the task at once")]
        public int WalkerCount = 1;
        [Tooltip("total duration that has to be spent by walkers progressing the task before it is finished")]
        public float Duration = 1f;

        public float Progress => Mathf.Min(1f, _work / Duration);

        public override IEnumerable<TownWalker> Walkers => _walkers;

        private List<TownWalker> _walkers = new List<TownWalker>();
        private float _work;

        public override bool CanStartTask(TownWalker walker)
        {
            return _walkers.Count < WalkerCount;
        }
        public override WalkerAction[] StartTask(TownWalker walker)
        {
            _walkers.Add(walker);

            return new WalkerAction[]
            {
                new WalkPointAction(){ _point = Point},
                new TownProgressAction(string.Empty,TownManager.ChopParameter)
            };
        }
        public override void ContinueTask(TownWalker walker)
        {
            _walkers.Add(walker);
        }
        public override void FinishTask(TownWalker walker, ProcessState process)
        {
            _walkers.Remove(walker);

            if (process.IsCanceled)
                return;

            Terminate();
        }
        public override bool ProgressTask(TownWalker walker, string key)
        {
            walker.Work();

            _work += Time.deltaTime;

            return _work < Duration;
        }

        public override string GetDescription() => "constructing a building";
        public override string GetDebugText()
        {
            return "Build:" + Progress.ToString();
        }

        #region Saving
        public class TownBuildTaskData
        {
            public float Work;
        }

        protected override string saveExtras()
        {
            return JsonUtility.ToJson(new TownBuildTaskData()
            {
                Work = _work
            });
        }
        protected override void loadExtras(string json)
        {
            base.loadExtras(json);

            var data = JsonUtility.FromJson<TownBuildTaskData>(json);

            _work = data.Work;
        }
        #endregion
    }
}
