using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building componnent for risks<br/>
    /// updates risks, executes and resolves them and tries to transfer their values when the building gets replaced
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/risks">https://citybuilder.softleitner.com/manual/risks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_risker_component.html")]
    public class RiskerComponent : BuildingComponent, IRiskRecipient
    {
        public override string Key => "RSK";

        [Tooltip("one for each risk the building faces")]
        public RiskRecipient[] RiskRecipients;

        private IGameSettings _settings;

        private void Start()
        {
            foreach (var riskRecipient in RiskRecipients)
            {
                riskRecipient.Triggered += riskRecipientTriggered;
                riskRecipient.Resolved += riskRecipientResolved;
            }

            _settings = Dependencies.Get<IGameSettings>();
        }

        private void Update()
        {
            foreach (var riskRecipient in RiskRecipients)
            {
                riskRecipient.Update(_settings.RiskMultiplier * riskRecipient.Risk.GetMultiplier(Building));
            }
        }

        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var riskReplacement = replacement.GetBuildingComponent<IRiskRecipient>();
            if (riskReplacement == null)
                return;

            foreach (var riskRecipient in RiskRecipients)
            {
                riskReplacement.ModifyRisk(riskRecipient.Risk, riskRecipient.Value);
            }
        }

        public bool HasRiskValue(Risk risk)
        {
            foreach (var recipient in RiskRecipients)
            {
                if(recipient.Risk==risk)
                    return true;
            }
            return false;
        }
        public float GetRiskValue(Risk risk)
        {
            foreach (var recipient in RiskRecipients)
            {
                if (recipient.Risk == risk)
                    return recipient.Value;
            }
            return 0f;
        }
        public void ModifyRisk(Risk risk, float amount)
        {
            foreach (var recipient in RiskRecipients)
            {
                if (recipient.Risk = risk)
                    recipient.Modify(amount);
            }
        }

        private void riskRecipientTriggered(Risk risk) => risk.Execute(this);
        private void riskRecipientResolved(Risk risk) => risk.Resolve(this);

        #region Saving
        [Serializable]
        public class RiskerData
        {
            public RiskRecipient.RiskRecipientData[] RiskRecipients;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new RiskerData()
            {
                RiskRecipients = RiskRecipients.Select(r => r.GetData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            base.LoadData(json);

            var data = JsonUtility.FromJson<RiskerData>(json);
            foreach (var recipientData in data.RiskRecipients)
            {
                var recipient = RiskRecipients.FirstOrDefault(r => r.Risk.Key == recipientData.Key);
                if (recipient == null)
                    continue;
                recipient.Value = recipientData.Value;
            }
        }
        #endregion
    }
}