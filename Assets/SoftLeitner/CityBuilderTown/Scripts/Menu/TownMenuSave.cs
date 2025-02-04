using CityBuilderCore;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CityBuilderTown
{
    /// <summary>
    /// displays the created save games for the current mission and difficulty<br/>
    /// save games can be deleted or overriden with a new save, creating a new save is done with a seperate input and button
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_menu_save.html")]
    public class TownMenuSave : MonoBehaviour
    {
        [Tooltip("input for the name used for a new save")]
        public TMPro.TMP_InputField NameInput;
        [Tooltip("button that gets enabled when a valid name is entered")]
        public Button NewButton;
        [Tooltip("parent transform for the save games")]
        public Transform Content;
        [Tooltip("prefab used for the save games")]
        public TownSaveGame SaveGamePrefab;
        [Tooltip("button gets disabled when no save is selected")]
        public Button DeleteButton;
        [Tooltip("button gets disabled when no save is selected")]
        public Button OverrideButton;
        [Tooltip("used to check before overriding or deleting a save")]
        public MessageBoxDialog MessageBox;

        public UnityEvent Saved;

        public MissionParameters MissionParameters => Dependencies.Get<IMissionManager>().MissionParameters;

        private List<TownSaveGame> _saveGames;
        private TownSaveGame _selectedSaveGame;

        private void Start()
        {
            NameInput.onValueChanged.AddListener(nameChanged);
            nameChanged(NameInput.text);
        }

        private void OnEnable()
        {
            loadSaves();
        }

        private void OnDisable()
        {
            _saveGames.ForEach(b => Destroy(b.gameObject));
            _saveGames = null;
            _selectedSaveGame = null;
        }

        public void New()
        {
            Dependencies.Get<IGameSaver>().SaveNamed(NameInput.text);

            Saved?.Invoke();
        }

        public void Delete()
        {
            MessageBoxDialog.TryCheckYes(MessageBox, "Delete Save", "Are you sure you want to delete " + _selectedSaveGame.SaveName + "?", () =>
            {
                SaveHelper.Delete(SaveHelper.GetKey(MissionParameters?.Mission, MissionParameters?.Difficulty), _selectedSaveGame.SaveName);

                loadSaves();
            });
        }

        public void Override()
        {
            MessageBoxDialog.TryCheckYes(MessageBox, "Override Save", "Are you sure you want to override " + _selectedSaveGame.SaveName + "?", () =>
            {
                Dependencies.Get<IGameSaver>().SaveNamed(_selectedSaveGame.SaveName);

                Saved?.Invoke();
            });
        }

        private void nameChanged(string name)
        {
            NewButton.interactable = !string.IsNullOrWhiteSpace(name) && !_saveGames.Any(s => s.SaveName.Equals(name, System.StringComparison.OrdinalIgnoreCase));
        }

        private void loadSaves()
        {
            _selectedSaveGame = null;

            if (_saveGames == null)
            {
                _saveGames = new List<TownSaveGame>();
            }
            else
            {
                _saveGames.ForEach(b => Destroy(b.gameObject));
                _saveGames.Clear();
            }

            var parameters = MissionParameters;
            var saves = SaveHelper.GetSaves(SaveHelper.GetKey(parameters?.Mission, parameters?.Difficulty));

            if (saves != null)
            {
                foreach (var save in saves)
                {
                    if (string.IsNullOrWhiteSpace(save))
                        continue;//Can't manually save to Quick Slot

                    var saveGame = Instantiate(SaveGamePrefab, Content);

                    saveGame.Initialize(parameters?.Mission, parameters?.Difficulty, save);
                    saveGame.Button.onClick.AddListener(new UnityAction(() => select(saveGame)));

                    _saveGames.Add(saveGame);
                }
            }

            select(_saveGames.FirstOrDefault());
        }

        private void select(TownSaveGame saveGame)
        {
            _selectedSaveGame?.Deselect();
            _selectedSaveGame = saveGame;
            _selectedSaveGame?.Select();

            DeleteButton.interactable = _selectedSaveGame != null;
            OverrideButton.interactable = _selectedSaveGame != null;
        }
    }
}
