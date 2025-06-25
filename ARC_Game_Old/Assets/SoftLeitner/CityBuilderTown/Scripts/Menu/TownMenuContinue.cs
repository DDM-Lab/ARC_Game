using CityBuilderCore;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityBuilderTown
{
    /// <summary>
    /// used to continue the last saved game, disabled itself if there is no game to continue
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_menu_continue.html")]
    public class TownMenuContinue : MonoBehaviour
    {
        [Tooltip("used to fade out the screen before loading the game scene")]
        public Fader Fader;

        private void Start()
        {
            gameObject.SetActive(SaveHelper.GetContinue()?.CheckSave() ?? false);
        }

        public void Continue()
        {
            Fader.TryFadeOut(Fader, () =>
            {
                var data = SaveHelper.GetContinue();
                var mission = data.GetMission();
                var difficulty = data.GetDifficulty();

                SceneManager.LoadSceneAsync(mission.SceneName).completed += o =>
                {
                    Dependencies.Get<IMissionManager>().SetMissionParameters(new MissionParameters()
                    {
                        Mission = mission,
                        Difficulty = difficulty,
                        IsContinue = true,
                        ContinueName = data.Name
                    });
                };
            });
        }
    }
}
