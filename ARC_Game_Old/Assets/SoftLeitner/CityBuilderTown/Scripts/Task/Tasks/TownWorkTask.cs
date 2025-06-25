using CityBuilderCore;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// task that makes a walker work at its point on the map<br/>
    /// the <see cref="TownWorkComponent"/> uses this task to get walkers to work at it and check how many are doing so
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_work_task.html")]
    public class TownWorkTask : TownTask
    {
        [Tooltip("multiplies for the rate the walkers energy is decreased by working at this task")]
        public float Effort = 1f;
        [Tooltip("animation parameter set to true while working, leave empty for default work animation")]
        public string Animation;

        public override IEnumerable<TownWalker> Walkers
        {
            get
            {
                if (_walker != null)
                    yield return _walker;
            }
        }

        public bool IsWorking => _walker && _walker.CurrentAction is TownWorkAction;

        private bool _isNeeded;
        private TownWalker _walker;

        protected override void Update()
        {
            base.Update();

            if (Visual)
                Visual.gameObject.SetActive(_walker == null && _isNeeded);

            if (IsWorking)
                _walker.Work(Effort);
        }

        public override bool CanStartTask(TownWalker walker)
        {
            return _walker == null && _isNeeded;
        }
        public override WalkerAction[] StartTask(TownWalker walker)
        {
            _walker = walker;

            return new WalkerAction[]
            {
                new WalkPointAction(){ _point = Point},
                new RotateAction(Quaternion.LookRotation(transform.forward),0.2f),
                string.IsNullOrWhiteSpace(Animation)? new TownWorkAction(TownManager.WorkParameter):new TownWorkAction(Animation)
            };
        }
        public override void ContinueTask(TownWalker walker)
        {
            _walker = walker;
        }
        public override void FinishTask(TownWalker walker, ProcessState process)
        {
            _walker = null;
        }

        public void SetNeeded(bool value)
        {
            _isNeeded = value;

            if (!_isNeeded && _walker)
            {
                _walker.CancelProcess();
            }
        }

        public override string GetDescription() => $"working";
    }
}
