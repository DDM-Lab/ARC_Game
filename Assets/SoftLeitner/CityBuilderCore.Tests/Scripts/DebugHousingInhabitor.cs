using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class DebugHousingInhabitor : MonoBehaviour
    {
        public HousingComponent HousingComponent;
        public Population Population;

        private void Start()
        {
            this.Delay(5, () =>
            {
                HousingComponent.Inhabit(Population, HousingComponent.GetRemainingCapacity(Population));
            });
        }
    }
}