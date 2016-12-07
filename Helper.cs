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
			foreach (var plugin in PluginManager.instance.GetPluginsInfo())
			{
				if (!plugin.isEnabled)
					continue;
				foreach (var assembly in plugin.GetAssemblies())
				{
					try
					{
						var attributes = assembly.GetCustomAttributes(typeof(System.Runtime.InteropServices.GuidAttribute), false);
						foreach (var attribute in attributes)
						{
							var guidAttribute = attribute as System.Runtime.InteropServices.GuidAttribute;
							if (guidAttribute == null)
								continue;
							if (guidAttribute.Value == "837B2D75-956A-48B4-B23E-A07D77D55847")
								return true;
						}
					}
					catch (TypeLoadException)
					{
						// This occurs for some types, not sure why, but we should be able to just ignore them.
					}
				}
			}

			return false;
		}
    }
}