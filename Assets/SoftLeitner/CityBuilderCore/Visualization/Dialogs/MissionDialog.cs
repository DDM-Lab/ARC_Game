using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityBuilderCore
{
    /// <summary>
    /// dialog for mission stuff<br/>
    /// just a wrapper for <see cref="MissionVisualizer"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_mission_dialog.html")]
    public class MissionDialog : DialogBase
    {
        [Tooltip("the visualizer that actually displays the mission parameters, the dialog makes sure it gets the current mission")]
        public MissionVisualizer MissionVisualizer;
        [Tooltip("scene that will be loaded if exit is clicked")]
        public string ExitSceneName;
        [Tooltip("optional fader used when exiting")]
        public Fader Fader;
        [Tooltip("optional dialog shown before exiting")]
        public MessageBoxDialog ConfirmationDialog;

        public override void Activate()
        {
            base.Activate();

            MissionVisualizer.Mission = Dependencies.Get<IMissionManager>().MissionParameters.Mission;
            MissionVisualizer.UpdateVisuals();
        }

        public void ExitMission()
        {
            MessageBoxDialog.TryCheck(ConfirmationDialog, "Exiting", "You are about to Exit the game, unsaved progress will be lost!", 
                () => Fader.TryFadeOut(Fader, 
                () => SceneManager.LoadSceneAsync(ExitSceneName)));
        }
    }
}