/*
 * -----------------------------------------------------------------
 * Copyright (c) 2012 Intel Corporation
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are
 * met:
 *
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *
 *     * Redistributions in binary form must reproduce the above
 *       copyright notice, this list of conditions and the following
 *       disclaimer in the documentation and/or other materials provided
 *       with the distribution.
 *
 *     * Neither the name of the Intel Corporation nor the names of its
 *       contributors may be used to endorse or promote products derived
 *       from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 * "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 * A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE INTEL OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
 * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
 * PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
 * LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 * EXPORT LAWS: THIS LICENSE ADDS NO RESTRICTIONS TO THE EXPORT LAWS OF
 * YOUR JURISDICTION. It is licensee's responsibility to comply with any
 * export regulations applicable in licensee's jurisdiction. Under
 * CURRENT (May 2000) U.S. export regulations this software is eligible
 * for export from the U.S. and can be downloaded by or otherwise
 * exported or reexported worldwide EXCEPT to U.S. embargoed destinations
 * which include Cuba, Iraq, Libya, North Korea, Iran, Syria, Sudan,
 * Afghanistan and any other country to which the U.S. has embargoed
 * goods and services.
 * -----------------------------------------------------------------
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using log4net;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace DSG.RegionSync
{
    public class SyncedProperty
    {
        //private ILog m_log;
        public static ILog DebugLog;
        private Object m_lock = new Object();

        public long LastUpdateTimeStamp { get; private set; }
        public string LastUpdateSyncID { get; private set; }

        /// <summary>
        /// The value of the most recent value sent/received by Sync Module.
        /// For property with simple types, the value is copied directly. 
        /// For property with complex data structures, the value (values of
        /// subproperties) is serialized and stored.
        /// </summary>
        public Object LastUpdateValue { get; private set; }

        /// <summary>
        /// Record the time the last sync message about this property is received.
        /// This value is only meaninful when LastUpdateSource==BySync
        /// </summary>
        public long LastSyncUpdateRecvTime { get; set; }
        public PropertyUpdateSource LastUpdateSource { get; private set; }
        public SyncableProperties.Type Property { get; private set; }

        // Constructor
        public SyncedProperty(SyncableProperties.Type property, Object initValue, long initTS, string syncID)
        {
            Property = property;
            LastUpdateValue = initValue;
            LastUpdateTimeStamp = initTS;
            LastUpdateSyncID = syncID;
        }

        /// <summary>
        /// Initialize from data in given OSDArray.
        /// </summary>
        /// <param name="property"></param>
        /// <param name="syncData"></param>
        public SyncedProperty(OSDArray syncData)
        {
            FromOSDArray(syncData);
        }

        public static string SyncIDTieBreak(string aID, string bID)
        {
            if (aID.CompareTo(bID)>0)
                return aID;
            else
                return bID;
        }

        /// <summary>
        /// Update SyncInfo when the property is updated locally. This interface
        /// is for properties of simple types.
        /// </summary>
        /// <param name="ts"></param>
        /// <param name="syncID"></param>
        /// <param name="pValue"></param>
        public void UpdateSyncInfoByLocal(long ts, string syncID, Object pValue)
        {
            // DebugLog.WarnFormat("[SYNCED PROPERTY] UpdateSyncInfoByLocal property={0}", Property.ToString());
            lock (m_lock)
            {
                LastUpdateValue = pValue;
                LastUpdateTimeStamp = ts;
                LastUpdateSyncID = syncID;
                LastUpdateSource = PropertyUpdateSource.Local;
            }
        }

        /// <summary>
        /// Compare the local timestamp with that in pSyncInfo. If the one in
        /// pSyncInfo is newer, copy its members to the local record.
        /// </summary>
        /// <param name="pSyncInfo"></param>
        /// <returns>'true' if the received sync value was accepted and the sync cache was updated with the received value</returns>
        public bool CompareAndUpdateSyncInfoBySync(SyncedProperty pSyncInfo, long recvTS)
        {
            // KLUDGE!!! THIS TRIES TO FORCE THE SCRIPT SETTING OF ANIMATIONS VS DEFAULT ANIMATIONS.
            // Unconditionally accept an animation update if the received update has extended animations
            //     and the existing animation is a default animation. This case is most often
            //     from a script setting a sit or animation override.
            if (RegionSyncModule.ShouldUnconditionallyAcceptAnimationOverrides && Property == SyncableProperties.Type.Animations)
            {
                OSDArray receivedAnimations = pSyncInfo.LastUpdateValue as OSDArray;
                OSDArray existingAnimations = LastUpdateValue as OSDArray;
                if (receivedAnimations != null && existingAnimations != null)
                {
                    /*
                    // If going from default animation to something better, always accept it
                    if (receivedAnimations.Count > 2 && existingAnimations.Count <= 2)
                    {
                        // The new animation has more info than the existing so accept it.
                        // DebugLog.DebugFormat("{0} SyncedProperty.CompareAndUpdateSyncInfoBySync: forcing. receivedCnt={1}, existingCnt={2}",    // DEBUG DEBUG
                        //         "[DSG SYNCED PROPERTY]", receivedAnimations.Count, existingAnimations.Count);    // DEBUG DEBUG
                        UpdateSyncInfoBySync(pSyncInfo.LastUpdateTimeStamp, pSyncInfo.LastUpdateSyncID, pSyncInfo.LastUpdateValue, recvTS);
                        return true;
                    }
                    */
                    // For the moment, just accept received animations
                    UpdateSyncInfoBySync(pSyncInfo.LastUpdateTimeStamp, pSyncInfo.LastUpdateSyncID, pSyncInfo.LastUpdateValue, recvTS);
                    return true;
                }
            }

            // If update timestamp is later, or we do some tie breaking, then update
            if ((pSyncInfo.LastUpdateTimeStamp > LastUpdateTimeStamp) ||
                ((pSyncInfo.LastUpdateTimeStamp == LastUpdateTimeStamp) &&
                 (!LastUpdateSyncID.Equals(pSyncInfo.LastUpdateSyncID)) &&
                 (SyncIDTieBreak(LastUpdateSyncID, pSyncInfo.LastUpdateSyncID).Equals(pSyncInfo.LastUpdateSyncID))))
            {
                UpdateSyncInfoBySync(pSyncInfo.LastUpdateTimeStamp, pSyncInfo.LastUpdateSyncID, pSyncInfo.LastUpdateValue, recvTS);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Update SyncInfo when the property is updated by receiving a sync 
        /// message.
        /// </summary>
        /// <param name="ts"></param>
        /// <param name="syncID"></param>
        public void UpdateSyncInfoBySync(long ts, string syncID, Object pValue, long recvTS)
        {
            lock (m_lock)
            {
                LastUpdateValue = pValue;
                LastUpdateTimeStamp = ts;
                LastUpdateSyncID = syncID;
                LastSyncUpdateRecvTime = recvTS;
                LastUpdateSource = PropertyUpdateSource.BySync;
            }
        }

        /// <summary>
        /// Convert the value of the given property to OSD type.
        /// </summary>
        /// <param name="property"></param>
        /// <returns></returns>
        public OSDArray ToOSDArray()
        {
            //DebugLog.WarnFormat("[SYNCED PROPERTY] ToOSDArray called for property {0}", Property.ToString());
            lock (m_lock)
            {
                OSDArray propertyData = new OSDArray();
                propertyData.Add(OSD.FromInteger((uint)Property));
                propertyData.Add(OSD.FromLong(LastUpdateTimeStamp));
                propertyData.Add(OSD.FromString(LastUpdateSyncID));

                OSD value = null;
                switch (Property)
                {
                    ///////////////////////////////////////
                    //SOP properties with complex structure
                    ///////////////////////////////////////
                    case SyncableProperties.Type.AgentCircuitData:
                    case SyncableProperties.Type.AvatarAppearance:
                        value = (OSDMap)LastUpdateValue;
                        break;

                    case SyncableProperties.Type.Animations:
                        value = (OSDArray)LastUpdateValue;
                        break;

                    ////////////////////////////
                    // Integer/enum type properties
                    ////////////////////////////
                    case SyncableProperties.Type.CreationDate:          // int
                    case SyncableProperties.Type.LinkNum:               // int
                    case SyncableProperties.Type.OwnershipCost:         // int
                    case SyncableProperties.Type.SalePrice:             // int
                    case SyncableProperties.Type.ScriptAccessPin:       // int
                    case SyncableProperties.Type.AggregateScriptEvents: // enum
                    case SyncableProperties.Type.Flags:                 // enum
                    case SyncableProperties.Type.LocalFlags:            // enum
                    case SyncableProperties.Type.PresenceType:          // enum
                        value = OSD.FromInteger((int)LastUpdateValue);
                        break;

                    ////////////////////////////
                    // Byte type properties
                    ////////////////////////////
                    case SyncableProperties.Type.ClickAction:
                    case SyncableProperties.Type.Material:
                    case SyncableProperties.Type.ObjectSaleType:
                        value = OSD.FromInteger((byte)LastUpdateValue);
                        break;

                    ////////////////////////////
                    // Boolean type properties
                    ////////////////////////////
                    case SyncableProperties.Type.AllowedDrop:
                    case SyncableProperties.Type.IsAttachment:
                    case SyncableProperties.Type.PassTouches:
                    case SyncableProperties.Type.VolumeDetectActive:
                    case SyncableProperties.Type.Flying:
                    case SyncableProperties.Type.IsColliding:
                    case SyncableProperties.Type.CollidingGround:
                    case SyncableProperties.Type.Kinematic:
                    case SyncableProperties.Type.IsSelected:
                    case SyncableProperties.Type.AllowMovement:
                        value = OSD.FromBoolean((bool)LastUpdateValue);
                        break;

                    ////////////////////////////
                    // Vector3 type properties
                    ////////////////////////////
                    case SyncableProperties.Type.AngularVelocity:
                    case SyncableProperties.Type.AttachedPos:
                    case SyncableProperties.Type.GroupPosition:
                    case SyncableProperties.Type.OffsetPosition:
                    case SyncableProperties.Type.Scale:
                    case SyncableProperties.Type.SitTargetPosition:
                    case SyncableProperties.Type.SitTargetPositionLL:
                    case SyncableProperties.Type.SOP_Acceleration:
                    case SyncableProperties.Type.Velocity:
                    case SyncableProperties.Type.Force:
                    case SyncableProperties.Type.PA_Acceleration:
                    case SyncableProperties.Type.PA_Velocity:
                    case SyncableProperties.Type.PA_TargetVelocity:
                    case SyncableProperties.Type.Position:
                    case SyncableProperties.Type.RotationalVelocity:
                    case SyncableProperties.Type.Size:
                    case SyncableProperties.Type.Torque:
                    case SyncableProperties.Type.AbsolutePosition:
                        value = OSD.FromVector3((Vector3)LastUpdateValue);
                        break;

                    ////////////////////////////
                    // UUID type properties
                    ////////////////////////////
                    case SyncableProperties.Type.AttachedAvatar:
                    case SyncableProperties.Type.CollisionSound:
                    case SyncableProperties.Type.CreatorID:
                    case SyncableProperties.Type.FolderID:
                    case SyncableProperties.Type.GroupID:
                    case SyncableProperties.Type.LastOwnerID:
                    case SyncableProperties.Type.OwnerID:
                    case SyncableProperties.Type.Sound:
                        value = OSD.FromUUID((UUID)LastUpdateValue);
                        break;

                    ////////////////////////////
                    // UInt type properties
                    ////////////////////////////
                    case SyncableProperties.Type.LocalId:
                    case SyncableProperties.Type.AttachmentPoint:
                    case SyncableProperties.Type.BaseMask:
                    case SyncableProperties.Type.Category:
                    case SyncableProperties.Type.EveryoneMask:
                    case SyncableProperties.Type.GroupMask:
                    case SyncableProperties.Type.InventorySerial:
                    case SyncableProperties.Type.NextOwnerMask:
                    case SyncableProperties.Type.OwnerMask:
                    case SyncableProperties.Type.AgentControlFlags:
                    case SyncableProperties.Type.ParentId:
                        value = OSD.FromUInteger((uint)LastUpdateValue);
                        break;

                    ////////////////////////////
                    // Float type properties
                    ////////////////////////////
                    case SyncableProperties.Type.CollisionSoundVolume:
                    case SyncableProperties.Type.Buoyancy:
                        value = OSD.FromReal((float)LastUpdateValue);
                        break;
                    
                    ////////////////////////////
                    // String type properties
                    ////////////////////////////
                    case SyncableProperties.Type.Color:
                    case SyncableProperties.Type.CreatorData:
                    case SyncableProperties.Type.Description:
                    case SyncableProperties.Type.MediaUrl:
                    case SyncableProperties.Type.Name:
                    case SyncableProperties.Type.RealRegion:
                    case SyncableProperties.Type.Shape:
                    case SyncableProperties.Type.SitName:
                    case SyncableProperties.Type.TaskInventory:
                    case SyncableProperties.Type.Text:
                    case SyncableProperties.Type.TouchName:
                        value = OSD.FromString((string)LastUpdateValue);
                        break;

                    ////////////////////////////
                    // byte[] (binary data) type properties
                    ////////////////////////////
                    case SyncableProperties.Type.ParticleSystem:
                    case SyncableProperties.Type.TextureAnimation:
                        value = OSD.FromBinary((byte[])LastUpdateValue);
                        break;

                    ////////////////////////////
                    // Quaternion type properties
                    ////////////////////////////
                    case SyncableProperties.Type.RotationOffset:
                    case SyncableProperties.Type.SitTargetOrientation:
                    case SyncableProperties.Type.SitTargetOrientationLL:
                    case SyncableProperties.Type.Orientation:
                    case SyncableProperties.Type.Rotation:
                        value = OSD.FromQuaternion((Quaternion)LastUpdateValue);
                        break;

                    default:
                        DebugLog.WarnFormat("[SYNCED PROPERTY] ToOSDArray: No handler for property {0} ", Property);
                        break;
                }
                propertyData.Add(value);
                return propertyData;
            }
        }

        /// <summary>
        /// Set member values by decoding out of propertyData. Should only
        /// be called in initialization time (e.g. from constructor).
        /// </summary>
        /// <param name="propertyData"></param>
        private void FromOSDArray(OSDArray propertyData)
        {
            Property = (SyncableProperties.Type)(propertyData[0].AsInteger());
            LastUpdateTimeStamp = propertyData[1].AsLong();
            LastUpdateSyncID = propertyData[2].AsString();

            OSD value = propertyData[3];
            switch (Property)
            {
                ///////////////////////////////////////
                // Complex structure properties
                ///////////////////////////////////////
                case SyncableProperties.Type.AgentCircuitData:
                case SyncableProperties.Type.AvatarAppearance:
                    LastUpdateValue = (OSDMap)value;
                    break;

                case SyncableProperties.Type.Animations:
                    LastUpdateValue = (OSDArray)value;
                    break;

                ////////////////////////////
                // Integer/enum type properties
                ////////////////////////////
                case SyncableProperties.Type.CreationDate:          // int
                case SyncableProperties.Type.LinkNum:               // int
                case SyncableProperties.Type.OwnershipCost:         // int
                case SyncableProperties.Type.SalePrice:             // int
                case SyncableProperties.Type.ScriptAccessPin:       // int
                case SyncableProperties.Type.AggregateScriptEvents: // enum
                case SyncableProperties.Type.Flags:                 // enum
                case SyncableProperties.Type.LocalFlags:            // enum
                case SyncableProperties.Type.PresenceType:          // enum
                    LastUpdateValue = value.AsInteger();
                    break;

                case SyncableProperties.Type.ClickAction:
                case SyncableProperties.Type.Material:
                case SyncableProperties.Type.ObjectSaleType:
                    LastUpdateValue = (byte)value.AsInteger();
                    break;

                ////////////////////////////
                // Boolean type properties
                ////////////////////////////
                case SyncableProperties.Type.AllowedDrop:
                case SyncableProperties.Type.IsAttachment:
                case SyncableProperties.Type.PassTouches:
                case SyncableProperties.Type.VolumeDetectActive:
                case SyncableProperties.Type.Flying:
                case SyncableProperties.Type.IsColliding:
                case SyncableProperties.Type.CollidingGround:
                case SyncableProperties.Type.Kinematic:
                case SyncableProperties.Type.IsSelected:
                case SyncableProperties.Type.AllowMovement:
                    LastUpdateValue = value.AsBoolean();
                    break;

                ////////////////////////////
                // Vector3 type properties
                ////////////////////////////
                case SyncableProperties.Type.AngularVelocity:
                case SyncableProperties.Type.AttachedPos:
                case SyncableProperties.Type.GroupPosition:
                case SyncableProperties.Type.OffsetPosition:
                case SyncableProperties.Type.Scale:
                case SyncableProperties.Type.SitTargetPosition:
                case SyncableProperties.Type.SitTargetPositionLL:
                case SyncableProperties.Type.SOP_Acceleration:
                case SyncableProperties.Type.Velocity:
                case SyncableProperties.Type.Force:
                case SyncableProperties.Type.PA_Acceleration:
                case SyncableProperties.Type.PA_Velocity:
                case SyncableProperties.Type.PA_TargetVelocity:
                case SyncableProperties.Type.Position:
                case SyncableProperties.Type.RotationalVelocity:
                case SyncableProperties.Type.Size:
                case SyncableProperties.Type.Torque:
                case SyncableProperties.Type.AbsolutePosition:
                    LastUpdateValue = value.AsVector3();
                    break;

                ////////////////////////////
                // UUID type properties
                ////////////////////////////
                case SyncableProperties.Type.AttachedAvatar:
                case SyncableProperties.Type.CollisionSound:
                case SyncableProperties.Type.CreatorID:
                case SyncableProperties.Type.FolderID:
                case SyncableProperties.Type.GroupID:
                case SyncableProperties.Type.LastOwnerID:
                case SyncableProperties.Type.OwnerID:
                case SyncableProperties.Type.Sound:
                    LastUpdateValue = value.AsUUID();
                    break;

                ////////////////////////////
                // UInt type properties
                ////////////////////////////
                case SyncableProperties.Type.LocalId:
                case SyncableProperties.Type.AttachmentPoint:
                case SyncableProperties.Type.BaseMask:
                case SyncableProperties.Type.Category:
                case SyncableProperties.Type.EveryoneMask:
                case SyncableProperties.Type.GroupMask:
                case SyncableProperties.Type.InventorySerial:
                case SyncableProperties.Type.NextOwnerMask:
                case SyncableProperties.Type.OwnerMask:
                case SyncableProperties.Type.AgentControlFlags:
                case SyncableProperties.Type.ParentId:
                    LastUpdateValue = value.AsUInteger();
                    break;

                ////////////////////////////
                // Float type properties
                ////////////////////////////
                case SyncableProperties.Type.CollisionSoundVolume:
                case SyncableProperties.Type.Buoyancy:
                    LastUpdateValue = (float)value.AsReal();
                    break;

                ////////////////////////////
                // String type properties
                ////////////////////////////
                case SyncableProperties.Type.Color:
                case SyncableProperties.Type.CreatorData:
                case SyncableProperties.Type.Description:
                case SyncableProperties.Type.MediaUrl:
                case SyncableProperties.Type.Name:
                case SyncableProperties.Type.RealRegion:
                case SyncableProperties.Type.Shape:
                case SyncableProperties.Type.SitName:
                case SyncableProperties.Type.TaskInventory:
                case SyncableProperties.Type.Text:
                case SyncableProperties.Type.TouchName:
                    LastUpdateValue = value.AsString();
                    break;

                ////////////////////////////
                // byte[] (binary data) type properties
                ////////////////////////////
                case SyncableProperties.Type.ParticleSystem:
                case SyncableProperties.Type.TextureAnimation:
                    LastUpdateValue = value.AsBinary();
                    break;

                ////////////////////////////
                // Quaternion type properties
                ////////////////////////////
                case SyncableProperties.Type.RotationOffset:
                case SyncableProperties.Type.SitTargetOrientation:
                case SyncableProperties.Type.SitTargetOrientationLL:
                case SyncableProperties.Type.Orientation:
                case SyncableProperties.Type.Rotation:
                    LastUpdateValue = value.AsQuaternion();
                    break;

                default:
                    DebugLog.WarnFormat("[SYNCED PROPERTY] FromOSDArray: No handler for property {0} ", Property);
                    break;
            }
        }
    }
}
