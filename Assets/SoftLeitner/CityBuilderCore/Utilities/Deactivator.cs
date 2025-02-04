using UnityEngine;

namespace CityBuilderCore
{
    public class Deactivator : MonoBehaviour
    {
        public void SetInactive(bool value) => gameObject.SetActive(!value);
    }
}
