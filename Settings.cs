﻿using System;
using System.Collections.Generic;

namespace EnhancedGarbageTruckAI
{
    public sealed class Settings
    {
        private Settings()
        {
            Tag = "[ARIS] Enhanced Garbage Truck AI";
        }

        private static readonly Settings _Instance = new Settings();
        public static Settings Instance { get { return _Instance; } }

        public readonly string Tag;
    }
}