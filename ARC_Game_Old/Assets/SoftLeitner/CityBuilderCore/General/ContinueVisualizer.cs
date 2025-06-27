using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityBuilderCore
{
    /// <summary>
    /// checks whether the game can be continued<br/>
    /// mission and difficulty can be shown in a text<br/>
    /// whether continuing is possible is fired as an event
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_continue_visualizer.html")]
    public class ContinueVisualizer : MonoBehaviour
    {
        [Tooltip("optional, can be used to show the mission and difficulty when continuing is possible")]
        public TMPro.TMP_Text Text;
        [Tooltip("when set the fader is faded out before continuing")]
        public Fader Fader;
        [Tooltip("fires on start, parameter is whether continuing is possible, use to enable/disable buttons or show/hide ui elements")]
        public BoolEvent Checked;

        private SaveHelper.ContinueData _continueData;

        private void Start()
        {
            Checked?.Invoke(check());
        }

        public void Continue()
        {
            if (check())
            {
                Fader.TryFadeOut(Fader, () =>
                {
                    var mission = _continueData.GetMission();
                    var difficulty = _continueData.GetDifficulty();
                    var name = _continueData.Name;

                    SceneManager.LoadSceneAsync(_continueData.GetMission().SceneName).completed += o =>
                    {
                        Dependencies.Get<IMissionManager>().SetMissionParameters(new MissionParameters() { Mission = mission, Difficulty = difficulty, IsContinue = true, ContinueName = name });
                    };
                });
            }
            else
            {
                Checked?.Invoke(false);//continue no longer valid, something changed after Start
            }
        }

        public void Exit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private bool check()
        {
            var data = SaveHelper.GetContinue();
            if (data == null || !data.CheckSave())
            {
                _continueData = null;

                if (Text)
                    Text.text = string.Empty;

                return false;
            }
            else
            {
                _continueData = data;

                var mission = _continueData.GetMission();
                var difficulty = _continueData.GetDifficulty();
                if (Text)
                    Text.text = mission.Name + (difficulty == null ? string.Empty : difficulty.Name);

                return true;
            }
        }
    }
}