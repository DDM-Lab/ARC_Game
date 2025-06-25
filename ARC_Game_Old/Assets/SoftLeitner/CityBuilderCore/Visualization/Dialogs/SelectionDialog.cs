using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// dialog that displays information about the selected building
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_selection_dialog.html")]
    public class SelectionDialog : DialogBase
    {
        public TMPro.TMP_Text TitleText;
        public UnityEngine.UI.Toggle BuildingToggle;
        public UnityEngine.UI.Button MoveButton;
        [Header("Description")]
        public GameObject DescriptionPanel;
        public TMPro.TMP_Text DescriptionText;
        [Header("Evolution")]
        public GameObject EvolutionPanel;
        public TMPro.TMP_Text EvolutionText;
        public ItemsPanel EvolutionItems;
        [Header("Employment")]
        public GameObject EmploymentPanel;
        public TMPro.TMP_Text EmploymentText;
        [Header("Housing")]
        public GameObject HousingPanel;
        public TMPro.TMP_Text HousingText;
        [Header("Production")]
        public GameObject ProductionPanel;
        public RectTransform ProductionBar;
        public ItemsPanel ProductionConsumerItems;
        public ItemsPanel ProductionProducerItems;
        private float _productionBarWidth;
        [Header("Storage")]
        public GameObject StoragePanel;
        public StorageOrdersPanel StorageOrders;
        [Header("Distribution")]
        public GameObject DistributionPanel;
        public DistributionOrdersPanel DistributionOrders;
        [Header("Working")]
        public GameObject WorkerPanel;
        public WorkersPanel WorkingWorkers;
        public WorkersPanel QueuedWorkers;
        public WorkersPanel AssignedWorkers;
        [Header("Walker Storage")]
        public GameObject WalkerStoragePanel;
        public ItemsPanel WalkerStorageItems;
        [Header("Item Efficiency")]
        public GameObject ItemEfficiencyPanel;
        public ItemsPanel EfficiencyItems;
        [Header("Road Blocker")]
        public GameObject RoadBlockerPanel;
        public RoadBlockerPanel RoadBlocker;
        [Header("Addons")]
        public BuildingAddon BuildingAddon;
        public WalkerAddon WalkerAddon;
        [Header("Events")]
        public UnityEvent<object> TargetChanged;

        private object _currentTarget;
        public object CurrentTarget
        {
            get
            {
                return _currentTarget;
            }
            private set
            {
                if (_currentTarget == value)
                    return;
                setAddon(_currentTarget, value);
                _currentTarget = value;
                TargetChanged?.Invoke(_currentTarget);
            }
        }

        protected override void Awake()
        {
            base.Awake();

            _productionBarWidth = ProductionBar.sizeDelta.x;
        }

        protected override void Start()
        {
            base.Start();

            BuildingToggle.onValueChanged.AddListener(new UnityAction<bool>(buildingToggleChanged));
            MoveButton.onClick.AddListener(new UnityAction(Move));
        }

        public void Activate(object target)
        {
            CurrentTarget = target;

            if (CurrentTarget is Walker walker)
                Dependencies.GetOptional<IMainCamera>()?.Follow(walker.Pivot);
            else if (CurrentTarget is IBuilding b)
                Dependencies.GetOptional<IMainCamera>()?.Jump(b.WorldCenter);
            else if (CurrentTarget is BuildingReference buildingReference && buildingReference.HasInstance)
                Dependencies.GetOptional<IMainCamera>()?.Jump(buildingReference.Instance.WorldCenter);

            if (target is BuildingReference reference)
                target = reference.Instance;

            if (target is IBuilding building)
            {
                BuildingToggle.gameObject.SetActive(true);
                BuildingToggle.SetIsOnWithoutNotify(!building.IsSuspended);

                MoveButton.gameObject.SetActive(true);
            }
            else
            {
                BuildingToggle.gameObject.SetActive(false);

                MoveButton.gameObject.SetActive(false);
            }

            base.Activate();
        }

        public override void Deactivate()
        {
            base.Deactivate();

            CurrentTarget = null;
        }

        protected override void updateContent(bool initiate)
        {
            base.updateContent(initiate);

            var target = CurrentTarget;

            if (target is Object unityObject && !unityObject)
            {
                Deactivate();
                return;
            }

            if (target is BuildingReference reference)
                target = reference.Instance;

            if (target is IBuilding building)
            {
                SetTitle(building.GetName());
                SetDescrition(building.GetDescription());

                SetEvolution(building.GetBuildingComponent<IEvolution>());
                SetEmployment(building.GetBuildingComponent<IEmployment>());
                SetHousing(building.GetBuildingComponent<IHousing>());
                SetProduction(building.GetBuildingComponent<IProductionComponent>());
                SetStorage(building.GetBuildingComponent<IStorageComponent>(), initiate);
                SetDistribution(building.GetBuildingComponent<IDistributionComponent>(), initiate);
                SetWorkerUser(building.GetBuildingComponent<IWorkerUser>());
                SetItemEfficiency(building.GetBuildingComponent<ItemEfficiencyComponent>());
                SetRoadBlocker(building.GetBuildingComponent<RoadBlockerComponent>(), initiate);

                SetWalkerStorage(null);
            }
            else if (target is Walker walker)
            {
                SetTitle(walker.GetName());
                SetDescrition(walker.GetDescription());

                SetEvolution(null);
                SetEmployment(null);
                SetHousing(null);
                SetProduction(null);
                SetStorage(null, false);
                SetDistribution(null, false);
                SetWorkerUser(null);
                SetItemEfficiency(null);
                SetRoadBlocker(null, false);

                SetWalkerStorage(walker.ItemStorage);
            }
        }

        public void SetTitle(string title)
        {
            TitleText.text = title;
        }

        public void SetDescrition(string description)
        {
            DescriptionPanel.SetActive(true);
            DescriptionText.text = description;
        }

        public void SetEvolution(IEvolution evolution)
        {
            if (evolution == null)
            {
                EvolutionPanel.SetActive(false);
            }
            else
            {
                EvolutionPanel.SetActive(true);
                EvolutionText.text = evolution.GetDescription();
                EvolutionItems.SetItems(evolution.ItemContainer);
            }
        }

        public void SetEmployment(IEmployment employment)
        {
            if (employment == null)
            {
                EmploymentPanel.SetActive(false);
            }
            else
            {
                EmploymentPanel.SetActive(true);
                EmploymentText.text = employment.GetDescription();
            }
        }

        public void SetHousing(IHousing housing)
        {
            if (housing == null)
            {
                HousingPanel.SetActive(false);
            }
            else
            {
                HousingPanel.SetActive(true);
                HousingText.text = housing.GetDescription();
            }
        }

        public void SetProduction(IProductionComponent production)
        {
            if (production == null)
            {
                ProductionPanel.SetActive(false);
            }
            else
            {
                ProductionPanel.SetActive(true);

                ProductionBar.sizeDelta = new Vector2(_productionBarWidth * production.Progress, ProductionBar.sizeDelta.y);
                ProductionConsumerItems.SetItems(production.GetNeededItems().ToList());
                ProductionProducerItems.SetItems(production.GetProducedItems().ToList());
            }
        }

        public void SetStorage(IStorageComponent storage, bool initiate)
        {
            if (storage == null)
            {
                StoragePanel.SetActive(false);
            }
            else
            {
                StoragePanel.SetActive(true);
                StorageOrders.SetOrders(storage, initiate);
            }
        }

        public void SetDistribution(IDistributionComponent distribution, bool initiate)
        {
            if (distribution == null)
            {
                DistributionPanel.SetActive(false);
            }
            else
            {
                DistributionPanel.SetActive(true);
                DistributionOrders.SetOrders(distribution, initiate);
            }
        }

        public void SetWorkerUser(IWorkerUser workerUser)
        {
            if (workerUser == null)
            {
                WorkerPanel.SetActive(false);
            }
            else
            {
                WorkerPanel.SetActive(true);
                WorkingWorkers.SetWorkers(workerUser.GetWorking());
                QueuedWorkers.SetWorkers(workerUser.GetQueued());
                AssignedWorkers.SetWorkers(workerUser.GetAssigned());
            }
        }

        public void SetWalkerStorage(ItemStorage storage)
        {
            if (storage == null)
            {
                WalkerStoragePanel.SetActive(false);
            }
            else
            {
                WalkerStoragePanel.SetActive(true);
                WalkerStorageItems.SetItems(storage);
            }
        }

        public void SetItemEfficiency(ItemEfficiencyComponent itemEfficiencyComponent)
        {
            if (itemEfficiencyComponent == null)
            {
                ItemEfficiencyPanel.SetActive(false);
            }
            else
            {
                ItemEfficiencyPanel.SetActive(true);
                EfficiencyItems.SetItems(itemEfficiencyComponent.GetItems().ToList());
            }
        }

        public void SetRoadBlocker(RoadBlockerComponent roadBlockerComponent, bool initiate)
        {
            if (roadBlockerComponent == null || !roadBlockerComponent.IsTagged)
            {
                RoadBlockerPanel.SetActive(false);
            }
            else
            {
                RoadBlockerPanel.SetActive(true);
                if (initiate)
                    RoadBlocker.SetBlocker(roadBlockerComponent);
            }
        }

        public void Move()
        {
            var target = _currentTarget;
            if (target is BuildingReference reference)
                target = reference.Instance;

            if (target is IBuilding building)
            {
                Deactivate();

                var moveTool = Dependencies.Get<MoveTool>();
                Dependencies.Get<IToolsManager>().ActivateTool(moveTool);
                moveTool.MoveOnce(building);
            }
        }

        private void buildingToggleChanged(bool value)
        {
            if (CurrentTarget is BuildingReference buildingReference)
            {
                if (value)
                    buildingReference.Instance.Resume();
                else
                    buildingReference.Instance.Suspend();
            }
        }

        private void setAddon(object oldTarget, object newTarget)
        {
            if (BuildingAddon == null && WalkerAddon == null)
                return;

            if (oldTarget == newTarget)
                return;
            {
                if (oldTarget is Building building)
                {
                    if (BuildingAddon)
                        building.RemoveAddon(BuildingAddon.Key);
                }
                else if (oldTarget is BuildingReference buildingReference && buildingReference.HasInstance)
                {
                    if (BuildingAddon)
                        buildingReference.Instance.RemoveAddon(BuildingAddon.Key);
                }
                else if (oldTarget is Walker walker)
                {
                    if (WalkerAddon)
                        walker.RemoveAddon(WalkerAddon.Key);
                }
            }

            {
                if (newTarget is Building building)
                {
                    if (BuildingAddon)
                        building.AddAddon(BuildingAddon);
                }
                else if (newTarget is BuildingReference buildingReference && buildingReference.HasInstance)
                {
                    if (BuildingAddon)
                        buildingReference.Instance.AddAddon(BuildingAddon);
                }
                else if (newTarget is Walker walker)
                {
                    if (WalkerAddon)
                        walker.AddAddon(WalkerAddon);
                }
            }
        }
    }
}