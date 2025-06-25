using CityBuilderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CityBuilderTown
{
    /// <summary>
    /// visualizes the state of a town building in the UI
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_building_dialog.html")]
    public class TownBuildingDialog : MonoBehaviour
    {
        [Tooltip("can be switched to by clicking one of the walkers buttons")]
        public TownWalkerDialog WalkerDialog;
        [Tooltip("displays the buildings name")]
        public TMPro.TMP_Text Name;
        [Tooltip("displays the buildings description")]
        public TMPro.TMP_Text Description;
        [Tooltip("toggle that suspends the building when unchecked")]
        public Toggle Toggle;
        [Tooltip("shows the total quantity and capacity when the building has a StorageComponent or stack count when stacked")]
        public StorageCapacityPanel StorageCapacityPanel;
        [Tooltip("displays the buildings items")]
        public ItemsPanel ItemsPanel;
        [Tooltip("parent for the walker panels that are instantiated for each inhabitant of a TownHomeComponent")]
        public Transform WalkerParent;
        [Tooltip("template for the walker panels that are instantiated for each inhabitant of a TownHomeComponent")]
        public TooltipArea WalkerTemplate;
        [Tooltip("addon added to selected building to visualize the selection")]
        public BuildingAddon SelectionAddon;
        [Tooltip("fired whenever the current walker of the dialog is changed")]
        public UnityEvent<BuildingReference> BuildingChanged;

        private CoroutineToken _followToken;
        private BuildingReference _currentBuilding;
        private List<TooltipArea> _walkerPanels = new List<TooltipArea>();

        private void Start()
        {
            Toggle.onValueChanged.AddListener(new UnityAction<bool>(buildingToggleChanged));

            Hide();
        }

        private void Update()
        {
            if (_followToken?.IsActive == false)
            {
                Hide();
                _followToken = null;
                return;
            }

            if (!_currentBuilding.HasInstance)
            {
                Hide();
                return;
            }

            var construction = _currentBuilding.Instance.GetBuildingComponent<TownConstructionComponent>();
            if (construction == null)
            {
                var itemOwner = _currentBuilding.Instance.GetBuildingParts<IItemOwner>().FirstOrDefault();
                if (itemOwner == null)
                    ItemsPanel.Clear();
                else
                    ItemsPanel.SetItems(itemOwner.ItemContainer);
            }
            else
            {
                ItemsPanel.SetItems(construction.GetReceiveLevels().ToList());
            }

            if (StorageCapacityPanel)
                StorageCapacityPanel.Set(_currentBuilding.Instance.GetBuildingComponent<StorageComponent>()?.Storage);

            var description = _currentBuilding.Instance.GetDescription();
            var home = _currentBuilding.Instance.GetBuildingComponent<TownHomeComponent>();
            if (home)
                description += $"({home.Inhabitants.Count}/{home.WalkerCapacity})";
            Description.text = description;

            for (int i = 0; i < Math.Max(_walkerPanels.Count, home?.Inhabitants.Count ?? 0); i++)
            {
                var panel = _walkerPanels.ElementAtOrDefault(i);
                var walker = home?.Inhabitants.ElementAtOrDefault(i);

                if (walker == null)
                {
                    _walkerPanels.Remove(panel);
                    Destroy(panel.gameObject);
                    continue;
                }

                if (panel == null)
                {
                    int index = i;

                    panel = Instantiate(WalkerTemplate, WalkerParent);
                    panel.gameObject.SetActive(true);
                    panel.GetComponent<Button>().onClick.AddListener(new UnityAction(() => ShowWalker(index)));
                    _walkerPanels.Add(panel);
                }

                panel.Name = walker.Identity.FullName;
                panel.Description = walker.GetActivityText();
            }
        }

        public void Show(object target) => Show(target as BuildingReference);
        public void Show(BuildingReference building)
        {
            if (_currentBuilding == building)
                return;

            gameObject.SetActive(true);

            if (SelectionAddon && _currentBuilding?.HasInstance == true)
                _currentBuilding.Instance.RemoveAddon(SelectionAddon.Key);
            _currentBuilding = building;
            if (SelectionAddon)
                _currentBuilding.Instance.AddAddon(SelectionAddon);

            Name.text = building.Instance.GetName();

            Toggle.SetIsOnWithoutNotify(!building.Instance.IsSuspended);

            _followToken = Dependencies.GetOptional<IMainCamera>()?.Follow(building.Instance.Pivot);

            BuildingChanged?.Invoke(_currentBuilding);
        }

        public void Hide()
        {
            if (SelectionAddon && _currentBuilding?.HasInstance == true)
                _currentBuilding.Instance.RemoveAddon(SelectionAddon.Key);

            _currentBuilding = null;

            _followToken?.Stop();
            _followToken = null;

            gameObject.SetActive(false);

            BuildingChanged?.Invoke(_currentBuilding);
        }

        public void ShowWalker(int index)
        {
            var home = _currentBuilding.Instance.GetBuildingComponent<TownHomeComponent>();

            Hide();
            WalkerDialog.Show(home.Inhabitants.ElementAtOrDefault(index));
        }

        private void buildingToggleChanged(bool value)
        {
            if (value)
                _currentBuilding.Instance.Resume();
            else
                _currentBuilding.Instance.Suspend();
        }
    }
}
