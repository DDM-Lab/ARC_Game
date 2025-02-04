using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// behaviour that makes save/load accessible to unity events<br/>
    /// unity events could also be pointed directly to the game manager but if that is in a different prefab that can get annoying
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_game_saver_proxy.html")]
    public class GameSaverProxy : MonoBehaviour, IGameSaver
    {
        /// <summary>
        /// fired true at the start of saving and false at the end, might be used to display save messages
        /// </summary>
        public BoolEvent IsSavingChanged;
        /// <summary>
        /// fired true at the start of loading and false at the end, might be used to display loading messages
        /// </summary>
        public BoolEvent IsLoadingChanged;

        public bool IsSaving => Dependencies.Get<IGameSaver>().IsSaving;
        public bool IsLoading => Dependencies.Get<IGameSaver>().IsLoading;

        private void Start()
        {
            var saver = Dependencies.Get<IGameSaver>();

            if (saver is DefaultGameManager manager)
            {
                manager.IsSavingChanged.AddListener(new UnityEngine.Events.UnityAction<bool>(v => IsSavingChanged?.Invoke(v)));
                manager.IsLoadingChanged.AddListener(new UnityEngine.Events.UnityAction<bool>(v => IsLoadingChanged?.Invoke(v)));
            }
        }

        public void Save() => Dependencies.Get<IGameSaver>().Save();
        public void SaveNamed(string name) => Dependencies.Get<IGameSaver>().SaveNamed(name);
        public void Load() => Dependencies.Get<IGameSaver>().Load();
        public void LoadNamed(string name) => Dependencies.Get<IGameSaver>().LoadNamed(name);
    }
}
