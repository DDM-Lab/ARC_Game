using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// a risk that when executed replaces the building with structures(eg Rubble)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/risks">https://citybuilder.softleitner.com/manual/risks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_risk_structure_replacement.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Risks/" + nameof(RiskStructureReplacement))]
    public class RiskStructureReplacement : Risk
    {
        [Tooltip("when the risk triggers the afflicted building will be terminated and its points added to the structure with this key")]
        public string StructureCollectionKey;

        public override void Execute(IRiskRecipient risker)
        {
            base.Execute(risker);

            var collection = Dependencies.Get<IStructureManager>().GetStructure(StructureCollectionKey) as StructureCollection;
            var positions = PositionHelper.GetBoxPositions(risker.Building.Point, risker.Building.Point + risker.Building.Size - Vector2Int.one, collection.ObjectSize);

            risker.Building.Terminate();
            collection.Add(positions);
        }
    }
}