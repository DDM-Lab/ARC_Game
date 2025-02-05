using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// holds the value for a service and decreases it over time<br/>
    /// </summary>
    [Serializable]
    public class ServiceRecipient
    {
        [Tooltip("the service for which this recipient holds the value")]
        public Service Service;
        [Tooltip("how much of the service value is lost per second(50 would make a full recipient lose access in 2 seconds)")]
        public float LossPerSecond;

        public float Value { get; set; }
        public bool HasAccess => Value > 0f;

        public event Action Gained;
        public event Action Lost;

        public void Update(float multiplier) => reduce(-LossPerSecond * multiplier * Time.deltaTime);

        public void Modify(float amount)
        {
            if (amount > 0)
                raise(amount);
            else
                reduce(amount);
        }

        private void raise(float amount)
        {
            var hadAccess = HasAccess;

            Value = Mathf.Clamp(Value + amount, 0f, 100f);

            if (!hadAccess && HasAccess)
                Gained?.Invoke();
            if (hadAccess && !HasAccess)
                Lost?.Invoke();
        }

        private void reduce(float amount)
        {
            if (Value == 0f)
                return;

            Value = Mathf.Max(0f, Value + amount);

            if (Value == 0f)
                Lost?.Invoke();
        }

        #region Saving
        [Serializable]
        public class ServiceRecipientData
        {
            public string Key;
            public float Value;
        }

        public ServiceRecipientData SaveData() => new ServiceRecipientData() { Key = Service.Key, Value = Value };
        public void LoadData(ServiceRecipientData data)
        {
            Value = data.Value;
        }
        #endregion
    }
}