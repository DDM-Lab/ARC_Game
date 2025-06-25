using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// roams around and collects items from the <see cref="IGenerationComponent"/> it encounters<br/>
    /// the kinds of items it collects must be configured in <see cref="Items"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_collection_walker.html")]
    public class CollectionWalker : BuildingComponentWalker<IGenerationComponent>
    {
        [Tooltip("stores items from generators it comes across until it gets home")]
        public ItemStorage Storage;
        [Tooltip("determines which items can be collected")]
        public Item[] Items;

        public override ItemStorage ItemStorage => Storage;
        public IItemContainer ItemContainer => Storage;

        protected override void onComponentEntered(IGenerationComponent buildingComponent)
        {
            base.onComponentEntered(buildingComponent);

            buildingComponent.Collect(Storage, Items);
        }

        public override string GetDebugText() => Storage.GetDebugText();

        #region Saving
        [Serializable]
        public class CollectionWalkerData : RoamingWalkerData
        {
            public ItemStorage.ItemStorageData Storage;
            public string[] Items;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new CollectionWalkerData()
            {
                WalkerData = savewalkerData(),
                State = (int)_state,
                Storage = Storage.SaveData(),
                Items = Items.Select(w => w.Key).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            base.LoadData(json);

            var data = JsonUtility.FromJson<CollectionWalkerData>(json);
            var items = Dependencies.Get<IKeyedSet<Item>>();

            Storage.LoadData(data.Storage);

            Items = data.Items.Select(k => items.GetObject(k)).ToArray();
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualCollectionWalkerSpawner : ManualWalkerSpawner<CollectionWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicCollectionWalkerSpawner : CyclicWalkerSpawner<CollectionWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledCollectionWalkerSpawner : PooledWalkerSpawner<CollectionWalker> { }
}