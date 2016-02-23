using ColossalFramework;
using ColossalFramework.Plugins;
using System;
using UnityEngine;

namespace EnhancedGarbageTruckAI
{
    internal sealed class Helper
    {
        private Helper()
        {
            GameLoaded = false;
        }

        private static readonly Helper _Instance = new Helper();
        public static Helper Instance { get { return _Instance; } }

        internal bool GameLoaded;

        public void Log(string message)
        {
            Debug.Log(String.Format("{0}: {1}", Settings.Instance.Tag, message));
        }

        public void NotifyPlayer(string message)
        {
            DebugOutputPanel.AddMessage(PluginManager.MessageType.Message, String.Format("{0}: {1}", Settings.Instance.Tag, message));
            Log(message);
        }

        public static double GetAngleDifference(double a, double b)
        {
            if (a < 0) a += Math.PI * 2;
            if (b < 0) b += Math.PI * 2;

            double diff = a - b;

            if (diff > Math.PI)
                diff -= Math.PI * 2;
            else if (diff < -Math.PI)
                diff += Math.PI * 2;

            return diff;
        }

        public static bool IsBuildingWithGarbage(ushort id)
        {
            return ((Singleton<BuildingManager>.instance.m_buildings.m_buffer[id].m_flags & (Building.Flags.Abandoned | Building.Flags.BurnedDown)) == Building.Flags.None
                && Singleton<BuildingManager>.instance.m_buildings.m_buffer[id].Info.m_buildingAI.GetGarbageAmount(id, ref Singleton<BuildingManager>.instance.m_buildings.m_buffer[id]) > 2500);
        }

        public static bool IsOverwatched()
        {
#if DEBUG

            return true;

#else

            foreach (var plugin in PluginManager.instance.GetPluginsInfo())
            {
                if (plugin.name == "583538182" && plugin.publishedFileID.ToString() == "583538182")
                    return plugin.isEnabled;
            }

            return false;

#endif
        }
    }
}