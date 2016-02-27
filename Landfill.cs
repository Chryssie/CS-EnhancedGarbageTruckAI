using ColossalFramework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnhancedGarbageTruckAI
{
    public class Landfill
    {
        [Flags]
        private enum SearchDirection : byte
        {
            None = 0,
            Ahead = 1,
            Left = 2,
            Right = 4
        }

        private readonly ushort _buildingID;

        private Dictionary<ushort, Claimant> _master;
        public HashSet<ushort> _primary;
        public HashSet<ushort> _secondary;
        public List<ushort> _checkups;
        private Dictionary<ushort, HashSet<ushort>> _oldtargets;
        private Dictionary<ushort, DateTime> _lastchangetimes;

        public Landfill(ushort id, ref Dictionary<ushort, Claimant> master, ref Dictionary<ushort, HashSet<ushort>> oldtargets, ref Dictionary<ushort, DateTime> lastchangetimes)
        {
            _buildingID = id;

            _master = master;
            _primary = new HashSet<ushort>();
            _secondary = new HashSet<ushort>();
            _checkups = new List<ushort>();
            _oldtargets = oldtargets;
            _lastchangetimes = lastchangetimes;
        }

        public void AddPickup(ushort id)
        {
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

            if (WithinPrimaryRange(id) && SkylinesOverwatch.Data.Instance.IsPrivateBuilding(id))
                _checkups.Add(id);
        }

        private bool WithinPrimaryRange(ushort id)
        {
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            Building landfill = buildings[(int)_buildingID];
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

            Building landfill = buildings[(int)_buildingID];
            Building target = buildings[(int)id];

            float range = landfill.Info.m_buildingAI.GetCurrentRange(_buildingID, ref landfill);
            range = range * range;

            float distance = (landfill.m_position - target.m_position).sqrMagnitude;

            return distance <= range;
        }

        public void DispatchIdleVehicle()
        {
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            Building me = buildings[_buildingID];

            if ((me.m_flags & Building.Flags.Active) == Building.Flags.None && me.m_productionRate == 0) return;

            if ((me.m_flags & Building.Flags.Downgrading) != Building.Flags.None) return;

            if (me.Info.m_buildingAI.IsFull(_buildingID, ref buildings[_buildingID])) return;
            int max, now;
            CaluculateWorkingVehicles(out max, out now);

            if (now >= max)
                return;
            ushort target = GetUnclaimedTarget();

            if (target == 0)
                return;

            TransferManager.TransferOffer offer = default(TransferManager.TransferOffer);
            offer.Building = target;
            offer.Position = buildings[target].m_position;

            me.Info.m_buildingAI.StartTransfer(
                _buildingID,
                ref buildings[_buildingID],
                TransferManager.TransferReason.Garbage,
                offer
            );
        }

        public void CaluculateWorkingVehicles(out int max, out int now)
        {
            max = (PlayerBuildingAI.GetProductionRate(100, Singleton<EconomyManager>.instance.GetBudget(Singleton<BuildingManager>.instance.m_buildings.m_buffer[_buildingID].Info.m_class)) * ((LandfillSiteAI)Singleton<BuildingManager>.instance.m_buildings.m_buffer[_buildingID].Info.m_buildingAI).m_garbageTruckCount + 99) / 100;
            now = 0;
            VehicleManager instance = Singleton<VehicleManager>.instance;
            ushort num = Singleton<BuildingManager>.instance.m_buildings.m_buffer[_buildingID].m_ownVehicles;
            while (num != 0)
            {
                if ((TransferManager.TransferReason)instance.m_vehicles.m_buffer[(int)num].m_transferType == TransferManager.TransferReason.Garbage)
                {
                    now++;
                }
                num = instance.m_vehicles.m_buffer[(int)num].m_nextOwnVehicle;
            }
        }

        public ushort GetUnclaimedTarget(ushort vehicleID = 0)
        {
            ushort target = 0;

            target = GetUnclaimedTarget(_primary, vehicleID);
            if (target == 0)
                target = GetUnclaimedTarget(_secondary, vehicleID);
            if (vehicleID != 0 && target == 0 && _checkups.Count > 0)
            {
                target = _checkups[0];
                _checkups.RemoveAt(0);
            }

            return target;
        }

        private ushort GetUnclaimedTarget(ICollection<ushort> targets, ushort vehicleID)
        {
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            List<ushort> removals = new List<ushort>();

            ushort target = 0;
            int targetProblematicLevel = 0;
            float distance = float.PositiveInfinity;

            Building landfill = buildings[(int)_buildingID];
            foreach (ushort id in targets)
            {
                if (target == id)
                    continue;

                if (_oldtargets.ContainsKey(vehicleID) && _oldtargets[vehicleID].Contains(id))
                    continue;

                if (!Helper.IsBuildingWithGarbage(id))
                {
                    removals.Add(id);
                    continue;
                }

                Vector3 p = buildings[id].m_position;
                float d = (p - landfill.m_position).sqrMagnitude;

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
                if (_master.ContainsKey(id) && _master[id].IsValid)
                {
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
                        if (d > distance)
                            continue;
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

        public ushort AssignTarget(ushort vehicleID)
        {
            Vehicle vehicle = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID];
            ushort target = 0;

            if (vehicle.m_sourceBuilding != _buildingID)
                return target;

            if (Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicle.m_path].m_nextPathUnit == 0)
            {
                byte b = vehicle.m_pathPositionIndex;
                if (b == 255)
                {
                    b = 0;
                }
                if ((b & 1) == 0)
                {
                    b += 1;
                }
                if ((b >> 1) + 1 >= Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicle.m_path].m_positionCount)
                    return target;
            }

            ushort current = vehicle.m_targetBuilding;
            if (!Helper.IsBuildingWithGarbage(current))
            {
                _oldtargets.Remove(vehicleID);
                _master.Remove(current);
                _primary.Remove(current);
                _secondary.Remove(current);

                current = 0;
            }
            else if (_master.ContainsKey(current))
            {
                if (_master[current].IsValid && _master[current].Vehicle != vehicleID)
                    current = 0;
            }

            int vehicleStatus = Dispatcher.GetGarbageTruckStatus(ref vehicle);
            if (current != 0 && vehicleStatus == Dispatcher.VEHICLE_STATUS_GARBAGE_COLLECT && _lastchangetimes.ContainsKey(vehicleID) && (SimulationManager.instance.m_currentGameTime - _lastchangetimes[vehicleID]).TotalDays < 0.5)
                return target;

            bool immediateOnly = (_primary.Contains(current) || _secondary.Contains(current));
            SearchDirection immediateDirection = GetImmediateSearchDirection(vehicleID);

            if (immediateOnly && immediateDirection == SearchDirection.None)
                target = current;
            else
            {
                target = GetClosestTarget(vehicleID, ref _primary, immediateOnly, immediateDirection);

                if (target == 0)
                    target = GetClosestTarget(vehicleID, ref _secondary, immediateOnly, immediateDirection);
            }

            if (target == 0)
            {
                _oldtargets.Remove(vehicleID);

                if ((vehicle.m_targetBuilding != 0 && WithinPrimaryRange(vehicle.m_targetBuilding)) || _checkups.Count == 0)
                    target = vehicle.m_targetBuilding;
                else
                {
                    target = _checkups[0];
                    _checkups.RemoveAt(0);
                }
            }

            return target;
        }

        private SearchDirection GetImmediateSearchDirection(ushort vehicleID)
        {
            Vehicle vehicle = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID];

            PathManager pm = Singleton<PathManager>.instance;

            PathUnit pu = pm.m_pathUnits.m_buffer[vehicle.m_path];

            byte pi = vehicle.m_pathPositionIndex;
            if (pi == 255) pi = 0;

            PathUnit.Position position = pu.GetPosition(pi >> 1);

            NetManager nm = Singleton<NetManager>.instance;

            NetSegment segment = nm.m_segments.m_buffer[position.m_segment];

            int laneCount = 0;

            int leftLane = -1;
            float leftPosition = float.PositiveInfinity;

            int rightLane = -1;
            float rightPosition = float.NegativeInfinity;

            for (int i = 0; i < segment.Info.m_lanes.Length; i++)
            {
                NetInfo.Lane l = segment.Info.m_lanes[i];

                if (l.m_laneType != NetInfo.LaneType.Vehicle || l.m_vehicleType != VehicleInfo.VehicleType.Car)
                    continue;

                laneCount++;

                if (l.m_position < leftPosition)
                {
                    leftLane = i;
                    leftPosition = l.m_position;
                }

                if (l.m_position > rightPosition)
                {
                    rightLane = i;
                    rightPosition = l.m_position;
                }
            }

            SearchDirection dir = SearchDirection.None;

            if (laneCount == 0)
            {
            }
            else if (position.m_lane != leftLane && position.m_lane != rightLane)
            {
                dir = SearchDirection.Ahead;
            }
            else if (leftLane == rightLane)
            {
                dir = SearchDirection.Left | SearchDirection.Right | SearchDirection.Ahead;
            }
            else if (laneCount == 2 && segment.Info.m_lanes[leftLane].m_direction != segment.Info.m_lanes[rightLane].m_direction)
            {
                dir = SearchDirection.Left | SearchDirection.Right | SearchDirection.Ahead;
            }
            else
            {
                if (position.m_lane == leftLane)
                    dir = SearchDirection.Left | SearchDirection.Ahead;
                else
                    dir = SearchDirection.Right | SearchDirection.Ahead;
            }

            return dir;
        }

        private ushort GetClosestTarget(ushort vehicleID, ref HashSet<ushort> targets, bool immediateOnly, SearchDirection immediateDirection)
        {
            Vehicle vehicle = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID];

            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            List<ushort> removals = new List<ushort>();

            ushort target = vehicle.m_targetBuilding;
            if (_master.ContainsKey(target) && _master[target].IsValid && _master[target].Vehicle != vehicleID)
                target = 0;
            int targetProblematicLevel = 0;
            float targetdistance = float.PositiveInfinity;
            float distance = float.PositiveInfinity;

            Vector3 velocity = vehicle.GetLastFrameVelocity();
            Vector3 position = vehicle.GetLastFramePosition();

            double bearing = double.PositiveInfinity;
            double facing = Math.Atan2(velocity.z, velocity.x);

            if (targets.Contains(target))
            {
                if (!Helper.IsBuildingWithGarbage(target))
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

                    targetdistance = distance = (a - position).sqrMagnitude;

                    bearing = Math.Atan2(a.z - position.z, a.x - position.x);
                }
            }
            else if (!immediateOnly)
                target = 0;

            foreach (ushort id in targets)
            {
                if (target == id)
                    continue;

                if (!Helper.IsBuildingWithGarbage(id))
                {
                    removals.Add(id);
                    continue;
                }

                if (_master.ContainsKey(id) && _master[id].IsValid && !_master[id].IsChallengable)
                    continue;

                if (_master.ContainsKey(id) && _master[id].IsValid && _master[id].Vehicle != vehicleID)
                {
                    Vehicle vehicle2 = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[_master[id].Vehicle];
                    if ((vehicle2.m_flags & Vehicle.Flags.Spawned) != Vehicle.Flags.None && vehicle2.m_path != 0
                        && Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicle2.m_path].m_nextPathUnit == 0)
                    {
                        byte b = vehicle2.m_pathPositionIndex;
                        if (b == 255)
                        {
                            b = 0;
                        }
                        if ((b & 1) == 0)
                        {
                            b += 1;
                        }
                        if ((b >> 1) + 1 >= Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicle2.m_path].m_positionCount)
                            continue;
                    }
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

                if (_master.ContainsKey(id) && _master[id].IsValid && _master[id].IsChallengable)
                {
                    if (targetProblematicLevel > candidateProblematicLevel)
                        continue;

                    if (d > targetdistance * 0.9)
                        continue;

                    if (d > distance)
                        continue;

                    if (d > _master[id].Distance * 0.9)
                        continue;

                    double angle = Helper.GetAngleDifference(facing, Math.Atan2(p.z - position.z, p.x - position.x));

                    int immediateLevel = GetImmediateLevel(d, angle, immediateDirection);

                    if (immediateLevel == 0)
                        continue;

                    if (_oldtargets.ContainsKey(vehicleID) && _oldtargets[vehicleID].Contains(id))
                        continue;
                }
                else
                {
                    double angle = Helper.GetAngleDifference(facing, Math.Atan2(p.z - position.z, p.x - position.x));
                    int immediateLevel = GetImmediateLevel(d, angle, immediateDirection);

                    if (immediateOnly && immediateLevel == 0)
                        continue;

                    if (_oldtargets.ContainsKey(vehicleID) && _oldtargets[vehicleID].Contains(id))
                        continue;

                    if (targetProblematicLevel > candidateProblematicLevel)
                        continue;

                    if (targetProblematicLevel < candidateProblematicLevel)
                    {
                        // No additonal conditions at the moment. Problematic buildings always have priority over nonproblematic buildings
                    }
                    else
                    {
                        if (d > targetdistance * 0.9)
                            continue;

                        if (d > distance)
                            continue;

                        if (immediateLevel > 0)
                        {
                            // If it's that close, no need to further qualify its priority
                        }
                        else if (IsAlongTheWay(d, angle))
                        {
                            // If it's in the general direction the vehicle is facing, it's good enough
                        }
                        else if (!double.IsPositiveInfinity(bearing))
                        {
                            if (IsAlongTheWay(d, Helper.GetAngleDifference(bearing, Math.Atan2(p.z - position.z, p.x - position.x))))
                            {
                                // If it's in the general direction along the vehicle's target path, we will have to settle for it at this point
                            }
                            else
                                continue;
                        }
                        else
                        {
                            // If it's not closeby and not in the direction the vehicle is facing, but our vehicle also has no bearing, we will take whatever is out there
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

        private int GetImmediateLevel(float distance, double angle, SearchDirection immediateDirection)
        {
            // -90 degrees to 90 degrees. This is the default search angle
            double l = -1.5707963267948966;
            double r = 1.5707963267948966;

            if (distance < Settings.Instance.ImmediateRange1)
            {
                // Prevent searching on the non-neighboring side
                if ((immediateDirection & SearchDirection.Left) == SearchDirection.None) l = 0;
                if ((immediateDirection & SearchDirection.Right) == SearchDirection.None) r = 0;
                if (l <= angle && angle <= r) return 2;
            }
            else if (distance < Settings.Instance.ImmediateRange2 && (immediateDirection & SearchDirection.Ahead) != SearchDirection.None)
            {
                // Restrict the search on the non-neighboring side to 60 degrees to give enough space for merging
                if ((immediateDirection & SearchDirection.Left) == SearchDirection.None) l = -1.0471975512;
                if ((immediateDirection & SearchDirection.Right) == SearchDirection.None) r = 1.0471975512;
                if (l <= angle && angle <= r) return 1;
            }
            return 0;
        }

        private bool IsAlongTheWay(float distance, double angle)
        {
            if (distance < Settings.Instance.ImmediateRange2) // This is within the immediate range. Use IsImmediate() instead
                return false;

            // -90 degrees to 90 degrees. This is the default search angle
            return -1.5707963267948966 <= angle && angle <= 1.5707963267948966;
        }
    }
}

