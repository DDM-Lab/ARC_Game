using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// receives any service of a specified category and uses it up over time<br/>
    /// basically just automatically manages a <see cref="ServiceRecipient"/> for every active service<br/>
    /// values are kept seperately, evolutions check how many different services of the category are currently available
    /// </summary>
    [Serializable]
    public class ServiceCategoryRecipient
    {
        [Tooltip("recipient will create a seperate value for each service in the category")]
        public ServiceCategory ServiceCategory;
        [Tooltip("how much of every active service is lost per second")]
        public float LossPerSecond;

        /// <summary>
        /// fires when any of its services either lose or gain access
        /// </summary>
        public event Action Changed;

        private List<ServiceRecipient> _recipients = new List<ServiceRecipient>();

        public void Update(float multiplier, IBuilding building)
        {
            _recipients.ToArray().ForEach(r => r.Update(multiplier * r.Service.GetMultiplier(building)));
        }

        public float GetValue(Service service) => _recipients.FirstOrDefault(r => r.Service == service)?.Value ?? 0f;
        public void Modify(Service service, float amount)
        {
            if (!ServiceCategory.Contains(service))
                return;

            var recipient = _recipients.FirstOrDefault(r => r.Service == service);
            
            if (recipient == null)
            {
                if (amount < 0f)
                    return;

                recipient = new ServiceRecipient() { Service = service, LossPerSecond = LossPerSecond };
                recipient.Modify(amount);
                recipient.Lost += recipientLost;

                _recipients.Add(recipient);

                onChanged();
            }
            else
            {
                recipient.Modify(amount);
            }
        }

        public void Transfer(IServiceRecipient target)
        {
            foreach (var recipient in _recipients)
            {
                target.ModifyService(recipient.Service, recipient.Value);
            }
        }

        public IEnumerable<Service> GetAccessibleServices() => _recipients.Select(r => r.Service);

        private void recipientLost()
        {
            var recipient = _recipients.First(r => !r.HasAccess);

            recipient.Lost -= recipientLost;

            _recipients.Remove(recipient);

            onChanged();
        }

        private void onChanged() => Changed?.Invoke();

        #region Saving
        [Serializable]
        public class ServiceCategoryRecipientData
        {
            public string Key;
            public ServiceRecipient.ServiceRecipientData[] Recipients;
        }

        public ServiceCategoryRecipientData SaveData() => new ServiceCategoryRecipientData() { Key = ServiceCategory.Key, Recipients = _recipients.Select(r => r.SaveData()).ToArray() };
        public void LoadData(ServiceCategoryRecipientData data)
        {
            foreach (var recipientData in data.Recipients)
            {
                var service = ServiceCategory.Services.First(s => s.Key == recipientData.Key);
                var recipient = new ServiceRecipient() { Service = service, LossPerSecond = LossPerSecond };

                recipient.LoadData(recipientData);
                recipient.Lost += recipientLost;

                _recipients.Add(recipient);
            }
        }
        #endregion
    }
}