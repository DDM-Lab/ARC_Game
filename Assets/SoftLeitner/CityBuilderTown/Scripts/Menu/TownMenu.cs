using CityBuilderCore;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityBuilderTown
{
    /// <summary>
    /// visualy fades between the different menu panels and provides logic for exiting and quitting the game
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_menu.html")]
    public class TownMenu : MonoBehaviour
    {
        [Tooltip("canvas group that contains the main menu buttons that open the other panels")]
        public CanvasGroup Buttons;
        [Tooltip("canvas group that contains the new game dialog")]
        public CanvasGroup New;
        [Tooltip("canvas group that contains the load game dialog")]
        public CanvasGroup Load;
        [Tooltip("canvas group that contains the save game dialog")]
        public CanvasGroup Save;
        [Tooltip("canvas group that contains the options dialog")]
        public CanvasGroup Options;
        [Tooltip("used to check if the player accidentally clicked exit")]
        public MessageBoxDialog MessageBox;
        [Tooltip("used to fade out the screen when exiting")]
        public Fader Fader;

        private CanvasGroup _currentGroup;

        private void Start()
        {
            _currentGroup = Buttons;
        }

        private void OnDisable()
        {
            if (_currentGroup != Buttons)
            {
                _currentGroup.alpha = 0f;
                _currentGroup.gameObject.SetActive(false);
                _currentGroup = Buttons;
                _currentGroup.alpha = 1f;
                _currentGroup.gameObject.SetActive(true);
            }
        }

        public void ShowButtons() => ShowSubMenu(Buttons);
        public void ShowNew() => ShowSubMenu(New);
        public void ShowLoad() => ShowSubMenu(Load);
        public void ShowSave() => ShowSubMenu(Save);
        public void ShowOptions() => ShowSubMenu(Options);
        public void ShowSubMenu(CanvasGroup canvasGroup)
        {
            StopAllCoroutines();
            StartCoroutine(fade(_currentGroup, canvasGroup));

            _currentGroup = canvasGroup;
        }

        public void ExitToTitle()
        {
            MessageBoxDialog.TryCheckYes(MessageBox, "Exit to Title", "Unsaved progress may be lost! Are you sure you want to exit the game?", 
                () => Fader.TryFadeOut(Fader, 
                () => SceneManager.LoadScene("TownTitle")));
        }

        public void Quit()
        {
            Fader.TryFadeOut(Fader, () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
            });
        }

        public void LoadDebug()
        {
            SceneManager.LoadScene("TownDebugFilled");
        }

        private IEnumerator fade(CanvasGroup from, CanvasGroup to, float duration = 0.2f)
        {
            to.gameObject.SetActive(true);

            var time = 0f;

            while (time < duration)
            {
                var value = time / duration;

                to.alpha = value;
                from.alpha = 1 - value;

                time += Time.unscaledDeltaTime;
                yield return null;
            }

            from.alpha = 0f;
            to.alpha = 1f;

            from.gameObject.SetActive(false);
        }
    }
}
