using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper class for <see cref="Walker"/> that holds the current status of a running process<br/>
    /// a process is a series of <see cref="WalkerAction"/>s that is usually started from within the walker<br/> 
    /// ProcessState takes care of tracking the currently active action as well as advancing to the next one or canceling the process <br/>
    /// <see cref="Walker.StartProcess(WalkerAction[], string)"/> starts a new process, when a walker is loaded use <see cref="Walker.continueProcess"/><br/>
    /// a process can be canceled by <see cref="Walker.CancelProcess"/> which may not end the process immediately but rather when the walker is in an ok state next
    /// </summary>
    public class ProcessState
    {
        /// <summary>
        /// identifier that can be used to check what the walker is currently doing
        /// </summary>
        public string Key { get; private set; }
        /// <summary>
        /// actions that are executed in order
        /// </summary>
        public WalkerAction[] Actions { get; private set; }
        /// <summary>
        /// index of the currently active action
        /// </summary>
        public int CurrentIndex { get; private set; }
        /// <summary>
        /// whether the process has been canceled, useful to check if a task has actually been performed or just distruped by something else
        /// </summary>
        public bool IsCanceled { get; private set; }

        /// <summary>
        /// currently active action
        /// </summary>
        public WalkerAction CurrentAction => Actions.ElementAtOrDefault(CurrentIndex);

        private ProcessState()
        {

        }
        public ProcessState(string key, WalkerAction[] actions)
        {
            Key = key;
            Actions = actions;
        }

        /// <summary>
        /// starts the process and, in extension, the first action in it
        /// </summary>
        /// <param name="walker">the walker the process is performed on</param>
        public void Start(Walker walker)
        {
            CurrentAction?.Start(walker);
        }
        /// <summary>
        /// moves the process to the next action or finishes it when the last one has been finished
        /// </summary>
        /// <param name="walker">the walker the process is performed on</param>
        /// <returns>true if the next action has been started successfully, false if the process is finished or canceled</returns>
        public bool Advance(Walker walker)
        {
            if (IsCanceled)
                return false;

            CurrentAction?.End(walker);
            CurrentIndex++;
            if (CurrentIndex >= Actions.Length)
                return false;
            CurrentAction?.Start(walker);
            return true;
        }
        /// <summary>
        /// called when a game is loaded so the process can continue where it left off
        /// </summary>
        /// <param name="walker">the walker the process is performed on</param>
        public void Continue(Walker walker)
        {
            CurrentAction?.Continue(walker);
        }
        /// <summary>
        /// requests for the process to be canceled, the process may not finish immediately but rather when it next is in a good state
        /// </summary>
        /// <param name="walker">the walker the process is performed on</param>
        public void Cancel(Walker walker)
        {
            IsCanceled = true;
            CurrentAction?.Cancel(walker);
        }

        #region Saving
        [Serializable]
        public class ProcessData
        {
            public string Key;
            [SerializeReference]
            public WalkerAction[] Actions;
            public int CurrentAction;
            public bool IsCanceled;
        }

        public ProcessData GetData() => new ProcessData()
        {
            Key = Key,
            Actions = Actions,
            CurrentAction = CurrentIndex,
            IsCanceled = IsCanceled
        };
        public static ProcessState FromData(ProcessData data)
        {
            if (data == null || data.Actions == null || data.Actions.Length == 0)
                return null;
            return new ProcessState()
            {
                Key = data.Key,
                Actions = data.Actions,
                CurrentIndex = data.CurrentAction,
                IsCanceled = data.IsCanceled
            };
        }
        #endregion
    }
}