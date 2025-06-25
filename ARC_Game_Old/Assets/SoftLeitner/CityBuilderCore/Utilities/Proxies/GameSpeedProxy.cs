using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// behaviour that makes game speed controls accessible to unity events<br/>
    /// unity events could also be pointed directly to the game manager but if that is in a different prefab that can get annoying
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_game_speed_proxy.html")]
    public class GameSpeedProxy : MonoBehaviour, IGameSpeed
    {
        public float Playtime => Dependencies.Get<IGameSpeed>().Playtime;
        public bool IsPaused => Dependencies.Get<IGameSpeed>().IsPaused;

        public void Pause() => Dependencies.Get<IGameSpeed>().Pause();
        public void Resume() => Dependencies.Get<IGameSpeed>().Resume();
        public void SetSpeed(float speed) => Dependencies.Get<IGameSpeed>().SetSpeed(speed);
    }
}
