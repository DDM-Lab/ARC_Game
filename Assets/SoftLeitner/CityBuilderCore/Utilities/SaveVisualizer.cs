using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CityBuilderCore
{
    public class SaveVisualizer : MonoBehaviour
    {
        [Header("Filters")]
        [Tooltip("set to retrieve the current mission and difficulty from the mission manager")]
        public bool UseMissionManager;
        [Tooltip("set to only show saves from one specific mission")]
        public Mission Mission;
        [Tooltip("set to show saves for all missions in this set")]
        public MissionSet Missions;
        [Tooltip("set to only show saves made in a specific difficulty")]
        public Difficulty Difficulty;
        [Header("Items")]
        [Tooltip("prefab for save items")]
        public SaveVisualizerItem Prefab;
        [Tooltip("parent save items get instantiated under")]
        public Transform ItemParent;
        [Header("Buttons")]
        [Tooltip("saves when clicked, enabled only when an item is selected or the SaveNameInput has text")]
        public Button SaveButton;
        [Tooltip("loads when cliked, only enabled when an item is selected")]
        public Button LoadButton;
        [Tooltip("deletes the selected save when cliked, only enabled when an item is selected")]
        public Button DeleteButton;
        [Header("Other")]
        [Tooltip("input for setting the save name, when this field is set the visualizer assumes it is used for saving")]
        public TMPro.TMP_InputField SaveNameInput;
        [Tooltip("optional dialog shown before deleting or overriding save games")]
        public MessageBoxDialog ConfirmationDialog;
        [Tooltip("when set the fader is faded out before loading")]
        public Fader Fader;

        public bool HasSelected => _selectedItem;

        private List<SaveVisualizerItem> _items;
        private SaveVisualizerItem _selectedItem;

        private LazyDependency<IGameSaver> _saver = new LazyDependency<IGameSaver>();

        private void Start()
        {
            _items = new List<SaveVisualizerItem>();

            if (UseMissionManager && Dependencies.TryGet(out IMissionManager missionManager))
            {
                Mission = missionManager.MissionParameters.Mission;
                Difficulty = missionManager.MissionParameters.Difficulty;
            }

            if (SaveNameInput)
                SaveNameInput.onValueChanged.AddListener(new UnityAction<string>(nameChanged));

            if (SaveButton)
                SaveButton.onClick.AddListener(new UnityAction(Save));
            if (LoadButton)
                LoadButton.onClick.AddListener(new UnityAction(Load));
            if (DeleteButton)
                DeleteButton.onClick.AddListener(new UnityAction(Delete));

            Visualize();
        }

        private void OnEnable()
        {
            if (_items != null)
                Visualize();
        }

        public void SetMission(Mission mission)
        {
            Mission = mission;
            Visualize();
        }
        public void SetDifficulty(Difficulty difficulty)
        {
            Difficulty = difficulty;
            Visualize();
        }

        public void Visualize()
        {
            Deselect();

            _items.ForEach(s => Destroy(s.gameObject));
            _items.Clear();

            if (Mission)
            {
                visualizeMission(Mission);
            }
            else if (Missions)
            {
                foreach (var mission in Missions.Objects)
                {
                    visualizeMission(mission);
                }
            }
        }
        private void visualizeMission(Mission mission)
        {
            foreach (var save in mission.GetSaves(Difficulty))
            {
                if (string.IsNullOrEmpty(save) && SaveNameInput)
                    continue;//no quick saves in save dialog

                var item = Instantiate(Prefab, ItemParent);
                item.Initialize(this, mission, Difficulty, save);
                item.gameObject.SetActive(true);
                _items.Add(item);
            }
        }

        public void Deselect() => Select(null);
        public void Select(SaveVisualizerItem item)
        {
            if (_selectedItem != null && SaveNameInput != null && item == null)
                SaveNameInput.SetTextWithoutNotify(string.Empty);

            _selectedItem?.SetSelected(false);
            _selectedItem = item;
            _selectedItem?.SetSelected(true);

            if (_selectedItem != null && SaveNameInput)
                SaveNameInput.SetTextWithoutNotify(_selectedItem.SaveName);

            checkButtons();
        }

        public void Use()
        {
            if (SaveNameInput)
                Save();
            else
                Load();
        }

        public void Save()
        {
            if (ConfirmationDialog && _selectedItem != null)
                ConfirmationDialog.Check("Override", "You are about to override the savegame " + _selectedItem.SaveName, save);
            else
                save();
        }
        private void save()
        {
            _saver.Value.SaveNamed(SaveNameInput.text);
            this.Delay(() => !_saver.Value.IsSaving, Visualize);
        }

        public void Load()
        {
            if (_selectedItem == null)
                return;

            Fader.TryFadeOut(Fader, _selectedItem.Load);
        }

        public void Delete()
        {
            if (_selectedItem == null)
                return;

            if (ConfirmationDialog)
                ConfirmationDialog.Check("Deleting", "You are about to delete a savegame permanently!", delete);
            else
                delete();
        }
        private void delete()
        {
            _selectedItem?.Delete();
            this.Delay(2, Visualize);
        }

        private void nameChanged(string value)
        {
            _selectedItem?.SetSelected(false);
            _selectedItem = _items.FirstOrDefault(i => i.SaveName == value);
            _selectedItem?.SetSelected(true);

            checkButtons();
        }
        private void checkButtons()
        {
            if (SaveButton)
                SaveButton.interactable = _selectedItem != null || !string.IsNullOrWhiteSpace(SaveNameInput?.text);
            if (LoadButton)
                LoadButton.interactable = _selectedItem != null;
            if (DeleteButton)
                DeleteButton.interactable = _selectedItem != null;
        }
    }
}
