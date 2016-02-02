using System;
using System.Collections.Generic;

namespace EnhancedGarbageTruckAI
{
    public sealed class Settings
    {
        private Settings()
        {
            Tag = "Enhanced Garbage Truck AI [Fixed for v1.2+]";

            DispatchGap = 5;

            ImmediateRange1 = 4000;
            ImmediateRange2 = 20000;
        }

        private static readonly Settings _Instance = new Settings();
        public static Settings Instance { get { return _Instance; } }

        public readonly string Tag;

        public readonly int DispatchGap;

        public readonly int ImmediateRange1;
        public readonly int ImmediateRange2;
    }
}