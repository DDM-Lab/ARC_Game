using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderUrban
{
    /// <summary>
    /// walks across the map along the y axis and destroys any structure it passes over
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/urban">https://citybuilder.softleitner.com/manual/urban</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_urban_1_1_tornado_walker.html")]
    public class TornadoWalker : Walker
    {
        public StructureLevelMask DestructionLevel;
        public GameObject DestructionPrefab;

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            var x = UnityEngine.Random.Range(0, 25);

            Walk(new WalkingPath(new Vector2Int[] { new Vector2Int(x, 31), new Vector2Int(x, 0) }));

            Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest($"TORNADO !!!", transform));
        }

        private void Update()
        {
            var point = Dependencies.Get<IGridPositions>().GetGridPoint(transform.position);

            if (Dependencies.Get<IStructureManager>().Remove(new Vector2Int[] { point }, DestructionLevel.Value, false) > 0 && DestructionPrefab)
            {
                Instantiate(DestructionPrefab, Dependencies.Get<IGridPositions>().GetWorldPosition(point), Quaternion.identity);
            }
        }

        public override void LoadData(string json)
        {
            base.LoadData(json);

            ContinueWalk();
        }
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualTornadoWalkerSpawner : ManualWalkerSpawner<TornadoWalker> { }
}
