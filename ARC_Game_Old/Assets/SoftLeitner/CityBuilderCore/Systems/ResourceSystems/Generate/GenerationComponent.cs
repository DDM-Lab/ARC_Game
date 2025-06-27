using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that periodically generates items(speed influenced by efficiency)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_generation_component.html")]
    public class GenerationComponent : ProgressComponent, IGenerationComponent
    {
        public override string Key => "GEN";

        public enum GenerationState
        {
            Generating,//progress going up according to efficiency
            Done//waiting for items to be collected
        }

        [Tooltip("one for each item that is generated whenever the progress is full")]
        public ItemProducer[] ItemsProducers;

        ItemProducer[] IGenerationComponent.ItemsProducers => ItemsProducers;
        public IItemContainer ItemContainer { get; private set; }

        private GenerationState _generationState;

        private void Awake()
        {
            ItemContainer = new SplitItemContainer(ItemsProducers.Select(i => i.Storage), i => ItemsProducers.FirstOrDefault(p => p.Items.Item == i)?.Storage);
        }
        private void Update()
        {
            updateGeneration();
        }

        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var generationReplacement = replacement.GetBuildingComponent<IGenerationComponent>();
            if (generationReplacement == null)
                return;

            foreach (var itemsProducer in ItemsProducers)
            {
                var producerReplacement = generationReplacement.ItemsProducers.FirstOrDefault(p => p.Items.Item == itemsProducer.Items.Item);
                if (producerReplacement != null)
                    itemsProducer.Storage.MoveItemsTo(producerReplacement.Storage);
            }
        }

        public void Collect(ItemStorage storage, Item[] items)
        {
            foreach (var producer in ItemsProducers)
            {
                if (items.Contains(producer.Items.Item))
                    producer.Storage.MoveItemsTo(storage);
            }
        }

        private void updateGeneration()
        {
            switch (_generationState)
            {
                case GenerationState.Generating:
                    if (addProgress(Building.Efficiency))
                    {
                        _generationState = GenerationState.Done;
                        IsProgressing?.Invoke(false);
                    }
                    break;
                case GenerationState.Done:
                    if (ItemsProducers.All(p => p.FitsItems))
                    {
                        foreach (var itemsProducer in ItemsProducers)
                        {
                            itemsProducer.Produce();
                        }
                        _generationState = GenerationState.Generating;
                        resetProgress();
                        IsProgressing?.Invoke(true);
                    }
                    break;
                default:
                    break;
            }
        }

        #region Saving
        [Serializable]
        public class GenerationData
        {
            public int State;
            public float Time;
            public ItemStorage.ItemStorageData[] Producers;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new GenerationData()
            {
                State = (int)_generationState,
                Time = _progressTime,
                Producers = ItemsProducers.Select(c => c.Storage.SaveData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<GenerationData>(json);

            _generationState = (GenerationState)data.State;
            _progressTime = data.Time;
            for (int i = 0; i < ItemsProducers.Length; i++)
            {
                ItemsProducers[i].Storage.LoadData(data.Producers[i]);
            }
        }
        #endregion
    }
}