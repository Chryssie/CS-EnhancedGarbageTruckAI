using ColossalFramework;
using ColossalFramework.Plugins;
using ICities;
using System;
using System.Collections.Generic;

namespace EnhancedGarbageTruckAI
{
    public class Dispatcher : ThreadingExtensionBase
    {
        private Settings _settings;
        private Helper _helper;

        private bool _initialized;
        private bool _baselined;
        private bool _terminated;

        public static Dictionary<ushort, Landfill> _landfills;
        public static Dictionary<ushort, Claimant> _master;
        private HashSet<ushort> _updated;
        private uint _lastProcessedFrame;
        public static Dictionary<ushort, HashSet<ushort>> _oldtargets;
        public static Dictionary<ushort, ushort> _lasttargets;
        public static Dictionary<ushort, ushort> _PathfindCount;
        private CustomGarbageTruckAI _CustomGarbageTruckAI;

        protected bool IsOverwatched()
        {
            #if DEBUG

            return true;

            #else

            foreach (var plugin in PluginManager.instance.GetPluginsInfo())
            {
                if (plugin.publishedFileID.AsUInt64 == 583538182) 
                    return true;
            }

            return false;

            #endif
        }

        public override void OnCreated(IThreading threading)
        {
            _settings = Settings.Instance;
            _helper = Helper.Instance;
            _CustomGarbageTruckAI = new CustomGarbageTruckAI();
            _initialized = false;
            _baselined = false;
            _terminated = false;

            base.OnCreated(threading);
        }

        public override void OnBeforeSimulationTick()
        {
            if (_terminated) return;

            if (!_helper.GameLoaded)
            {
                _initialized = false;
                _baselined = false;
                return;
            }

            base.OnBeforeSimulationTick();
        }

        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            if (_terminated) return;

            if (!_helper.GameLoaded) return;

            try
            {
                if (!_initialized)
                {
                    if (!IsOverwatched())
                    {
                        _helper.NotifyPlayer("Skylines Overwatch not found. Terminating...");
                        _terminated = true;

                        return;
                    }

                    SkylinesOverwatch.Settings.Instance.Enable.BuildingMonitor = true;
                    SkylinesOverwatch.Settings.Instance.Enable.VehicleMonitor = true;

                    _landfills = new Dictionary<ushort, Landfill>();
                    _master = new Dictionary<ushort, Claimant>();
                    _updated = new HashSet<ushort>();
                    _oldtargets = new Dictionary<ushort, HashSet<ushort>>();
                    _lasttargets = new Dictionary<ushort, ushort>();
                    _PathfindCount = new Dictionary<ushort, ushort>();

                    _initialized = true;

                    _helper.NotifyPlayer("Initialized");
                }
                else if (!_baselined)
                {
                    CreateBaseline();
                }
                else
                {
                    ProcessNewLandfills();
                    ProcessRemovedLandfills();

                    ProcessNewPickups();

                    if (!SimulationManager.instance.SimulationPaused)
                    {
                        ProcessIdleGarbageTrucks();
                        UpdateGarbageTrucks();
                    }
                    _lastProcessedFrame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
                }
            }
            catch (Exception e)
            {
                string error = String.Format("Failed to {0}\r\n", !_initialized ? "initialize" : "update");
                error += String.Format("Error: {0}\r\n", e.Message);
                error += "\r\n";
                error += "==== STACK TRACE ====\r\n";
                error += e.StackTrace;

                _helper.Log(error);

                if (!_initialized)
                    _terminated = true;
            }

            base.OnUpdate(realTimeDelta, simulationTimeDelta);
        }

        public override void OnReleased()
        {
            _initialized = false;
            _baselined = false;
            _terminated = false;

            base.OnReleased();
        }

        private void CreateBaseline()
        {
            SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;

            foreach (ushort id in data.LandfillSites)
                _landfills.Add(id, new Landfill(id, ref _master, ref _oldtargets));

            foreach (ushort pickup in data.BuildingsWithGarbage)
            {
                foreach (ushort id in _landfills.Keys)
                    _landfills[id].AddPickup(pickup);
            }

            _baselined = true;
        }

        private void ProcessNewLandfills()
        {
            SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;

            foreach (ushort x in data.BuildingsUpdated)
            {
                if (!data.IsLandfillSite(x))
                    continue;

                if (_landfills.ContainsKey(x))
                    continue;

                _landfills.Add(x, new Landfill(x, ref _master, ref _oldtargets));

                foreach (ushort pickup in data.BuildingsWithGarbage)
                    _landfills[x].AddPickup(pickup);
            }
        }

        private void ProcessRemovedLandfills()
        {
            SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;

            foreach (ushort id in data.BuildingsRemoved)
                _landfills.Remove(id);
        }

        private void ProcessNewPickups()
        {
            SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;

            foreach (ushort pickup in data.BuildingsUpdated)
            {
                if (data.IsLandfillSite(pickup))
                    continue;

                if (data.IsBuildingWithGarbage(pickup))
                {
                    foreach (ushort id in _landfills.Keys)
                        _landfills[id].AddPickup(pickup);
                }
                else
                {
                    foreach (ushort id in _landfills.Keys)
                        _landfills[id].AddCheckup(pickup);
                }
            }
        }

        private void ProcessIdleGarbageTrucks()
        {
            SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;

            foreach (ushort x in data.BuildingsUpdated)
            {
                if (!data.IsLandfillSite(x))
                    continue;

                if (!_landfills.ContainsKey(x))
                    continue;

                _landfills[x].DispatchIdleVehicle();
            }
        }

        private void UpdateGarbageTrucks()
        {
            SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;
            Vehicle[] vehicles = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            uint num1 = Singleton<SimulationManager>.instance.m_currentFrameIndex >> 4 & 7u;
            uint num2 = _lastProcessedFrame >> 4 & 7u;

            foreach (ushort vehicleID in data.VehiclesRemoved)
            {
                if (_lasttargets.ContainsKey(vehicleID)
                    && Helper.IsBuildingWithGarbage(_lasttargets[vehicleID]))
                {
                    foreach (ushort id in _landfills.Keys)
                        _landfills[id].AddPickup(_lasttargets[vehicleID]);
                }
                _oldtargets.Remove(vehicleID);
                if (_lasttargets.ContainsKey(vehicleID))
                {
                    _master.Remove(_lasttargets[vehicleID]);
                }
                _lasttargets.Remove(vehicleID);
                _PathfindCount.Remove(vehicleID);
            }

            foreach (ushort vehicleID in data.VehiclesUpdated)
            {
                if (!data.IsGarbageTruck(vehicleID))
                    continue;

                Vehicle v = vehicles[vehicleID];

                if (!_landfills.ContainsKey(v.m_sourceBuilding))
                    continue;

                if (_master.ContainsKey(v.m_targetBuilding))
                {
                    if (_master[v.m_targetBuilding].Truck != vehicleID)
                        _master[v.m_targetBuilding] = new Claimant(vehicleID, v.m_targetBuilding);
                }
                else
                    _master.Add(v.m_targetBuilding, new Claimant(vehicleID, v.m_targetBuilding));
                

                if ((v.m_flags & (Vehicle.Flags.Stopped | Vehicle.Flags.WaitingSpace | Vehicle.Flags.WaitingPath | Vehicle.Flags.WaitingLoading | Vehicle.Flags.WaitingCargo)) != Vehicle.Flags.None) continue;
                if ((v.m_flags & (Vehicle.Flags.Spawned)) == Vehicle.Flags.None) continue;
                if (v.m_path == 0u) continue;
                
                _PathfindCount.Remove(vehicleID);

                if ((v.m_flags & (Vehicle.Flags.WaitingTarget)) == Vehicle.Flags.None)
                {
                    uint num3 = vehicleID & 7u;
                    if (num1 != num3 && num2 != num3)
                    {
                        _updated.Remove(vehicleID);
                        continue;
                    }
                    else if (_updated.Contains(vehicleID))
                    {
                        continue;
                    }
                }

                _updated.Add(vehicleID);

                int truckStatus = GetGarbageTruckStatus(ref v);

                if (truckStatus == VEHICLE_STATUS_GARBAGE_RETURN && _lasttargets.ContainsKey(vehicleID))
                {
                    if (Helper.IsBuildingWithGarbage(_lasttargets[vehicleID]))
                    {
                        foreach (ushort id in _landfills.Keys)
                            _landfills[id].AddPickup(_lasttargets[vehicleID]);
                    }
                    _lasttargets.Remove(vehicleID);
                    continue;
                }
                if (truckStatus != VEHICLE_STATUS_GARBAGE_COLLECT && truckStatus != VEHICLE_STATUS_GARBAGE_WAIT)
                    continue;
                
                ushort target = _landfills[v.m_sourceBuilding].AssignTarget(vehicleID);

                if (target != 0 && target != v.m_targetBuilding)
                {
                    if (Helper.IsBuildingWithGarbage(v.m_targetBuilding))
                    {
                        foreach (ushort id in _landfills.Keys)
                            _landfills[id].AddPickup(v.m_targetBuilding);
                    }

                    _master.Remove(v.m_targetBuilding);
                    _lasttargets[vehicleID] = v.m_targetBuilding;
                    _CustomGarbageTruckAI.SetTarget(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID], target);
                }
            }
        }

        const int VEHICLE_STATUS_GARBAGE_RETURN = 0;
        const int VEHICLE_STATUS_GARBAGE_UNLOAD = 1;
        const int VEHICLE_STATUS_GARBAGE_TRANSFER = 2;
        const int VEHICLE_STATUS_CONFUSED = 3;
        public const int VEHICLE_STATUS_GARBAGE_WAIT = 4;
        public const int VEHICLE_STATUS_GARBAGE_COLLECT = 5;

        public static int GetGarbageTruckStatus(ref Vehicle data)
        {
            if ((data.m_flags & Vehicle.Flags.TransferToSource) == Vehicle.Flags.None)
            {
                if ((data.m_flags & Vehicle.Flags.TransferToTarget) != Vehicle.Flags.None)
                {
                    if ((data.m_flags & Vehicle.Flags.GoingBack) != Vehicle.Flags.None)
                    {
                        return VEHICLE_STATUS_GARBAGE_RETURN;
                    }
                    if ((data.m_flags & Vehicle.Flags.WaitingTarget) != Vehicle.Flags.None)
                    {
                        return VEHICLE_STATUS_GARBAGE_UNLOAD;
                    }
                    if (data.m_targetBuilding != 0)
                    {
                        return VEHICLE_STATUS_GARBAGE_TRANSFER;
                    }
                }
                return VEHICLE_STATUS_CONFUSED;
            }
            if ((data.m_flags & Vehicle.Flags.GoingBack) != Vehicle.Flags.None)
            {
                return VEHICLE_STATUS_GARBAGE_RETURN;
            }
            if ((data.m_flags & Vehicle.Flags.WaitingTarget) != Vehicle.Flags.None)
            {
                return VEHICLE_STATUS_GARBAGE_WAIT;
            }
            return VEHICLE_STATUS_GARBAGE_COLLECT;
        }
    }
}

