using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class DebugGameSettings : MonoBehaviour, IGameSettings
    {
        public bool HasEfficiency = false;
        public bool HasEmployment = false;

        public float RiskMultiplier = 0;
        public float ServiceMultiplier = 0;
        public float ItemsMultiplier = 1;
        public float CheckInterval = 1;

        bool IGameSettings.HasEfficiency => HasEfficiency;
        bool IGameSettings.HasEmployment => HasEmployment;

        float IGameSettings.RiskMultiplier => RiskMultiplier;
        float IGameSettings.ServiceMultiplier => ServiceMultiplier;
        float IGameSettings.ItemsMultiplier => ItemsMultiplier;
        float IGameSettings.CheckInterval => CheckInterval;

        protected virtual void Awake()
        {
            Dependencies.Register<IGameSettings>(this);
        }

        public void DeregisterDifficultyFactor(IDifficultyFactor difficultyFactor)
        {
        }

        public void RegisterDifficultyFactor(IDifficultyFactor difficultyFactor)
        {
        }

        void IGameSettings.RegisterDifficultyFactor(IDifficultyFactor difficultyFactor)
        {
            throw new System.NotImplementedException();
        }

        void IGameSettings.DeregisterDifficultyFactor(IDifficultyFactor difficultyFactor)
        {
            throw new System.NotImplementedException();
        }
    }
}