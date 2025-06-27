using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that goes through multiple stages of building before being replaced be the finished building<br/>
    /// the building process uses workers and items, the workers can take custom paths in and out of the site
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/monuments">https://citybuilder.softleitner.com/manual/monuments</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_monument_site_component.html")]
    public class MonumentSiteComponent : BuildingComponent, IWorkerUser
    {
        public override string Key => "MNS";

        [Tooltip("the building used as a replacement once building is finished")]
        public Building Monument;
        [Tooltip("the stages the monument will go through before it is completed")]
        public MonumentStage[] Stages;

        private int _currentStageIndex = 0;

        public MonumentStage CurrentStage => Stages.ElementAtOrDefault(_currentStageIndex);

        public BuildingComponentReference<IWorkerUser> Reference { get; set; }

        private void Update()
        {
            CurrentStage?.UpdateStage();

            if (CurrentStage.IsFinished)
            {
                if (!string.IsNullOrWhiteSpace(CurrentStage.Notification))
                    Dependencies.Get<INotificationManager>()?.Notify(new NotificationRequest(CurrentStage.Notification, Building.WorldCenter));

                _currentStageIndex++;

                if (CurrentStage == null)
                {
                    Building.Replace(Monument.Info.GetPrefab(Building.Index));
                }
            }
        }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IWorkerUser>(this);
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var replacementMonumentSite = replacement.GetBuildingComponent<MonumentSiteComponent>();

            replaceTrait<IWorkerUser>(this, replacementMonumentSite);
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IWorkerUser>(this);
        }

        public ItemQuantity GetItemsNeeded(Worker worker) =>  CurrentStage.GetItemsNeeded(worker);
        public float GetWorkerNeed(Worker worker) => Building.IsWorking ? CurrentStage.GetWorkerNeed(worker) : 0f;

        public void ReportAssigned(WorkerWalker walker) => CurrentStage.Assign(walker);
        public Vector3[] ReportArrived(WorkerWalker walker) => CurrentStage.Arrive(walker);
        public void ReportInside(WorkerWalker walker) => CurrentStage.Inside(walker);

        public IEnumerable<Worker> GetAssigned() => CurrentStage?.GetAssigned();
        public IEnumerable<Worker> GetQueued() => CurrentStage?.GetQueued();
        public IEnumerable<Worker> GetWorking() => CurrentStage?.GetWorking();

        #region Saving
        [Serializable]
        public class MonumentSiteData
        {
            public int CurrentStageIndex;
            public MonumentStep.MonumentStepData[] Steps;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new MonumentSiteData()
            {
                CurrentStageIndex = _currentStageIndex,
                Steps = CurrentStage?.Steps.Select(s => s.SaveData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<MonumentSiteData>(json);

            _currentStageIndex = data.CurrentStageIndex;

            for (int i = 0; i < _currentStageIndex; i++)
            {
                var stage = Stages.ElementAtOrDefault(i);

                if (stage?.Steps != null)
                {
                    foreach (var step in stage.Steps)
                    {
                        step.FinishObjects();
                    }
                }
            }

            if (data.Steps != null)
            {
                for (int i = 0; i < data.Steps.Length; i++)
                {
                    CurrentStage.Steps[i].LoadData(data.Steps[i]);
                }
            }
        }
        #endregion
    }
}