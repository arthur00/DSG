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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Reflection;

using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using OpenMetaverse;
using OpenMetaverse.StructuredData;

using log4net;
using System.Xml;
using System.Drawing;

namespace DSG.RegionSync
{
    public abstract class SyncMsg
    {
        public enum MsgType
        {
            Null,

            UpdatedProperties, // per property sync for SP and SOP

            // Actor -> SIM(Scene)
            GetTerrain,
            GetObjects,
            GetPresences,
            GetRegionInfo,
            GetEnvironment,
            
            // SIM <-> CM
            Terrain,
            Objects,
            RegionInfo,
            Environment,
            
            NewObject,       // objects
            RemovedObject,   // objects
            LinkObject,
            DelinkObject,
            
            UpdatedBucketProperties, //object properties in one bucket
            
            
            NewPresence,
            RemovedPresence,
            RegionName,
            RegionStatus,
            ActorID,
            ActorType,
            //events
            NewScript,
            UpdateScript,
            ScriptReset,
            ChatFromClient,
            ChatFromWorld,
            ChatBroadcast,
            ObjectGrab,
            ObjectGrabbing,
            ObjectDeGrab,
            Attach,
            PhysicsCollision,
            ScriptCollidingStart,
            ScriptColliding,
            ScriptCollidingEnd,
            ScriptLandCollidingStart,
            ScriptLandColliding,
            ScriptLandCollidingEnd,
            //control command
            SyncStateReport,
            TimeStamp,
            KeepAlive,
            
            /////////////////////
            // Quarks
            /////////////////////

            // Quark Subscription
            QuarkSubscription,

            // Dynamic Quark Subscription
            QuarkSubAdd,
            QuarkSubRemove,
            QuarkSubSwitch,

            // Quark Crossing
            QuarkPrimCrossing,
            QuarkPresenceCrossing

            
        }

        /// <summary>
        /// SyncMsg processing progression on reception:
        ///       The stream reader creates a SyncMsg instance by calling:
        ///              msg = SyncMsg.SyncMsgFactory(stream);
        ///       This creates the msg of the correct type with the binary constructor. For instance,
        ///              msg = new SyncMsgTimeStamp(type, length, data);
        ///       On a possibly different thread, the binary input is converted into load data via:
        ///              msg.ConvertIn(pRegionContext, pConnectorContext);
        ///       The message is acted on by calling:
        ///              msg.HandleIn(pRegionContext, pConnectorContext);
        /// The processing progression on sending is:
        ///       Someone creates a message of the desired type. For instance:
        ///              msg = new SyncMsgTimeStamp(RegionSyncModule.NowTicks());
        ///       The message can be operated on as its methods allow (like adding updates, for instance).
        ///       Before sending, the local variables are converted into binary for sending via:
        ///              msg.ConvertOut(pRegionContext)
        ///       This prepares the message for sending and can be done once before sending on multiple SyncConnectors.
        ///       The binary data to send is fetched via:
        ///              byte[] outBytes = msg.GetWireBytes();
        ///       This last byte buffer contains the type and length header required for parsing at the other end.
        /// 
        /// A message has a 'direction' state: In or Out. This controls the processing of the buffers. The meaning:
        ///     SyncMsg.Direction.In: message received off the stream as a binary block.
        ///             The message has a "ConnectorContext".
        ///     SyncMsg.Direction.Out: the message created as a message to send.
        ///             The message has a "RegionContext"
        /// The operation of the base class methods for each mode are:
        /// 
        /// new(type, buff, buffLen)
        ///             set direction to In.
        /// new(typeSpecificParameters)
        ///             set direction to Out
        /// ConvertIn
        ///             In: convert m_data to local varaibles
        ///             Out: no operation
        /// HandleIn
        ///             In: operate on the local variables
        ///             Out: operate on the local variables
        /// ConvertOut
        ///             In: presumed sending binary buffer back out. No conversion done.
        ///                 To cause local variables to be converted, set direction to "Out".
        ///             Out: if not already converted, convert local variables to binary format.
        ///                 For SyncMsgOSDMap, if DataMap does not exist, create it from local variables and
        ///                 serialize to m_data.
        /// GetWireBytes
        ///             In: if binary buffer does not already exist create from m_data and DataLength to create wire format bytes.
        ///             Out: if binary buffer does not already exist create from m_data and DataLength to create wire format bytes.
        ///       
        /// </summary>
        protected static ILog m_log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        protected static readonly string LogHeader = "[SYNCMSG]";
        protected static readonly string ZeroUUID = "00000000-0000-0000-0000-000000000000";

        public enum Direction
        {
            In,
            Out
        }

        public Direction Dir { get; set; }

        // They type of this message
        public MsgType MType { get; protected set; }

        // Lock used to protect data creation since multiple threads can try to convert this from data to buffers on output.
        protected object m_dataLock = new object();

        // The binary data this type of message is built from or converted into
        public int DataLength { get; protected set; }
        protected byte[] m_data;

        // The binary, wire encoding of the object. Includes the type and length header.
        private byte[] m_rawOutBytes;

        // The connector this message was received on.
        public SyncConnector ConnectorContext { get; set; }

        // The region the message was created for.
        public RegionSyncModule RegionContext { get; set; }

        // Given an incoming stream of bytes, create a SyncMsg from the next data on that stream.
        // The input stream should contain:
        //      4 bytes: the msgType code
        //      4 bytes: number of bytes following for the message data
        //      N bytes: the data for this type of messsage
        public static SyncMsg SyncMsgFactory(Stream pStream, SyncConnector pConnectorContext)
        {
            SyncMsg ret = null;

            MsgType mType = (MsgType)Utils.BytesToInt(GetBytesFromStream(pStream, 4));
            int length = Utils.BytesToInt(GetBytesFromStream(pStream, 4));
            byte[] data = GetBytesFromStream(pStream, length);

            switch (mType)
            {
                case MsgType.UpdatedProperties: ret = new SyncMsgUpdatedProperties(length, data);    break;
                case MsgType.GetTerrain:        ret = new SyncMsgGetTerrain(length, data);           break;
                case MsgType.GetObjects:        ret = new SyncMsgGetObjects(length, data);           break;
                case MsgType.GetPresences:      ret = new SyncMsgGetPresences(length, data);         break;
                case MsgType.GetRegionInfo:     ret = new SyncMsgGetRegionInfo(length, data);        break;
                case MsgType.GetEnvironment:    ret = new SyncMsgGetEnvironment(length, data);       break;
                case MsgType.Terrain:           ret = new SyncMsgTerrain(length, data);              break;
                case MsgType.Objects:           ret = new SyncMsgObjects(length, data);              break;
                case MsgType.RegionInfo:        ret = new SyncMsgRegionInfo(length, data);           break;
                case MsgType.Environment:       ret = new SyncMsgEnvironment(length, data);          break;
                case MsgType.NewObject:         ret = new SyncMsgNewObject(length, data);            break;
                case MsgType.RemovedObject:     ret = new SyncMsgRemovedObject(length, data);        break;
                case MsgType.LinkObject:        ret = new SyncMsgLinkObject(length, data);           break;
                case MsgType.DelinkObject:      ret = new SyncMsgDelinkObject(length, data);         break;
                case MsgType.NewPresence:       ret = new SyncMsgNewPresence(length, data);          break;
                case MsgType.RemovedPresence:   ret = new SyncMsgRemovedPresence(length, data);      break;
                case MsgType.RegionName:        ret = new SyncMsgRegionName(length, data);           break;
                case MsgType.ActorID:           ret = new SyncMsgActorID(length, data);              break;
                case MsgType.RegionStatus:      ret = new SyncMsgRegionStatus(length, data);         break;

                case MsgType.NewScript:         ret = new SyncMsgNewScript(length, data);            break;
                case MsgType.UpdateScript:      ret = new SyncMsgUpdateScript(length, data);         break;
                case MsgType.ScriptReset:       ret = new SyncMsgScriptReset(length, data);          break;
                case MsgType.ChatFromClient:    ret = new SyncMsgChatFromClient(length, data);       break;
                case MsgType.ChatFromWorld:     ret = new SyncMsgChatFromWorld(length, data);        break;
                case MsgType.ChatBroadcast:     ret = new SyncMsgChatBroadcast(length, data);        break;
                case MsgType.ObjectGrab:        ret = new SyncMsgObjectGrab(length, data);           break;
                case MsgType.ObjectGrabbing:    ret = new SyncMsgObjectGrabbing(length, data);       break;
                case MsgType.ObjectDeGrab:      ret = new SyncMsgObjectDeGrab(length, data);         break;
                case MsgType.Attach:            ret = new SyncMsgAttach(length, data);               break;

                // case MsgType.PhysicsCollision:      ret = new SyncMsgPhysicsCollision(length, data);     break;
                case MsgType.ScriptCollidingStart:  ret = new SyncMsgScriptCollidingStart(length, data); break;
                case MsgType.ScriptColliding:       ret = new SyncMsgScriptColliding(length, data);      break;
                case MsgType.ScriptCollidingEnd:    ret = new SyncMsgScriptCollidingEnd(length, data);   break;
                case MsgType.ScriptLandCollidingStart:ret = new SyncMsgScriptLandCollidingStart(length, data);   break;
                case MsgType.ScriptLandColliding:   ret = new SyncMsgScriptLandColliding(length, data);  break;
                case MsgType.ScriptLandCollidingEnd:ret = new SyncMsgScriptLandCollidingEnd(length, data);   break;

                case MsgType.TimeStamp:         ret = new SyncMsgTimeStamp(length, data);            break;
                // case MsgType.UpdatedBucketProperties: ret = new SyncMsgUpdatedBucketProperties(length, data); break;

                case MsgType.KeepAlive: ret = new SyncMsgKeepAlive(length, data); break;
                case MsgType.QuarkSubscription: ret = new SyncMsgQuarkSubscription(length, data); break;
                case MsgType.QuarkSubAdd: ret = new SyncMsgQuarkSubAdd(length, data); break;
                case MsgType.QuarkPrimCrossing: ret = new SyncMsgPrimQuarkCrossing(length, data); break;
                case MsgType.QuarkPresenceCrossing: ret = new SyncMsgPresenceQuarkCrossing(length, data); break;
                default:
                    m_log.ErrorFormat("{0}: Unknown Sync Message Type {1}", LogHeader, mType);
                    break;
            }

            if (ret != null)
            {
                ret.ConnectorContext = pConnectorContext;
            }

            return ret;
        }

        public SyncMsg()
        {
            DataLength = 0;
            m_rawOutBytes = null;
            RegionContext = null;
            ConnectorContext = null;
        }
        // Constructor called when building a packet to send.
        public SyncMsg(MsgType pMType, RegionSyncModule pRegionContext)
        {
            MType = pMType;
            Dir = Direction.Out;
            DataLength = 0;
            m_data = null;
            m_rawOutBytes = null;
            RegionContext = pRegionContext;
            ConnectorContext = null;
        }
        // Constructor called when a packet of this type has been received.
        public SyncMsg(MsgType pType, int pLength, byte[] pData)
        {
            Dir = Direction.In;
            MType = pType;
            DataLength = pLength;
            m_data = pData;
            m_rawOutBytes = null;
            RegionContext = null;
            ConnectorContext = null;
        }
        private static byte[] GetBytesFromStream(Stream stream, int count)
        {
            // Loop to receive the message length
            byte[] ret = new byte[count];
            int i = 0;
            while (i < count)
            {
                i += stream.Read(ret, i, count - i);
            }
            return ret;
        }

        // Get the bytes to put on the stream.
        // This creates the raw message format with the header type and length.
        // If there was any internal data manipulated, someone must call ProcessOut() before this is called.
        public byte[] GetWireBytes()
        {
            lock (m_dataLock)
            {
                if (m_rawOutBytes == null)
                {
                    // This is a temporary fix, m_data should not be null
                    m_rawOutBytes = new byte[DataLength + 8];
                    Utils.IntToBytes((int)MType, m_rawOutBytes, 0);
                    Utils.IntToBytes(DataLength, m_rawOutBytes, 4);
                    if (DataLength > 0)
                    {
                        // Temporary fix: m_data should not be null if DataLength > 0
                        if (m_data == null)
                            return null;
                        Array.Copy(m_data, 0, m_rawOutBytes, 8, DataLength);
                    }
                }
            }
            // m_log.DebugFormat("{0} GetWireByte: typ={1}, len={2}", LogHeader, MType, DataLength);
            return m_rawOutBytes;
        }

        // Called after message received to convert binary stream data into internal representation.
        // A separate call so parsing can happen on a different thread from the input reader.
        // Return 'true' if successful handling.
        public virtual bool ConvertIn(RegionSyncModule pRegionContext)
        {
            // m_log.DebugFormat("{0} ConvertIn: typ={1}", LogHeader, MType);
            RegionContext = pRegionContext;
            return true;
        }
        // Handle the received message. ConvertIn() has been called before this.
        public virtual bool HandleIn(RegionSyncModule pRegionContext)
        {
            // m_log.DebugFormat("{0} HandleIn: typ={1}", LogHeader, MType);
            RegionContext = pRegionContext;
            if (ConnectorContext != null)
            {
                LogReception(RegionContext, ConnectorContext);
            }
            return true;
        }
        // Called before message is sent to convert the internal representation into binary stream data.
        // A separate call so parsing can happen on a different thread from the output writer.
        // Return 'true' if successful handling.
        public virtual bool ConvertOut(RegionSyncModule pRegionContext)
        {
            // m_log.DebugFormat("{0} ConvertOut: typ={1}", LogHeader, MType);
            RegionContext = pRegionContext;
            return true;
        }

        // Overwritten by each msg class so log messages will be uniquely tagged.
        public virtual string DetailLogTagRcv { get { return "RcvSyncMsg"; } }
        public virtual string DetailLogTagSnd { get { return "SndSyncMsg"; } }

        // Called in HandleIn() to do any logging about the reception of the message.
        // Invoked in HandleIn() because the message has been decoded and useful stuff is available to print.
        public virtual void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            if (pRegionContext != null && pConnectorContext != null)
                pRegionContext.DetailedUpdateWrite(DetailLogTagRcv, ZeroUUID, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
        // Called before the message is written to the wire.
        // Not done inside the message processing since a message can be sent repeatedly to multiple receptors.
        public virtual void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null && pConnectorContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagSnd, ZeroUUID, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
    }

    // ====================================================================================================
    // A base class for sync messages whose underlying structure is just an OSDMap
    public abstract class SyncMsgOSDMapData : SyncMsg
    {
        protected OSDMap DataMap { get; set; }

        public SyncMsgOSDMapData(MsgType pMType, RegionSyncModule pRegionSyncModule)
            : base(pMType, pRegionSyncModule)
        {
            // m_log.DebugFormat("{0} SyncMsgOSDMapData.constructor: dir=out, type={1}, syncMod={2}", LogHeader, pMType, pRegionSyncModule.Name);
            DataMap = null;
        }
        public SyncMsgOSDMapData(MsgType pType, int pLength, byte[] pData)
            : base(pType, pLength, pData)
        {
            // m_log.DebugFormat("{0} SyncMsgOSDMapData.constructor: dir=in, type={1}, len={2}", LogHeader, pType, pLength);
            DataMap = null;
        }
        // Convert the received block of binary bytes into an OSDMap (DataMap)
        // Return 'true' if successfully converted.
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            base.ConvertIn(pRegionContext);
            switch (Dir)
            {
                case Direction.In:
                    // A received message so convert the buffer data to an OSDMap for use by children classes
                    if (DataLength != 0)
                    {
                        DataMap = DeserializeMessage(m_data, DataLength);
                        // m_log.DebugFormat("{0} SyncMsgOSDMapData.ConvertIn: dir=in, map={1}", LogHeader, DataMap.ToString());
                    }
                    else
                    {
                        m_log.WarnFormat("{0} SyncMsgOSDMapData.ConvertIn: dir=in, no data", LogHeader);
                    }
                    break;
                case Direction.Out:
                    // A message being built for output.
                    // It is actually an error that this method is called as there should be no binary data.
                    DataMap = null;
                    // m_log.DebugFormat("{0} SyncMsgOSDMapData.ConvertIn: dir=out, map=NULL", LogHeader);
                    break;
            }
            return (DataMap != null);
        }
        // Convert the OSDMap of data into a binary array of bytes.
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            switch (Dir)
            {
                case Direction.In:
                    // A message received being sent out again.
                    // Current architecture is to just output the existing binary buffer so
                    //     nothing is converted.
                    // m_log.DebugFormat("{0} SyncMsgOSDMapData.ConvertOut: dir=in, typ={1}", LogHeader, MType);
                    break;
                case Direction.Out:
                    // Message being created for output. The children of this base class
                    //    should have created a DataMap from the local variables.
                    lock (m_dataLock)
                    {
                        if (DataMap != null)
                        {
                            if (m_data == null)
                            {
                                string s = OSDParser.SerializeJsonString(DataMap, true);
                                m_data = System.Text.Encoding.ASCII.GetBytes(s);
                                DataLength = m_data.Length;
                                // m_log.DebugFormat("{0} SyncMsgOSDMapData.ConvertOut: building m_data, dir=out, typ={1}, len={2}",
                                //                                    LogHeader, MType, DataLength);
                            }
                        }
                        else
                        {
                            DataLength = 0;
                            // m_log.DebugFormat("{0} SyncMsgOSDMapData.ConvertOut: no m_data, dir=out, typ={1}", LogHeader, MType);
                        }
                    }
                    break;
            }
            return base.ConvertOut(pRegionContext);
        }

        // Turn the binary bytes into and OSDMap
        private static HashSet<string> exceptions = new HashSet<string>();
        public static OSDMap DeserializeMessage(byte[] dataSource, int length)
        {
            OSDMap data = null;
            try
            {
                data = OSDParser.DeserializeJson(Encoding.ASCII.GetString(dataSource, 0, length)) as OSDMap;
            }
            catch (Exception e)
            {
                lock (exceptions)
                {
                    // If this is a new message, then print the underlying data that caused it
                    //if (!exceptions.Contains(e.Message))
                    {
                        exceptions.Add(e.Message);  // remember we've seen this type of error
                        // print out the unparsable message
                        m_log.Error(LogHeader + " " + Encoding.ASCII.GetString(dataSource, 0, length));
                        // after all of that, print out the actual error
                        m_log.ErrorFormat("{0}: {1}", LogHeader, e);
                    }
                }
                data = null;
            }
            return data;
        }


        #region Activate SOG and SP
        protected void ReactivateSOG(SceneObjectGroup SOG, Dictionary<UUID,SyncInfoBase> PartsSyncInfo)
        {
            // If it's an attachment, connect this to the presence
            if (SOG.IsAttachmentCheckFull())
            {
                //m_log.WarnFormat("{0}: HandleSyncNewObject: Adding attachement to presence", LogHeader);
                ScenePresence sp = RegionContext.Scene.GetScenePresence(SOG.AttachedAvatar);
                if (sp != null)
                {
                    sp.AddAttachment(SOG);
                    SOG.RootPart.SetParentLocalId(sp.LocalId);

                    // In case it is later dropped, don't let it get cleaned up
                    SOG.RootPart.RemFlag(PrimFlags.TemporaryOnRez);

                    SOG.HasGroupChanged = true;
                }
            }

            /* Uncomment when quarks exist
            //If we just keep a copy of the object in our local Scene,
            //and is not supposed to operation on it (e.g. object in 
            //passive quarks), then ignore the event.
            if (!ToOperateOnObject(group))
                return;
             */

            // Now that (if) the PhysActor of each part in sog has been created, set the PhysActor properties.
            if (SOG.RootPart.PhysActor != null)
            {
                foreach (SyncInfoBase syncInfo in PartsSyncInfo.Values)
                {
                    // m_log.DebugFormat("{0}: HandleSyncNewObject: setting physical properties", LogHeader);
                    syncInfo.SetPropertyValues(SyncableProperties.PhysActorProperties);
                }
            }
            
            SOG.CreateScriptInstances(0, false, RegionContext.Scene.DefaultScriptEngine, 0);

            // Trigger aggregateScriptEventSubscriptions since it may access PhysActor to link collision events
            foreach (SceneObjectPart part in SOG.Parts)
            {
                /*
                List<TaskInventoryItem> scripts = part.Inventory.GetInventoryItems(InventoryType.LSL);
                foreach (TaskInventoryItem item in scripts)
                {
                    RegionContext.Scene.EventManager.TriggerStopScript(0, item.ItemID);
                    RegionContext.RememberLocallyGeneratedEvent(MsgType.ScriptReset, item.ItemID, SOG.UUID, true, item.AssetID);
                    RegionContext.Scene.EventManager.TriggerScriptReset(0, item.ItemID);
                    RegionContext.ForgetLocallyGeneratedEvent();
                    RegionContext.Scene.EventManager.TriggerStartScript(0, item.ItemID);
                }
                 * */
                //part.aggregateScriptEvents();
                part.SubscribeForCollisionEvents();
            }

            SOG.ResumeScripts();
            SOG.ScheduleGroupForFullUpdate();
        }

        protected void ReactivateSP(SyncInfoBase SyncInfo)
        {
            // Get ACD and PresenceType from decoded SyncInfoPresence
            // NASTY CASTS AHEAD!
            AgentCircuitData acd = new AgentCircuitData();
            acd.UnpackAgentCircuitData((OSDMap)(((SyncInfoPresence)SyncInfo).CurrentlySyncedProperties[SyncableProperties.Type.AgentCircuitData].LastUpdateValue));
            // Unset the ViaLogin flag since this presence is being added to the scene by sync (not via login)
            acd.teleportFlags &= ~(uint)TeleportFlags.ViaLogin;
            PresenceType pt = (PresenceType)(int)(((SyncInfoPresence)SyncInfo).CurrentlySyncedProperties[SyncableProperties.Type.PresenceType].LastUpdateValue);
            // Fake like we're not a user so normal teleport processing will not happen.
            // No. If we make the presence an NPC, then we cannot prevent the local scene from rezzing a duplicate set of attachments. 
            // So, ScenePresence will be added with the original presence type. NPC presences will always get duplicate attachments which will need
            // to be fixed to support NPC presences in DSG regions. We can fix that eventually with a DSGAttachmentsModule which will only rez for 
            // presences added by the local scene. Then, we can go back to all Region Sync Avatars being NPCs.
            // PresenceType pt = PresenceType.Npc;

            // Add the decoded circuit to local scene
            RegionContext.Scene.AuthenticateHandler.AddNewCircuit(acd.circuitcode, acd);

            // Create a client and add it to the local scene at the position of the last update from sync cache
            Vector3 currentPos = (Vector3)(((SyncInfoPresence)SyncInfo).CurrentlySyncedProperties[SyncableProperties.Type.AbsolutePosition].LastUpdateValue);
            Quaternion bodyDirection = (Quaternion)SyncInfo.CurrentlySyncedProperties[SyncableProperties.Type.Rotation].LastUpdateValue;
            bool flyState = (bool)SyncInfo.CurrentlySyncedProperties[SyncableProperties.Type.Flying].LastUpdateValue;

            IClientAPI client = new RegionSyncAvatar(acd.circuitcode, RegionContext.Scene, acd.AgentID, acd.firstname, acd.lastname, currentPos);
            try
            {
                SyncInfo.SceneThing = RegionContext.Scene.AddNewAgent(client, pt);
            }
            catch (Exception e)
            {
                m_log.WarnFormat("{0}: Exception in AddNewAgent: {1}", LogHeader, e.ToString());
            }

            // Fix body rotation
            //ScenePresence sp = RegionContext.Scene.GetScenePresence(SyncInfo.UUID);
            ScenePresence sp = (ScenePresence)SyncInfo.SceneThing; 
            sp.Rotation = bodyDirection;

            // Fix flying state (when crossing)
            if (sp.PhysicsActor != null)
                sp.Flying = flyState;
            // SOME UNFORTUNATE REFLECTION IS REQUIRED TO GET THIS PRESENCE CREATED
            {
                // m_originRegionID is a private instance member of ScenePresence
                BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

                // Get the field info instance corresponding to m_originRegionID
                FieldInfo field = typeof(ScenePresence).GetField("m_originRegionID", flags);

                // Set the value to the current scene
                field.SetValue(sp, RegionContext.Scene.RegionInfo.RegionID);
            }

            // Now that we have a presence and a client, tell the region sync "client" to finish connecting. 
            ((RegionSyncAvatar)client).PostCreateRegionSyncAvatar();

            // Might need to trigger something here to send new client messages to connected clients
        }
        #endregion // Activate SOG and SP

        #region Encode/DecodeSceneObject and Encode/DecodeScenePresence
        
        // Clones (shallow-copy) an OSDMap
        protected OSDMap BuildNewMap(OSDMap oldMap)
        {
            OSDMap newMap = new OSDMap();
            foreach (KeyValuePair<string, OSD> item in oldMap)
            {
                newMap[item.Key] = item.Value;
            }
            return newMap;
        }

        /// <summary>
        /// Encode a SOG. Values of each part's properties are copied from SyncInfo, instead of from SOP's data. 
        /// If the SyncInfo is not maintained by SyncInfoManager yet, add it first.
        /// </summary>
        /// <param name="sog"></param>
        /// <returns></returns>
        protected OSDMap EncodeSceneObject(SceneObjectGroup sog, RegionSyncModule pRegionContext)
        {
            //This should not happen, but we deal with it by inserting a newly created PrimSynInfo
            if (!pRegionContext.InfoManager.SyncInfoExists(sog.RootPart.UUID))
            {
                m_log.ErrorFormat("{0}: EncodeSceneObject -- SOP {1},{2} not in SyncInfoManager's record yet. Adding.", LogHeader, sog.RootPart.Name, sog.RootPart.UUID);
                pRegionContext.InfoManager.InsertSyncInfoLocal(sog.RootPart.UUID, RegionSyncModule.NowTicks(), pRegionContext.SyncID);
            }

            OSDMap data = new OSDMap();
            data["uuid"] = OSD.FromUUID(sog.UUID);
            data["absPosition"] = OSD.FromVector3(sog.AbsolutePosition);
            // EncodeProperties can return null but it should never do that for adding an object
            data["RootPart"] = pRegionContext.InfoManager.EncodeProperties(sog.RootPart.UUID, sog.RootPart.PhysActor == null ? SyncableProperties.NonPhysActorProperties : SyncableProperties.FullUpdateProperties);

            OSDArray otherPartsArray = new OSDArray();
            foreach (SceneObjectPart part in sog.Parts)
            {
                if (!part.UUID.Equals(sog.RootPart.UUID))
                {
                    if (!pRegionContext.InfoManager.SyncInfoExists(part.UUID))
                    {
                        m_log.ErrorFormat("{0}: EncodeSceneObject -- SOP {1},{2} not in SyncInfoManager's record yet", 
                                    LogHeader, part.Name, part.UUID);
                        //This should not happen, but we deal with it by inserting a newly created PrimSynInfo
                        pRegionContext.InfoManager.InsertSyncInfoLocal(part.UUID, RegionSyncModule.NowTicks(), pRegionContext.SyncID);
                    }
                    // EncodeProperties can return null but it should never do that for adding an object
                    OSDMap partData = pRegionContext.InfoManager.EncodeProperties(part.UUID, part.PhysActor == null ? SyncableProperties.NonPhysActorProperties : SyncableProperties.FullUpdateProperties);
                    otherPartsArray.Add(partData);
                }
            }
            data["OtherParts"] = otherPartsArray;

            data["IsAttachment"] = OSD.FromBoolean(sog.IsAttachment);
            data["AttachedAvatar"] = OSD.FromUUID(sog.AttachedAvatar);
            data["AttachmentPoint"] = OSD.FromUInteger(sog.AttachmentPoint);
            /*
            if (sog.ScriptCount() > 0)
            {
                using (StringWriter sw = new StringWriter())
                {
                    using (XmlTextWriter writer = new XmlTextWriter(sw))
                    {
                        sog.SaveScriptedState(writer);
                    }

                    data["ScriptState"] = OSD.FromString(sw.ToString());
                }
            }
             * */
            
            return data;
        }

        /// <summary>
        /// Decode & create a SOG data structure. Due to the fact that PhysActor
        /// is only created when SOG.AttachToScene() is called, the returned SOG
        /// here only have non PhysActor properties decoded and values set. The
        /// PhysActor properties should be set later by the caller.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="sog"></param>
        /// <param name="syncInfos"></param>
        /// <returns>True of decoding sucessfully</returns>
        protected bool DecodeSceneObject(OSDMap data, out SceneObjectGroup sog, out Dictionary<UUID, SyncInfoBase> syncInfos, Scene scene)
        {
            sog = new SceneObjectGroup();
            syncInfos = new Dictionary<UUID, SyncInfoBase>();
            bool ret = true;

            try{
                OSDMap rootPart = (OSDMap)data["RootPart"];
                UUID uuid = rootPart["uuid"].AsUUID();
                OSDArray properties = (OSDArray)rootPart["properties"];

                //m_log.WarnFormat("{0} DecodeSceneObject for RootPart uuid: {1}", LogHeader, uuid);

                //Decode and copy to the list of PrimSyncInfo
                SyncInfoPrim sip = new SyncInfoPrim(uuid, properties, scene);
                SceneObjectPart root = (SceneObjectPart)sip.SceneThing;

                sog.SetRootPart(root);
                sip.SetPropertyValues(SyncableProperties.GroupProperties);
                syncInfos.Add(root.UUID, sip);

                if (sog.UUID == UUID.Zero)
                    sog.UUID = sog.RootPart.UUID;

                //Decode the remaining parts and add them to the object group
                if (data.ContainsKey("OtherParts"))
                {
                    //int otherPartsCount = data["OtherPartsCount"].AsInteger();
                    OSDArray otherPartsArray = (OSDArray)data["OtherParts"];
                    foreach (OSDMap otherPart in otherPartsArray)
                    {
                        uuid = otherPart["uuid"].AsUUID();
                        properties = (OSDArray)otherPart["properties"];

                        //m_log.WarnFormat("{0} DecodeSceneObject for OtherParts[{1}] uuid: {2}", LogHeader, i, uuid);
                        sip = new SyncInfoPrim(uuid, properties, scene);
                        SceneObjectPart part = (SceneObjectPart)sip.SceneThing;

                        if (part == null)
                        {
                            m_log.ErrorFormat("{0} DecodeSceneObject could not decode root part.", LogHeader);
                            sog = null;
                            return false;
                        }
                        sog.AddPart(part);
                        // Should only need to set group properties from the root part, not other parts
                        //sip.SetPropertyValues(SyncableProperties.GroupProperties);
                        syncInfos.Add(part.UUID, sip);
                    }
                }

                sog.IsAttachment = data["IsAttachment"].AsBoolean();
                sog.AttachedAvatar = data["AttachedAvatar"].AsUUID();
                uint ap = data["AttachmentPoint"].AsUInteger();
                if (sog.RootPart == null)
                {
                    //m_log.WarnFormat("{0} DecodeSceneObject - ROOT PART IS NULL", LogHeader);
                }
                else if (sog.RootPart.Shape == null)
                {
                    //m_log.WarnFormat("{0} DecodeSceneObject - ROOT PART SHAPE IS NULL", LogHeader);
                }
                else
                {
                    sog.AttachmentPoint = ap;
                    //m_log.WarnFormat("{0}: DecodeSceneObject AttachmentPoint = {1}", LogHeader, sog.AttachmentPoint);
                }
            }
            catch (Exception e)
            {
                m_log.WarnFormat("{0} Encountered an exception: {1} {2} {3}", "DecodeSceneObject", e.Message, e.TargetSite, e.ToString());
                ret = false;
            }
            //else
            //    m_log.WarnFormat("{0}: DecodeSceneObject AttachmentPoint = null", LogHeader);

            return ret;
        }

        /// <summary>
        /// Encode a SP. Values of each part's properties are copied from SyncInfo, instead of from SP's data. 
        /// If the SyncInfo is not maintained by SyncInfoManager yet, add it first.
        /// </summary>
        /// <param name="sog"></param>
        /// <returns></returns>
        protected OSDMap EncodeScenePresence(ScenePresence sp, RegionSyncModule pRegionContext)
        {
            //This should not happen, but we deal with it by inserting it now
            if (!pRegionContext.InfoManager.SyncInfoExists(sp.UUID))
            {
                m_log.ErrorFormat("{0}: ERROR: EncodeScenePresence -- SP {1},{2} not in SyncInfoManager's record yet. Adding.", LogHeader, sp.Name, sp.UUID);
                pRegionContext.InfoManager.InsertSyncInfoLocal(sp.UUID, RegionSyncModule.NowTicks(), pRegionContext.SyncID);
            }

            OSDMap data = new OSDMap();
            data["uuid"] = OSD.FromUUID(sp.UUID);
            data["absPosition"] = OSD.FromVector3(sp.AbsolutePosition);
            // EncodeProperties can return null but it should never do that for adding a presence
            data["ScenePresence"] = pRegionContext.InfoManager.EncodeProperties(sp.UUID, SyncableProperties.AvatarProperties);

            return data;
        }

        // Decodes scene presence data into sync info
        protected void DecodeScenePresence(OSDMap data, out SyncInfoBase syncInfo, Scene scene)
        {
            //Decode the syncInfo
            try
            {
                UUID uuid = data["uuid"].AsUUID();
                OSDMap presenceData = (OSDMap)data["ScenePresence"];
                OSDArray properties = (OSDArray)presenceData["properties"];
                syncInfo = new SyncInfoPresence(uuid, (OSDArray)properties, scene);
            }
            catch (Exception)
            {
                m_log.ErrorFormat("{0}: DecodeScenePresence missing required data", LogHeader);
                syncInfo = null;
            }
        }

        #endregion // Encode/DecodeSceneObject and Encode/DecodeScenePresence

        #region Quark Subscription

        protected OSDMap EncodeQuarkSubscriptions(bool request)
        {
            return EncodeQuarkSubscriptions(request, RegionContext.QuarkManager.ActiveQuarkDictionary.Values, RegionContext.QuarkManager.PassiveQuarkDictionary.Values, false);
        }

        /// <summary>
        /// Encode quark subscriptions.
        /// </summary>
        /// <param name="ack">Indicate if the encoded message would be sent
        /// back to a child process as an ACK. </param>
        /// <returns></returns>
        protected OSDMap EncodeQuarkSubscriptions(bool request, IEnumerable<SyncQuark> activeQuarkSet, IEnumerable<SyncQuark> passiveQuarkSet, bool requestPrims)
        {
            //Subscription sent to a parent node -- efficiently, it should be the
            //set of quarks that this node wants to receive updates from the parent
            //(e.g. the intersection of their quark sets). For now, we haven't 
            //optimize the code for that customization yet -- the child simply sends
            //out subscriptions for all quarks it covers, the parent will do an 
            //intersection on the quarks.

            OSDArray activeQuarks = new OSDArray();
            OSDArray passiveQuarks = new OSDArray();

            foreach (SyncQuark aQuark in activeQuarkSet)
                activeQuarks.Add(aQuark.QuarkName);
            foreach (SyncQuark pQuark in passiveQuarkSet)
                passiveQuarks.Add(pQuark.QuarkName);

            OSDMap subscriptionEncoding = new OSDMap();
            subscriptionEncoding["ActiveQuarks"] = activeQuarks;
            subscriptionEncoding["PassiveQuarks"] = passiveQuarks;
            // 1. If I am the child initiating quark subscription, request is true
            // 2. If I am the parent that just received a quark subscription, request is false (see DecodeQuarkSubscriptionFromMsg)
            // 3. If I am the child that received the parent's Quark Subscriptions I shouldn't be here at all, since reply==false
            if (!requestPrims)
                subscriptionEncoding["Request"] = request;
            else
            {
                OSDMap quarkToActor = new OSDMap();
                foreach (SyncQuark quark in activeQuarkSet)
                {
                    // Find one of the actors that have the prims for that region and request it to send new objects.
                    List<SyncConnector> actorsSync = new List<SyncConnector>(RegionContext.QuarkManager.GetQuarkSubscribers(quark.QuarkName));
                    if (actorsSync.Count > 0)
                        quarkToActor[quark.QuarkName] = OSD.FromString(actorsSync[0].otherSideActorID);
                    else
                        throw new Exception("At least one of my neighbors should be subscribed to this quark: " + quark.QuarkName);
                }
                subscriptionEncoding["QuarkToActor"] = quarkToActor;
            }

            return subscriptionEncoding;
        }

        protected Dictionary<string, HashSet<SyncQuark>> DecodeQuarkSubscriptionsFromMsg(OSDMap data)
        {
            Dictionary<string, HashSet<SyncQuark>> decodedSubscriptions = new Dictionary<string, HashSet<SyncQuark>>();
            OSDArray aQuarks = (OSDArray)data["ActiveQuarks"];
            OSDArray pQuarks = (OSDArray)data["PassiveQuarks"];

            HashSet<SyncQuark> activeQuarks = new HashSet<SyncQuark>();
            HashSet<SyncQuark> passiveQuarks = new HashSet<SyncQuark>();

            Actor actor = new Actor();
            actor.ActiveQuarks = new HashSet<SyncQuark>(activeQuarks);
            actor.PassiveQuarks = new HashSet<SyncQuark>(passiveQuarks);
            RegionContext.QuarkManager.ActorSubscriptions[ConnectorContext] = actor;

            foreach (OSD quark in aQuarks)
            {
                activeQuarks.Add(new SyncQuark(quark.AsString()));
            }

            foreach (OSD quark in pQuarks)
            {
                passiveQuarks.Add(new SyncQuark(quark.AsString()));
            }

            decodedSubscriptions["Active"] = activeQuarks;
            decodedSubscriptions["Passive"] = passiveQuarks;
            
            return decodedSubscriptions;
        }

        #endregion // Quark Subscription
    }

        // ====================================================================================================
    // A base class for sync messages whose underlying structure is just an OSDMap
    public abstract class SyncMsgBinaryData : SyncMsg
    {
        protected byte[] Data { get; set; }
        protected OSDMap DataMap { get; set; }
        protected int DataMapLength { get; set; }
        protected int DataBinaryLength { get; set; }

        public SyncMsgBinaryData(MsgType pMType, RegionSyncModule pRegionSyncModule)
            : base(pMType, pRegionSyncModule)
        {
            // m_log.DebugFormat("{0} SyncMsgOSDMapData.constructor: dir=out, type={1}, syncMod={2}", LogHeader, pMType, pRegionSyncModule.Name);
            Data = null;
            DataMap = null;
        }
        public SyncMsgBinaryData(MsgType pType, int pLength, byte[] pData)
            : base(pType, pLength, pData)
        {
            // m_log.DebugFormat("{0} SyncMsgOSDMapData.constructor: dir=in, type={1}, len={2}", LogHeader, pType, pLength);
            Data = null;
            DataMap = null;
        }

        // Convert the received block of binary bytes into an OSDMap (DataMap) and a binary array
        // Return 'true' if successfully converted.
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            base.ConvertIn(pRegionContext);
            switch (Dir)
            {
                case Direction.In:
                    // A received message so convert the first block of buffer data to an OSDMap, and the rest save as binary data
                    if (DataLength != 0)
                    {
                        DataMapLength = Utils.BytesToInt(m_data);
                        DataBinaryLength = DataLength - DataMapLength - 4;
                        if (DataMapLength > 0)
                        {
                            byte[] osdMap = new byte[DataMapLength];
                            Buffer.BlockCopy(m_data, 4, osdMap, 0, DataMapLength);
                            DataMap = SyncMsgOSDMapData.DeserializeMessage(osdMap, DataMapLength);
                        }
                        if (DataBinaryLength > 0)
                        {
                            Data = new byte[DataBinaryLength];
                            Buffer.BlockCopy(m_data, 4 + DataMapLength, Data, 0, DataBinaryLength);
                        }
                        
                        // m_log.DebugFormat("{0} SyncMsgOSDMapData.ConvertIn: dir=in, map={1}", LogHeader, DataMap.ToString());
                    }
                    else
                    {
                        // m_log.DebugFormat("{0} SyncMsgOSDMapData.ConvertIn: dir=in, no data", LogHeader);
                    }
                    break;
                case Direction.Out:
                    // A message being built for output.
                    // It is actually an error that this method is called as there should be no binary data.
                    Data = null;
                    // m_log.DebugFormat("{0} SyncMsgOSDMapData.ConvertIn: dir=out, map=NULL", LogHeader);
                    break;
            }
            return (Data != null);
        }
        // Convert the OSDMap of data into a binary array of bytes.
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            switch (Dir)
            {
                case Direction.In:
                    // A message received being sent out again.
                    // Current architecture is to just output the existing binary buffer so
                    //     nothing is converted.
                    // m_log.DebugFormat("{0} SyncMsgOSDMapData.ConvertOut: dir=in, typ={1}", LogHeader, MType);
                    break;
                case Direction.Out:
                    // Message being created for output. The children of this base class
                    //    should have created a DataMap from the local variables.
                    lock (m_dataLock)
                    {
                        byte[] osdMap = null;
                        if (DataMap != null)
                        {
                            string s = OSDParser.SerializeJsonString(DataMap, true);
                            osdMap = System.Text.Encoding.ASCII.GetBytes(s);
                            DataMapLength = osdMap.Length;
                        }
                        else
                            DataMapLength = 0;

                        if (Data != null)
                            DataBinaryLength = Data.Length;
                        else
                            DataBinaryLength = 0;

                        if (DataMapLength == 0 && DataBinaryLength == 0)
                        {
                            DataLength = 0;
                        }
                        else
                        {
                            // Assemble the binary map and binary together
                            m_data = new byte[DataBinaryLength + DataMapLength + 4];
                            Utils.IntToBytes(DataMapLength, m_data, 0);
                            if (DataMapLength > 0)
                                Buffer.BlockCopy(osdMap, 0, m_data, 4, DataMapLength);
                            if (DataBinaryLength > 0)
                                Buffer.BlockCopy(Data, 0, m_data, DataMapLength + 4, DataBinaryLength);
                            DataLength = m_data.Length;
                        }
                    }
                    break;
            }
            return base.ConvertOut(pRegionContext);
        }
    }

    // ====================================================================================================
    public class SyncMsgUpdatedProperties : SyncMsgOSDMapData
    {
        // On transmission, the properties that are to be sent.
        public HashSet<SyncableProperties.Type> SyncableProperties { get; set; }
        public UUID Uuid { get; set; }

        // On reception, the properties updated with their values
        public HashSet<SyncedProperty> SyncedProperties;

        public SyncMsgUpdatedProperties(RegionSyncModule pRegionContext, UUID pUuid, HashSet<SyncableProperties.Type> pSyncableProperties)
            : base(MsgType.UpdatedProperties, pRegionContext)
        {
            Uuid = pUuid;
            SyncableProperties = pSyncableProperties;
        }
        public SyncMsgUpdatedProperties(int pLength, byte[] pData)
            : base(MsgType.UpdatedProperties, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                ret = true;
                // Decode synced properties from the message. never null.
                SyncedProperties = SyncInfoBase.DecodeSyncedProperties(DataMap);
                Uuid = DataMap["uuid"].AsUUID();
            }
            else
            {
                m_log.ErrorFormat("{0} UpdatedProperties.ConvertIn failed to convert input data", LogHeader);
                ret = false;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                if (SyncedProperties != null && SyncedProperties.Count > 0)
                {
                    // Update local sync info and scene object/presence
                    pRegionContext.RememberLocallyGeneratedEvent(MType);
                    HashSet<SyncableProperties.Type> propertiesUpdated = pRegionContext.InfoManager.UpdateSyncInfoBySync(Uuid, SyncedProperties);
                    if (propertiesUpdated.Contains(RegionSync.SyncableProperties.Type.AvatarAppearance))
                        m_log.DebugFormat("{0} SyncMsgUpdatedProperties:HandleIn AvatarAppearance for uuid {1}", LogHeader, Uuid);
                    pRegionContext.ForgetLocallyGeneratedEvent();

                    // Do our own detail logging after we know which properties are actually updated (in propertiesUpdated)
                    pRegionContext.DetailedUpdateLogging(Uuid, propertiesUpdated, "RecUpdateN", ConnectorContext.otherSideActorID, DataLength);

                    // Relay the update properties
                    if (pRegionContext.IsSyncRelay)
                        pRegionContext.EnqueueUpdatedProperty(Uuid, propertiesUpdated);
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    DataMap = pRegionContext.InfoManager.EncodeProperties(Uuid, SyncableProperties);
                    if (DataMap == null)
                        return false;
                    // m_log.DebugFormat("{0} SyncMsgUpdatedProperties.ConvertOut, syncProp={1}, DataMap={2}", LogHeader, SyncableProperties, DataMap);
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        // Add new updates to this update message.
        // Happens when this message is in the output queue and more updated properties are 
        //     ready to be output. This adds to the list of properties to send and a final
        //     ConvertOut() will gather the updated properties for transmission.
        // The tricky part is this could be a received message and this changes the direction of the
        //     message so it will be rebuilt for output.
        public void AddUpdates(HashSet<SyncableProperties.Type> pNewSyncableProperties)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.In)
                {
                    // If this was an input message, create the list of properties to send from the received list.
                    SyncableProperties = new HashSet<SyncableProperties.Type>();
                    foreach (SyncedProperty sp in SyncedProperties)
                    {
                        SyncableProperties.Add(sp.Property);
                    }
                }
                // m_log.DebugFormat("{0} UpdatedProperties.AddUpdates: uuid={1}, prevProp={2}, addProp={3}",
                //                         LogHeader, Uuid, PropToString(SyncableProperties), PropToString(pNewSyncableProperties));
                Dir = Direction.Out;
                SyncableProperties.Union(pNewSyncableProperties);
                // Any output data buffers must be rebuilt
                DataMap = null;
                DataLength = 0;
                m_data = null;
            }
        }
        public string PropsAsString()
        {
            string ret = "";
            if (SyncableProperties != null)
                ret = PropToString(SyncableProperties);
            return ret;
        }
        private string PropToString(HashSet<SyncableProperties.Type> props)
        {
            StringBuilder buff = new StringBuilder();
            buff.Append("<");
            foreach (SyncableProperties.Type t in props)
            {
                buff.Append(t.ToString());
                buff.Append(",");
            }
            buff.Append(">");
            return buff.ToString();
        }
        public override void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            // This override is here so the reception will not be default logged and the actual logging can occur
            //     after the properties have been processed.
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateLogging(Uuid, SyncableProperties, "SendUpdate", pConnectorContext.otherSideActorID, DataLength);
        }
    }
    // ====================================================================================================
    // Send to have other side send us their environment info.
    // If received, send our environment info.
    public class SyncMsgGetEnvironment: SyncMsg
    {
        public override string DetailLogTagRcv { get { return "RcvGetEnvi"; } }
        public override string DetailLogTagSnd { get { return "SndGetEnvi"; } }

        public SyncMsgGetEnvironment(RegionSyncModule pRegionContext)
            : base(MsgType.GetEnvironment, pRegionContext)
        {
        }
        public SyncMsgGetEnvironment(int pLength, byte[] pData)
            : base(MsgType.GetEnvironment, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                string environment = pRegionContext.Scene.SimulationDataService.LoadRegionEnvironmentSettings(pRegionContext.Scene.RegionInfo.RegionID);
                SyncMsgEnvironment msg = new SyncMsgEnvironment(pRegionContext, environment);
                msg.ConvertOut(pRegionContext);
                ConnectorContext.ImmediateOutgoingMsg(msg);
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    // Sent to tell the other end our environment info.
    // When received, it is the other side's environment info.
    public class SyncMsgEnvironment: SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvEnviron"; } }
        public override string DetailLogTagSnd { get { return "SndEnviron"; } }

        public string Env { get; set; }

        public SyncMsgEnvironment(RegionSyncModule pRegionContext, string env)
            : base(MsgType.Environment, pRegionContext)
        {
            Env = env;
        }
        public SyncMsgEnvironment(int pLength, byte[] pData)
            : base(MsgType.Environment, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                Env = DataMap["environment"].AsString();
                RegionContext.Scene.SimulationDataService.StoreRegionEnvironmentSettings(RegionContext.Scene.RegionInfo.RegionID, Env);
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap(1);
                    data["environment"] = OSD.FromString(Env);
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        // Logs the whole string of parameters received. Only happens once per region connect.
        public override void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            pRegionContext.DetailedUpdateWrite(DetailLogTagRcv, ZeroUUID, 0, ZeroUUID, DataMap.ToString(), DataLength);
        }
    }
    // ====================================================================================================
    // Send to have other side send us their region info.
    // If received, send our region info.
    public class SyncMsgGetRegionInfo : SyncMsg
    {
        public override string DetailLogTagRcv { get { return "RcvGetRegn"; } }
        public override string DetailLogTagSnd { get { return "SndGetRegn"; } }

        public SyncMsgGetRegionInfo(RegionSyncModule pRegionContext)
            : base(MsgType.GetRegionInfo, pRegionContext)
        {
        }
        public SyncMsgGetRegionInfo(int pLength, byte[] pData)
            : base(MsgType.GetRegionInfo, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                SyncMsgRegionInfo msg = new SyncMsgRegionInfo(pRegionContext, pRegionContext.Scene.RegionInfo);
                msg.ConvertOut(pRegionContext);
                ConnectorContext.ImmediateOutgoingMsg(msg);
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    // Sent to tell the other end our region info.
    // When received, it is the other side's region info.
    public class SyncMsgRegionInfo : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvRgnInfo"; } }
        public override string DetailLogTagSnd { get { return "SndRgnInfo"; } }

        public RegionInfo RegInfo { get; set; }

        public SyncMsgRegionInfo(RegionSyncModule pRegionContext, RegionInfo pRegionInfo)
            : base(MsgType.RegionInfo, pRegionContext)
        {
            RegInfo = pRegionInfo;
        }
        public SyncMsgRegionInfo(int pLength, byte[] pData)
            : base(MsgType.RegionInfo, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                RegInfo = pRegionContext.Scene.RegionInfo;
                RegInfo.RegionSettings.AgentLimit = DataMap["agentLimit"].AsInteger();
                RegInfo.RegionSettings.AllowDamage = DataMap["allowDamage"].AsBoolean() ;
                RegInfo.RegionSettings.AllowLandJoinDivide = DataMap["allowLandJoinDivide"].AsBoolean() ;
                RegInfo.RegionSettings.AllowLandResell = DataMap["allowLandResale"].AsBoolean() ;
                RegInfo.RegionSettings.BlockFly = DataMap["blockFly"].AsBoolean() ;
                RegInfo.RegionSettings.BlockShowInSearch = DataMap["blockShowInSearch"].AsBoolean() ;
                RegInfo.RegionSettings.BlockTerraform = DataMap["blockTerraform"].AsBoolean() ;
                RegInfo.RegionSettings.Covenant = DataMap["covenant"].AsUUID() ;
                RegInfo.RegionSettings.DisableCollisions = DataMap["disableCollisions"].AsBoolean() ;
                RegInfo.RegionSettings.DisablePhysics = DataMap["disablePhysics"].AsBoolean() ;
                RegInfo.RegionSettings.DisableScripts = DataMap["disableScripts"].AsBoolean() ;
                RegInfo.RegionSettings.Elevation1NE = DataMap["elevation1NE"];
                RegInfo.RegionSettings.Elevation1NW = DataMap["elevation1NW"];
                RegInfo.RegionSettings.Elevation1SE = DataMap["elevation1SE"];
                RegInfo.RegionSettings.Elevation1SW = DataMap["elevation1SW"];
                RegInfo.RegionSettings.Elevation2NE = DataMap["elevation2NE"];
                RegInfo.RegionSettings.Elevation2NW = DataMap["elevation2NW"];
                RegInfo.RegionSettings.Elevation2SE = DataMap["elevation2SE"];
                RegInfo.RegionSettings.Elevation2SW = DataMap["elevation2SW"];
                RegInfo.RegionSettings.FixedSun = DataMap["regionFixedSun"].AsBoolean() ;
                RegInfo.RegionSettings.Maturity = DataMap["maturity"].AsInteger() ;
                RegInfo.RegionSettings.ParcelImageID = DataMap["parcelImageID"].AsUUID() ;
                RegInfo.RegionSettings.RestrictPushing = DataMap["restrictPushing"].AsBoolean() ;
                RegInfo.RegionSettings.Sandbox = DataMap["sandbox"].AsBoolean() ;
                RegInfo.RegionSettings.TelehubObject = DataMap["telehubObject"].AsUUID() ;
                RegInfo.RegionSettings.TerrainImageID = DataMap["terrainImageID"].AsUUID() ;
                RegInfo.RegionSettings.TerrainLowerLimit = DataMap["terrainLowerLimit"].AsReal() ;
                RegInfo.RegionSettings.TerrainRaiseLimit = DataMap["terrainRaiseLimit"].AsReal() ;
                RegInfo.RegionSettings.TerrainTexture1 = DataMap["terrainTexture1"].AsUUID() ;
                RegInfo.RegionSettings.TerrainTexture2 = DataMap["terrainTexture2"].AsUUID() ;
                RegInfo.RegionSettings.TerrainTexture3 = DataMap["terrainTexture3"].AsUUID() ;
                RegInfo.RegionSettings.TerrainTexture4 = DataMap["terrainTexture4"].AsUUID() ;
                RegInfo.RegionSettings.UseEstateSun = DataMap["useEstateSun"].AsBoolean() ;
                RegInfo.RegionSettings.WaterHeight = DataMap["waterHeight"].AsReal() ;

                RegInfo.EstateSettings.AllowLandmark = DataMap["allowLandmark"].AsBoolean();
                RegInfo.EstateSettings.AllowParcelChanges = DataMap["allowParcelChanges"].AsBoolean();
                RegInfo.EstateSettings.AllowSetHome = DataMap["allowSetHome"].AsBoolean();
                RegInfo.EstateSettings.AllowVoice = DataMap["allowVoice"].AsBoolean();
                RegInfo.EstateSettings.DenyAnonymous = DataMap["denyAnonymous"].AsBoolean();
                RegInfo.EstateSettings.DenyIdentified = DataMap["denyIdentified"].AsBoolean();
                RegInfo.EstateSettings.DenyMinors = DataMap["denyMinors"].AsBoolean();
                RegInfo.EstateSettings.DenyTransacted = DataMap["denyTransacted"].AsBoolean();
                // RegInfo.EstateSettings.EstateAccess = DataMap["estateAccess"].AsUUIDArray();
                // RegInfo.EstateSettings.EstateBans = DataMap["estateBans"].AsUUIDArray();
                // RegInfo.EstateSettings.EstateGroups = DataMap["estateGroups"].AsUUIDArray();
                // RegInfo.EstateSettings.EstateManagers = DataMap["estateManagers"].AsUUIDArray();
                RegInfo.EstateSettings.EstateName = DataMap["estateName"].AsString();
                RegInfo.EstateSettings.EstateOwner = DataMap["estateOwner"].AsUUID();
                RegInfo.EstateSettings.EstateSkipScripts = DataMap["estateSkipScripts"].AsBoolean();
                RegInfo.EstateSettings.FixedSun = DataMap["estateFixedSun"].AsBoolean();
                RegInfo.EstateSettings.PublicAccess = DataMap["publicAccess"].AsBoolean();

                IEstateModule estate = RegionContext.Scene.RequestModuleInterface<IEstateModule>();
                if (estate != null)
                {
                    estate.sendRegionHandshakeToAll();
                    estate.TriggerEstateInfoChange();
                }
                RegionContext.Scene.TriggerEstateSunUpdate();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap(5);
                    data["agentLimit"] = OSD.FromInteger(RegInfo.RegionSettings.AgentLimit);
                    data["allowDamage"] = OSD.FromBoolean(RegInfo.RegionSettings.AllowDamage);
                    data["allowLandJoinDivide"] = OSD.FromBoolean(RegInfo.RegionSettings.AllowLandJoinDivide);
                    data["allowLandResale"] = OSD.FromBoolean(RegInfo.RegionSettings.AllowLandResell);
                    data["blockFly"] = OSD.FromBoolean(RegInfo.RegionSettings.BlockFly);
                    data["blockShowInSearch"] = OSD.FromBoolean(RegInfo.RegionSettings.BlockShowInSearch);
                    data["blockTerraform"] = OSD.FromBoolean(RegInfo.RegionSettings.BlockTerraform);
                    data["covenant"] = OSD.FromUUID(RegInfo.RegionSettings.Covenant);
                    data["disableCollisions"] = OSD.FromBoolean(RegInfo.RegionSettings.DisableCollisions);
                    data["disablePhysics"] = OSD.FromBoolean(RegInfo.RegionSettings.DisablePhysics);
                    data["disableScripts"] = OSD.FromBoolean(RegInfo.RegionSettings.DisableScripts);
                    data["elevation1NE"] = OSD.FromReal(RegInfo.RegionSettings.Elevation1NE);
                    data["elevation1NW"] = OSD.FromReal(RegInfo.RegionSettings.Elevation1NW);
                    data["elevation1SE"] = OSD.FromReal(RegInfo.RegionSettings.Elevation1SE);
                    data["elevation1SW"] = OSD.FromReal(RegInfo.RegionSettings.Elevation1SW);
                    data["elevation2NE"] = OSD.FromReal(RegInfo.RegionSettings.Elevation2NE);
                    data["elevation2NW"] = OSD.FromReal(RegInfo.RegionSettings.Elevation2NW);
                    data["elevation2SE"] = OSD.FromReal(RegInfo.RegionSettings.Elevation2SE);
                    data["elevation2SW"] = OSD.FromReal(RegInfo.RegionSettings.Elevation2SW);
                    data["regionFixedSun"] = OSD.FromBoolean(RegInfo.RegionSettings.FixedSun);
                    data["maturity"] = OSD.FromInteger(RegInfo.RegionSettings.Maturity);
                    data["parcelImageID"] = OSD.FromUUID(RegInfo.RegionSettings.ParcelImageID);
                    data["restrictPushing"] = OSD.FromBoolean(RegInfo.RegionSettings.RestrictPushing);
                    data["sandbox"] = OSD.FromBoolean(RegInfo.RegionSettings.Sandbox);
                    data["telehubObject"] = OSD.FromUUID(RegInfo.RegionSettings.TelehubObject);
                    data["terrainImageID"] = OSD.FromUUID(RegInfo.RegionSettings.TerrainImageID);
                    data["terrainLowerLimit"] = OSD.FromReal(RegInfo.RegionSettings.TerrainLowerLimit);
                    data["terrainRaiseLimit"] = OSD.FromReal(RegInfo.RegionSettings.TerrainRaiseLimit);
                    data["terrainTexture1"] = OSD.FromUUID(RegInfo.RegionSettings.TerrainTexture1);
                    data["terrainTexture2"] = OSD.FromUUID(RegInfo.RegionSettings.TerrainTexture2);
                    data["terrainTexture3"] = OSD.FromUUID(RegInfo.RegionSettings.TerrainTexture3);
                    data["terrainTexture4"] = OSD.FromUUID(RegInfo.RegionSettings.TerrainTexture4);
                    data["useEstateSun"] = OSD.FromBoolean(RegInfo.RegionSettings.UseEstateSun);
                    data["waterHeight"] = OSD.FromReal(RegInfo.RegionSettings.WaterHeight);
                    
                    data["allowLandmark"] = OSD.FromBoolean(RegInfo.EstateSettings.AllowLandmark);
                    data["allowParcelChanges"] = OSD.FromBoolean(RegInfo.EstateSettings.AllowParcelChanges);
                    data["allowSetHome"] = OSD.FromBoolean(RegInfo.EstateSettings.AllowSetHome);
                    data["allowVoice"] = OSD.FromBoolean(RegInfo.EstateSettings.AllowVoice);
                    data["denyAnonymous"] = OSD.FromBoolean(RegInfo.EstateSettings.DenyAnonymous);
                    data["denyIdentified"] = OSD.FromBoolean(RegInfo.EstateSettings.DenyIdentified);
                    data["denyMinors"] = OSD.FromBoolean(RegInfo.EstateSettings.DenyMinors);
                    data["denyTransacted"] = OSD.FromBoolean(RegInfo.EstateSettings.DenyTransacted);
                    // data["estateAccess"] = OSD.FromUUIDArray(RegInfo.EstateSettings.EstateAccess);
                    // data["estateBans"] = OSD.FromBoolean(RegInfo.EstateSettings.EstateBans);
                    // data["estateGroups"] = OSD.FromBoolean(RegInfo.EstateSettings.EstateGroups);
                    // data["estateManagers"] = OSD.FromBoolean(RegInfo.EstateSettings.EstateManagers);
                    data["estateName"] = OSD.FromString(RegInfo.EstateSettings.EstateName);
                    data["estateOwner"] = OSD.FromUUID(RegInfo.EstateSettings.EstateOwner);
                    data["estateSkipScripts"] = OSD.FromBoolean(RegInfo.EstateSettings.EstateSkipScripts);
                    data["estateFixedSun"] = OSD.FromBoolean(RegInfo.EstateSettings.FixedSun);
                    data["publicAccess"] = OSD.FromBoolean(RegInfo.EstateSettings.PublicAccess);

                    /* Someday send the parcel information
                    List<ILandObject> parcels = RegionContext.Scene.LandChannel.AllParcels();
                    foreach (ILandObject parcel in parcels)
                    {
                    }
                     */

                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        // Logs the whole string of parameters received. Only happens once per region connect.
        public override void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            pRegionContext.DetailedUpdateWrite(DetailLogTagRcv, ZeroUUID, 0, ZeroUUID, DataMap.ToString(), DataLength);
        }
    }
    // ====================================================================================================
    public class SyncMsgTimeStamp : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvTimStmp"; } }
        public override string DetailLogTagSnd { get { return "SndTimStmp"; } }

        public long TickTime { get; set; }

        public SyncMsgTimeStamp(RegionSyncModule pRegionContext, long pTickTime)
            : base(MsgType.TimeStamp, pRegionContext)
        {
            TickTime = pTickTime;
        }
        public SyncMsgTimeStamp(int pLength, byte[] pData)
            : base(MsgType.TimeStamp, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            // Do something interesting with the time code from the other side
            return base.HandleIn(pRegionContext);
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap(1);
                    data["timeStamp"] = OSD.FromLong(TickTime);
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    // Sending asks the other end to send us information about the terrain.
    // When received, send back information about the terrain.
    public class SyncMsgGetTerrain : SyncMsg
    {
        public override string DetailLogTagRcv { get { return "RcvGetTerr"; } }
        public override string DetailLogTagSnd { get { return "SndGetTerr"; } }

        public SyncMsgGetTerrain(RegionSyncModule pRegionContext)
            : base(MsgType.GetTerrain, pRegionContext)
        {
        }
        public SyncMsgGetTerrain(int pLength, byte[] pData)
            : base(MsgType.GetTerrain, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                SyncMsgTerrain msg = new SyncMsgTerrain(pRegionContext, pRegionContext.TerrainSyncInfo);
                msg.ConvertOut(pRegionContext);
                ConnectorContext.ImmediateOutgoingMsg(msg);
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
        public override void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            pRegionContext.DetailedUpdateWrite(DetailLogTagRcv, ZeroUUID, pRegionContext.TerrainSyncInfo.LastUpdateTimeStamp, ZeroUUID, pConnectorContext.otherSideActorID, 0);
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagSnd, ZeroUUID, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
    }
    // ====================================================================================================
    public class SyncMsgTerrain : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvTerrain"; } }
        public override string DetailLogTagSnd { get { return "SndTerrain"; } }

        public string TerrainData { get; set; }
        public long LastUpdateTimeStamp { get; set; }
        public string LastUpdateActorID { get; set; }

        public SyncMsgTerrain(RegionSyncModule pRegionContext, TerrainSyncInfo pTerrainInfo)
            : base(MsgType.Terrain, pRegionContext)
        {
        }
        public SyncMsgTerrain(int pLength, byte[] pData)
            : base(MsgType.Terrain, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                TerrainData = DataMap["terrain"].AsString();
                LastUpdateTimeStamp = DataMap["timeStamp"].AsLong();
                LastUpdateActorID = DataMap["actorID"].AsString();
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                //update the terrain if the incoming terrain data has a more recent timestamp
                pRegionContext.TerrainSyncInfo.UpdateTerrianBySync(LastUpdateTimeStamp, LastUpdateActorID, TerrainData);
                if (pRegionContext.IsSyncRelay)
                {
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this);
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap(3);
                    data["terrain"] = OSD.FromString((string)pRegionContext.TerrainSyncInfo.LastUpdateValue);
                    data["actorID"] = OSD.FromString(pRegionContext.TerrainSyncInfo.LastUpdateActorID);
                    data["timeStamp"] = OSD.FromLong(pRegionContext.TerrainSyncInfo.LastUpdateTimeStamp);
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }

    // ====================================================================================================
    public class SyncMsgObjects : SyncMsgBinaryData
    {
        public override string DetailLogTagRcv { get { return "RcvGetObjj"; } }
        public override string DetailLogTagSnd { get { return "SndGetObjj"; } }

        Guid m_requestId;
        long m_timestamp;
        HashSet<UUID> m_sogs;
        MemoryStream m_archiveWriteStream;
        byte[] m_encodedOar;
        bool m_merge;

        public SyncMsgObjects(RegionSyncModule pRegionContext, HashSet<UUID> sogs, SyncConnector returnAddress, bool merge)
            : base(MsgType.Objects, pRegionContext)
        {
            m_sogs = sogs;
            m_archiveWriteStream = new MemoryStream();
            m_requestId = Guid.NewGuid();
            ConnectorContext = returnAddress;

            pRegionContext.Scene.EventManager.OnOarFileSaved += SaveCompleted;
            Dictionary<string, Object> options = new Dictionary<string, Object>();
            options.Add("objects", sogs);
            options.Add("noassets", true);
            m_merge = merge;
            IRegionArchiverModule archiver = pRegionContext.Scene.RequestModuleInterface<IRegionArchiverModule>();
            archiver.ArchiveRegion(m_archiveWriteStream, m_requestId, options);
        }

        public SyncMsgObjects(int pLength, byte[] pData)
            : base(MsgType.Objects, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            if (base.ConvertIn(pRegionContext))
            {
                m_encodedOar = Data;
                m_timestamp = DataMap["ts"].AsLong();
                m_merge = DataMap["merge"].AsBoolean();

                pRegionContext.Scene.EventManager.OnOarFileLoaded += LoadCompleted;
                return true;
            }
            return false;
        }

        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            MemoryStream ms = new MemoryStream(m_encodedOar);
            IRegionArchiverModule archiver = pRegionContext.Scene.RequestModuleInterface<IRegionArchiverModule>();
            // Despite what the event OnOarFileLoaded suggests, DeArchiving is monothreaded method, so RememberLocallyGenerated will work
            pRegionContext.RememberLocallyGeneratedEvent(MType, m_timestamp);
            archiver.DearchiveRegion(ms, m_merge, true, new Guid(),false);
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            if (Data == null || DataMap == null)
                m_log.ErrorFormat("{0}: Not finished archiving process. Please wait.", LogHeader);
            else
                return base.ConvertOut(pRegionContext);
            return false;
        }

        // Direction == Out
        private void SaveCompleted(Guid requestId, string errorMessage)
        {
            if (requestId == m_requestId)
            {
                RegionContext.Scene.EventManager.OnOarFileSaved -= SaveCompleted;
                if (errorMessage.Length > 0)
                    m_log.ErrorFormat("{0}: Error packing OAR in SyncMsgObjects. Error: {1}", LogHeader, errorMessage);
                else
                {
                    m_encodedOar = m_archiveWriteStream.ToArray();
                    Data = m_encodedOar;
                    DataMap = new OSDMap();
                    DataMap["ts"] = RegionSyncModule.NowTicks();
                    DataMap["merge"] = m_merge;
                    ConnectorContext.ImmediateOutgoingMsg(this);
                }
            }
        }

        // Direction == In
        private void LoadCompleted(Guid guid, List<UUID> loadedScenes, string message)
        {
            RegionContext.ForgetLocallyGeneratedEvent();
            RegionContext.Scene.EventManager.OnOarFileLoaded -= LoadCompleted;
            m_log.WarnFormat("{0}: Oar from SyncMsgObjects finished loading", LogHeader);
        }
    }

    // ====================================================================================================
    public class SyncMsgGetObjects : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvGetObjj"; } }
        public override string DetailLogTagSnd { get { return "SndGetObjj"; } }

        HashSet<UUID> m_objects = new HashSet<UUID>();

        public SyncMsgGetObjects(RegionSyncModule pRegionContext)
            : base(MsgType.GetObjects, pRegionContext)
        {
        }

        public SyncMsgGetObjects(int pLength, byte[] pData)
            : base(MsgType.GetObjects, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            if (base.ConvertIn(pRegionContext))
            {
            }
            return true;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            m_log.WarnFormat("{0}: Started sending objects", LogHeader);
            if (base.HandleIn(pRegionContext))
            {
                HashSet<SyncQuark> allQuarks = pRegionContext.QuarkManager.ActorSubscriptions[ConnectorContext].AllQuarks;
                // If I received a list of new quarks, send all the objects of quarks that are 1) subscribed in this actor and 2) added remotely.
                
                pRegionContext.Scene.ForEachSOG(delegate(SceneObjectGroup sog)
                {
                    if (allQuarks.Contains(new SyncQuark(sog.AbsolutePosition)))
                    {
                        m_objects.Add(sog.UUID);
                    }
                });
            }

            // Reply back with all the relevant scene object groups in OAR format. This SyncMsg auto-sends itself back to the origin.
            SyncMsgObjects syncMsg = new SyncMsgObjects(pRegionContext, m_objects, ConnectorContext, false);
            m_log.WarnFormat("{0}: Done sending objects", LogHeader);
            return true;
        }

        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            // Sending a request to get objects. If m_newQuarks is not defined, DataMap can be empty.
            if (DataMap == null)
            {
                DataMap = new OSDMap();
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgGetPresences : SyncMsg
    {
        public override string DetailLogTagRcv { get { return "RcvGetPres"; } }
        public override string DetailLogTagSnd { get { return "SndGetPres"; } }

        public SyncMsgGetPresences(RegionSyncModule pRegionContext)
            : base(MsgType.GetPresences, pRegionContext)
        {
        }
        public SyncMsgGetPresences(int pLength, byte[] pData)
            : base(MsgType.GetPresences, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                HashSet<SyncQuark> allQuarks = pRegionContext.QuarkManager.ActorSubscriptions[ConnectorContext].AllQuarks;
                EntityBase[] entities = pRegionContext.Scene.GetEntities();
                foreach (EntityBase e in entities)
                {
                    ScenePresence sp = e as ScenePresence;
                    if (sp != null)
                    {
                        if (allQuarks.Contains(new SyncQuark(sp.AbsolutePosition)))
                        {
                            // This will sync the appearance that's currently in the agent circuit data.
                            // If the avatar has updated their appearance since they connected, the original data will still be in ACD.
                            // The ACD normally only gets updated when an avatar is moving between regions.
                            SyncMsgNewPresence msg = new SyncMsgNewPresence(pRegionContext, sp);
                            msg.ConvertOut(pRegionContext);
                            // m_log.DebugFormat("{0}: Send NewPresence message for {1} ({2})", LogHeader, sp.Name, sp.UUID);
                            ConnectorContext.ImmediateOutgoingMsg(msg);
                        }
                    }
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgNewObject : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvNewObjj"; } }
        public override string DetailLogTagSnd { get { return "SndNewObjj"; } }

        public SceneObjectGroup SOG;
        public Dictionary<UUID, SyncInfoBase> SyncInfos;

        public SyncMsgNewObject(RegionSyncModule pRegionContext, SceneObjectGroup pSog)
            : base(MsgType.NewObject, pRegionContext)
        {
            SOG = pSog;
        }
        public SyncMsgNewObject(int pLength, byte[] pData)
            : base(MsgType.NewObject, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                if (!DecodeSceneObject(DataMap, out SOG, out SyncInfos, pRegionContext.Scene))
                {
                    m_log.WarnFormat("{0}: Failed to decode scene object in HandleSyncNewObject", LogHeader);
                    return false;
                }

                if (SOG.RootPart.Shape == null)
                {
                    m_log.WarnFormat("{0}: group.RootPart.Shape is null", LogHeader);
                    return false;
                }
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                pRegionContext.RememberLocallyGeneratedEvent(MType);
                // If this is a relay node, forward the message
                if (pRegionContext.IsSyncRelay)
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this, 
                        SyncInfos[SOG.RootPart.UUID].CurQuark.QuarkName);

                //Add the list of PrimSyncInfo to SyncInfoManager
                foreach (SyncInfoBase syncInfo in SyncInfos.Values)
                    pRegionContext.InfoManager.InsertSyncInfoRemote(syncInfo.UUID, syncInfo);

                // Add the decoded object to Scene
                // This will invoke OnObjectAddedToScene but the syncinfo has already been created so that's a NOP
                pRegionContext.Scene.EventManager.TriggerParcelPrimCountTainted();
                pRegionContext.Scene.AddNewSceneObject(SOG, true);

                ReactivateSOG(SOG, SyncInfos);
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    DataMap = EncodeSceneObject(SOG, pRegionContext);
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        public override void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagRcv, SOG == null ? UUID.Zero : SOG.UUID, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagSnd, SOG == null ? UUID.Zero : SOG.UUID, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
    }
    // ====================================================================================================
    public class SyncMsgRemovedObject : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvRemObjj"; } }
        public override string DetailLogTagSnd { get { return "SndRemObjj"; } }

        public UUID Uuid { get; set; }
        public bool SoftDelete { get; set; }
        public string ActorID { get; set; }

        public SyncMsgRemovedObject(RegionSyncModule pRegionContext, UUID pUuid, string pActorID, bool pSoftDelete)
            : base(MsgType.RemovedObject, pRegionContext)
        {
            Uuid = pUuid;
            SoftDelete = pSoftDelete;
            ActorID = pActorID;
        }
        public SyncMsgRemovedObject(int pLength, byte[] pData)
            : base(MsgType.RemovedObject, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {

                Uuid = DataMap["uuid"].AsUUID();
                SoftDelete = DataMap["softDelete"].AsBoolean();
                ActorID = DataMap["actorID"].AsString();

                if (pRegionContext.InfoManager.SyncInfoExists(Uuid))
                    ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                SceneObjectGroup sog = pRegionContext.Scene.GetGroupByPrim(Uuid);
                if (sog != null)
                {
                    // If this is a relay node, forward the message
                    if (pRegionContext.IsSyncRelay)
                        pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this,
                            pRegionContext.InfoManager.GetSyncInfo(sog.RootPart.UUID).CurQuark.QuarkName);

                
                    if (!SoftDelete)
                    {
                        //m_log.DebugFormat("{0}: hard delete object {1}", LogHeader, sog.UUID);
                        foreach (SceneObjectPart part in sog.Parts)
                        {
                            pRegionContext.InfoManager.RemoveSyncInfo(part.UUID);
                        }
                        pRegionContext.Scene.DeleteSceneObject(sog, false);
                    }
                    else
                    {
                        //m_log.DebugFormat("{0}: soft delete object {1}", LogHeader, sog.UUID);
                        pRegionContext.Scene.UnlinkSceneObject(sog, true);
                    }
                }
                else 
                {
                    // ?? This is only for debugging, for now..
                    // m_log.WarnFormat("{0}: Got a RemoveObject for unexisting ID!",LogHeader);
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap(2);
                    data["uuid"] = OSD.FromUUID(Uuid);
                    data["softDelete"] = OSD.FromBoolean(SoftDelete);
                    data["actorID"] = OSD.FromString(ActorID);
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        public override void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            if (pRegionContext != null)
                pRegionContext.DetailedUpdateWrite(DetailLogTagRcv, Uuid, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagSnd, Uuid, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
    }
    // ====================================================================================================
    public class SyncMsgLinkObject : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvLinkObj"; } }
        public override string DetailLogTagSnd { get { return "SndLinkObj"; } }

        public SceneObjectGroup LinkedGroup;
        public UUID RootUUID;
        public List<UUID> ChildrenIDs;
        public string ActorID;

        public Dictionary<UUID, SyncInfoBase> GroupSyncInfos;
        public OSDMap EncodedSOG;
        public int PartCount;

        public SyncMsgLinkObject(RegionSyncModule pRegionContext, SceneObjectGroup pLinkedGroup, UUID pRootUUID, List<UUID> pChildrenUUIDs, string pActorID)
            : base(MsgType.LinkObject, pRegionContext)
        {
            LinkedGroup = pLinkedGroup;
            RootUUID = pRootUUID;
            ChildrenIDs = pChildrenUUIDs;
            ActorID = pActorID;
        }
        public SyncMsgLinkObject(int pLength, byte[] pData)
            : base(MsgType.LinkObject, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                EncodedSOG = (OSDMap)DataMap["linkedGroup"];
                if (!DecodeSceneObject(EncodedSOG, out LinkedGroup, out GroupSyncInfos, pRegionContext.Scene))
                {
                    m_log.WarnFormat("{0}: Failed to decode scene object in HandleSyncLinkObject", LogHeader);
                    return false; ;
                }

                if (LinkedGroup == null)
                {
                    m_log.ErrorFormat("{0}: HandleSyncLinkObject, no valid Linked-Group has been deserialized", LogHeader);
                    return false;
                }

                RootUUID = DataMap["rootID"].AsUUID();
                PartCount = DataMap["partCount"].AsInteger();
                ChildrenIDs = new List<UUID>();

                for (int i = 0; i < PartCount; i++)
                {
                    string partTempID = "part" + i;
                    ChildrenIDs.Add(DataMap[partTempID].AsUUID());
                }
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                // if this is a relay node, forward the message
                if (pRegionContext.IsSyncRelay)
                {
                    //SendSceneEventToRelevantSyncConnectors(senderActorID, msg, linkedGroup);
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this, 
                        GroupSyncInfos[RootUUID].CurQuark.QuarkName);
                }

                //m_log.DebugFormat("{0}: received LinkObject from {1}", LogHeader, senderActorID);

                //Update properties, if any has changed
                foreach (KeyValuePair<UUID, SyncInfoBase> partSyncInfo in GroupSyncInfos)
                {
                    UUID uuid = partSyncInfo.Key;
                    SyncInfoBase updatedPrimSyncInfo = partSyncInfo.Value;

                    SceneObjectPart part = pRegionContext.Scene.GetSceneObjectPart(uuid);
                    if (part == null)
                    {
                        m_log.ErrorFormat("{0}: HandleSyncLinkObject, prim {1} not in local Scene Graph after LinkObjectBySync is called", LogHeader, uuid);
                    }
                    else
                    {
                        pRegionContext.InfoManager.UpdateSyncInfoBySync(part.UUID, updatedPrimSyncInfo);
                    }
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    //Now encode the linkedGroup for sync
                    OSDMap data = new OSDMap();
                    OSDMap encodedSOG = EncodeSceneObject(LinkedGroup, pRegionContext);
                    data["linkedGroup"] = encodedSOG;
                    data["rootID"] = OSD.FromUUID(RootUUID);
                    data["partCount"] = OSD.FromInteger(ChildrenIDs.Count);
                    data["actorID"] = OSD.FromString(ActorID);
                    int partNum = 0;

                    string debugString = "";
                    foreach (UUID partUUID in ChildrenIDs)
                    {
                        string partTempID = "part" + partNum;
                        data[partTempID] = OSD.FromUUID(partUUID);
                        partNum++;

                        //m_log.DebugFormat("{0}: SendLinkObject to link {1},{2} with {3}, {4}", part.Name, part.UUID, root.Name, root.UUID);
                        debugString += partUUID + ", ";
                    }
                    // m_log.DebugFormat("SyncLinkObject: SendLinkObject to link parts {0} with {1}, {2}", debugString, root.Name, root.UUID);
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgDelinkObject : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvDLnkObj"; } }
        public override string DetailLogTagSnd { get { return "SndDLnkObj"; } }

        //public List<SceneObjectPart> LocalPrims = new List<SceneObjectPart>();
        public List<UUID> DelinkPrimIDs;
        public List<UUID> BeforeDelinkGroupIDs;
        public List<SceneObjectGroup> AfterDelinkGroups;
        public List<Dictionary<UUID, SyncInfoBase>> PrimSyncInfo;

        public SyncMsgDelinkObject(RegionSyncModule pRegionContext, List<UUID> pDelinkPrimIDs, List<UUID> pBeforeDlinkGroupIDs, List<SceneObjectGroup> pAfterDelinkGroups)
            : base(MsgType.DelinkObject, pRegionContext)
        {
            DelinkPrimIDs = pDelinkPrimIDs;
            BeforeDelinkGroupIDs = pBeforeDlinkGroupIDs;
            AfterDelinkGroups = pAfterDelinkGroups;
        }
        public SyncMsgDelinkObject(int pLength, byte[] pData)
            : base(MsgType.DelinkObject, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {

                //LocalPrims = new List<SceneObjectPart>();
                DelinkPrimIDs = new List<UUID>();
                BeforeDelinkGroupIDs = new List<UUID>();
                AfterDelinkGroups = new List<SceneObjectGroup>();
                PrimSyncInfo = new List<Dictionary<UUID, SyncInfoBase>>();

                int partCount = DataMap["partCount"].AsInteger();
                for (int i = 0; i < partCount; i++)
                {
                    string partTempID = "part" + i;
                    UUID primID = DataMap[partTempID].AsUUID();
                    //SceneObjectPart localPart = Scene.GetSceneObjectPart(primID);
                    //localPrims.Add(localPart);
                    DelinkPrimIDs.Add(primID);
                }

                int beforeGroupCount = DataMap["beforeGroupsCount"].AsInteger();
                for (int i = 0; i < beforeGroupCount; i++)
                {
                    string groupTempID = "beforeGroup" + i;
                    UUID beforeGroupID = DataMap[groupTempID].AsUUID();
                    BeforeDelinkGroupIDs.Add(beforeGroupID);
                }

                int afterGroupsCount = DataMap["afterGroupsCount"].AsInteger();
                for (int i = 0; i < afterGroupsCount; i++)
                {
                    string groupTempID = "afterGroup" + i;
                    //string sogxml = data[groupTempID].AsString();
                    SceneObjectGroup afterGroup;
                    OSDMap encodedSOG = (OSDMap)DataMap[groupTempID];
                    Dictionary<UUID, SyncInfoBase> groupSyncInfo;
                    if (!DecodeSceneObject(encodedSOG, out afterGroup, out groupSyncInfo, pRegionContext.Scene))
                    {
                        m_log.WarnFormat("{0}: Failed to decode scene object in HandleSyncDelinkObject", LogHeader);
                        return false;
                    }

                    AfterDelinkGroups.Add(afterGroup);
                    PrimSyncInfo.Add(groupSyncInfo);
                }
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                // if this is a relay node, forward the message
                if (pRegionContext.IsSyncRelay)
                {
                    List<SceneObjectGroup> tempBeforeDelinkGroups = new List<SceneObjectGroup>();
                    foreach (UUID sogID in BeforeDelinkGroupIDs)
                    {
                        SceneObjectGroup sog = pRegionContext.Scene.GetGroupByPrim(sogID);
                        tempBeforeDelinkGroups.Add(sog);
                    }
                    pRegionContext.SendDelinkObjectToRelevantSyncConnectors(ConnectorContext.otherSideActorID, tempBeforeDelinkGroups, this);
                }

                //DSL Scene.DelinkObjectsBySync(delinkPrimIDs, beforeDelinkGroupIDs, incomingAfterDelinkGroups);

                //Sync properties 
                //Update properties, for each prim in each deLinked-Object
                foreach (Dictionary<UUID, SyncInfoBase> primsSyncInfo in PrimSyncInfo)
                {
                    foreach (KeyValuePair<UUID, SyncInfoBase> inPrimSyncInfo in primsSyncInfo)
                    {
                        UUID uuid = inPrimSyncInfo.Key;
                        SyncInfoBase updatedPrimSyncInfo = inPrimSyncInfo.Value;

                        SceneObjectPart part = pRegionContext.Scene.GetSceneObjectPart(uuid);
                        if (part == null)
                        {
                            m_log.ErrorFormat("{0}: HandleSyncDelinkObject, prim {1} not in local Scene Graph after DelinkObjectsBySync is called", LogHeader, uuid);
                        }
                        else
                        {
                            pRegionContext.InfoManager.UpdateSyncInfoBySync(part.UUID, updatedPrimSyncInfo);
                        }
                    }
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap();
                    data["partCount"] = OSD.FromInteger(DelinkPrimIDs.Count);
                    int partNum = 0;
                    foreach (UUID partUUID in DelinkPrimIDs)
                    {
                        string partTempID = "part" + partNum;
                        data[partTempID] = OSD.FromUUID(partUUID);
                        partNum++;
                    }
                    //We also include the IDs of beforeDelinkGroups, for now it is more for sanity checking at the receiving end, so that the receiver 
                    //could make sure its delink starts with the same linking state of the groups/prims.
                    data["beforeGroupsCount"] = OSD.FromInteger(BeforeDelinkGroupIDs.Count);
                    int groupNum = 0;
                    foreach (UUID affectedGroupUUID in BeforeDelinkGroupIDs)
                    {
                        string groupTempID = "beforeGroup" + groupNum;
                        data[groupTempID] = OSD.FromUUID(affectedGroupUUID);
                        groupNum++;
                    }

                    //include the property values of each object after delinking, for synchronizing the values
                    data["afterGroupsCount"] = OSD.FromInteger(AfterDelinkGroups.Count);
                    groupNum = 0;
                    foreach (SceneObjectGroup afterGroup in AfterDelinkGroups)
                    {
                        string groupTempID = "afterGroup" + groupNum;
                        //string sogxml = SceneObjectSerializer.ToXml2Format(afterGroup);
                        //data[groupTempID] = OSD.FromString(sogxml);
                        OSDMap encodedSOG = EncodeSceneObject(afterGroup, pRegionContext);
                        data[groupTempID] = encodedSOG;
                        groupNum++;
                    }
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgNewPresence : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvNewPres"; } }
        public override string DetailLogTagSnd { get { return "SndNewPres"; } }

        public UUID Uuid = UUID.Zero;
        public ScenePresence SP { get; set; }
        public SyncInfoBase SyncInfo;

        public SyncMsgNewPresence(RegionSyncModule pRegionContext, ScenePresence pSP)
            : base(MsgType.NewPresence, pRegionContext)
        {
            SP = pSP;
            Uuid = SP.UUID;
        }
        public SyncMsgNewPresence(int pLength, byte[] pData)
            : base(MsgType.NewPresence, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                // Decode presence and syncInfo from message data
                DecodeScenePresence(DataMap, out SyncInfo, pRegionContext.Scene);
                Uuid = SyncInfo.UUID;
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                // if this is a relay node, forward the message
                if (pRegionContext.IsSyncRelay)
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this, SyncInfo.CurQuark.QuarkName);

                // If we receive a new presence that for some reason indicates this region is its "real region" then ignore it.
                // Maybe we should even send out a message to remove the avatar from other actors? 
                if ((string)(((SyncInfoPresence)SyncInfo).CurrentlySyncedProperties[SyncableProperties.Type.RealRegion].LastUpdateValue) == RegionContext.Scene.Name)
                {
                    m_log.WarnFormat("{0}: Attempt to handle NewPresence message with RealRegion = {1}", LogHeader, RegionContext.Scene.Name);
                    return false;
                }

                //Add the SyncInfo to SyncInfoManager
                pRegionContext.InfoManager.InsertSyncInfoRemote(SyncInfo.UUID, SyncInfo);
                ReactivateSP(SyncInfo);
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    DataMap = EncodeScenePresence(SP, pRegionContext);
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        public override void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            if (pRegionContext != null)
                pRegionContext.DetailedUpdateWrite(DetailLogTagRcv, Uuid, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagSnd, Uuid, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
    }
    // ====================================================================================================
    public class SyncMsgRemovedPresence : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvRemPres"; } }
        public override string DetailLogTagSnd { get { return "SndRemPres"; } }

        public UUID Uuid = UUID.Zero;

        public SyncMsgRemovedPresence(RegionSyncModule pRegionContext, UUID pUuid)
            : base(MsgType.RemovedPresence, pRegionContext)
        {
            Uuid = pUuid;
        }
            
        public SyncMsgRemovedPresence(int pLength, byte[] pData)
            : base(MsgType.RemovedPresence, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                Uuid = DataMap["uuid"].AsUUID();
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                if (!pRegionContext.InfoManager.SyncInfoExists(Uuid))
                    return false;

                // if this is a relay node, forward the message
                if (pRegionContext.IsSyncRelay)
                {
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this, pRegionContext.InfoManager.GetSyncInfo(Uuid).CurQuark.QuarkName);
                }

                // This limits synced avatars to real clients (no npcs) until we sync PresenceType field

                // Event issue: Calling RemoveClient starts a Remove Object event to remove the attachments, which
                // eventually become a remove object sync message. We need to remember this.
                pRegionContext.RememberLocallyGeneratedEvent(SyncMsg.MsgType.RemovedPresence, Uuid);
                //pRegionContext.Scene.RemoveClient(Uuid, false);
                pRegionContext.Scene.CloseAgent(Uuid, true);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap();
                    data["uuid"] = OSD.FromUUID(Uuid);
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        public override void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            if (pRegionContext != null)
                pRegionContext.DetailedUpdateWrite(DetailLogTagRcv, Uuid, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagSnd, Uuid, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
    }
    // ====================================================================================================
    public class SyncMsgRegionName : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvRegName"; } }
        public override string DetailLogTagSnd { get { return "SndRegName"; } }

        public string RegName;

        public SyncMsgRegionName(RegionSyncModule pRegionContext, string pRegionName)
            : base(MsgType.RegionName, pRegionContext)
        {
            RegName = pRegionName;
        }
        public SyncMsgRegionName(int pLength, byte[] pData)
            : base(MsgType.RegionName, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                RegName = DataMap["regionName"];
                ret = true;
            }
            else
            {
                RegName = "UNKNOWN";
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                ConnectorContext.otherSideRegionName = RegName;
                if (pRegionContext.IsSyncRelay)
                {
                    SyncMsgRegionName msg = new SyncMsgRegionName(pRegionContext, pRegionContext.Scene.RegionInfo.RegionName);
                    msg.ConvertOut(pRegionContext);
                    ConnectorContext.ImmediateOutgoingMsg(msg);
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap();
                    data["regionName"] = RegName;
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagSnd, ZeroUUID, 0, RegionContext.Scene.RegionInfo.RegionName,
                                        pConnectorContext.otherSideActorID, DataLength);
        }
    }
    // ====================================================================================================
    public class SyncMsgActorID : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvActorID"; } }
        public override string DetailLogTagSnd { get { return "SndActorID"; } }

        private string ActorID;

        public SyncMsgActorID(RegionSyncModule pRegionContext, string pActorID)
            : base(MsgType.ActorID, pRegionContext)
        {
            ActorID = pActorID;
        }
        public SyncMsgActorID(int pLength, byte[] pData)
            : base(MsgType.ActorID, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                ActorID = DataMap["actorID"];
                ret = true;
            }
            else
            {
                ActorID = "UNKNOWN";
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                ConnectorContext.otherSideActorID = ActorID;
                if (pRegionContext.IsSyncRelay)
                {
                    SyncMsgActorID msg = new SyncMsgActorID(pRegionContext, pRegionContext.ActorID);
                    msg.ConvertOut(pRegionContext);
                    ConnectorContext.ImmediateOutgoingMsg(msg);
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap();
                    data["actorID"] = ActorID;
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagSnd, ZeroUUID, 0, ActorID, pConnectorContext.otherSideActorID, DataLength);
        }
    }
    // ====================================================================================================
    public class SyncMsgRegionStatus : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvRgnStat"; } }
        public override string DetailLogTagSnd { get { return "SndRgnStat"; } }

        public SyncMsgRegionStatus(RegionSyncModule pRegionContext)
            : base(MsgType.RegionStatus, pRegionContext)
        {
        }
        public SyncMsgRegionStatus(int pLength, byte[] pData)
            : base(MsgType.RegionStatus, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            return base.HandleIn(pRegionContext);
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public abstract class SyncMsgEvent : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvEventtt"; } }
        public override string DetailLogTagSnd { get { return "SndEventtt"; } }

        public string SyncID { get; set; }
        public string QuarkName { get; set; }
        public ulong SequenceNum { get; set; }

        public SyncMsgEvent(MsgType pMType, RegionSyncModule pRegionContext, UUID quarkUUID)
            : base(pMType, pRegionContext)
        {
            SyncID = "UNKNOWN";
            SequenceNum = 1;
            if (pRegionContext.InfoManager.SyncInfoExists(quarkUUID))
                QuarkName = pRegionContext.InfoManager.GetSyncInfo(quarkUUID).CurQuark.QuarkName;
            else
            {
                SceneObjectPart part = pRegionContext.Scene.GetSceneObjectPart(quarkUUID);
                if (part == null)
                    QuarkName = null;
                else
                    QuarkName = SyncQuark.GetQuarkNameByPosition(part.AbsolutePosition);
            }
        }

        public SyncMsgEvent(MsgType pMType, RegionSyncModule pRegionContext, Vector3 position)
            : base(pMType, pRegionContext)
        {
            SyncID = "UNKNOWN";
            SequenceNum = 1;
            QuarkName = SyncQuark.GetQuarkNameByPosition(position);
        }

        public SyncMsgEvent(MsgType pMType, RegionSyncModule pRegionContext, string pSyncID, ulong pSeqNum, UUID quarkUUID)
            : base(pMType, pRegionContext)
        {
            SyncID = pSyncID;
            SequenceNum = pSeqNum;
            QuarkName = pRegionContext.InfoManager.GetSyncInfo(quarkUUID).CurQuark.QuarkName;
        }

        public SyncMsgEvent(MsgType pMsgType, int pLength, byte[] pData)
            : base(pMsgType, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {   
                SyncID = DataMap["syncID"].AsString();
                SequenceNum = DataMap["seqNum"].AsULong();
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.HandleIn(pRegionContext))
            {
                //check if this is a duplicate event message that we have received before
                if (pRegionContext.EventRecord.IsSEQReceived(SyncID, SequenceNum))
                {
                    m_log.ErrorFormat("Duplicate event {0} originated from {1}, seq# {2} has been received", MType, SyncID, SequenceNum);
                    return false;
                }
                else
                {
                    pRegionContext.EventRecord.RecordEventReceived(SyncID, SequenceNum);
                }

                // if this is a relay node, forward the message
                if (pRegionContext.IsSyncRelay)
                {
                    // Currently being forwarded to all actors.
                    //if (QuarkName == null)
                    //    m_log.WarnFormat("{0}: Quark name is null. Event is being forwarded to all actors",LogHeader);
                    pRegionContext.SendSceneEventToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this, null, QuarkName);
                }
                ret = true;
            }
            return ret;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap != null)
                {
                    DataMap["syncID"] = OSD.FromString(SyncID);
                    DataMap["seqNum"] = OSD.FromULong(SequenceNum);
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        // Helper routines.
        // TODO: There could be an intermediate class SyncMsgEventChat
        protected OSChatMessage PrepareOnChatArgs(OSDMap data, RegionSyncModule pRegionContext)
        {
            OSChatMessage args = new OSChatMessage();
            args.Channel = data["channel"].AsInteger();
            args.Message = data["msg"].AsString();
            args.Position = data["pos"].AsVector3();
            args.From = data["name"].AsString();
            args.SenderUUID = data["id"].AsUUID();
            args.Scene = pRegionContext.Scene;
            args.Type = (ChatTypeEnum)data["type"].AsInteger();

            // Need to look up the sending object within this scene!
            args.SenderObject = pRegionContext.Scene.GetScenePresence(args.SenderUUID);
            if(args.SenderObject != null)
                args.Sender = ((ScenePresence)args.SenderObject).ControllingClient;
            else
                args.SenderObject = pRegionContext.Scene.GetSceneObjectPart(args.SenderUUID);
            //m_log.WarnFormat("RegionSyncModule.PrepareOnChatArgs: name:\"{0}\" msg:\"{1}\" pos:{2} id:{3}", args.From, args.Message, args.Position, args.SenderUUID);
            return args;
        }
        protected OSDMap PrepareChatArgs(OSChatMessage chat)
        {
            OSDMap data = new OSDMap();
            data["channel"] = OSD.FromInteger(chat.Channel);
            data["msg"] = OSD.FromString(chat.Message);
            data["pos"] = OSD.FromVector3(chat.Position);
            data["name"] = OSD.FromString(chat.From); //note this is different from OnLocalChatFromClient
            data["id"] = OSD.FromUUID(chat.SenderUUID);
            data["type"] = OSD.FromInteger((int)chat.Type);
            return data;
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            // Suppress automatic logging as message logging is done in RegionSyncModule when more info is known.
        }
    }

    public class SyncMsgKeepAlive : SyncMsg
    {
        public override string DetailLogTagRcv { get { return "RcvKpAlive"; } }
        public override string DetailLogTagSnd { get { return "SndKpAlive"; } }

        //public string RegName;

        public SyncMsgKeepAlive(RegionSyncModule pRegionContext)
            : base(MsgType.KeepAlive, pRegionContext)
        {
        }
        public SyncMsgKeepAlive(int pLength, byte[] pData)
            : base(MsgType.KeepAlive, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                ret = true;
            }

            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }

    }

    // ====================================================================================================
    public class SyncMsgNewScript : SyncMsgEvent
    {
        public override string DetailLogTagRcv { get { return "RcvNewScpt"; } }
        public override string DetailLogTagSnd { get { return "SndNewScpt"; } }

        public UUID Uuid { get; set; }
        public UUID AgentID { get; set; }
        public UUID ItemID { get; set; }
        public HashSet<SyncableProperties.Type> SyncableProperties;

        public HashSet<SyncedProperty> UpdatedProperties;

        public SyncMsgNewScript(RegionSyncModule pRegionContext, UUID pUuid, UUID pAgentID, UUID pItemID, HashSet<SyncableProperties.Type> pSyncableProperties)
            : base(MsgType.NewScript, pRegionContext, pUuid)
        {
            Uuid = pUuid;
            AgentID = pAgentID;
            ItemID = pItemID;
            SyncableProperties = pSyncableProperties;
        }
        public SyncMsgNewScript(int pLength, byte[] pData)
            : base(MsgType.NewScript, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                AgentID = DataMap["agentID"].AsUUID();
                Uuid = DataMap["uuid"].AsUUID();
                ItemID = DataMap["itemID"].AsUUID();
                // TODO: Currently broadcasting events, until quark solution is defined.
                /*
                if (pRegionContext.IsSyncRelay)
                    QuarkName = pRegionContext.InfoManager.GetSyncInfo(Uuid).CurQuark.QuarkName;
                */
                UpdatedProperties = SyncInfoBase.DecodeSyncedProperties(DataMap);
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                SceneObjectPart localPart = pRegionContext.Scene.GetSceneObjectPart(Uuid);

                if (localPart == null || localPart.ParentGroup.IsDeleted)
                {
                    m_log.ErrorFormat("{0}: HandleRemoteEvent_OnNewScript: prim {1} no longer in local SceneGraph", LogHeader, Uuid);
                    return false;
                }

                if (UpdatedProperties.Count > 0)
                {
                    HashSet<SyncableProperties.Type> propertiesUpdated = pRegionContext.InfoManager.UpdateSyncInfoBySync(Uuid, UpdatedProperties);
                }

                //The TaskInventory value might have already been sync'ed by UpdatedPrimProperties, 
                //but we still need to create the script instance by reading out the inventory.
                pRegionContext.RememberLocallyGeneratedEvent(MsgType.NewScript, AgentID, localPart, ItemID);
                pRegionContext.Scene.EventManager.TriggerNewScript(AgentID, localPart, ItemID);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = pRegionContext.InfoManager.EncodeProperties(Uuid, SyncableProperties);
                    if (data != null)
                    {
                        //syncData already includes uuid, add agentID and itemID next
                        data["agentID"] = OSD.FromUUID(AgentID);
                        data["itemID"] = OSD.FromUUID(ItemID);
                    }
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgUpdateScript : SyncMsgEvent
    {
        public override string DetailLogTagRcv { get { return "RcvUpDScpt"; } }
        public override string DetailLogTagSnd { get { return "SndUpDScpt"; } }

        public UUID AgentID { get; set; }
        public UUID ItemID { get; set; }
        public UUID PrimID { get; set; }
        public bool IsRunning { get; set; }
        public UUID AssetID { get; set; }

        public SyncMsgUpdateScript(RegionSyncModule pRegionContext, UUID pAgentID, UUID pItemID, UUID pPrimID, bool pIsRunning, UUID pAssetID)
            : base(MsgType.UpdateScript, pRegionContext, pPrimID)
        {
            AgentID = pAgentID;
            ItemID = pItemID;
            PrimID = pPrimID;
            IsRunning = pIsRunning;
            AssetID = pAssetID;
        }
        public SyncMsgUpdateScript(int pLength, byte[] pData)
            : base(MsgType.UpdateScript, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                AgentID = DataMap["agentID"].AsUUID();
                ItemID = DataMap["itemID"].AsUUID();
                PrimID = DataMap["primID"].AsUUID();
                // TODO: Currently broadcasting events, until quark solution is defined.
                /*
                if (pRegionContext.IsSyncRelay && PrimID != UUID.Zero)
                {
                    try
                    {
                        QuarkName = pRegionContext.InfoManager.GetSyncInfo(PrimID).CurQuark.QuarkName;
                    }
                    catch
                    {
                        m_log.WarnFormat("{0}: Could not retrieve prim SyncInfo.",LogHeader);
                    }
                }
                */
                IsRunning = DataMap["running"].AsBoolean();
                AssetID = DataMap["assetID"].AsUUID();
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                ArrayList errors = new ArrayList();
                if (PrimID != UUID.Zero)
                {
                    SceneObjectPart sop = pRegionContext.Scene.GetSceneObjectPart(PrimID);
                    if (sop != null)
                    {
                        TaskInventoryItem taskItem = sop.Inventory.GetInventoryItem(ItemID);
                        taskItem.AssetID = AssetID;
                        sop.ParentGroup.UpdateInventoryItem(taskItem);
                        if (IsRunning)
                        {
                            // Scripts are recreated when edited so replace the new script inventory item
                            sop.Inventory.RemoveScriptInstance(ItemID, false);
                            sop.Inventory.CreateScriptInstance(ItemID, 0, false, pRegionContext.Scene.DefaultScriptEngine, 0);
                            errors = sop.Inventory.GetScriptErrors(ItemID);
                        }
                        sop.ParentGroup.ResumeScripts();
                        if (errors.Count > 0)
                        {
                            m_log.ErrorFormat("{0} Script errors detected. TODO: Send errors back to actor that created script", LogHeader);
                        }

                        //trigger the event in the local scene
                        pRegionContext.RememberLocallyGeneratedEvent(MsgType.UpdateScript, AgentID, ItemID, PrimID, IsRunning, AssetID);
                        pRegionContext.Scene.EventManager.TriggerUpdateScript(AgentID, ItemID, PrimID, IsRunning, AssetID);
                        pRegionContext.ForgetLocallyGeneratedEvent();
                    }
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap(5 + 2);
                    data["agentID"] = OSD.FromUUID(AgentID);
                    data["itemID"] = OSD.FromUUID(ItemID);
                    data["primID"] = OSD.FromUUID(PrimID);
                    data["running"] = OSD.FromBoolean(IsRunning);
                    data["assetID"] = OSD.FromUUID(AssetID);
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgScriptReset : SyncMsgEvent
    {
        public override string DetailLogTagRcv { get { return "RcvRstScpt"; } }
        public override string DetailLogTagSnd { get { return "SndRstScpt"; } }

        public UUID ItemID { get; set; }
        public UUID PrimID { get; set; }

        public SyncMsgScriptReset(RegionSyncModule pRegionContext, UUID pItemID, UUID pPrimID)
            : base(MsgType.ScriptReset, pRegionContext, pPrimID)
        {
            ItemID = pItemID;
            PrimID = pPrimID;
        }
        public SyncMsgScriptReset(int pLength, byte[] pData)
            : base(MsgType.ScriptReset, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                ItemID = DataMap["itemID"].AsUUID();
                PrimID = DataMap["primID"].AsUUID();
                // TODO: Currently broadcasting events, until quark solution is defined.
                /*
                if (pRegionContext.IsSyncRelay)
                    QuarkName = pRegionContext.InfoManager.GetSyncInfo(PrimID).CurQuark.QuarkName;
                */
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                SceneObjectPart part = pRegionContext.Scene.GetSceneObjectPart(PrimID);
                if (part == null || part.ParentGroup.IsDeleted)
                {
                    m_log.ErrorFormat("{0}: part {1} does not exist, or is deleted", LogHeader, PrimID);
                    return false;
                }
                pRegionContext.RememberLocallyGeneratedEvent(MsgType.ScriptReset, part.LocalId, ItemID);
                pRegionContext.Scene.EventManager.TriggerScriptReset(part.LocalId, ItemID);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return false;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap(3 + 2);
                    data["itemID"] = OSD.FromUUID(ItemID);
                    data["primID"] = OSD.FromUUID(PrimID);
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgChatFromClient : SyncMsgEvent
    {
        public override string DetailLogTagRcv { get { return "RcvChatClt"; } }
        public override string DetailLogTagSnd { get { return "SndChatClt"; } }

        public OSChatMessage ChatMessage { get; set; }

        public SyncMsgChatFromClient(RegionSyncModule pRegionContext, OSChatMessage pChatMessage)
            : base(MsgType.ChatFromClient, pRegionContext, pChatMessage.Position)
        {
            ChatMessage = pChatMessage;
        }
        public SyncMsgChatFromClient(int pLength, byte[] pData)
            : base(MsgType.ChatFromClient, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                ChatMessage = PrepareOnChatArgs(DataMap, pRegionContext);
                //TODO: Currently broadcasting events until quark solution is defined.
                /*
                if (pRegionContext.IsSyncRelay)
                    QuarkName = SyncQuark.GetQuarkNameByPosition(ChatMessage.Position);
                */
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.HandleIn(pRegionContext))
            {
                m_log.DebugFormat("{0} SyncMsgChatFromClient: {1} : {2}", LogHeader, ChatMessage.From, ChatMessage.Message);
                if (ChatMessage.Sender is RegionSyncAvatar)
                    ((RegionSyncAvatar)ChatMessage.Sender).SyncChatFromClient(ChatMessage);
                else
                {
                    // With quarks, it is possible to receive messages from unknown clients. TODO: Fix events with quarks
                    pRegionContext.RememberLocallyGeneratedEvent(MsgType.ChatFromClient, ChatMessage);
                    pRegionContext.Scene.EventManager.TriggerOnChatFromClient(null, ChatMessage);
                    pRegionContext.ForgetLocallyGeneratedEvent();
                }
                ret = true;
            }
            return ret;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    DataMap = PrepareChatArgs(ChatMessage);
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgChatFromWorld : SyncMsgEvent
    {
        public override string DetailLogTagRcv { get { return "RcvChatWld"; } }
        public override string DetailLogTagSnd { get { return "SndChatWld"; } }

        public OSChatMessage ChatMessage { get; set; }

        public SyncMsgChatFromWorld(RegionSyncModule pRegionContext, OSChatMessage pChatMessage)
            : base(MsgType.ChatFromWorld, pRegionContext,pChatMessage.Position)
        {
            ChatMessage = pChatMessage;
        }
        public SyncMsgChatFromWorld(int pLength, byte[] pData)
            : base(MsgType.ChatFromWorld, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                ChatMessage = PrepareOnChatArgs(DataMap, pRegionContext);
                //TODO: Currently broadcasting events, until quark solution is defined
                /*
                if (pRegionContext.IsSyncRelay)
                    QuarkName = SyncQuark.GetQuarkNameByPosition(ChatMessage.Position);
                */
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                m_log.DebugFormat("{0} SyncMsgChatFromWorld: {1} : {2}", LogHeader, ChatMessage.From, ChatMessage.Message);
                pRegionContext.RememberLocallyGeneratedEvent(MsgType.ChatFromWorld, ChatMessage);
                // Let ChatModule get the event and deliver it to avatars
                pRegionContext.Scene.EventManager.TriggerOnChatFromWorld(ChatMessage.SenderObject, ChatMessage);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    DataMap = PrepareChatArgs(ChatMessage);
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgChatBroadcast : SyncMsgEvent
    {
        public override string DetailLogTagRcv { get { return "RcvChatBct"; } }
        public override string DetailLogTagSnd { get { return "SndChatBct"; } }

        public OSChatMessage ChatMessage { get; set; }

        public SyncMsgChatBroadcast(RegionSyncModule pRegionContext, OSChatMessage pChatMessage)
            : base(MsgType.ChatBroadcast, pRegionContext,pChatMessage.Position)
        {
            ChatMessage = pChatMessage;
        }
        public SyncMsgChatBroadcast(int pLength, byte[] pData)
            : base(MsgType.ChatBroadcast, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                ChatMessage = PrepareOnChatArgs(DataMap, pRegionContext);
                // TODO: Currently broadcasting events, until quark solution is defined.
                /*
                if (pRegionContext.IsSyncRelay)
                    QuarkName = SyncQuark.GetQuarkNameByPosition(ChatMessage.Position);
                */
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                m_log.DebugFormat("{0} SyncMsgChatBroadcast: {1} : {2}", LogHeader, ChatMessage.From, ChatMessage.Message);
                pRegionContext.RememberLocallyGeneratedEvent(MsgType.ChatBroadcast, ChatMessage);
                pRegionContext.Scene.EventManager.TriggerOnChatBroadcast(ChatMessage.SenderObject, ChatMessage);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    DataMap = PrepareChatArgs(ChatMessage);
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public abstract class SyncMsgEventGrabber : SyncMsgEvent
    {
        public UUID AgentID { get; set; }
        public UUID PrimID { get; set; }
        public UUID OriginalPrimID { get; set; }
        public Vector3 OffsetPos { get; set; }
        public SurfaceTouchEventArgs SurfaceArgs { get; set; }

        public SceneObjectPart SOP { get; set; }
        public uint OriginalID { get; set; }
        public ScenePresence SP;
        
        public SyncMsgEventGrabber(MsgType pMType, RegionSyncModule pRegionContext, UUID pAgentID, UUID pPrimID, UUID pOrigPrimID, Vector3 pOffset, SurfaceTouchEventArgs pTouchArgs)
            : base(pMType, pRegionContext, pPrimID)
        {
            AgentID = pAgentID;
            PrimID = pPrimID;
            OriginalPrimID = pOrigPrimID;
            OffsetPos = pOffset;
            SurfaceArgs = pTouchArgs;
        }
        public SyncMsgEventGrabber(MsgType pMsgType, int pLength, byte[] pData)
            : base(pMsgType, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                AgentID = DataMap["agentID"].AsUUID();
                PrimID = DataMap["primID"].AsUUID();
                // TODO: Currently broadcasting events, until quark solution is defined.
                /*
                if (pRegionContext.IsSyncRelay)
                    QuarkName = pRegionContext.InfoManager.GetSyncInfo(PrimID).CurQuark.QuarkName;
                */
                OriginalPrimID = DataMap["originalPrimID"].AsUUID();
                OffsetPos = DataMap["offsetPos"].AsVector3();
                SurfaceArgs = new SurfaceTouchEventArgs();
                SurfaceArgs.Binormal = DataMap["binormal"].AsVector3();
                SurfaceArgs.FaceIndex = DataMap["faceIndex"].AsInteger();
                SurfaceArgs.Normal = DataMap["normal"].AsVector3();
                SurfaceArgs.Position = DataMap["position"].AsVector3();
                SurfaceArgs.STCoord = DataMap["stCoord"].AsVector3();
                SurfaceArgs.UVCoord = DataMap["uvCoord"].AsVector3();
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                SOP = pRegionContext.Scene.GetSceneObjectPart(PrimID);
                if (SOP == null)
                {
                    //m_log.ErrorFormat("{0}: HandleRemoteEvent_OnObjectGrab: no prim with ID {1}", LogHeader, PrimID);
                    return false;
                }
                if (OriginalPrimID != UUID.Zero)
                {
                    SceneObjectPart originalPart = pRegionContext.Scene.GetSceneObjectPart(OriginalPrimID);
                    OriginalID = originalPart.LocalId;
                }

                // Get the scene presence in local scene that triggered the event
                if (!pRegionContext.Scene.TryGetScenePresence(AgentID, out SP))
                {
                    // If the user is on an active quark that my actor does not have of a different actor, this is expected.
                    m_log.ErrorFormat("{0} HandleRemoteEvent_OnObjectGrab: could not get ScenePresence for uuid {1}", LogHeader, AgentID);
                    return false;
                }
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap();
                    data["agentID"] = OSD.FromUUID(AgentID);
                    data["primID"] = OSD.FromUUID(PrimID);
                    data["originalPrimID"] = OSD.FromUUID(OriginalPrimID);
                    data["offsetPos"] = OSD.FromVector3(OffsetPos);
                    data["binormal"] = OSD.FromVector3(SurfaceArgs.Binormal);
                    data["faceIndex"] = OSD.FromInteger(SurfaceArgs.FaceIndex);
                    data["normal"] = OSD.FromVector3(SurfaceArgs.Normal);
                    data["position"] = OSD.FromVector3(SurfaceArgs.Position);
                    data["stCoord"] = OSD.FromVector3(SurfaceArgs.STCoord);
                    data["uvCoord"] = OSD.FromVector3(SurfaceArgs.UVCoord);
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgObjectGrab : SyncMsgEventGrabber
    {
        public override string DetailLogTagRcv { get { return "RcvGrabbbb"; } }
        public override string DetailLogTagSnd { get { return "SndGrabbbb"; } }

        public SyncMsgObjectGrab(RegionSyncModule pRegionContext, UUID pAgentID, UUID pPrimID, UUID pOrigPrimID, Vector3 pOffset, SurfaceTouchEventArgs pTouchArgs)
            : base(MsgType.ObjectGrab, pRegionContext, pAgentID, pPrimID, pOrigPrimID, pOffset, pTouchArgs)
        {
        }
        public SyncMsgObjectGrab(int pLength, byte[] pData)
            : base(MsgType.ObjectGrab, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            bool ret = base.HandleIn(pRegionContext);
            if (ret)
            {
                pRegionContext.RememberLocallyGeneratedEvent(MsgType.ObjectGrab, SOP.LocalId, OriginalID, OffsetPos, SP.ControllingClient, SurfaceArgs);
                pRegionContext.Scene.EventManager.TriggerObjectGrab(SOP.LocalId, OriginalID, OffsetPos, SP.ControllingClient, SurfaceArgs);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return ret;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgObjectGrabbing : SyncMsgEventGrabber
    {
        public override string DetailLogTagRcv { get { return "RcvGrabing"; } }
        public override string DetailLogTagSnd { get { return "SndGrabing"; } }

        public SyncMsgObjectGrabbing(RegionSyncModule pRegionContext, UUID pAgentID, UUID pPrimID, UUID pOrigPrimID, Vector3 pOffset, SurfaceTouchEventArgs pTouchArgs)
            : base(MsgType.ObjectGrabbing, pRegionContext, pAgentID, pPrimID, pOrigPrimID, pOffset, pTouchArgs)
        {
        }
        public SyncMsgObjectGrabbing(int pLength, byte[] pData)
            : base(MsgType.ObjectGrabbing, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            bool ret = base.HandleIn(pRegionContext);
            if (ret)
            {
                pRegionContext.RememberLocallyGeneratedEvent(MsgType.ObjectGrabbing, SOP.LocalId, OriginalID, OffsetPos, SP.ControllingClient, SurfaceArgs);
                pRegionContext.Scene.EventManager.TriggerObjectGrabbing(SOP.LocalId, OriginalID, OffsetPos, SP.ControllingClient, SurfaceArgs);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return ret;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgObjectDeGrab : SyncMsgEventGrabber
    {
        public override string DetailLogTagRcv { get { return "RcvDeGrabb"; } }
        public override string DetailLogTagSnd { get { return "SndDeGrabb"; } }

        public SyncMsgObjectDeGrab(RegionSyncModule pRegionContext, UUID pAgentID, UUID pPrimID, UUID pOrigPrimID, Vector3 pOffset, SurfaceTouchEventArgs pTouchArgs)
            : base(MsgType.ObjectDeGrab, pRegionContext, pAgentID, pPrimID, pOrigPrimID, pOffset, pTouchArgs)
        {
        }
        public SyncMsgObjectDeGrab(int pLength, byte[] pData)
            : base(MsgType.ObjectDeGrab, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            bool ret = base.HandleIn(pRegionContext);
            if (ret)
            {
                pRegionContext.RememberLocallyGeneratedEvent(MsgType.ObjectDeGrab, SOP.LocalId, OriginalID, SP.ControllingClient, SurfaceArgs);
                pRegionContext.Scene.EventManager.TriggerObjectDeGrab(SOP.LocalId, OriginalID, SP.ControllingClient, SurfaceArgs);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return ret;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgAttach : SyncMsgEvent
    {
        public override string DetailLogTagRcv { get { return "RcvAttachh";  } }
        public override string DetailLogTagSnd { get { return "SndAttachh"; } }

        public UUID PrimID { get; set; }
        public UUID ItemID { get; set; }
        public UUID AvatarID { get; set; }

        public SyncMsgAttach(RegionSyncModule pRegionContext, UUID pPrimID, UUID pItemID, UUID pAvatarID)
            : base(MsgType.Attach, pRegionContext, pAvatarID)
        {
            PrimID = pPrimID;
            ItemID = pItemID;
            AvatarID = pAvatarID;
        }
        public SyncMsgAttach(int pLength, byte[] pData)
            : base(MsgType.Attach, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                PrimID = DataMap["primID"].AsUUID();
                ItemID = DataMap["itemID"].AsUUID();
                AvatarID = DataMap["avatarID"].AsUUID();
                // TODO: Currently broadcasting events, until quark solution is defined.
                /*
                if (pRegionContext.IsSyncRelay)
                    QuarkName = pRegionContext.InfoManager.GetSyncInfo(AvatarID).CurQuark.QuarkName;
                */
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                SceneObjectPart part = pRegionContext.Scene.GetSceneObjectPart(PrimID);
                if (part == null)
                {
                    //m_log.WarnFormat("{0} HandleRemoteEvent_OnAttach: no part with UUID {1} found", LogHeader, PrimID);
                    return false;
                }

                uint localID = part.LocalId;
                pRegionContext.RememberLocallyGeneratedEvent(MsgType.Attach, localID, ItemID, AvatarID);
                pRegionContext.Scene.EventManager.TriggerOnAttach(localID, ItemID, AvatarID);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap(3 + 2);
                    data["primID"] = OSD.FromUUID(PrimID);
                    data["itemID"] = OSD.FromUUID(ItemID);
                    data["avatarID"] = OSD.FromUUID(AvatarID);
                    DataMap = data;
                }
            }
            return base.ConvertOut(pRegionContext);
        }
        public override void LogReception(RegionSyncModule pRegionContext, SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagRcv, AvatarID, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
        public override void LogTransmission(SyncConnector pConnectorContext)
        {
            if (RegionContext != null)
                RegionContext.DetailedUpdateWrite(DetailLogTagSnd, AvatarID, 0, ZeroUUID, pConnectorContext.otherSideActorID, DataLength);
        }
    }
    // ====================================================================================================
    public abstract class SyncMsgEventCollision : SyncMsgEvent
    {
        public UUID CollideeUUID;
        public uint CollideeID;
        public List<DetectedObject> Colliders;
        protected List<UUID> CollidersNotFound;

        public SyncMsgEventCollision(MsgType pMType, RegionSyncModule pRegionContext, UUID pCollidee, uint pCollideeID, List<DetectedObject> pColliders)
            : base(pMType, pRegionContext, pCollidee)
        {
            CollideeUUID = pCollidee;
            CollideeID = pCollideeID;
            Colliders = pColliders;
        }
        public SyncMsgEventCollision(MsgType pMsgType, int pLength, byte[] pData)
            : base(pMsgType, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                CollideeUUID = DataMap["uuid"].AsUUID();
                SceneObjectPart collisionPart = pRegionContext.Scene.GetSceneObjectPart(CollideeUUID);
                if (collisionPart == null)
                {
                    //m_log.ErrorFormat("{0}: HandleRemoteEvent_{1}: no part with UUID {2} found, event initiator {3}", 
                    //                LogHeader, MType.ToString(), CollideeUUID, pRegionContext.SyncID);
                    return false;
                }
                // TODO: Currently broadcasting events until quark solutuion for events is done.
                /*
                if (pRegionContext.IsSyncRelay)
                try
                {
                    QuarkName = pRegionContext.InfoManager.GetSyncInfo(CollideeUUID).CurQuark.QuarkName;
                }
                catch
                {
                    m_log.WarnFormat("{0}: Could not retrieve quark name in SyncMsgEventCollision.",LogHeader);
                }
                */
                
                CollideeID = collisionPart.LocalId;

                // Loop through all the passed UUIDs and build DetectedObject's for the collided with objects
                OSDArray collisionUUIDs = (OSDArray)DataMap["collisionUUIDs"];
                if (collisionUUIDs != null)
                {
                    Colliders = new List<DetectedObject>();
                    CollidersNotFound = new List<UUID>();

                    switch (MType)
                    {
                        case MsgType.ScriptColliding:
                        case MsgType.ScriptCollidingStart:
                        case MsgType.ScriptCollidingEnd:
                            BuildColliders(collisionUUIDs, pRegionContext);
                            break;
                        case MsgType.ScriptLandColliding:
                        case MsgType.ScriptLandCollidingStart:
                        case MsgType.ScriptLandCollidingEnd:
                            BuildLandColliders(collisionPart, collisionUUIDs, pRegionContext);
                            break;
                    }
                    if (CollidersNotFound.Count > 0)
                    {
                        // If collision events are received for objects that are not found, that usually
                        //     means the events beat the object creation. This re-queues the collision event to
                        //     replay when, hopefully, the object has been created.
                        RequeueCollisionsForNotFoundObjects(pRegionContext, collisionPart, CollidersNotFound);
                    }
                }
                else
                {
                    m_log.WarnFormat("{0}: Collision UUIDs was empty", LogHeader);
                }
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (Colliders == null || Colliders.Count == 0)
            {
                //m_log.WarnFormat("{0}: Could not find any colliders for {1}, discarding..", LogHeader, CollideeUUID);
                return false;
            }
            return base.HandleIn(pRegionContext);
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                {
                    OSDMap data = new OSDMap();
                    OSDArray collisionUUIDs = new OSDArray();
                    foreach (DetectedObject detObj in Colliders)
                    {
                        collisionUUIDs.Add(OSD.FromUUID(detObj.keyUUID));
                    }

                    data["uuid"] = OSD.FromUUID(CollideeUUID);
                    data["collisionUUIDs"] = collisionUUIDs;
                    DataMap = data;
                    // m_log.DebugFormat("{0} EventCollision.ConvertOut: data={1}", LogHeader, DataMap);   // DEBUG DEBUG
                }
            }
            return base.ConvertOut(pRegionContext);
        }

        #region UUID To DetectedObject converters
        private void BuildColliders(OSDArray collisionUUIDs, RegionSyncModule pRegionContext)
        {
            for (int i = 0; i < collisionUUIDs.Count; i++)
            {
                OSD arg = collisionUUIDs[i];
                UUID collidingUUID = arg.AsUUID();

                SceneObjectPart obj = pRegionContext.Scene.GetSceneObjectPart(collidingUUID);
                if (obj != null)
                {
                    DetectedObject detobj = new DetectedObject();
                    detobj.keyUUID = obj.UUID;
                    detobj.nameStr = obj.Name;
                    detobj.ownerUUID = obj.OwnerID;
                    detobj.posVector = obj.AbsolutePosition;
                    detobj.rotQuat = obj.GetWorldRotation();
                    detobj.velVector = obj.Velocity;
                    detobj.colliderType = 0;
                    detobj.groupUUID = obj.GroupID;
                    Colliders.Add(detobj);
                }
                else
                {
                    //collision object is not a prim, check if it's an avatar
                    ScenePresence av = pRegionContext.Scene.GetScenePresence(collidingUUID);
                    if (av != null)
                    {
                        DetectedObject detobj = new DetectedObject();
                        detobj.keyUUID = av.UUID;
                        detobj.nameStr = av.ControllingClient.Name;
                        detobj.ownerUUID = av.UUID;
                        detobj.posVector = av.AbsolutePosition;
                        detobj.rotQuat = av.Rotation;
                        detobj.velVector = av.Velocity;
                        detobj.colliderType = 0;
                        detobj.groupUUID = av.ControllingClient.ActiveGroupId;
                        Colliders.Add(detobj);
                    }
                    else
                    {
                        // m_log.WarnFormat("HandleRemoteEvent_ScriptCollidingStart for SOP {0},{1} with SOP/SP {2}, but the latter is not found in local Scene. Saved for later processing",
                        //             collisionPart.Name, collisionPart.UUID, collidingUUID);
                        CollidersNotFound.Add(collidingUUID);
                    }
                }
            }
        }
        private void BuildLandColliders(SceneObjectPart collisionPart, OSDArray collisionUUIDs, RegionSyncModule pRegionContext)
        {
            for (int i = 0; i < collisionUUIDs.Count; i++)
            {
                OSD arg = collisionUUIDs[i];
                UUID collidingUUID = arg.AsUUID();
                if (collidingUUID.Equals(UUID.Zero))
                {
                    //Hope that all is left is ground!
                    DetectedObject detobj = new DetectedObject();
                    detobj.keyUUID = UUID.Zero;
                    detobj.nameStr = "";
                    detobj.ownerUUID = UUID.Zero;
                    detobj.posVector = collisionPart.ParentGroup.RootPart.AbsolutePosition;
                    detobj.rotQuat = Quaternion.Identity;
                    detobj.velVector = Vector3.Zero;
                    detobj.colliderType = 0;
                    detobj.groupUUID = UUID.Zero;
                    Colliders.Add(detobj);
                }
            }
        }
        // If collision events are received for objects that are not found, that usually means the events beat the
        //     object creation. This re-queues the collision event to replay when, hopefully, the object has been created.
        // TODO:
        private void RequeueCollisionsForNotFoundObjects(RegionSyncModule pRegionContext, SceneObjectPart collisionPart, List<UUID> CollidersNotFound)
        {
        }
        #endregion // UUID To DetectedObject converters
    }
    // ====================================================================================================
    public class SyncMsgScriptCollidingStart : SyncMsgEventCollision
    {
        public override string DetailLogTagRcv { get { return "RcvColStrt"; } }
        public override string DetailLogTagSnd { get { return "SndColStrt"; } }

        public SyncMsgScriptCollidingStart(RegionSyncModule pRegionContext, UUID pCollidee, uint pCollideeID, List<DetectedObject> pColliders)
            : base(MsgType.ScriptCollidingStart, pRegionContext, pCollidee, pCollideeID, pColliders)
        {
        }
        public SyncMsgScriptCollidingStart(int pLength, byte[] pData)
            : base(MsgType.ScriptLandCollidingStart, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.HandleIn(pRegionContext))
            {
                ColliderArgs CollidingMessage = new ColliderArgs();
                CollidingMessage.Colliders = Colliders;

                // m_log.DebugFormat("ScriptCollidingStart received for {0}", CollideeID);
                pRegionContext.RememberLocallyGeneratedEvent(MType, CollideeID, CollidingMessage);
                pRegionContext.Scene.EventManager.TriggerScriptCollidingStart(CollideeID, CollidingMessage);
                pRegionContext.ForgetLocallyGeneratedEvent();

                ret = true;
            }
            return ret;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgScriptColliding : SyncMsgEventCollision
    {
        public override string DetailLogTagRcv { get { return "RcvColidng"; } }
        public override string DetailLogTagSnd { get { return "SndColidng"; } }

        public SyncMsgScriptColliding(RegionSyncModule pRegionContext, UUID pCollidee, uint pCollideeID, List<DetectedObject> pColliders)
            : base(MsgType.ScriptColliding, pRegionContext, pCollidee, pCollideeID, pColliders)
        {
        }
        public SyncMsgScriptColliding(int pLength, byte[] pData)
            : base(MsgType.ScriptColliding, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                ColliderArgs CollidingMessage = new ColliderArgs();
                CollidingMessage.Colliders = Colliders;

                // m_log.DebugFormat("ScriptColliding received for {0}", CollideeID);
                pRegionContext.RememberLocallyGeneratedEvent(MType, CollideeID, CollidingMessage);
                pRegionContext.Scene.EventManager.TriggerScriptColliding(CollideeID, CollidingMessage);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgScriptCollidingEnd : SyncMsgEventCollision
    {
        public override string DetailLogTagRcv { get { return "RcvCollEnd"; } }
        public override string DetailLogTagSnd { get { return "SndCollEnd"; } }

        public SyncMsgScriptCollidingEnd(RegionSyncModule pRegionContext, UUID pCollidee, uint pCollideeID, List<DetectedObject> pColliders)
            : base(MsgType.ScriptCollidingEnd, pRegionContext, pCollidee, pCollideeID, pColliders)
        {
        }
        public SyncMsgScriptCollidingEnd(int pLength, byte[] pData)
            : base(MsgType.ScriptCollidingEnd, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                ColliderArgs CollidingMessage = new ColliderArgs();
                CollidingMessage.Colliders = Colliders;

                // m_log.DebugFormat("ScriptCollidingEnd received for {0}", CollideeID);
                pRegionContext.RememberLocallyGeneratedEvent(MType, CollideeID, CollidingMessage);
                pRegionContext.Scene.EventManager.TriggerScriptCollidingEnd(CollideeID, CollidingMessage);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgScriptLandCollidingStart : SyncMsgEventCollision
    {
        public override string DetailLogTagRcv { get { return "RcvLColSrt"; } }
        public override string DetailLogTagSnd { get { return "SndLColSrt"; } }

        public SyncMsgScriptLandCollidingStart(RegionSyncModule pRegionContext, UUID pCollidee, uint pCollideeID, List<DetectedObject> pColliders)
            : base(MsgType.ScriptLandCollidingStart, pRegionContext, pCollidee, pCollideeID, pColliders)
        {
        }
        public SyncMsgScriptLandCollidingStart(int pLength, byte[] pData)
            : base(MsgType.ScriptLandCollidingStart, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                ColliderArgs CollidingMessage = new ColliderArgs();
                CollidingMessage.Colliders = Colliders;

                // m_log.DebugFormat("ScriptLandCollidingStart received for {0}", CollideeID);
                pRegionContext.RememberLocallyGeneratedEvent(MType, CollideeID, CollidingMessage);
                pRegionContext.Scene.EventManager.TriggerScriptLandCollidingStart(CollideeID, CollidingMessage);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgScriptLandColliding : SyncMsgEventCollision
    {
        public override string DetailLogTagRcv { get { return "RcvLColing"; } }
        public override string DetailLogTagSnd { get { return "SndLColing"; } }

        public SyncMsgScriptLandColliding(RegionSyncModule pRegionContext, UUID pCollidee, uint pCollideeID, List<DetectedObject> pColliders)
            : base(MsgType.ScriptLandColliding, pRegionContext, pCollidee, pCollideeID, pColliders)
        {
        }
        public SyncMsgScriptLandColliding(int pLength, byte[] pData)
            : base(MsgType.ScriptLandColliding, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                ColliderArgs CollidingMessage = new ColliderArgs();
                CollidingMessage.Colliders = Colliders;

                // m_log.DebugFormat("ScriptLandColliding received for {0}", CollideeID);
                pRegionContext.RememberLocallyGeneratedEvent(MType, CollideeID, CollidingMessage);
                pRegionContext.Scene.EventManager.TriggerScriptLandColliding(CollideeID, CollidingMessage);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }
    // ====================================================================================================
    public class SyncMsgScriptLandCollidingEnd : SyncMsgEventCollision
    {
        public override string DetailLogTagRcv { get { return "RcvLColEnd"; } }
        public override string DetailLogTagSnd { get { return "SndLColEnd"; } }

        public SyncMsgScriptLandCollidingEnd(RegionSyncModule pRegionContext, UUID pCollidee, uint pCollideeID, List<DetectedObject> pColliders)
            : base(MsgType.ScriptLandCollidingEnd, pRegionContext, pCollidee, pCollideeID, pColliders)
        {
        }
        public SyncMsgScriptLandCollidingEnd(int pLength, byte[] pData)
            : base(MsgType.ScriptLandCollidingEnd, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            return base.ConvertIn(pRegionContext);
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                ColliderArgs CollidingMessage = new ColliderArgs();
                CollidingMessage.Colliders = Colliders;

                // m_log.DebugFormat("ScriptLandCollidingEnd received for {0}", CollideeID);
                pRegionContext.RememberLocallyGeneratedEvent(MType, CollideeID, CollidingMessage);
                pRegionContext.Scene.EventManager.TriggerScriptLandCollidingEnd(CollideeID, CollidingMessage);
                pRegionContext.ForgetLocallyGeneratedEvent();
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            return base.ConvertOut(pRegionContext);
        }
    }

    // =============================================================================================================
    // SyncMsgQuarkSubscription: Carries quark subscription (active and passive quarks) information at RegionStarted
    // =============================================================================================================
    public class SyncMsgQuarkSubscription : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvQuarSub"; } }
        public override string DetailLogTagSnd { get { return "SndQuarSub"; } }

        Dictionary<string, HashSet<SyncQuark>> syncQuarks;
        QuarkManager m_quarkManager;
        bool m_request = false;
        bool m_reply = false;

        public SyncMsgQuarkSubscription(RegionSyncModule pRegionContext, bool request)
            : base(MsgType.QuarkSubscription, pRegionContext)
        {
            if (request)
                m_request = true;
            else
                m_reply = true;
            m_quarkManager = pRegionContext.QuarkManager;
        }
        public SyncMsgQuarkSubscription(int pLength, byte[] pData)
            : base(MsgType.QuarkSubscription, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            m_quarkManager = pRegionContext.QuarkManager;
            if (base.ConvertIn(pRegionContext))
            {
                syncQuarks = DecodeQuarkSubscriptionsFromMsg(DataMap);

                // If I received request == true, means Im the parent receiving a request. Set reply to True, so ConvertOut will reply
                if ((bool)DataMap["Request"])
                {
                    m_request = false;
                    m_reply = true;
                }
                // If request == false, means Im the child receiving back the parent's quarks. Set all to false, so reply is not triggered again.
                else
                {
                    m_request = false;
                    m_reply = false;
                }
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                Actor actor = new Actor();
                foreach (SyncQuark quark in syncQuarks["Active"])
                {
                    if (!m_quarkManager.QuarkSubscriptions.ContainsKey(quark.QuarkName))
                        m_quarkManager.QuarkSubscriptions[quark.QuarkName] = new QuarkPublisher(quark);
                    m_quarkManager.QuarkSubscriptions[quark.QuarkName].AddActiveSubscriber(ConnectorContext);
                    actor.ActiveQuarks.Add(quark); 
                }
                foreach (SyncQuark quark in syncQuarks["Passive"])
                {
                    if (!m_quarkManager.QuarkSubscriptions.ContainsKey(quark.QuarkName))
                        m_quarkManager.QuarkSubscriptions[quark.QuarkName] = new QuarkPublisher(quark);
                    m_quarkManager.QuarkSubscriptions[quark.QuarkName].AddPassiveSubscriber(ConnectorContext);
                    actor.PassiveQuarks.Add(quark);
                }
                m_quarkManager.ActorSubscriptions[ConnectorContext] = actor;
                if (m_reply)
                {
                    SyncMsgQuarkSubscription msg = new SyncMsgQuarkSubscription(pRegionContext,false);
                    msg.ConvertOut(pRegionContext);
                    ConnectorContext.ImmediateOutgoingMsg(msg);
                }
                
            }
            return true;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            DataMap = EncodeQuarkSubscriptions(m_request);
            return base.ConvertOut(pRegionContext);
        }

        
    }

    // =============================================================================================================
    // SyncMsgQuarkSubAdd: Synchronizes a new passive/active quark subscription for the actor
    // =============================================================================================================
    public class SyncMsgQuarkSubAdd : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvLColEnd"; } }
        public override string DetailLogTagSnd { get { return "SndLColEnd"; } }

        Dictionary<string, HashSet<SyncQuark>> syncQuarks= new Dictionary<string, HashSet<SyncQuark>>();
        QuarkManager m_quarkManager;
        Dictionary<string, string> quarkToActor = new Dictionary<string,string>();

        public SyncMsgQuarkSubAdd(RegionSyncModule pRegionContext, HashSet<SyncQuark> newActiveQuarks, HashSet<SyncQuark> newPassiveQuarks)
            : base(MsgType.QuarkSubAdd, pRegionContext)
        {
            syncQuarks["Active"] = newActiveQuarks;
            syncQuarks["Passive"] = newPassiveQuarks;
            m_quarkManager = pRegionContext.QuarkManager;
        }
        public SyncMsgQuarkSubAdd(int pLength, byte[] pData)
            : base(MsgType.QuarkSubAdd, pLength, pData)
        {
        }

        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            if (base.ConvertIn(pRegionContext))
            {
                m_quarkManager = pRegionContext.QuarkManager;
                OSDMap actorToPrim = (OSDMap)DataMap["QuarkToActor"];
                Dictionary<string, HashSet<SyncQuark>> newSubscriptions = DecodeQuarkSubscriptionsFromMsg(DataMap);
                foreach (string quark in actorToPrim.Keys)
                    quarkToActor[quark] = actorToPrim[quark].AsString();
                
                syncQuarks["Active"] = new HashSet<SyncQuark>();
                syncQuarks["Passive"] = new HashSet<SyncQuark>();
                foreach (string quark in (OSDArray)DataMap["ActiveQuarks"])
                    syncQuarks["Active"].Add(new SyncQuark(quark));
                foreach (string quark in (OSDArray)DataMap["PassiveQuarks"])
                    syncQuarks["Passive"].Add(new SyncQuark(quark));

                return true;
            }
            else
                return false;
        }

        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            if (base.HandleIn(pRegionContext))
            {
                HashSet<SyncQuark> sendObjects = new HashSet<SyncQuark>();
                Actor actor = m_quarkManager.ActorSubscriptions[ConnectorContext];

                foreach (SyncQuark quark in syncQuarks["Active"])
                {
                    if (!m_quarkManager.QuarkSubscriptions.ContainsKey(quark.QuarkName))
                        m_quarkManager.QuarkSubscriptions[quark.QuarkName] = new QuarkPublisher(quark);
                    m_quarkManager.QuarkSubscriptions[quark.QuarkName].AddActiveSubscriber(ConnectorContext);
                    actor.ActiveQuarks.Add(quark);

                    // If the sender requested prims for the new quark from me, send them.
                    if (quarkToActor.ContainsKey(quark.QuarkName) && (quarkToActor[quark.QuarkName] == pRegionContext.ActorID))
                        sendObjects.Add(quark);
                }
                foreach (SyncQuark quark in syncQuarks["Passive"])
                {
                    if (!m_quarkManager.QuarkSubscriptions.ContainsKey(quark.QuarkName))
                        m_quarkManager.QuarkSubscriptions[quark.QuarkName] = new QuarkPublisher(quark);
                    m_quarkManager.QuarkSubscriptions[quark.QuarkName].AddPassiveSubscriber(ConnectorContext);
                    actor.PassiveQuarks.Add(quark);

                    // If the sender requested prims for the new quark from me, send them.
                    if (quarkToActor.ContainsKey(quark.QuarkName) && (quarkToActor[quark.QuarkName] == pRegionContext.ActorID))
                        sendObjects.Add(quark);
                }

                HashSet<UUID> objects = new HashSet<UUID>();
                foreach (SyncQuark quark in sendObjects)
                {
                    pRegionContext.Scene.ForEachSOG(delegate(SceneObjectGroup sog)
                    {
                        if (quark.IsPositionInQuark(sog.AbsolutePosition))
                        {
                            objects.Add(sog.UUID);
                        }
                    });
                }

                // Reminder: this is an auto-sending message, due to its asynchronous nature
                if (objects.Count > 0)
                    new SyncMsgObjects(pRegionContext, objects, ConnectorContext, true);
                else
                    m_log.WarnFormat("{0}: Did not have any objects to send",LogHeader);

                return true;
            }
            else
                return false;
        }

        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            if (DataMap == null && Dir == Direction.Out)
            {
                DataMap = EncodeQuarkSubscriptions(true, syncQuarks["Active"], syncQuarks["Passive"], true);
            }
            return base.ConvertOut(pRegionContext);
        }
    }

    // ====================================================================================================
    /// <summary>
    /// Creates a message informing that a prim is crossing quark boundaries
    /// Pre-condition: The prim crossing is a root prim of a scene object group.
    /// </summary>
    public class SyncMsgPrimQuarkCrossing : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvQuaPCrs"; } }
        public override string DetailLogTagSnd { get { return "SndQuaPCrs"; } }

        QuarkManager m_quarkManager;
        UUID m_primUUID;
        SceneObjectGroup m_sog;
        SceneObjectPart m_sop;
        SyncInfoPrim m_sip;
        bool m_encodeFull;
        string curQuark;
        string prevQuark;
        // Used when creating message locally, to update others about new properties
        Dictionary<UUID, SyncInfoBase> m_parts;
        // Used when receiving message, list of properties that were synced
        Dictionary<SyncableProperties.Type, SyncedProperty> SyncedProperties;
        OSDMap m_encodedProperties = null;
        bool m_leftMyQuarks = false;
        bool m_toRemove = false;

        public SyncMsgPrimQuarkCrossing(RegionSyncModule pRegionContext, SceneObjectPart sop, HashSet<SyncableProperties.Type> updatedProperties, bool encodeFull)
            : base(MsgType.QuarkPrimCrossing, pRegionContext)
        {
            m_quarkManager = pRegionContext.QuarkManager;
            m_sop = sop;
            m_primUUID = sop.UUID;
            m_sog = m_sop.ParentGroup;
            m_encodeFull = encodeFull;
            if (pRegionContext.InfoManager.SyncInfoExists(m_primUUID))
                m_sip = (SyncInfoPrim)pRegionContext.InfoManager.GetSyncInfo(m_primUUID);
            else
            {
                m_log.ErrorFormat("{0}: SyncMsgPrimQuarkCrossing: No Sync info for the Prim {1}", LogHeader, m_sop.UUID);
                return;
            }
            m_encodedProperties = pRegionContext.InfoManager.EncodeProperties(m_sog.RootPart.UUID, updatedProperties);
        }

        // Constructor for msg 
        public SyncMsgPrimQuarkCrossing(RegionSyncModule pRegionContext, OSDMap dataMap)
            : base(MsgType.QuarkPrimCrossing, pRegionContext)
        {
            DataMap = dataMap;
        }

        public SyncMsgPrimQuarkCrossing(int pLength, byte[] pData)
            : base(MsgType.QuarkPrimCrossing, pLength, pData)
        {
        }
        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                m_primUUID = DataMap["primUUID"].AsUUID();
                m_quarkManager = pRegionContext.QuarkManager;
                m_sop = pRegionContext.Scene.GetSceneObjectPart(m_primUUID);
                if (m_sop != null)
                {
                    m_sog = m_sop.ParentGroup;
                }
                SyncedProperties = SyncInfoBase.DecodeSyncedPropertiesAsDictionary(DataMap);
                curQuark = ((SyncQuark)SyncedProperties[SyncableProperties.Type.CurrentQuark].LastUpdateValue).QuarkName;
                prevQuark = ((SyncQuark)SyncedProperties[SyncableProperties.Type.PreviousQuark].LastUpdateValue).QuarkName;

                // Prim has crossed beyond my quarks. I should delete all local information, no need to decode update/full SOG.
                if (!m_quarkManager.IsInActiveQuark(curQuark) && !m_quarkManager.IsInPassiveQuark(curQuark))
                    m_leftMyQuarks = true;
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.HandleIn(pRegionContext))
            {
                int casecode = m_sop == null ? 0 : 1;
                casecode += m_leftMyQuarks ? 0 : 2;
                switch (casecode)
                {
                    case 0: // current is not our quark and we don't know about the object
                        //m_log.WarnFormat("{0}: Prim crossing: Case 0, from {1} to {2}", LogHeader,prevQuark,curQuark);
                        break;
                    case 1: // current is not our quark and we know about the object
                        // Mark for object for removal
                        m_toRemove = true;
                        //m_log.WarnFormat("{0}: Prim crossing: Case 1, from {1} to {2}", LogHeader, prevQuark, curQuark);
                        break;
                    case 2: // current is one of our quarks and we don't know about the object
                        if (DataMap == null || !DataMap.ContainsKey("FULL-SOG"))
                        {
                            m_log.WarnFormat("{0}: Received prim {1} crossing but no full SOG was not encoded. SOG was likely deleted.",LogHeader,m_primUUID);
                            return true;
                        }
                        DecodeSceneObject((OSDMap)DataMap["FULL-SOG"], out m_sog, out m_parts, pRegionContext.Scene);
                        if (m_sog == null || m_parts == null)
                            throw new Exception("Could not retrieve Scene Object Group from Sync Crossing message.");
                        // The object is not in our scenegraph. We need to add it but this is not like a new object.
                        m_quarkManager.LeftQuarks[m_primUUID] = false;
                        // Create the object
                        foreach(KeyValuePair<UUID,SyncInfoBase> part in m_parts)
                        {
                            pRegionContext.InfoManager.InsertSyncInfoRemote(part.Key, part.Value);
                        }

                        // Update it with the latest properties
                        if (pRegionContext.InfoManager.SyncInfoExists(m_primUUID))
                        {
                            pRegionContext.RememberLocallyGeneratedEvent(SyncMsg.MsgType.UpdatedProperties);
                            pRegionContext.InfoManager.UpdateSyncInfoBySync(m_primUUID, new HashSet<SyncedProperty>(SyncedProperties.Values));
                            pRegionContext.ForgetLocallyGeneratedEvent();
                        }

                        m_sip = (SyncInfoPrim)pRegionContext.InfoManager.GetSyncInfo(m_primUUID);
                        //m_log.WarnFormat("{0}: Prim crossing: Case 2, from {1} to {2}", LogHeader, prevQuark, curQuark);
                        pRegionContext.Scene.AddNewSceneObject(m_sog, true);
                        ReactivateSOG(m_sog, m_parts);
                        break;
                    case 3:// current is one of our quarks and we know about the object
                        // This is just an update
                        // This happens when an object moves across a border and we have both quarks in our set. It just moves.
                        m_quarkManager.LeftQuarks[m_primUUID] = false;
                        if (pRegionContext.InfoManager.SyncInfoExists(m_primUUID))
                        {
                            pRegionContext.RememberLocallyGeneratedEvent(SyncMsg.MsgType.UpdatedProperties);
                            pRegionContext.InfoManager.UpdateSyncInfoBySync(m_primUUID, new HashSet<SyncedProperty>(SyncedProperties.Values));
                            pRegionContext.ForgetLocallyGeneratedEvent();
                        }
                        else
                        {
                            m_log.ErrorFormat("{0}: Knew about the SOP, but don't have SyncInfo", LogHeader);
                        }
                        //m_log.WarnFormat("{0}: Prim crossing: Case 3, from {1} to {2}", LogHeader, prevQuark, curQuark);
                        break;
                }            
                

                // Forward the message, if relaying actor
                if (pRegionContext.IsSyncRelay)
                    ForwardCrossingMessage(pRegionContext);

                // If scheduled for removal, now it is safe.
                if (m_toRemove)
                {
                    if (m_sog != null)
                    {
                        foreach (SceneObjectPart part in m_sog.Parts)
                        {
                            try
                            {
                                pRegionContext.InfoManager.RemoveSyncInfo(part.UUID);
                            }
                            catch (KeyNotFoundException)
                            {
                                m_log.WarnFormat("{0}: Object part {1} did not have a SyncInfoprim.", LogHeader, part.UUID);
                            }
                        }
                        pRegionContext.RememberLocallyGeneratedEvent(MType, m_sog.UUID);
                        pRegionContext.Scene.DeleteSceneObject(m_sog, false);
                        pRegionContext.ForgetLocallyGeneratedEvent();
                    }
                }

                ret = true;
            }
            return ret;
        }
        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                    DataMap = CreatePrimQuarkCrossingMessage(pRegionContext);
            }
            return base.ConvertOut(pRegionContext);
        }

        private void ForwardCrossingMessage(RegionSyncModule pRegionContext)
        {
            HashSet<SyncConnector> actorsNeedFull = new HashSet<SyncConnector>();
            HashSet<SyncConnector> actorsNeedUpdate = new HashSet<SyncConnector>();
            m_quarkManager.RequiresFullObject(new SyncQuark(prevQuark), new SyncQuark(curQuark), ref actorsNeedFull, ref actorsNeedUpdate);
            if (DataMap.ContainsKey("FULL-SOG"))
            {
                if (actorsNeedFull.Count > 0)
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this, actorsNeedFull);
                if (actorsNeedUpdate.Count > 0)
                {
                    OSDMap newMap = BuildNewMap(DataMap);
                    newMap.Remove("FULL-SOG");
                    SyncMsgPrimQuarkCrossing newMsg = new SyncMsgPrimQuarkCrossing(pRegionContext, newMap);
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, newMsg, actorsNeedUpdate);
                }
            }
            else
            {
                if (actorsNeedUpdate.Count > 0)
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this, actorsNeedUpdate);
                if (actorsNeedFull.Count > 0)
                {
                    OSDMap newMap = BuildNewMap(DataMap);
                    newMap["FULL-SOG"] = EncodeSceneObject(m_sog, pRegionContext);
                    SyncMsgPrimQuarkCrossing newMsg = new SyncMsgPrimQuarkCrossing(pRegionContext, newMap);
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, newMsg, actorsNeedFull);
                }
            }
        }

        // Given an SOG and sync information, create a QuarkCrossing message
        private OSDMap CreatePrimQuarkCrossingMessage(RegionSyncModule pRegionContext)
        {
            OSDMap ret = null;

            try
            {
                // If moving across quark boundries we pack both the sop update and the
                // full sog just in case the receiver has to create a new object (moving into a quark)
                ret = m_encodedProperties;
                if (m_encodeFull)
                {
                    if (m_sog == null)
                        throw new Exception("Could not generate a full scene object group update; the SOG is unkown");
                    ret["FULL-SOG"] = EncodeSceneObject(m_sog, pRegionContext);
                }
                ret["primUUID"] = m_primUUID;
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0}: CreatePrimQuarkCrossingMessage: Error in encoding SOP {1} : {2}", LogHeader, m_primUUID, e);
                ret = null;
            }
            return ret;
        }
    }

    // ====================================================================================================
    /// <summary>
    /// Creates a message informing that a scene presence (avatar) is crossing quark boundaries
    /// </summary>
    public class SyncMsgPresenceQuarkCrossing : SyncMsgOSDMapData
    {
        public override string DetailLogTagRcv { get { return "RcvQuaPCrs"; } }
        public override string DetailLogTagSnd { get { return "SndQuaPCrs"; } }

        QuarkManager m_quarkManager;
        ScenePresence m_sp;
        SyncInfoPresence m_sip;
        UUID m_presenceUUID;
        string curQuark;
        string prevQuark;
        bool m_toRemove = false;
        bool m_encodeFull;
        
        OSDMap m_encodedProperties = null;

        Dictionary<SyncableProperties.Type, SyncedProperty> SyncedProperties;
        bool m_leftMyQuarks;

        // Assumption: SyncInfo for presence exists. Current Quark and Previous Quark have already been updated somewhere else.
        public SyncMsgPresenceQuarkCrossing(RegionSyncModule pRegionContext, ScenePresence sp, HashSet<SyncableProperties.Type> updatedProperties, bool encodeFull)
            : base(MsgType.QuarkPresenceCrossing, pRegionContext)
        {
            m_sp = sp;
            m_encodeFull = encodeFull;
            m_presenceUUID = sp.UUID;
            if (pRegionContext.InfoManager.SyncInfoExists(m_presenceUUID))
                m_sip = (SyncInfoPresence)pRegionContext.InfoManager.GetSyncInfo(m_presenceUUID);
            else
                m_log.ErrorFormat("{0}: SyncMsgPresenceQuarkCrossing: No Sync info for the Scene Presence {1}", LogHeader, m_sp.UUID);
            m_quarkManager = pRegionContext.QuarkManager;
            m_leftMyQuarks = false;
            m_encodedProperties = pRegionContext.InfoManager.EncodeProperties(m_presenceUUID, updatedProperties);
        }
        public SyncMsgPresenceQuarkCrossing(int pLength, byte[] pData)
            : base(MsgType.QuarkPresenceCrossing, pLength, pData)
        {
        }

        // Only for Out direction.
        public SyncMsgPresenceQuarkCrossing(RegionSyncModule pRegionContext, OSDMap dataMap)
            : base(MsgType.QuarkPresenceCrossing, pRegionContext)
        {
            DataMap = dataMap;
        }

        public override bool ConvertIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.ConvertIn(pRegionContext))
            {
                SyncedProperties = SyncInfoBase.DecodeSyncedPropertiesAsDictionary(DataMap);
                curQuark = ((SyncQuark)SyncedProperties[SyncableProperties.Type.CurrentQuark].LastUpdateValue).QuarkName;
                prevQuark = ((SyncQuark)SyncedProperties[SyncableProperties.Type.PreviousQuark].LastUpdateValue).QuarkName;

                m_quarkManager = pRegionContext.QuarkManager;
                m_presenceUUID = DataMap["presenceUUID"].AsUUID();
                m_sp = pRegionContext.Scene.GetScenePresence(m_presenceUUID);
                if (!m_quarkManager.IsInActiveQuark(curQuark) && !m_quarkManager.IsInPassiveQuark(curQuark))
                    m_leftMyQuarks = true;
                ret = true;
            }
            return ret;
        }
        public override bool HandleIn(RegionSyncModule pRegionContext)
        {
            bool ret = false;
            if (base.HandleIn(pRegionContext))
            {
                int casecode = m_sp == null ? 0 : 1;
                casecode += m_leftMyQuarks ? 0 : 2;
                switch (casecode)
                {
                    case 0:
                        m_log.WarnFormat("{0}: HandleQuarkCrossingSPFullUpdate: Case 0. Received from: {1}", LogHeader, ConnectorContext.otherSideActorID);
                        break;
                    case 1: // current is not our quark and we know about the presence
                        // Remove the presence from the scenegraph
                        m_toRemove = true;
                        //m_log.WarnFormat("{0}: HandleQuarkCrossingSPFullUpdate: Case 1. Received from: {1}", LogHeader, ConnectorContext.otherSideActorID);
                        break;
                    case 2:
                        // Do not know about this Scene Presence. Create it from fully encoded SP.
                        SyncInfoBase sib = null;
                        DecodeScenePresence((OSDMap)DataMap["FULL-SP"], out sib, pRegionContext.Scene);
                        if (sib == null)
                            throw new Exception("Could not retrieve Scene Presence from Sync Crossing message.");
                        m_sip = (SyncInfoPresence)sib;

                        // Create sync info
                        pRegionContext.InfoManager.InsertSyncInfoRemote(m_presenceUUID, (SyncInfoBase)m_sip);

                        // Update with latest properties
                        pRegionContext.RememberLocallyGeneratedEvent(SyncMsg.MsgType.UpdatedProperties);
                        pRegionContext.InfoManager.UpdateSyncInfoBySync(m_presenceUUID, new HashSet<SyncedProperty>(SyncedProperties.Values));
                        pRegionContext.ForgetLocallyGeneratedEvent();

                        m_quarkManager.LeftQuarks[m_presenceUUID] = false;
                        ReactivateSP(m_sip);
                        
                        break;
                    case 3:
                        m_quarkManager.LeftQuarks[m_presenceUUID] = false;
                        if (pRegionContext.InfoManager.SyncInfoExists(m_presenceUUID))
                        {
                            // I already know about this Scene Presence. Just need to get its last updated properties and apply.
                            m_sip = (SyncInfoPresence)pRegionContext.InfoManager.GetSyncInfo(m_presenceUUID);
                            pRegionContext.RememberLocallyGeneratedEvent(SyncMsg.MsgType.UpdatedProperties);
                            pRegionContext.InfoManager.UpdateSyncInfoBySync(m_presenceUUID, new HashSet<SyncedProperty>(SyncedProperties.Values));
                            pRegionContext.ForgetLocallyGeneratedEvent();
                        }
                        else
                        {
                            m_log.ErrorFormat("{0}: Knew about the SP, but don't have SyncInfo", LogHeader);
                        }
                        //m_log.WarnFormat("{0}: HandleQuarkCrossingSPFullUpdate: Case 3. Received from: {1}", LogHeader, ConnectorContext.otherSideActorID);
                        break;

                }

                if (pRegionContext.IsSyncRelay)
                    ForwardCrossingMessage(pRegionContext);

                // It is now safe to remove the scene presence and sync info.
                if (m_toRemove)
                {
                    if (m_sp != null)
                    {
                        pRegionContext.InfoManager.RemoveSyncInfo(m_presenceUUID);
                        try
                        {
                            // Removing a client triggers OnRemovePresence. I should only remove the client from this actor, not propagate it.
                            pRegionContext.RememberLocallyGeneratedEvent(MType);
                            //pRegionContext.Scene.RemoveClient(m_presenceUUID, false);
                            pRegionContext.Scene.CloseAgent(m_presenceUUID, true);
                            pRegionContext.ForgetLocallyGeneratedEvent();
                        }
                        catch (Exception e)
                        {
                            m_log.WarnFormat("{0}: No client to remove from here. {1}", LogHeader, e);
                        }
                    }
                }
                ret = true;
            }
            return ret;
        }

        public override bool ConvertOut(RegionSyncModule pRegionContext)
        {
            lock (m_dataLock)
            {
                if (Dir == Direction.Out && DataMap == null)
                    DataMap = CreatePresenceQuarkCrossingMessage(pRegionContext);
            }
            return base.ConvertOut(pRegionContext);
        }

        /// <summary>
        /// Forwards crossing message to neighbors (if the actor is SyncRelay). This method checks if neighbors need the full or only the
        /// update message, and sends accordingly. 
        /// </summary>
        /// <param name="pRegionContext"></param>
        private void ForwardCrossingMessage(RegionSyncModule pRegionContext)
        {
            HashSet<SyncConnector> actorsNeedFull = new HashSet<SyncConnector>();
            HashSet<SyncConnector> actorsNeedUpdate = new HashSet<SyncConnector>();
            m_quarkManager.RequiresFullObject(new SyncQuark(prevQuark), new SyncQuark(curQuark), ref actorsNeedFull, ref actorsNeedUpdate);
            if (DataMap.ContainsKey("FULL-SP"))
            {
                if (actorsNeedFull.Count > 0)
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this, actorsNeedFull);
                if (actorsNeedUpdate.Count > 0)
                {
                    OSDMap newMap = BuildNewMap(DataMap);
                    newMap.Remove("FULL-SP");
                    SyncMsgPresenceQuarkCrossing newMsg = new SyncMsgPresenceQuarkCrossing(pRegionContext, newMap);
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, newMsg, actorsNeedUpdate);
                }
            }
            else
            {
                if (actorsNeedUpdate.Count > 0)
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, this, actorsNeedUpdate);
                if (actorsNeedFull.Count > 0)
                {
                    OSDMap newMap = BuildNewMap(DataMap);
                    newMap["FULL-SP"] = EncodeScenePresence(m_sp, pRegionContext);
                    SyncMsgPresenceQuarkCrossing newMsg = new SyncMsgPresenceQuarkCrossing(pRegionContext, newMap);
                    pRegionContext.SendSpecialUpdateToRelevantSyncConnectors(ConnectorContext.otherSideActorID, newMsg, actorsNeedFull);
                }
            }
        }

        /// <summary>
        /// Given an Scene Presence and sync information, create a QuarkCrossing message
        /// </summary>
        /// <param name="pRegionContext"></param>
        /// <returns></returns>
        private OSDMap CreatePresenceQuarkCrossingMessage(RegionSyncModule pRegionContext)
        {
            OSDMap ret = null;

            try
            {
                ret = m_encodedProperties;
                ret["presenceUUID"] = m_presenceUUID;
                // If my neighbors need the full update, econde the scene presence in the message.
                if (m_encodeFull)
                {
                    if (m_sp == null)
                        throw new Exception("Could not generate a full scene presence update; the Scene Presence is unkown");
                    ret["FULL-SP"] = EncodeScenePresence(m_sp, pRegionContext);
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0}: CreatePresenceQuarkCrossingMessage: Error in encoding SP {1} : {2}", LogHeader, m_presenceUUID, e);
                ret = null;
            }
            return ret;
        }
    }
}
