using CityBuilderCore;
using UnityEngine;

namespace CityBuilderManual.Custom
{
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(CustomScore))]
    public class CustomScore : Score
    {
        public float Multiplier;

        public override int Calculate()
        {
            return Mathf.RoundToInt(Dependencies.Get<ICustomManager>().GetTotalValue() * Multiplier);
        }
    }
}