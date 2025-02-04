using CityBuilderCore;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// makes walker go to point> work> wait> work and then places an item on the ground<br/>
    /// when gathering a tree the first work fells the tree and removes it from the primary structure<br/>
    /// the wait time is spent waiting for the tree to fall<br/>
    /// working the second duration represents the walker making the falling ready for pickup
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_harvest_task.html")]
    public class TownHarvestTask : TownTask
    {
        [Tooltip("item task that is placed on the ground when this task is finished")]
        public TownItemTask ItemTask;
        [Tooltip("how far away from the task the walker can stop when using a NavMeshAgent")]
        public float Distance;
        [Header("Structures")]
        [Tooltip("the point is removed from this structure when the primary duration is finished(optional)")]
        public string PrimaryStructureKey;
        [Tooltip("added to after the primary duration and removed after the second one(optional)")]
        public string SecondaryStructureKey;
        [Header("Duration")]
        [Tooltip("how long the worker has to spend to finish the primary phase(optional)")]
        public float PrimaryDuration;
        [Tooltip("how long the worker has to spend waiting between primary and secondary(optional)")]
        public float WaitDuration;
        [Tooltip("how long the worker has to spend to finish the secondary phase(optional)")]
        public float SecondaryDuration;
        [Header("Animation")]
        [Tooltip("animation parameter set to true during primary duration, leave empty for default work animation")]
        public string PrimaryAnimation;
        [Tooltip("animation parameter set to true during secondary duration, leave empty for default work animation")]
        public string SecondaryAnimation;

        public override IEnumerable<TownWalker> Walkers
        {
            get
            {
                if (_walker != null)
                    yield return _walker;
            }
        }

        private TownWalker _walker;
        private float _primaryWork;
        private float _secondaryWork;

        public override bool CanStartTask(TownWalker walker)
        {
            return _walker == null;
        }
        public override WalkerAction[] StartTask(TownWalker walker)
        {
            _walker = walker;

            var actions = new List<WalkerAction>();

            if (walker.Agent)
                actions.Add(new WalkAgentAction(transform.position, Distance));
            else
                actions.Add(new WalkPointAction() { _point = Point });

            if (PrimaryDuration > 0f && _primaryWork < PrimaryDuration)
                actions.Add(new TownProgressAction("A", string.IsNullOrWhiteSpace(PrimaryAnimation) ? TownManager.WorkParameter : Animator.StringToHash(PrimaryAnimation)));

            if (WaitDuration > 0f)
                actions.Add(new WaitAction(WaitDuration));

            if (SecondaryDuration > 0f && _secondaryWork < SecondaryDuration)
                actions.Add(new TownProgressAction("B", string.IsNullOrWhiteSpace(SecondaryAnimation) ? TownManager.WorkParameter : Animator.StringToHash(SecondaryAnimation)));

            return actions.ToArray();
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

            TownManager.Instance.CreateTask(ItemTask, Point);
        }

        public override bool ProgressTask(TownWalker walker, string key)
        {
            switch (key)
            {
                case "A":
                    walker.Work();

                    _primaryWork += Time.deltaTime;
                    if (_primaryWork >= PrimaryDuration)
                    {
                        var primaryStructure = string.IsNullOrWhiteSpace(PrimaryStructureKey) ? null : Dependencies.Get<IStructureManager>().GetStructure(PrimaryStructureKey);
                        var secondaryStructure = string.IsNullOrWhiteSpace(SecondaryStructureKey) ? null : Dependencies.Get<IStructureManager>().GetStructure(SecondaryStructureKey);

                        IStructure.ReplacePoints(primaryStructure, secondaryStructure, Point);

                        return false;
                    }
                    break;
                case "B":
                    walker.Work();

                    _secondaryWork += Time.deltaTime;
                    if (_secondaryWork >= SecondaryDuration)
                    {
                        if (!string.IsNullOrWhiteSpace(SecondaryStructureKey))
                            Dependencies.Get<IStructureManager>().GetStructure(SecondaryStructureKey).Remove(new Vector2Int[] { Point });

                        return false;
                    }
                    break;
            }

            return true;
        }

        public override string GetDescription() => $"harvesting {ItemTask.Items.Item.Name}";
        public override string GetDebugText()
        {
            return
                "A:" + (PrimaryDuration - _primaryWork) + Environment.NewLine +
                "B:" + (SecondaryDuration - _secondaryWork) + Environment.NewLine;
        }

        #region Saving
        public class TownHarvestTaskData
        {
            public float PrimaryWork;
            public float SecondaryWork;
        }

        protected override string saveExtras()
        {
            return JsonUtility.ToJson(new TownHarvestTaskData()
            {
                PrimaryWork = _primaryWork,
                SecondaryWork = _secondaryWork
            });
        }
        protected override void loadExtras(string json)
        {
            base.loadExtras(json);

            var data = JsonUtility.FromJson<TownHarvestTaskData>(json);

            _primaryWork = data.PrimaryWork;
            _secondaryWork = data.SecondaryWork;
        }
        #endregion
    }
}
