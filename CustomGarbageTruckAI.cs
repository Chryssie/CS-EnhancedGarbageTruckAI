using ColossalFramework;
using ColossalFramework.Math;
using System.Collections.Generic;
using UnityEngine;

namespace EnhancedGarbageTruckAI
{
    class CustomGarbageTruckAI
    {
        public void SetTarget(ushort vehicleID, ref Vehicle data, ushort targetBuilding)
        {
            if (targetBuilding == data.m_targetBuilding)
            {
                if (data.m_path == 0u)
                {
                    if (!StartPathFind(vehicleID, ref data))
                    {
                        data.Unspawn(vehicleID);
                    }
                }
                else
                {
                    data.Info.m_vehicleAI.TrySpawn(vehicleID, ref data);
                }
            }
            else
            {
                if(!data.m_flags.IsFlagSet(Vehicle.Flags.Spawned))
                {
                    if (Identity.ModConf.MinimizeGarbageTrucks)
                    {
                        if (Dispatcher._landfills != null && Dispatcher._landfills.ContainsKey(data.m_sourceBuilding))
                        {
                            if (Dispatcher._landfills[data.m_sourceBuilding]._primary.Count > 0 || Dispatcher._landfills[data.m_sourceBuilding]._secondary.Count > 0 || Dispatcher._landfills[data.m_sourceBuilding]._checkups.Count > 0)
                                if ((!Dispatcher._landfills[data.m_sourceBuilding]._primary.Contains(targetBuilding) && !Dispatcher._landfills[data.m_sourceBuilding]._secondary.Contains(targetBuilding)) || !Helper.IsBuildingWithGarbage(targetBuilding))
                                {
                                    data.Unspawn(vehicleID);
                                    return;
                                }
                        }
                        else
                        {
                            data.Unspawn(vehicleID);
                            return;
                        }
                    }

                    if (Dispatcher._landfills != null && Dispatcher._landfills.ContainsKey(data.m_sourceBuilding))
                    {
                        int max, now;
                        Dispatcher._landfills[data.m_sourceBuilding].CaluculateWorkingVehicles(out max, out now);

                        if (now > max)
                        {
                            data.Unspawn(vehicleID);
                            return;
                        }
                    }
                }
                ushort current = data.m_targetBuilding;

                uint path = data.m_path;
                byte pathPositionIndex = data.m_pathPositionIndex;
                byte lastPathOffset = data.m_lastPathOffset;
                ushort target = targetBuilding;
                
                int vehicleStatus = Dispatcher.GetGarbageTruckStatus(ref data);
                int retry_max = 1;
                if (vehicleStatus == Dispatcher.VEHICLE_STATUS_GARBAGE_WAIT)
                {
                    if(Dispatcher._oldtargets != null) Dispatcher._oldtargets.Remove(vehicleID);
                    retry_max = 20;
                }
                
                for (int retry = 0; retry < retry_max; retry++)
                {
                    if (retry > 0)
                    {
                        if (Dispatcher._landfills == null || !Dispatcher._landfills.ContainsKey(data.m_sourceBuilding)) break;
                        target = Dispatcher._landfills[data.m_sourceBuilding].GetUnclaimedTarget(vehicleID);
                        if (target == 0) break;

                        if (Dispatcher._oldtargets != null)
                        {
                            if (!Dispatcher._oldtargets.ContainsKey(vehicleID))
                                Dispatcher._oldtargets.Add(vehicleID, new HashSet<ushort>());
                            Dispatcher._oldtargets[vehicleID].Add(target);
                        }
                    }
                    
                    RemoveTarget(vehicleID, ref data);
                    data.m_targetBuilding = targetBuilding;
                    data.m_flags &= ~Vehicle.Flags.WaitingTarget;
                    data.m_waitCounter = 0;
                    if (targetBuilding != 0)
                    {
                        Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)targetBuilding].AddGuestVehicle(vehicleID, ref data);
                        if ((Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)targetBuilding].m_flags & Building.Flags.IncomingOutgoing) != Building.Flags.None)
                        {
                            if (data.m_flags.IsFlagSet(Vehicle.Flags.TransferToTarget))
                            {
                                data.m_flags |= Vehicle.Flags.Exporting;
                            }
                            else if (data.m_flags.IsFlagSet(Vehicle.Flags.TransferToSource))
                            {
                                data.m_flags |= Vehicle.Flags.Importing;
                            }
                        }
                    }
                    else
                    {
						
                        if (data.m_flags.IsFlagSet(Vehicle.Flags.TransferToTarget))
                        {
                            if (data.m_transferSize > 0)
                            {
                                TransferManager.TransferOffer offer = default(TransferManager.TransferOffer);
                                offer.Priority = 7;
                                offer.Vehicle = vehicleID;
                                if (data.m_sourceBuilding != 0)
                                {
                                    offer.Position = (data.GetLastFramePosition() + Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)data.m_sourceBuilding].m_position) * 0.5f;
                                }
                                else
                                {
                                    offer.Position = data.GetLastFramePosition();
                                }
                                offer.Amount = 1;
                                offer.Active = true;
                                Singleton<TransferManager>.instance.AddOutgoingOffer((TransferManager.TransferReason)data.m_transferType, offer);
                                data.m_flags |= Vehicle.Flags.WaitingTarget;
                            }
                            else
                            {
                                data.m_flags |= Vehicle.Flags.GoingBack;
                            }
                        }
                        if (data.m_flags.IsFlagSet(Vehicle.Flags.TransferToSource))
                        {
                            VehicleInfo m_info = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].Info;
                            if ((int)data.m_transferSize < ((GarbageTruckAI)m_info.m_vehicleAI).m_cargoCapacity && !ShouldReturnToSource(vehicleID, ref data))
                            {
                                TransferManager.TransferOffer offer2 = default(TransferManager.TransferOffer);
                                offer2.Priority = 7;
                                offer2.Vehicle = vehicleID;
                                if (data.m_sourceBuilding != 0)
                                {
                                    offer2.Position = (data.GetLastFramePosition() + Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)data.m_sourceBuilding].m_position) * 0.5f;
                                }
                                else
                                {
                                    offer2.Position = data.GetLastFramePosition();
                                }
                                offer2.Amount = 1;
                                offer2.Active = true;
                                Singleton<TransferManager>.instance.AddIncomingOffer((TransferManager.TransferReason)data.m_transferType, offer2);
                                data.m_flags |= Vehicle.Flags.WaitingTarget;
                            }
                            else
                            {
                                data.m_flags |= Vehicle.Flags.GoingBack;
                            }
                        }
                    }

                    if ((targetBuilding == 0 ||
                        (vehicleStatus != Dispatcher.VEHICLE_STATUS_GARBAGE_COLLECT && vehicleStatus != Dispatcher.VEHICLE_STATUS_GARBAGE_WAIT)))
                    {
                        if (!StartPathFind(vehicleID, ref data))
                        {
                            data.Unspawn(vehicleID);
                        }
                        return;
                    }

                    if (StartPathFind(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID]))
                    {
                        if (Dispatcher._oldtargets != null)
                        {
                            if (!Dispatcher._oldtargets.ContainsKey(vehicleID))
                                Dispatcher._oldtargets.Add(vehicleID, new HashSet<ushort>());
                            Dispatcher._oldtargets[vehicleID].Add(target);
                        }
                        if (Dispatcher._master != null)
                        {
                            if (Dispatcher._master.ContainsKey(target))
                            {
                                if (Dispatcher._master[target].Vehicle != vehicleID)
                                    Dispatcher._master[target] = new Claimant(vehicleID, target);
                            }
                            else if (target != 0)
                                Dispatcher._master.Add(target, new Claimant(vehicleID, target));
                        }
                        return;
                    }
                }

                if (vehicleStatus == Dispatcher.VEHICLE_STATUS_GARBAGE_COLLECT)
                {
                    target = current;
                    RemoveTarget(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID]);
                    Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_targetBuilding = target;
                    Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_path = path;
                    Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_pathPositionIndex = pathPositionIndex;
                    Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_lastPathOffset = lastPathOffset;
                    Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)current].AddGuestVehicle(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID]);

                    if (Dispatcher._master != null)
                    {
                        if (Dispatcher._master.ContainsKey(target))
                        {
                            if (Dispatcher._master[target].Vehicle != vehicleID)
                                Dispatcher._master[target] = new Claimant(vehicleID, target);
                        }
                        else if (target != 0)
                            Dispatcher._master.Add(target, new Claimant(vehicleID, target));
                    }
                }
                else
                {
                    Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].Unspawn(vehicleID);
                }
            }
        }

        private static bool ShouldReturnToSource(ushort vehicleID, ref Vehicle data)
        {
            if (data.m_sourceBuilding != 0)
            {
                BuildingManager instance = Singleton<BuildingManager>.instance;
                if ((instance.m_buildings.m_buffer[(int)data.m_sourceBuilding].m_productionRate == 0 || (instance.m_buildings.m_buffer[(int)data.m_sourceBuilding].m_flags & (Building.Flags.Downgrading | Building.Flags.BurnedDown)) != Building.Flags.None) && instance.m_buildings.m_buffer[(int)data.m_sourceBuilding].m_fireIntensity == 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void RemoveTarget(ushort vehicleID, ref Vehicle data)
        {
            if (data.m_targetBuilding != 0)
            {
                Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)data.m_targetBuilding].RemoveGuestVehicle(vehicleID, ref data);
                data.m_targetBuilding = 0;
            }
        }

        private static bool StartPathFind(ushort vehicleID, ref Vehicle vehicleData)
        {
            VehicleInfo m_info = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].Info;
            if (vehicleData.m_flags.IsFlagSet(Vehicle.Flags.WaitingTarget))
            {
                return true;
            }
            if (vehicleData.m_flags.IsFlagSet(Vehicle.Flags.GoingBack))
            {
                if (vehicleData.m_sourceBuilding != 0)
                {
                    BuildingManager instance = Singleton<BuildingManager>.instance;
                    BuildingInfo info = instance.m_buildings.m_buffer[(int)vehicleData.m_sourceBuilding].Info;
                    Randomizer randomizer = new Randomizer((int)vehicleID);
                    Vector3 vector;
                    Vector3 endPos;
                    info.m_buildingAI.CalculateUnspawnPosition(vehicleData.m_sourceBuilding, ref instance.m_buildings.m_buffer[(int)vehicleData.m_sourceBuilding], ref randomizer, m_info, out vector, out endPos);
                    return StartPathFind(vehicleID, ref vehicleData, vehicleData.m_targetPos3, endPos);
                }
            }
            else if (vehicleData.m_targetBuilding != 0)
            {
                BuildingManager instance2 = Singleton<BuildingManager>.instance;
                BuildingInfo info2 = instance2.m_buildings.m_buffer[(int)vehicleData.m_targetBuilding].Info;
                Randomizer randomizer2 = new Randomizer((int)vehicleID);
                Vector3 vector2;
                Vector3 endPos2;
                info2.m_buildingAI.CalculateUnspawnPosition(vehicleData.m_targetBuilding, ref instance2.m_buildings.m_buffer[(int)vehicleData.m_targetBuilding], ref randomizer2, m_info, out vector2, out endPos2);
                return StartPathFind(vehicleID, ref vehicleData, vehicleData.m_targetPos3, endPos2);
            }
            return false;
        }

        protected static bool StartPathFind(ushort vehicleID, ref Vehicle vehicleData, Vector3 startPos, Vector3 endPos)
        {
            return StartPathFind(vehicleID, ref vehicleData, startPos, endPos, true, true);
        }

        protected static bool StartPathFind(ushort vehicleID, ref Vehicle vehicleData, Vector3 startPos, Vector3 endPos, bool startBothWays, bool endBothWays)
        {
            VehicleInfo info = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].Info;
			bool allowUnderground = vehicleData.m_flags.IsFlagSet(Vehicle.Flags.Underground) || vehicleData.m_flags.IsFlagSet(Vehicle.Flags.Transition);
            PathUnit.Position startPosA;
            PathUnit.Position startPosB;
            float num;
            float num2;
            PathUnit.Position endPosA;
            PathUnit.Position endPosB;
            float num3;
            float num4;
            if (PathManager.FindPathPosition(startPos, ItemClass.Service.Road, NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, info.m_vehicleType, allowUnderground, false, 32f, out startPosA, out startPosB, out num, out num2) && PathManager.FindPathPosition(endPos, ItemClass.Service.Road, NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, info.m_vehicleType, false, false, 32f, out endPosA, out endPosB, out num3, out num4))
            {
                if (!startBothWays || num < 10f)
                {
                    startPosB = default(PathUnit.Position);
                }
                if (!endBothWays || num3 < 10f)
                {
                    endPosB = default(PathUnit.Position);
                }
                uint path;
                if (Singleton<PathManager>.instance.CreatePath(out path, ref Singleton<SimulationManager>.instance.m_randomizer, Singleton<SimulationManager>.instance.m_currentBuildIndex, startPosA, startPosB, endPosA, endPosB, NetInfo.LaneType.Vehicle, info.m_vehicleType, 20000f, IsHeavyVehicle(), IgnoreBlocked(vehicleID, ref vehicleData), false, false))
                {
                    if (vehicleData.m_path != 0u)
                    {
                        Singleton<PathManager>.instance.ReleasePath(vehicleData.m_path);
                    }
                    vehicleData.m_path = path;
                    vehicleData.m_flags |= Vehicle.Flags.WaitingPath;
                    return true;
                }
            }
            return false;
        }

        protected static bool IsHeavyVehicle()
        {
            return false;
        }

        protected static bool IgnoreBlocked(ushort vehicleID, ref Vehicle vehicleData)
        {
            return false;
        }
    }
}
