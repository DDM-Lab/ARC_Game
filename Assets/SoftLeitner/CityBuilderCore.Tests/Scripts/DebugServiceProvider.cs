using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class DebugServiceProvider : MonoBehaviour
    {
        public Service Service;
        public float Amount;

        private void Start()
        {
            this.Delay(5, () =>
            {
                foreach (var recipient in GetComponentsInChildren<IServiceRecipient>())
                {
                    recipient.ModifyService(Service, Amount);
                }
            });
        }
    }
}