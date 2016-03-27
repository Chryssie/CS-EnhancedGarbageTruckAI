namespace EnhancedGarbageTruckAI
{
    public sealed class Settings
    {
        private Settings()
        {
            Tag = "Enhanced Garbage Truck AI [Fixed for v1.4+]";

            ImmediateRange1 = 4000;
            ImmediateRange2 = 20000;
        }

        private static readonly Settings _Instance = new Settings();
        public static Settings Instance { get { return _Instance; } }

        public readonly string Tag;

        public readonly int ImmediateRange1;
        public readonly int ImmediateRange2;
    }
}