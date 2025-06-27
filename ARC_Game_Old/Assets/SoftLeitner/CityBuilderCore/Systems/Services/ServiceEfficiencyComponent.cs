using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// component that influences the building efficiency base on its access to services
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/services">https://citybuilder.softleitner.com/manual/services</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_service_efficiency_component.html")]
    public class ServiceEfficiencyComponent : BuildingComponent, IEfficiencyFactor, IServiceRecipient
    {
        public override string Key => "SEF";

        [Tooltip("one for each service used to determine efficiency, when 1/2 service recipients have access the efficiency is 0.5")]
        public ServiceRecipient[] Recipients;

        public bool IsWorking => Recipients.All(r => r.HasAccess);
        public float Factor => (float)Recipients.Where(r => r.HasAccess).Count() / Recipients.Length;

        public ServiceRecipient[] ServiceRecipients => Recipients;

        private IGameSettings _settings;

        private void Start()
        {
            _settings = Dependencies.Get<IGameSettings>();
        }

        private void Update()
        {
            Recipients.ForEach(r => r.Update(_settings.ServiceMultiplier));
        }

        public bool HasServiceValue(Service service) => ServiceRecipients.Any(s => s.Service == service);
        public float GetServiceValue(Service service) => ServiceRecipients.FirstOrDefault(r => r.Service == service)?.Value ?? 0f;
        public void ModifyService(Service service, float amount) => ServiceRecipients.Where(r => r.Service == service).SingleOrDefault()?.Modify(amount);

        #region Saving
        [Serializable]
        public class ServiceEfficiencyData
        {
            public ServiceRecipient.ServiceRecipientData[] Recipients;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new ServiceEfficiencyData()
            {
                Recipients = Recipients.Select(r => r.SaveData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<ServiceEfficiencyData>(json);

            for (int i = 0; i < data.Recipients.Length; i++)
            {
                Recipients[i].LoadData(data.Recipients[i]);
            }
        }
        #endregion
    }
}