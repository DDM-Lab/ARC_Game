using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// holds the value for a risk and increases it over time<br/>
    /// </summary>
    [Serializable]
    public class RiskRecipient
    {
        [Serializable]
        public class RiskRecipientData
        {
            public string Key;
            public float Value;
        }

        [Tooltip("the risk that triggers when this recipient fills up")]
        public Risk Risk;
        [Tooltip("how much the recipient increases the risk value per second(50 would trigger the risk after 2 seconds)")]
        public float IncreasePerSecond;

        public float Value { get; set; }
        public bool HasTriggered => Value >= 100f;

        public event Action<Risk> Triggered;
        public event Action<Risk> Resolved;

        public void Update(float multiplier)
        {
            if (IncreasePerSecond != 0f)
                raise(IncreasePerSecond * multiplier * Time.deltaTime);
        }

        public void Modify(float amount)
        {
            if (amount > 0)
                raise(amount);
            else
                reduce(amount);
        }

        private void raise(float amount)
        {
            if (HasTriggered)
                return;

            Value = Mathf.Min(100f, Value + amount);

            if (HasTriggered)
                Triggered?.Invoke(Risk);
        }

        private void reduce(float amount)
        {
            var hadTriggered = HasTriggered;

            Value = Mathf.Clamp(Value + amount, 0f, 100f);

            if (hadTriggered && !HasTriggered)
                Resolved?.Invoke(Risk);
            if (!hadTriggered && HasTriggered)
                Triggered?.Invoke(Risk);
        }

        public RiskRecipientData GetData() => new RiskRecipientData() { Key = Risk.Key, Value = Value };
    }
}