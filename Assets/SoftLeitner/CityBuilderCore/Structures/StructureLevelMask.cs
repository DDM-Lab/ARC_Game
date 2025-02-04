using System;

namespace CityBuilderCore
{
    /// <summary>
    /// these flags define which levels a structure occupies, 0 for all levels<br/>
    /// a structure can occupy multiple levels, for example 5 would occupy level 1 and 3<br/>
    /// structures can only exist on the same point if they occupy different levels
    /// </summary>
    [Serializable]
    public class StructureLevelMask
    {
        public int Value;

        public bool Check(int value) => Check(Value,value);

        public static bool Check(int a, int b) => a == 0 || b == 0 || (a & b) != 0;
    }
}
