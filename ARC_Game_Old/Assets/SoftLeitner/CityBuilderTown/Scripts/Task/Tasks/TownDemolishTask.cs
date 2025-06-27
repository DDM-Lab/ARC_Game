using CityBuilderCore;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// task makes walker walk to it, work for a little and the removes the structure it sits on<br/>
    /// when it is placed on a building it makes the walker go to the building instead of the task point
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_demolish_task.html")]
    public class TownDemolishTask : TownTask
    {
        [Tooltip("can be filled if the demolish task is meant for a specific structure, the demolish task without a key is used if no specific one can be found")]
        public string StructureKey;
        [Tooltip("time it takes the walker to finish the task once it has arrived at the point")]
        public float Duration;

        public override IEnumerable<TownWalker> Walkers
        {
            get
            {
                if (_walker != null)
                    yield return _walker;
            }
        }

        private IStructure _structure;
        public IStructure Structure
        {
            get
            {
                if (_structure == null)
                    _structure = Dependencies.Get<IStructureManager>().GetStructures(Point).FirstOrDefault();
                return _structure;
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

            if (Structure is Building building)
            {
                return new WalkerAction[]
                {
                    new WalkBuildingAction(building),
                    new TownProgressAction(string.Empty,TownManager.WorkParameter)
                };
            }
            else
            {
                return new WalkerAction[]
                {
                    new WalkPointAction(Point),
                    new TownProgressAction(string.Empty,TownManager.WorkParameter)
                };
            }
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

            Dependencies.Get<IStructureManager>().Remove(new Vector2Int[] { Point }, 0, false);
        }

        public override bool ProgressTask(TownWalker walker, string key)
        {
            walker.Work();
            _work += Time.deltaTime;

            return _work < Duration;
        }

        public override string GetDescription() => $"demolishing {Structure.GetName()}";
        public override string GetDebugText()
        {
            return (Duration - _work).ToString("#.##");
        }

        #region Saving
        protected override string saveExtras() => _work.ToString(System.Globalization.CultureInfo.InvariantCulture);
        protected override void loadExtras(string json) => _work = float.Parse(json, System.Globalization.CultureInfo.InvariantCulture);
        #endregion
    }
}
