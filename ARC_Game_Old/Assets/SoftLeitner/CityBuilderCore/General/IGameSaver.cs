namespace CityBuilderCore
{
    /// <summary>
    /// interface for the class that handles the games saving and loading
    /// </summary>
    public interface IGameSaver
    {
        /// <summary>
        /// check if the game is currently saving
        /// </summary>
        bool IsSaving { get; }
        /// <summary>
        /// check if the game is currently loading, used to suppress certain checks and initializations during loading
        /// </summary>
        bool IsLoading { get; }

        /// <summary>
        /// saves to the quick slot
        /// </summary>
        void Save();
        /// <summary>
        /// saves the game under a specified name
        /// </summary>
        /// <param name="name">the name for the save that can later be used when loading</param>
        void SaveNamed(string name);
        /// <summary>
        /// loads from the quick slot
        /// </summary>
        void Load();
        /// <summary>
        /// loads from a specified name
        /// </summary>
        /// <param name="name">name that has been previously saved to</param>
        void LoadNamed(string name);
    }
}