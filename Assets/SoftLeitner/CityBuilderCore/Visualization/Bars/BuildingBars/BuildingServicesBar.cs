using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes services by displaying their icon if it is accessible<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_services_bar.html")]
    public class BuildingServicesBar : BuildingValueBar
    {
        [Tooltip("prefab for one icon instance")]
        public SpriteRenderer Prefab;
        [Tooltip("offset between icons")]
        public Vector3 Offset;

        private IMainCamera _mainCamera;
        private Service _service;
        private ServiceCategory _serviceCategory;
        private List<SpriteRenderer> _sprites = new List<SpriteRenderer>();

        private void Start()
        {
            _mainCamera = Dependencies.Get<IMainCamera>();

            setBar();
        }

        private void Update()
        {
            setBar();
        }

        public override void Initialize(IBuilding building, IBuildingValue value)
        {
            base.Initialize(building, value);

            _service = value as Service;
            _serviceCategory = value as ServiceCategory;
        }

        private void setBar()
        {
            transform.forward = _mainCamera.Camera.transform.forward;

            var startIndex = 0;

            if (_service != null)
            {
                if (_service.GetValue(Building) > 0f)
                    startIndex = setService(startIndex, _service);
            }
            else if (_serviceCategory != null)
            {
                foreach (var service in _serviceCategory.Services)
                {
                    if (service.GetValue(Building) > 0f)
                        startIndex = setService(startIndex, service);
                }
            }

            for (int i = _sprites.Count - 1; i >= startIndex; i--)
            {
                Destroy(_sprites[i].gameObject);
                _sprites.RemoveAt(i);
            }
        }

        private int setService(int startIndex, Service service)
        {
            var sprite = _sprites.ElementAtOrDefault(startIndex);
            if (sprite == null)
            {
                sprite = Instantiate(Prefab, transform);
                _sprites.Add(sprite);
            }

            sprite.sprite = service.Icon;
            sprite.transform.localPosition = Offset * startIndex;

            return startIndex + 1;
        }
    }
}