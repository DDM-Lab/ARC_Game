using CityBuilderCore;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// visualizes what the walker is currently doing using an icon<br/>
    /// tasks are visualized using <see cref="TownTask.Icon"/>, other processes use icons found in <see cref="TownManager"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_walker_action_bar.html")]
    public class TownWalkerActionBar : WalkerValueBar
    {
        public SpriteRenderer SpriteRenderer;
        [Header("Icons")]
        [Tooltip("used when walking home")]
        public Sprite Home;
        [Tooltip("used when chilling or idly wandering")]
        public Sprite Idle;
        [Tooltip("used when delivering items or supplying home")]
        public Sprite Item;

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

            if (townWalker.CurrentTask == null)
            {
                if (townWalker.CurrentProcess?.Key == null)
                {
                    SpriteRenderer.sprite = null;
                }
                else
                {
                    switch (townWalker.CurrentProcess.Key)
                    {
                        case TownWalker.HOME:
                            SpriteRenderer.sprite = Home;
                            break;
                        case TownWalker.CHILL:
                            SpriteRenderer.sprite = Idle;
                            break;
                        case TownWalker.WANDER:
                            SpriteRenderer.sprite = Idle;
                            break;
                        case TownWalker.DELIVER:
                            SpriteRenderer.sprite = Item;
                            break;
                        case TownWalker.PROVISION:
                            SpriteRenderer.sprite = Item;
                            break;
                        default:
                            SpriteRenderer.sprite = null;
                            break;
                    }
                }
            }
            else
            {
                SpriteRenderer.sprite = townWalker.CurrentTask.Icon;
            }
        }
    }
}