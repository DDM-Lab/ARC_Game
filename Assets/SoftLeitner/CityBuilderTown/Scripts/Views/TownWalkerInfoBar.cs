using CityBuilderCore;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// visualizes a walkers job and name
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_walker_info_bar.html")]
    public class TownWalkerInfoBar : WalkerValueBar
    {
        public SpriteRenderer JobIconRenderer;
        public TMPro.TMP_Text NameText;

        private IMainCamera _mainCamera;

        private void Start()
        {
            _mainCamera = Dependencies.Get<IMainCamera>();

            setBar();
        }

        private void Update()
        {
            setBar();
        }

        private void setBar()
        {
            transform.forward = _mainCamera.Camera.transform.forward;

            if (!(_walker is TownWalker townWalker))
                return;

            JobIconRenderer.sprite = townWalker.Job?.Icon;
            NameText.text = $"{townWalker.Identity.FullName} ({townWalker.VisualAge})";
        }
    }
}