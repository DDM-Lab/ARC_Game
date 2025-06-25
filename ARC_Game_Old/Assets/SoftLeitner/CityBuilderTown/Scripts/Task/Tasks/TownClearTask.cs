using CityBuilderCore;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// makes walkers walk to its point and play a quick work animation<br/>
    /// after that a specified structure has the point removed and the task finishes<br/>
    /// used by <see cref="TownConstructionComponent"/> to clear the ground of grass before the actual building starts
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_clear_task.html")]
    public class TownClearTask : TownTask
    {
        [Tooltip("keys of the structures that get cleared when the task finishes")]
        public string[] StructureKeys;
        [Tooltip("how long the walker has to spend in the work animation")]
        public float Duration;

        public override IEnumerable<TownWalker> Walkers
        {
            get
            {
                if (_walker != null)
                    yield return _walker;
            }
        }

        private TownWalker _walker;

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
                new MessageAction("WORK"),
                new WaitAnimatedAction(Duration,TownManager.WorkParameter),
                new WaitAction(0.25f)//get back up
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

            StructureKeys.ForEach(key => Dependencies.Get<IStructureManager>().GetStructure(key).Remove(new Vector2Int[] { Point }));
        }

        public override string GetDescription() => "clearing the ground";
    }
}
