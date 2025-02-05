using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that can block walkers that use <see cref="PathType.RoadBlocked"/> from using roads it is built on<br/>
    /// when no tags are defined it will block all walkers, otherwise it will block the defined tags which can be switched on and off(<see cref="RoadBlockerPanel"/>)<br/>
    /// the road blockers in THREE use <see cref="WalkerInfo"/>s as tags which are sent using <see cref="WalkerInfo.PathTagSelf"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_road_blocker_component.html")]
    public class RoadBlockerComponent : BuildingComponent
    {
        public override string Key => "ROB";

        [Tooltip("fill out to specify which road to block or leave empty to block all")]
        public Road Road;
        [Tooltip("fill out when you want to distinguis which walkers to block by tag(for example WalkerInfo)")]
        public KeyedObject[] Tags;

        public bool IsTagged => Tags.Length > 0;
        public List<string> BlockedKeys { get; private set; }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            block(Building.GetPoints());

            Building.PointsChanged += buildingPointsChanged;
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            unblock(Building.GetPoints());
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            unblock(Building.GetPoints());
        }
        public override void ResumeComponent()
        {
            base.ResumeComponent();

            block(Building.GetPoints());
        }

        public void SetBlockedKeys(List<string> blockedKeys)
        {
            changeTags(Building.GetPoints(), BlockedKeys, false);
            BlockedKeys = blockedKeys;
            changeTags(Building.GetPoints(), BlockedKeys, true);
        }

        private void block(IEnumerable<Vector2Int> points)
        {
            if (IsTagged)
            {
                BlockedKeys = Tags.Select(t => t.Key).ToList();

                changeTags(points, BlockedKeys, true);
            }
            else
            {
                Dependencies.Get<IRoadManager>().Block(Building.GetPoints(), Road);
            }
        }

        private void unblock(IEnumerable<Vector2Int> points)
        {
            if (IsTagged)
            {
                changeTags(points, BlockedKeys, false);
            }
            else
            {
                Dependencies.Get<IRoadManager>().Unblock(Building.GetPoints(), Road);
            }
        }

        private void changeTags(IEnumerable<Vector2Int> points, List<string> keys, bool block)
        {
            if (Building.IsSuspended)
                return;

            var blockedTags = Tags.Where(t => keys.Contains(t.Key)).ToList();

            if (block)
                Dependencies.Get<IRoadManager>().BlockTags(points, blockedTags, Road);
            else
                Dependencies.Get<IRoadManager>().UnblockTags(points, blockedTags, Road);
        }

        protected virtual void buildingPointsChanged(PointsChanged<IStructure> e)
        {
            unblock(e.RemovedPoints);
            block(e.AddedPoints);
        }

        #region Saving
        [Serializable]
        public class TaggedBlockerData
        {
            public string[] BlockedKeys;
        }

        public override string SaveData()
        {
            if (!IsTagged)
                return string.Empty;

            return JsonUtility.ToJson(new TaggedBlockerData()
            {
                BlockedKeys = BlockedKeys.ToArray()
            });
        }
        public override void LoadData(string json)
        {
            if (!IsTagged)
                return;

            var data = JsonUtility.FromJson<TaggedBlockerData>(json);

            SetBlockedKeys(data.BlockedKeys.ToList());
        }

        #endregion
    }
}
