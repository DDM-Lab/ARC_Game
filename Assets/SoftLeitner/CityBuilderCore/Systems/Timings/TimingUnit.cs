using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// describes some unit of time in the game that can be used to display playtime or specify when happenings should occur
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_timing_unit.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(TimingUnit))]
    public class TimingUnit : ScriptableObject
    {
        [Tooltip("display name(day, hour, season, made up time unit, ...)")]
        public string Name;
        [Tooltip("how far to count before reseting(ie weekday has 7), 0 for no reset")]
        public int Amount;
        [Tooltip("the duration of playtime in seconds for one unit of time")]
        public int Duration;
        [Tooltip("optional names for all the values(ie weekday has monday, tuesday, ...)")]
        public string[] Names;

        public int GetIndex(float playtime)
        {
            var num = (int)(playtime / Duration);
            if (Amount > 0)
                num = num % Amount;
            return num;
        }
        public int GetNumber(float playtime) => GetIndex(playtime) + 1;
        public int GetIteration(float playtime)
        {
            return (int)(playtime / Duration) + 1;
        }
        public float GetRatio(float playtime)
        {
            var num = playtime / Duration;
            if (Amount > 0)
                num = num % Amount;
            return num / Amount;
        }
        public string GetText(float playtime)
        {
            var num = GetNumber(playtime);

            if (Names != null && Names.Length > num - 1)
            {
                return Names[num - 1];
            }
            else
            {
                return $"{Name} {num}";
            }
        }
    }
}