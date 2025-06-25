using CityBuilderCore;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// task that makes a walker go to its point and spand a little time working<br/>
    /// after that a point is added to a structure at the tasks location<br/>
    /// for example the forrester creates a plant task for the Trees Growing structure<br/>
    /// which eventually turns into a full tree that can be harvested for logs
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_plant_task.html")]
    public class TownPlantTask : TownTask
    {
        [Tooltip("how long a walker has to work to finish the task")]
        public float Duration;
        [Tooltip("the road that is added at the location of the task when it is finished")]
        public string Structure;

        public override IEnumerable<TownWalker> Walkers
        {
            get
            {
                if (_walker != null)
                    yield return _walker;
            }
        }

        private TownWalker _walker;
        private float _work;

        public override bool CanStartTask(TownWalker walker)
        {
            return _walker == null;
        }
        public override WalkerAction[] StartTask(TownWalker walker)
        {
            _walker = walker;

            return new WalkerAction[]
            {
                new WalkPointAction(Point),
                new WaitAnimatedAction(Duration,TownManager.WorkParameter)
            };
        }
        public override void ContinueTask(TownWalker walker)
        {
            _walker = walker;
        }
        public override void FinishTask(TownWalker walker, ProcessState process)
        {
            _walker = null;

            if (process.IsCanceled)
                return;

            Terminate();

            Dependencies.Get<IStructureManager>().GetStructure(Structure).Add(Point);
        }

        public override bool ProgressTask(TownWalker walker, string key)
        {
            walker.Work();
            _work -= Time.deltaTime;

            return _work < Duration;
        }

        public override string GetDescription() => $"planting a tree";
        public override string GetDebugText() => (Duration - _work).ToString();

        #region Saving
        protected override string saveExtras() => _work.ToString(System.Globalization.CultureInfo.InvariantCulture);
        protected override void loadExtras(string json) => _work = float.Parse(json, System.Globalization.CultureInfo.InvariantCulture);
        #endregion
    }
}