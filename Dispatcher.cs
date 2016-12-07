using ColossalFramework;
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
        private Dictionary<ushort, ushort> _lasttargets;
        private Dictionary<ushort, DateTime> _lastchangetimes;
        private Dictionary<ushort, ushort> _PathfindCount;
        private CustomGarbageTruckAI _CustomGarbageTruckAI;

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
                    if (!Helper.IsOverwatched())
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
                    _lastchangetimes = new Dictionary<ushort, DateTime>();
                    _PathfindCount = new Dictionary<ushort, ushort>();

                    RedirectionHelper.RedirectCalls(Loader.m_redirectionStates, typeof(GarbageTruckAI), typeof(CustomGarbageTruckAI), "SetTarget", 3);

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
                    }
                    UpdateGarbageTrucks();
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
                _landfills.Add(id, new Landfill(id, ref _master, ref _oldtargets, ref _lastchangetimes));

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

                _landfills.Add(x, new Landfill(x, ref _master, ref _oldtargets, ref _lastchangetimes));

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
                if (!_landfills.ContainsKey(x))
                    continue;

                _landfills[x].DispatchIdleVehicle();
            }
        }

        private void UpdateGarbageTrucks()
        {
            SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;

            foreach (ushort vehicleID in data.VehiclesRemoved)
            {
                if (!data.IsGarbageTruck(vehicleID))
                    continue;

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
                _lastchangetimes.Remove(vehicleID);
                _PathfindCount.Remove(vehicleID);
            }

            if (!SimulationManager.instance.SimulationPaused)
            {
                uint num1 = Singleton<SimulationManager>.instance.m_currentFrameIndex >> 4 & 7u;
                uint num2 = _lastProcessedFrame >> 4 & 7u;
                foreach (ushort vehicleID in data.VehiclesUpdated)
                {
                    if (!data.IsGarbageTruck(vehicleID))
                        continue;

                    Vehicle v = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID];

                    if (!_landfills.ContainsKey(v.m_sourceBuilding))
                        continue;

					if (v.m_flags.IsFlagSet(Vehicle.Flags.Stopped) || v.m_flags.IsFlagSet(Vehicle.Flags.WaitingSpace) || v.m_flags.IsFlagSet(Vehicle.Flags.WaitingPath) || v.m_flags.IsFlagSet(Vehicle.Flags.WaitingLoading) || v.m_flags.IsFlagSet(Vehicle.Flags.WaitingCargo))
						continue;
					if (!v.m_flags.IsFlagSet(Vehicle.Flags.Spawned))
						continue;
                    if (v.m_path == 0u)
						continue;

                    _PathfindCount.Remove(vehicleID);

                    if (!v.m_flags.IsFlagSet(Vehicle.Flags.WaitingTarget))
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

                    int vehicleStatus = GetGarbageTruckStatus(ref v);

                    if (vehicleStatus == VEHICLE_STATUS_GARBAGE_RETURN && _lasttargets.ContainsKey(vehicleID))
                    {
                        if (Helper.IsBuildingWithGarbage(_lasttargets[vehicleID]))
                        {
                            foreach (ushort id in _landfills.Keys)
                                _landfills[id].AddPickup(_lasttargets[vehicleID]);
                        }
                        _lasttargets.Remove(vehicleID);
                        continue;
                    }
                    if (vehicleStatus != VEHICLE_STATUS_GARBAGE_COLLECT && vehicleStatus != VEHICLE_STATUS_GARBAGE_WAIT)
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
                        if (vehicleStatus == VEHICLE_STATUS_GARBAGE_COLLECT)
                        {
                            _lasttargets[vehicleID] = v.m_targetBuilding;
                            if (_lastchangetimes.ContainsKey(vehicleID))
                            {
                                _lastchangetimes[vehicleID] = SimulationManager.instance.m_currentGameTime;
                            }
                            else
                            {
                                _lastchangetimes.Add(vehicleID, SimulationManager.instance.m_currentGameTime);
                            }
                        }
                        _CustomGarbageTruckAI.SetTarget(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID], target);
                    }
                    else
                    {
                        if (_master.ContainsKey(v.m_targetBuilding))
                        {
                            if (_master[v.m_targetBuilding].Vehicle != vehicleID)
                                _master[v.m_targetBuilding] = new Claimant(vehicleID, v.m_targetBuilding);
                        }
                        else
                            _master.Add(v.m_targetBuilding, new Claimant(vehicleID, v.m_targetBuilding));
                    }
                }
            }

            foreach (ushort vehicleID in data.GarbageTrucks)
            {
                if (Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_flags.IsFlagSet(Vehicle.Flags.WaitingPath))
                {
                    PathManager instance = Singleton<PathManager>.instance;
                    byte pathFindFlags = instance.m_pathUnits.m_buffer[(int)((UIntPtr)Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_path)].m_pathFindFlags;
                    if ((pathFindFlags & 4) != 0)
                    {
                        _PathfindCount.Remove(vehicleID);
                    }
                    else if ((pathFindFlags & 8) != 0)
                    {
                        int vehicleStatus = GetGarbageTruckStatus(ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID]);
                        if (_lasttargets.ContainsKey(vehicleID))
                        {
                            Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_flags &= ~Vehicle.Flags.WaitingPath;
                            Singleton<PathManager>.instance.ReleasePath(Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_path);
                            Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_path = 0u;
                            _CustomGarbageTruckAI.SetTarget(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID], _lasttargets[vehicleID]);
                            _lasttargets.Remove(vehicleID);
                        }
                        else if ((vehicleStatus == VEHICLE_STATUS_GARBAGE_WAIT || vehicleStatus == VEHICLE_STATUS_GARBAGE_COLLECT)
                            && (Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_flags.IsFlagSet(Vehicle.Flags.Spawned))
                            && _landfills.ContainsKey(Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_sourceBuilding)
                            && (!_PathfindCount.ContainsKey(vehicleID) || _PathfindCount[vehicleID] < 20))
                        {
                            if (!_PathfindCount.ContainsKey(vehicleID)) _PathfindCount[vehicleID] = 0;
                            _PathfindCount[vehicleID]++;
                            ushort target = _landfills[Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_sourceBuilding].GetUnclaimedTarget(vehicleID);
                            if (target == 0)
                            {
                                _PathfindCount[vehicleID] = ushort.MaxValue;
                            }
                            else
                            {
                                if (Dispatcher._oldtargets != null)
                                {
                                    if (!Dispatcher._oldtargets.ContainsKey(vehicleID))
                                        Dispatcher._oldtargets.Add(vehicleID, new HashSet<ushort>());
                                    Dispatcher._oldtargets[vehicleID].Add(target);
                                }
                                Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_flags &= ~Vehicle.Flags.WaitingPath;
                                Singleton<PathManager>.instance.ReleasePath(Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_path);
                                Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_path = 0u;
                                _CustomGarbageTruckAI.SetTarget(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID], target);
                            }
                        }
                    }
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
            if (!data.m_flags.IsFlagSet(Vehicle.Flags.TransferToSource))
            {
                if (data.m_flags.IsFlagSet( Vehicle.Flags.TransferToTarget))
                {
                    if (data.m_flags.IsFlagSet( Vehicle.Flags.GoingBack))
                    {
                        return VEHICLE_STATUS_GARBAGE_RETURN;
                    }
                    if (data.m_flags.IsFlagSet( Vehicle.Flags.WaitingTarget) )
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
            if (data.m_flags.IsFlagSet( Vehicle.Flags.GoingBack))
            {
                return VEHICLE_STATUS_GARBAGE_RETURN;
            }
            if (data.m_flags.IsFlagSet( Vehicle.Flags.WaitingTarget))
            {
                return VEHICLE_STATUS_GARBAGE_WAIT;
            }
            return VEHICLE_STATUS_GARBAGE_COLLECT;
        }
    }
}

