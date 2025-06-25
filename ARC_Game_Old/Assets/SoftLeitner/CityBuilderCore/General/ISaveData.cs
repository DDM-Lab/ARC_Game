namespace CityBuilderCore
{
    /// <summary>
    /// interface for all kinds of components that contain state that needs saving
    /// </summary>
    public interface ISaveData
    {
        /// <summary>
        /// serializes the objects state(usually to json) and returns it
        /// </summary>
        /// <returns>serialized save data</returns>
        string SaveData();
        /// <summary>
        /// deserializes the serialized json data and loads the data as its new state
        /// </summary>
        /// <param name="json"></param>
        void LoadData(string json);
    }
}