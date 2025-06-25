using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace CityBuilderCore
{
    /// <summary>
    /// displays mission info in unity ui and provides methods for starting/continuing it
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_mission_visualizer.html")]
    public class MissionVisualizer : MonoBehaviour
    {
        [Tooltip("mission that gets visualized")]
        public Mission Mission;
        [Tooltip("object gets activated if the mission has been finished")]
        public GameObject FinishedObject;
        [Tooltip("object gets activated if the mission has been finished in this playthrough")]
        public GameObject FinishedCurrentlyObject;
        [Tooltip("objects gets activated if when a mission is set so it can be started")]
        public GameObject StartObject;
        [Tooltip("objects gets activated if the mission has a savegame and can be continued")]
        public GameObject ContinueObject;

        [Tooltip("when set displays the missions name")]
        public TMPro.TMP_Text NameText;
        [Tooltip("when set displays the missions description")]
        public TMPro.TMP_Text DescriptionText;
        [Tooltip("when set displays the missions win condition")]
        public TMPro.TMP_Text WinConditionsText;

        [Tooltip("optional fader that is used before moving to a different scene(start/continue/load)")]
        public Fader Fader;

        public UnityEvent<Mission> MissionChanged;

        private void Start()
        {
            UpdateVisuals();
        }

        private void Update()
        {
            updateWinConditions();
        }

        public void SetMission(Mission mission)
        {
            Mission = mission;
            UpdateVisuals();
            MissionChanged?.Invoke(Mission);
        }
        public void UnSetMission() => SetMission(null);

        public void UpdateVisuals()
        {
            if (FinishedObject)
                FinishedObject.SetActive(Mission?.GetFinished() ?? false);
            if (FinishedCurrentlyObject)
                FinishedCurrentlyObject.SetActive(Dependencies.GetOptional<IMissionManager>()?.IsFinished ?? Mission?.GetFinished() ?? false);

            if (StartObject)
                StartObject.SetActive(Mission);
            if (ContinueObject)
                ContinueObject.SetActive(Mission?.GetStarted() ?? false);

            if (NameText)
                NameText.text = Mission?.Name ?? string.Empty;
            if (DescriptionText)
                DescriptionText.text = Mission?.Description ?? string.Empty;

            updateWinConditions();
        }

        public void StartMission()
        {
            Fader.TryFadeOut(Fader, () => SceneManager.LoadSceneAsync(Mission.SceneName).completed += o =>
            {
                Dependencies.Get<IMissionManager>().SetMissionParameters(new MissionParameters()
                {
                    Mission = Mission
                });
            });
        }

        public void ContinueMission()
        {
            Fader.TryFadeOut(Fader, () => SceneManager.LoadSceneAsync(Mission.SceneName).completed += o =>
            {
                Dependencies.Get<IMissionManager>().SetMissionParameters(new MissionParameters()
                {
                    Mission = Mission,
                    IsContinue = true
                });
            });
        }

        public void LoadScene(string scene)
        {
            Fader.TryFadeOut(Fader, () => SceneManager.LoadSceneAsync(scene));
        }

        private void updateWinConditions()
        {
            if (!WinConditionsText)
                return;

            WinConditionsText.text = Mission?.GetWinConditionText(Dependencies.Get<IScoresCalculator>()) ?? string.Empty;
        }
    }
}