using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// requirement that specifies a point and some parameters a road at that point has to match<br/>
    /// both <see cref="Road"/> and <see cref="Stage"/> if any kind of road is ok<br/>
    /// currently only used as a requirement for buildings(<see cref="BuildingInfo.RoadRequirements"/>)<br/>
    /// for example in the stages and bridges in THREE
    /// </summary>
    [Serializable]
    public class RoadRequirement
    {
        [Tooltip("the point within the building to check")]
        public Vector2Int Point;
        [Tooltip("specifies the kind of road needed at the point, leave empty if any road is ok")]
        public Road Road;
        [Tooltip("key the target road stage has to start with(optional)")]
        public string Stage;
        [Tooltip("should the road be added by the builder if its missing")]
        public bool Amend;

        public bool Check(Vector2Int point, Road road, string stage)
        {
            if (road == null)
            {
                if (!Amend)
                    return false;

                if (Road)
                    return Dependencies.Get<IStructureManager>().CheckAvailability(point, Road.Level.Value, Road);
                else
                    return true;
            }
            else
            {
                if (Road)
                {
                    if (Road != road)
                        return false;//there is either no road or its the wrong one
                }
                else
                {
                    if (road == null)//there is no road
                        return false;
                }


                if (!string.IsNullOrEmpty(Stage) && !stage.StartsWith(Stage))
                    return false;

                return true;
            }
        }
    }
}