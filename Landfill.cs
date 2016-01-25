using ColossalFramework;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EnhancedGarbageTruckAI
{
    public class Landfill
    {
        private Settings _settings;
        private Helper _helper;

        private string _truckCount;

        private readonly ushort _id;

        private Dictionary<ushort, DateTime> _master;
        private HashSet<ushort> _primary;
        private HashSet<ushort> _secondary;
        private List<ushort> _checkups;
        private Dictionary<ushort, HashSet<ushort>> _oldtargets;

        public Landfill(ushort id, ref Dictionary<ushort, DateTime> master, ref Dictionary<ushort, HashSet<ushort>> oldtargets)
        {
            _settings = Settings.Instance;
            _helper = Helper.Instance;

            _truckCount = ColossalFramework.Globalization.Locale.Get("AIINFO_GARBAGE_TRUCKS");
            _truckCount = _truckCount.Substring(0, _truckCount.IndexOf(":") + 1);

            _id = id;

            _master = master;
            _primary = new HashSet<ushort>();
            _secondary = new HashSet<ushort>();
            _checkups = new List<ushort>();
            _oldtargets = oldtargets;
        }

        public void AddPickup(ushort id)
        {
            if (!_master.ContainsKey(id))
                _master.Add(id, SimulationManager.instance.m_currentGameTime.AddDays((_settings.DispatchGap + 1) * -1));

            if (_primary.Contains(id) || _secondary.Contains(id))
                return;

            if (WithinPrimaryRange(id))
                _primary.Add(id);
            else if (WithinSecondaryRange(id))
                _secondary.Add(id);
        }

        public void AddCheckup(ushort id)
        {
            if (_checkups.Count >= 20)
                return;

            SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;

            if (WithinPrimaryRange(id) && data.IsPrivateBuilding(id))
                _checkups.Add(id);
        }

        private bool WithinPrimaryRange(ushort id)
        {
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            Building landfill = buildings[(int)_id];
            Building target = buildings[(int)id];

            DistrictManager dm = Singleton<DistrictManager>.instance;
            byte district = dm.GetDistrict(landfill.m_position);

            if (district != dm.GetDistrict(target.m_position))
                return false;

            if (district == 0)
                return WithinSecondaryRange(id);

            return true;
        }

        private bool WithinSecondaryRange(ushort id)
        {
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            Building landfill = buildings[(int)_id];
            Building target = buildings[(int)id];

            float range = landfill.Info.m_buildingAI.GetCurrentRange(_id, ref landfill);
            range = range * range;

            float distance = (landfill.m_position - target.m_position).sqrMagnitude;

            return distance <= range;
        }

        public void DispatchIdleVehicle()
        {
            if (SimulationManager.instance.SimulationPaused) return;

            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            Building me = buildings[_id];

            if ((me.m_flags & Building.Flags.Active) == Building.Flags.None && me.m_productionRate == 0) return;

            if ((me.m_flags & Building.Flags.Downgrading) != Building.Flags.None) return;

            if (me.Info.m_buildingAI.IsFull(_id, ref buildings[_id])) return;

            string stats = me.Info.m_buildingAI.GetLocalizedStats(_id, ref buildings[_id]);
            stats = stats.Substring(stats.IndexOf(_truckCount));

            int now;
            int max;

            Match match = Regex.Match(stats, @"[0-9]+");

            if (match.Success)
                now = int.Parse(match.Value);
            else
                return;

            match = match.NextMatch();

            if (match.Success)
                max = int.Parse(match.Value);
            else
                return;

            if (now >= max)
                return;

            ushort target = 0;

            target = GetUnclaimedTarget(_primary);

            if (target == 0)
                target = GetUnclaimedTarget(_secondary);
            
            if (target == 0)
                return;

            TransferManager.TransferOffer offer = default(TransferManager.TransferOffer);
            offer.Building = target;
            offer.Position = buildings[target].m_position;

            me.Info.m_buildingAI.StartTransfer(
                _id,
                ref buildings[_id],
                TransferManager.TransferReason.Garbage,
                offer
            );

            _master[target] = SimulationManager.instance.m_currentGameTime;
        }

        private ushort GetUnclaimedTarget(ICollection<ushort> targets)
        {
            ushort target = 0;
            DateTime lastDispatch = SimulationManager.instance.m_currentGameTime;

            foreach (ushort i in targets)
            {
                if (_master.ContainsKey(i) && _master[i] < lastDispatch && (SimulationManager.instance.m_currentGameTime - _master[i]).TotalDays > _settings.DispatchGap)
                {
                    target = i;
                    lastDispatch = _master[i];
                }
            }

            return target;
        }

        public ushort AssignTarget(ushort truckID)
        {
            Vehicle truck = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[truckID];
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            ushort target = 0;
            ushort current = truck.m_targetBuilding;

            if (truck.m_sourceBuilding != _id)
                return target;

            target = GetClosestTarget(truckID, ref _primary);

            if (target == 0)
                target = GetClosestTarget(truckID, ref _secondary);

            if (target == 0)
            {
                _oldtargets.Remove(truckID);

                if ((truck.m_targetBuilding != 0 && WithinPrimaryRange(truck.m_targetBuilding)) || _checkups.Count == 0)
                    target = truck.m_targetBuilding;
                else
                {
                    target = _checkups[0];
                    _checkups.RemoveAt(0);
                }
            }
            else
            {
                if (target != current)
                {
                    if (_master.ContainsKey(current))
                        _master[current] = SimulationManager.instance.m_currentGameTime.AddDays((_settings.DispatchGap + 1) * -1);
                }

                if (_master.ContainsKey(target))
                    _master[target] = SimulationManager.instance.m_currentGameTime;
            }

            return target;
        }

        private ushort GetClosestTarget(ushort truckID, ref HashSet<ushort> targets)
        {
            Vehicle truck = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[truckID];

            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            List<ushort> removals = new List<ushort>();

            ushort target = truck.m_targetBuilding;
            int targetProblematicLevel = 0;
            float distance = float.PositiveInfinity;

            Vector3 velocity = truck.GetLastFrameVelocity();
            Vector3 position = truck.GetLastFramePosition();

            double bearing = double.PositiveInfinity;
            double facing = Math.Atan2(velocity.z, velocity.x);

            if (targets.Contains(target))
            {
                if (buildings[target].Info.m_buildingAI.GetGarbageAmount(target, ref Singleton<BuildingManager>.instance.m_buildings.m_buffer[target]) <= 2500)
                {
                    removals.Add(target);
                    target = 0;
                }
                else
                {
                    if ((buildings[target].m_problems & Notification.Problem.Garbage) != Notification.Problem.None)
                    {
                        if (Identity.ModConf.PrioritizeTargetWithRedSigns && (buildings[target].m_problems & Notification.Problem.MajorProblem) != Notification.Problem.None)
                        {
                            targetProblematicLevel = 2;
                        }
                        else
                        {
                            targetProblematicLevel = 1;
                        }
                    }

                    Vector3 a = buildings[target].m_position;

                    distance = (a - position).sqrMagnitude;
                    bearing = Math.Atan2(a.z - position.z, a.x - position.x);
                }
            }
            else
                target = 0;

            foreach (ushort id in targets)
            {
                if (target == id)
                    continue;

                if (_oldtargets.ContainsKey(truckID) && _oldtargets[truckID].Contains(id))
                    continue;

                if (!SkylinesOverwatch.Data.Instance.IsBuildingWithGarbage(id))
                {
                    removals.Add(id);
                    continue;
                }

                Vector3 p = buildings[id].m_position;
                float d = (p - position).sqrMagnitude;

                int candidateProblematicLevel = 0;
                if ((buildings[id].m_problems & Notification.Problem.Garbage) != Notification.Problem.None)
                {
                    if (Identity.ModConf.PrioritizeTargetWithRedSigns && (buildings[id].m_problems & Notification.Problem.MajorProblem) != Notification.Problem.None)
                    {
                        candidateProblematicLevel = 2;
                    }
                    else
                    {
                        candidateProblematicLevel = 1;
                    }
                }

                if ((SimulationManager.instance.m_currentGameTime - _master[id]).TotalDays <= _settings.DispatchGap)
                {
                    if (d > 2500)
                        continue;

                    if (d > distance)
                        continue;

                    double angle = Helper.GetAngleDifference(facing, Math.Atan2(p.z - position.z, p.x - position.x));

                    if (angle < -1.5707963267948966 || 1.5707963267948966 < angle)
                        continue;
                }
                else
                {
                    if (targetProblematicLevel > candidateProblematicLevel)
                        continue;

                    if (targetProblematicLevel < candidateProblematicLevel)
                    {
                        // No additonal conditions at the moment. Problematic buildings always have priority over nonproblematic buildings
                    }
                    else
                    {
                        if (d > (distance * 0.9))
                            continue;

                        if (!double.IsPositiveInfinity(bearing))
                        {
                            double angle = Helper.GetAngleDifference(bearing, Math.Atan2(p.z - position.z, p.x - position.x));

                            if (angle < -1.5707963267948966 || 1.5707963267948966 < angle)
                                continue;
                        }
                    }
                }

                target = id;
                targetProblematicLevel = candidateProblematicLevel;
                distance = d;
            }

            foreach (ushort id in removals)
            {
                _master.Remove(id);
                targets.Remove(id);
            }

            return target;
        }
    }
}

