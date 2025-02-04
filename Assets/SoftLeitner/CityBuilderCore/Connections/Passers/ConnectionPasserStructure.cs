using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// will pass a connection at the points of a structure
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connection_passer_structure.html")]
    public class ConnectionPasserStructure : ConnectionPasserBase
    {
        [Tooltip(@"when empty the passer looks for a structure to use on the gameobject or its parent
if a key is set the structure is retrieved by its key from the structure manager")]
        public string StructureKey;

        private IStructure _structure;

        private void Awake()
        {
            if (string.IsNullOrWhiteSpace(StructureKey))
            {
                _structure = GetComponent<IStructure>() ?? GetComponentInParent<IStructure>();
                _structure.PointsChanged += structurePointsChanged;
            }
            else
            {
                StartCoroutine(initializeByKey());//structures typically register in start, delay looking for them until after that
            }
        }

        protected override void Start()
        {
            if(string.IsNullOrWhiteSpace(StructureKey))
                base.Start();
        }

        private IEnumerator initializeByKey()
        {
            yield return new WaitForEndOfFrame();
            _structure = Dependencies.Get<IStructureManager>().GetStructure(StructureKey);
            _structure.PointsChanged += structurePointsChanged;
            base.Start();
        }

        public override IEnumerable<Vector2Int> GetPoints() => _structure.GetPoints();

        private void structurePointsChanged(PointsChanged<IStructure> change) => onPointsChanged(change.RemovedPoints, change.AddedPoints);
    }
}
