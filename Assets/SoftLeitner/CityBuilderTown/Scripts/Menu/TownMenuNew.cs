using CityBuilderCore;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityBuilderTown
{
    /// <summary>
    /// manages the options for new games and also starts a new game and passes the options into the mission parameters
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_menu_new.html")]
    public class TownMenuNew : MonoBehaviour
    {
        [Tooltip("dropdown that is used for the mission, options and events are hooked up by the behaviour")]
        public TMPro.TMP_Dropdown MissionDropdown;
        [Tooltip("dropdown that is used for the difficulty, options and events are hooked up by the behaviour")]
        public TMPro.TMP_Dropdown DifficultyDropdown;
        [Tooltip("input field used for the seed value, gets initialized with the current time milliseconds when the dialog is enabled")]
        public TMPro.TMP_InputField SeedInput;
        [Tooltip("used to fade out the screen before loading the game scene")]
        public Fader Fader;

        private Mission _mission;
        private Difficulty _difficulty;

        private void Start()
        {
            var missions = Dependencies.Get<IKeyedSet<Mission>>();

            _mission = missions.Objects[0];

            MissionDropdown.ClearOptions();
            MissionDropdown.AddOptions(missions.Objects.Select(o => new TMPro.TMP_Dropdown.OptionData(o.Name)).ToList());
            MissionDropdown.onValueChanged.AddListener(i => _mission = missions.Objects[i]);

            var difficulties = Dependencies.Get<IKeyedSet<Difficulty>>();

            _difficulty = difficulties.Objects[0];

            DifficultyDropdown.ClearOptions();
            DifficultyDropdown.AddOptions(difficulties.Objects.Select(o => new TMPro.TMP_Dropdown.OptionData(o.Name)).ToList());
            DifficultyDropdown.onValueChanged.AddListener(i => _difficulty = difficulties.Objects[i]);
        }

        private void OnEnable()
        {
            SeedInput.text = System.DateTime.Now.Millisecond.ToString();
        }

        public void StartGame()
        {
            Fader.TryFadeOut(Fader, () => SceneManager.LoadSceneAsync(_mission.SceneName).completed += o =>
            {
                TownManager.Instance.Fader.DelayedFadeIn();
                Dependencies.Get<IMissionManager>().SetMissionParameters(new MissionParameters()
                {
                    Mission = _mission,
                    Difficulty = _difficulty,
                    RandomSeed = int.Parse(SeedInput.text)
                });
            });
        }
    }
}
