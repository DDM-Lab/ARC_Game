using CityBuilderCore;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// walker action that just sends a signal in the form of a text key to the walkers current task<br/>
    /// may be used to signal to the task that a certain stage of the process has been reached
    /// </summary>
    public class TownSignalAction : WalkerAction
    {
        [SerializeField]
        private string _key;

        public TownSignalAction()
        {

        }
        public TownSignalAction(string key)
        {
            _key = key;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            var townWalker = (TownWalker)walker;

            townWalker.CurrentTask.SignalTask(townWalker, _key);
            walker.AdvanceProcess();
        }
    }
}