using System;

namespace CityBuilderCore
{
    [Serializable]
    public class GridPathfindingSettings
    {
        public enum GridPathfindingMode { Default, Burst }

        public GridPathfindingMode Mode = GridPathfindingMode.Default;
        public bool AllowDiagonal = false;
        public bool AllowInvalid = false;

        public GridPathfindingBase Create()
        {
            switch (Mode)
            {
                case GridPathfindingMode.Burst:
                    return new GridPathfindingBurst() { AllowDiagonal = AllowDiagonal, AllowInvalid = AllowInvalid };
                case GridPathfindingMode.Default:
                default:
                    return new GridPathfinding() { AllowDiagonal = AllowDiagonal, AllowInvalid = AllowInvalid };
            }
        }
    }
}
