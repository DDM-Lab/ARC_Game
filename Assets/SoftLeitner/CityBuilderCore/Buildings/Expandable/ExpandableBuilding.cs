using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// special building that can have different sizes<br/>
    /// for example bridges of variable size, fields, storage areas<br/>
    /// only works combines with <see cref="ExpandableBuildingInfo"/> which defines how exactly the building can be expanded
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_expandable_building.html")]
    public class ExpandableBuilding : Building
    {
        [Tooltip("used to set expansion when the building is placed in the scene manually instead of using a tool at runtime")]
        public Vector2Int ManualExpansion;

        public ExpandableBuildingInfo ExpandableInfo => (ExpandableBuildingInfo)Info;

        public override Vector2Int RawSize => Info.Size + Expansion + ExpandableInfo.SizePost;

        private Vector2Int _expansion;
        public Vector2Int Expansion
        {
            get { return _expansion; }
            set
            {
                _expansion = value;
                adjustPivot();
                ExpansionChanged?.Invoke(value);
            }
        }

        public event Action<Vector2Int> ExpansionChanged;

        protected override void Start()
        {
            bool isManual = StructureReference == null;

            if (isManual)
            {
                _expansion = ManualExpansion;
                adjustPivot();
            }

            base.Start();

            if (isManual)
            {
                ExpansionChanged?.Invoke(_expansion);
            }
        }

        public override void Initialize()
        {
            base.Initialize();

            adjustPivot();
        }
        protected override void onReplacing(IBuilding replacement)
        {
            base.onReplacing(replacement);

            if (replacement is ExpandableBuilding expandable)
                expandable.Expansion = Expansion;
        }

        public class ExpandableBuildingData : BuildingData
        {
            public Vector2Int Expansion;
        }

        private void adjustPivot()
        {
            if (Pivot)
            {
                if (Dependencies.Get<IMap>().IsXY)
                    Pivot.localPosition = new Vector3(RawSize.x / 2f, RawSize.y / 2f, 0f);
                else
                    Pivot.localPosition = new Vector3(RawSize.x / 2f, 0f, RawSize.y / 2f);
            }
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new ExpandableBuildingData()
            {
                Expansion = Expansion,
                Components = _components.Select(c =>
                {
                    var data = c.SaveData();
                    if (string.IsNullOrWhiteSpace(data))
                        return null;

                    return new BuildingComponentMetaData()
                    {
                        Key = c.Key,
                        Data = data
                    };
                }).Where(d => d != null).ToArray(),
                Addons = _addons.Where(a => a.Save).Select(a =>
                {
                    return new BuildingAddonMetaData()
                    {
                        Key = a.Key,
                        Data = a.SaveData()
                    };
                }).ToArray()
            });
        }
    }
}
