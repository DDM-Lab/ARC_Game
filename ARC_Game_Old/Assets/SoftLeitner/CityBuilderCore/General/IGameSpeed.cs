namespace CityBuilderCore
{
    public interface IGameSpeed
    {
        /// <summary>
        /// time passed since start of the mission, carries over saves
        /// </summary>
        float Playtime { get; }
        /// <summary>
        /// whether the game is currently paused
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// pauses game, continue with <see cref="Resume"/>
        /// </summary>
        void Pause();
        /// <summary>
        /// resumes game after pause has been called
        /// </summary>
        void Resume();
        /// <summary>
        /// set the current gamespeed
        /// </summary>
        /// <param name="speed"></param>
        void SetSpeed(float speed);
    }
}