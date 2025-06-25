using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// behaviour that can be used to adjust an expandable buildings visuals to its actual size<br/>
    /// also adjust the building pivot position to the expanded size<br/>
    /// can also be used for the ghost by only filling out the info and leaving the building empty
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_expandable_visual.html")]
    public class ExpandableVisual : MonoBehaviour
    {
        [Tooltip("info that defines how the expansion works")]
        public ExpandableBuildingInfo Info;
        [Tooltip("the building the visual belongs to, can be left empty for ghosts")]
        public ExpandableBuilding Building;
        [Tooltip("if set this pivot that will be moved to the center of the expaned building")]
        public Transform Pivot;

        [Tooltip("part will be moved to the center of the first point")]
        public Transform StartPart;
        [Tooltip("part that will be instantiated for each expansion point and placed at its center")]
        public Transform RepeatingPart;
        [Tooltip("part will be moved to the center of the last point")]
        public Transform EndPart;

        public List<Transform> RepeatedParts { get; private set; } = new List<Transform>();

        public event Action VisualsUpdated;

        protected virtual void Start()
        {
            if (Building)
            {
                if (Building.StructureReference != null)
                    UpdateVisual(Building.Expansion);
                Building.ExpansionChanged += UpdateVisual;
            }

            if (RepeatingPart)
            {
                RepeatingPart.gameObject.SetActive(false);
            }
        }

        public virtual void UpdateVisual(Vector2Int expansion)
        {
            updatePivot(expansion);

            if (Info.IsArea)
                updateAreaObjects(expansion);
            else
                updateLinearObjects(expansion);

            VisualsUpdated?.Invoke();
        }

        private void updatePivot(Vector2Int expansion)
        {
            if (Pivot)
            {
                var map = Dependencies.Get<IMap>();
                var size = Info.Size + expansion + Info.SizePost;

                if (map.IsXY)
                    Pivot.localPosition = new Vector3(size.x / 2f * map.CellOffset.x, size.y / 2f * map.CellOffset.y, 0f);
                else
                    Pivot.localPosition = new Vector3(size.x / 2f * map.CellOffset.x, 0f, size.y / 2f * map.CellOffset.y);
            }
        }

        private void instanceParts(int count)
        {
            if (!RepeatingPart)
                return;

            while (RepeatedParts.Count < count)
            {
                RepeatedParts.Add(Instantiate(RepeatingPart, RepeatingPart.parent));
            }

            while (RepeatedParts.Count > count)
            {
                Destroy(RepeatedParts[RepeatedParts.Count - 1].gameObject);
                RepeatedParts.RemoveAt(RepeatedParts.Count - 1);
            }
        }

        private void updateLinearObjects(Vector2Int expansion)
        {
            var map = Dependencies.Get<IMap>();
            var repeats = Mathf.Max(0, expansion.x);

            var cellOffset = map.CellOffset.x;
            var pivotOffset = (Info.Size.x + expansion.x + Info.SizePost.x) * map.CellOffset.x / 2f;

            instanceParts(repeats);

            if (StartPart)
            {
                setLinearPosition(0, StartPart, cellOffset, pivotOffset);
            }

            if (RepeatingPart)
            {
                for (int i = 0; i < repeats; i++)
                {
                    var part = RepeatedParts[i];
                    part.gameObject.SetActive(true);

                    setLinearPosition(Info.Size.x + i, part, cellOffset, pivotOffset);
                }
            }

            if (EndPart)
            {
                setLinearPosition(Info.Size.x + repeats, EndPart, cellOffset, pivotOffset);
            }
        }

        private void setLinearPosition(int x, Transform transform, float cellOffset, float pivotOffset)
        {
            transform.localPosition = new Vector3((x + 0.5f) * cellOffset - pivotOffset, transform.localPosition.y, transform.localPosition.z);
        }

        private void updateAreaObjects(Vector2Int expansion)
        {
            var map = Dependencies.Get<IMap>();
            var repeats = Mathf.Max(0, expansion.x * expansion.y);

            var cellOffset = map.CellOffset.x;
            var pivotOffset = new Vector2((Info.Size.x + expansion.x + Info.SizePost.x) * map.CellOffset.x / 2f, (Info.Size.y + expansion.y + Info.SizePost.y) * map.CellOffset.y / 2f);

            instanceParts(repeats);

            if (StartPart)
            {
                setAreaPosition(Vector2Int.zero, StartPart, cellOffset, pivotOffset, map.IsXY);
            }

            if (RepeatingPart)
            {
                for (int x = 0; x < expansion.x; x++)
                {
                    for (int y = 0; y < expansion.y; y++)
                    {
                        var part = RepeatedParts[x * expansion.y + y];
                        part.gameObject.SetActive(true);

                        setAreaPosition(Info.Size + new Vector2Int(x, y), part, cellOffset, pivotOffset, map.IsXY);
                    }
                }
            }

            if (EndPart)
            {
                setAreaPosition(Info.Size + expansion, EndPart, cellOffset, pivotOffset, map.IsXY);
            }
        }

        private void setAreaPosition(Vector2Int point, Transform transform, float cellOffset, Vector2 pivotOffset, bool isXY)
        {
            if (isXY)
                transform.localPosition = new Vector3((point.x + 0.5f) * cellOffset - pivotOffset.x, (point.y + 0.5f) * cellOffset - pivotOffset.y, transform.localPosition.z);
            else
                transform.localPosition = new Vector3((point.x + 0.5f) * cellOffset - pivotOffset.x, transform.localPosition.y, (point.y + 0.5f) * cellOffset - pivotOffset.y);
        }
    }
}