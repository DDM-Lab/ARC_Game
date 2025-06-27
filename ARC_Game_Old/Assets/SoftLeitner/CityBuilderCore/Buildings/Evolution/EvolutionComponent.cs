using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// default building component for handling evolution
    /// <para>
    /// determines the current evolution stage inside its sequence based on layers, services and items<br/>
    /// if the stage changes it waits for the delay specified in <see cref="IBuildingManager"/> before replacing the building<br/>
    /// evolution components on lower buildings also need the recipients for higher ones in order to evolve<br/>
    /// </para>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_evolution_component.html")]
    public class EvolutionComponent : BuildingComponent, IEvolution
    {
        public override string Key => "EVO";

        [Tooltip("the sequence that contains the information about which building should be evolved/devolved to and the circumstances under which that happens")]
        public EvolutionSequence EvolutionSequence;

        [Tooltip("add a recipient for every service a building can receive and define how fast the value decreases")]
        public ServiceRecipient[] ServiceRecipients;
        [Tooltip("add a category recipient when you want to check how many services in a category the building has available(for example religions of different gods, entertainers, educational facilities)")]
        public ServiceCategoryRecipient[] ServiceCategoryRecipients;
        [Tooltip("theses get items and use them up over time")]
        public ItemRecipient[] ItemsRecipients;
        [Tooltip("same as regular ItemsRecipients but can use all the items of a category interchangably")]
        public ItemCategoryRecipient[] ItemsCategoryRecipients;

        public IItemContainer ItemContainer { get; private set; }

        private IGameSettings _settings;
        private EvolutionStage _queuedStage;
        private string _queuedAddon;
        private bool _queueDirection;
        private float _queueTime;

        private void Awake()
        {
            if (ServiceCategoryRecipients == null)//backwards compatibility
                ServiceCategoryRecipients = new ServiceCategoryRecipient[] { };

            ItemContainer = new MultiItemContainer(ItemsRecipients.Select(i => i.Storage).Union(ItemsCategoryRecipients.Select(i => i.Storage)), getItemStorages, getItemCategoryStorages);
        }

        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var serviceReplacement = replacement.GetBuildingComponent<IServiceRecipient>();
            if (serviceReplacement != null)
            {
                foreach (var recipient in ServiceRecipients)
                {
                    serviceReplacement.ModifyService(recipient.Service, recipient.Value);
                }

                foreach (var recipient in ServiceCategoryRecipients)
                {
                    recipient.Transfer(serviceReplacement);
                }
            }

            var itemReplacement = replacement.GetBuildingComponent<IItemRecipient>();
            if (itemReplacement != null)
            {
                foreach (var recipient in ItemsRecipients)
                {
                    itemReplacement.FillRecipient(recipient.Storage);
                }

                foreach (var recipient in ItemsCategoryRecipients)
                {
                    itemReplacement.FillRecipient(recipient.Storage);
                }
            }
        }

        private void Start()
        {
            foreach (var recipient in ServiceRecipients)
            {
                recipient.Gained += CheckEvolution;
                recipient.Lost += CheckEvolution;
            }

            foreach (var recipient in ServiceCategoryRecipients)
            {
                recipient.Changed += CheckEvolution;
            }

            foreach (var recipient in ItemsRecipients)
            {
                recipient.Gained += CheckEvolution;
                recipient.Lost += CheckEvolution;
            }

            foreach (var recipient in ItemsCategoryRecipients)
            {
                recipient.Changed += CheckEvolution;
            }

            _settings = Dependencies.Get<IGameSettings>();

            this.Delay(5, CheckEvolution);
        }
        private void Update()
        {
            if (Building.IsSuspended)
                return;

            foreach (var recipient in ServiceRecipients)
            {
                recipient.Update(_settings.ServiceMultiplier * recipient.Service.GetMultiplier(Building));
            }

            foreach (var recipient in ServiceCategoryRecipients)
            {
                recipient.Update(_settings.ServiceMultiplier, Building);
            }

            foreach (var recipient in ItemsRecipients)
            {
                recipient.Update(_settings.ItemsMultiplier);
            }

            foreach (var recipient in ItemsCategoryRecipients)
            {
                recipient.Update(_settings.ItemsMultiplier);
            }

            if (_queuedStage?.BuildingInfo != null)
            {
                _queueTime += Time.deltaTime;
                if (_queueTime >= Dependencies.Get<IBuildingManager>().GetEvolutionDelay(_queueDirection))
                    Building.Replace(_queuedStage.BuildingInfo.GetPrefab(Building.Index));
            }
        }

        public void CheckLayers(IEnumerable<Vector2Int> positions) => CheckEvolution();
        public void CheckEvolution()
        {
            if (!gameObject.scene.isLoaded)
                return;

            if (EvolutionSequence == null)
                return;

            var stage = EvolutionSequence.GetStage(Building.Point, GetAccessibleServices().ToArray(), GetAccessibleItems().ToArray());

            if (_queuedStage != null)
            {
                if (stage == _queuedStage)
                    return;

                _queueTime = 0f;
                _queuedStage = null;
                if (!string.IsNullOrWhiteSpace(_queuedAddon))
                    Building.RemoveAddon(_queuedAddon);
            }

            if (stage != null && stage.BuildingInfo == Building.Info)
                return;

            var direction = EvolutionSequence.GetDirection(Building.Info, stage.BuildingInfo);
            var evolutionManager = Dependencies.Get<IBuildingManager>();

            if (evolutionManager.HasEvolutionDelay(direction))
            {
                _queueDirection = direction;
                _queueTime = 0f;
                _queuedStage = stage;
                _queuedAddon = evolutionManager.AddEvolutionAddon(Building, direction);
            }
            else
            {
                Building.Replace(stage.BuildingInfo.GetPrefab(Building.Index));
            }
        }

        public bool HasServiceValue(Service service) => ServiceRecipients.Any(s => s.Service == service) || ServiceCategoryRecipients.Any(s => s.ServiceCategory.Contains(service));
        public float GetServiceValue(Service service)
        {
            var serviceRecipient = ServiceRecipients.FirstOrDefault(r => r.Service == service);
            if (serviceRecipient != null)
                return serviceRecipient.Value;

            var serviceCategoryRecipient = ServiceCategoryRecipients.FirstOrDefault(s => s.ServiceCategory.Contains(service));
            if (serviceCategoryRecipient != null)
                return serviceCategoryRecipient.GetValue(service);

            return 0f;
        }

        public void ModifyService(Service service, float amount)
        {
            var serviceRecipient = ServiceRecipients.FirstOrDefault(r => r.Service == service);
            if (serviceRecipient != null)
                serviceRecipient.Modify(amount);

            var serviceCategoryRecipient = ServiceCategoryRecipients.FirstOrDefault(s => s.ServiceCategory.Contains(service));
            if (serviceCategoryRecipient != null)
                serviceCategoryRecipient.Modify(service, amount);
        }

        public IEnumerable<Service> GetAccessibleServices()
        {
            foreach (var recipient in ServiceRecipients.Where(r => r.HasAccess))
            {
                yield return recipient.Service;
            }

            foreach (var recipient in ServiceCategoryRecipients)
            {
                foreach (var service in recipient.GetAccessibleServices())
                {
                    yield return service;
                }
            }
        }
        public IEnumerable<Item> GetAccessibleItems()
        {
            List<Item> items = new List<Item>();

            foreach (var recipient in ItemsRecipients)
            {
                if (!recipient.HasAccess)
                    continue;

                if (!items.Contains(recipient.Item))
                    items.Add(recipient.Item);
            }

            foreach (var recipient in ItemsCategoryRecipients)
            {
                foreach (var item in recipient.Storage.GetItems())
                {
                    if (!items.Contains(item))
                        items.Add(item);
                }
            }

            return items;
        }

        public IEnumerable<Item> GetRecipientItems()
        {
            foreach (var recipient in ItemsRecipients)
            {
                yield return recipient.Item;
            }

            foreach (var recipient in ItemsCategoryRecipients)
            {
                foreach (var item in recipient.ItemCategory.Items)
                {
                    yield return item;
                }
            }
        }
        public IEnumerable<ItemQuantity> GetRecipientItemQuantities() => GetRecipientItems().Select(i => new ItemQuantity(i, ItemContainer.GetItemQuantity(i)));
        public void FillRecipient(ItemStorage itemStorage)
        {
            if (ItemsRecipients != null)
            {
                foreach (var recipient in ItemsRecipients)
                {
                    recipient.Fill(itemStorage);
                }
            }

            if (ItemsCategoryRecipients != null)
            {
                foreach (var recipient in ItemsCategoryRecipients)
                {
                    recipient.Fill(itemStorage);
                }
            }
        }

        public override string GetDescription()
        {
            return EvolutionSequence.GetDescription(Building.Info, _queuedStage?.BuildingInfo, Building.Point, GetAccessibleServices(), GetAccessibleItems());
        }

        private IEnumerable<ItemStorage> getItemStorages(Item item)
        {
            foreach (var recipient in ItemsRecipients)
            {
                if (recipient.Item == item)
                    yield return recipient.Storage;
            }
            foreach (var recipient in ItemsCategoryRecipients)
            {
                if (recipient.ItemCategory.Contains(item))
                    yield return recipient.Storage;
            }
        }
        private IEnumerable<ItemStorage> getItemCategoryStorages(ItemCategory itemCategory)
        {
            foreach (var recipient in ItemsRecipients)
            {
                if (itemCategory.Contains(recipient.Item))
                    yield return recipient.Storage;
            }
            foreach (var recipient in ItemsCategoryRecipients)
            {
                if (recipient.ItemCategory == itemCategory)
                    yield return recipient.Storage;
            }
        }

        #region Saving
        [Serializable]
        public class EvolutionData
        {
            public ServiceRecipient.ServiceRecipientData[] ServiceRecipients;
            public ServiceCategoryRecipient.ServiceCategoryRecipientData[] ServiceCategoryRecipients;
            public ItemRecipient.ItemsRecipientData[] ItemsRecipients;
            public ItemCategoryRecipient.ItemsCategoryRecipientData[] ItemsCategoryRecipients;
            public int QueuedStage;
            public string QueuedAddon;
            public bool QueueDirection;
            public float QueueTime;
        }

        public override string SaveData()
        {
            var data = new EvolutionData()
            {
                ServiceRecipients = ServiceRecipients.Select(r => r.SaveData()).ToArray(),
                ServiceCategoryRecipients = ServiceCategoryRecipients.Select(r => r.SaveData()).ToArray(),
                ItemsRecipients = ItemsRecipients.Select(i => i.SaveData()).ToArray(),
                ItemsCategoryRecipients = ItemsCategoryRecipients.Select(i => i.SaveData()).ToArray()
            };

            if (_queuedStage != null)
            {
                data.QueuedStage = Array.IndexOf(EvolutionSequence.Stages, _queuedStage);
                data.QueuedAddon = _queuedAddon;
                data.QueueDirection = _queueDirection;
                data.QueueTime = _queueTime;
            }
            else
            {
                data.QueuedStage = -1;
            }

            return JsonUtility.ToJson(data);
        }
        public override void LoadData(string json)
        {
            base.LoadData(json);

            var data = JsonUtility.FromJson<EvolutionData>(json);

            foreach (var recipientData in data.ServiceRecipients)
            {
                var recipient = ServiceRecipients.FirstOrDefault(r => r.Service.Key == recipientData.Key);
                if (recipient == null)
                    continue;
                recipient.LoadData(recipientData);
            }

            foreach (var recipientData in data.ServiceCategoryRecipients)
            {
                var recipient = ServiceCategoryRecipients.FirstOrDefault(r => r.ServiceCategory.Key == recipientData.Key);
                if (recipient == null)
                    continue;
                recipient.LoadData(recipientData);
            }

            foreach (var recipientData in data.ItemsRecipients)
            {
                var recipient = ItemsRecipients.FirstOrDefault(r => r.Item.Key == recipientData.Key);
                if (recipient == null)
                    continue;
                recipient.LoadData(recipientData);
            }

            foreach (var recipientData in data.ItemsCategoryRecipients)
            {
                var recipient = ItemsCategoryRecipients.FirstOrDefault(r => r.ItemCategory.Key == recipientData.Key);
                if (recipient == null)
                    continue;
                recipient.LoadData(recipientData);
            }

            if (data.QueuedStage >= 0)
            {
                _queuedStage = EvolutionSequence.Stages[data.QueuedStage];
                _queuedAddon = data.QueuedAddon;
                _queueDirection = data.QueueDirection;
                _queueTime = data.QueueTime;
            }
        }
        #endregion
    }
}