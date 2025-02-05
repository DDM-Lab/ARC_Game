using UnityEngine;

namespace CityBuilderThree
{
    /// <summary>
    /// no longer needed since migration sentiment is driven by a score since version 1.4 but kept for compatibility<br/>
    /// currently all logic in the three demo is covered by CityBuilderCore, if any custom logic is needed it could be placed here
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/three">https://citybuilder.softleitner.com/manual/three</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_three_1_1_three_manager.html")]
    public class ThreeManager : MonoBehaviour
    {
        void Update()
        {
            //1.4 > MIGRATION SENTIMENT IS NOW DRIVEN BY A SCORE
            //      MINIMUM POPULATION IS NOW A FIELD ON MIGRATION
            //
            //var populationManager = Dependencies.Get<IPopulationManager>();
            //var employmentManager = Dependencies.Get<IEmploymentManager>();

            //foreach (var population in Dependencies.Get<IObjectSet<Population>>().Objects)
            //{
            //    var migration = populationManager.GetMigration(population);

            //    var populationQuantity = populationManager.GetQuantity(migration.Population, true);
            //    var employmentQuantity = Mathf.Max(employmentManager.GetNeeded(migration.Population), 20);//20 people always stay even if they are unemployed
            //    var difference = populationQuantity - employmentQuantity;

            //    if (difference == 0 || (difference > 0 && difference < 10))
            //        migration.Sentiment = 0f;//close enough
            //    else
            //        migration.Sentiment = -((float)populationQuantity / (float)employmentQuantity - 1f);//simply balance population for full employment
            //}
        }
    }
}