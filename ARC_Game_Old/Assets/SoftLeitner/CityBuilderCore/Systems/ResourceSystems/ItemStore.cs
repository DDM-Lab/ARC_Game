using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// behaviour that does nothing except store items<br/>
    /// can be used in ItemStorages with <see cref="ItemStorageMode.Store"/> to combine storages<br/>
    /// for example this can be used to combine the storage across different components of the same building
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_item_store.html")]
    public class ItemStore : MonoBehaviour, IItemOwner, ISaveData
    {
        [Tooltip("stores items, can be referenced in other item storages with ItemStorageMode.Store to shift and combine storages")]
        public ItemStorage Storage;

        public IItemContainer ItemContainer => Storage;

        #region Saving
        public string SaveData() => JsonUtility.ToJson(Storage.SaveData());
        public void LoadData(string json) => Storage.LoadData(JsonUtility.FromJson<ItemStorage.ItemStorageData>(json));
        #endregion
    }
}
