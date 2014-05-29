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
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Region.Framework.Scenes;
using System.Reflection;

namespace DSG.RegionSync
{

    public class SyncInfoManager
    {
        //private ILog m_log;
        public static ILog DebugLog;

        /// <summary>
        /// Lock for write-accessing m_syncedUUIDs. We assume accesses to m_syncedUUIDs
        /// are in the "many reads, a few writes" pattern. Writers needs to lock on it.
        /// Readers who interate through m_syncedUUIDs need to copy a reference to 
        /// m_syncedUUIDs and operate on the reference, but no need to lock. (Readers who
        /// just grabs reference to one item in m_syncedUUIDs for further operation
        /// might not even need to copy a reference to m_syncedUUIDs initially???)
        /// </summary>
        private Object m_syncLock = new Object();
        private Dictionary<UUID, SyncInfoBase> m_syncedUUIDs;
        private RegionSyncModule m_regionSyncModule;

        /// <summary>
        /// The max time for a SOP's SyncInfo to sit in record 
        /// w/o being updated either locally or bySync.
        /// </summary>
        private long m_ageOutThreshold;

        public int Size
        {
            get
            {
                int estimateBytes = 0;
                lock (m_syncLock)
                {
                    foreach (SyncInfoBase syncInfo in m_syncedUUIDs.Values)
                    {
                        estimateBytes += syncInfo.Size;
                    }
                }
                return estimateBytes;
            }
        }

        public SyncInfoManager(RegionSyncModule syncModule, long ageOutTh)
        {
            m_syncedUUIDs = new Dictionary<UUID, SyncInfoBase>();
            m_regionSyncModule = syncModule;
            m_ageOutThreshold = ageOutTh;
        }

        public bool SyncInfoExists(UUID uuid)
        {
            lock(m_syncLock)
                return m_syncedUUIDs.ContainsKey(uuid);
        }

        public Scene Scene
        {
            get { return m_regionSyncModule.Scene; }
        }

        /// <summary>
        /// For each property in updatedProperties, (1) if the value in local sop/sp's
        /// data is different than that in SyncInfo, and what's in SyncInfo
        /// has an older timestamp, then update that property's value and syncInfo
        /// in SyncInfo; (2) otherwise, skip the property and do nothing.
        /// </summary>
        /// <param name="part"></param>
        /// <param name="updatedProperties"></param>
        /// <returns>The list properties among updatedProperties whose value have been copied over to SyncInfo.</returns>
        public HashSet<SyncableProperties.Type> UpdateSyncInfoByLocal(UUID uuid, HashSet<SyncableProperties.Type> updatedProperties)
        {
            SyncInfoBase thisSyncInfo=null;
            bool found = false;
            lock(m_syncLock){
                found = m_syncedUUIDs.TryGetValue(uuid, out thisSyncInfo);
            }
            if(found)
            {
                if (UpdateInActiveQuark(thisSyncInfo))
                {
                    // DebugLog.WarnFormat("[SYNC INFO MANAGER] UpdateSyncInfoByLocal SyncInfo for {0} FOUND.", uuid);
                    return thisSyncInfo.UpdatePropertiesByLocal(uuid, updatedProperties, RegionSyncModule.NowTicks(), m_regionSyncModule.SyncID);
                }
            }
            // DebugLog.WarnFormat("[SYNC INFO MANAGER] UpdateSyncInfoByLocal SyncInfo for {0} NOT FOUND.", uuid);
            return new HashSet<SyncableProperties.Type>();
        }

        public HashSet<SyncableProperties.Type> UpdateSyncInfoBySync(UUID uuid, HashSet<SyncedProperty> syncedProperties)
        {
            SyncInfoBase thisSyncInfo = null;
            bool found = false;
            lock (m_syncLock)
            {
                found = m_syncedUUIDs.TryGetValue(uuid, out thisSyncInfo);
            }
            if (found)
            {
                //DebugLog.WarnFormat("[SYNC INFO MANAGER] UpdateSyncInfoBySync SyncInfo for {0} FOUND.", uuid);
                //return m_syncedUUIDs[uuid].UpdatePropertiesBySync(uuid, syncedProperties);
                return thisSyncInfo.UpdatePropertiesBySync(uuid, syncedProperties);
            }
            //This should not happen, as we should only receive UpdatedPrimProperties after receiving a NewObject message
            //DebugLog.WarnFormat("[SYNC INFO MANAGER] UpdateSyncInfoBySync SyncInfo for {0} NOT FOUND.", uuid);
            return new HashSet<SyncableProperties.Type>();
        }

        public HashSet<SyncableProperties.Type> UpdateSyncInfoBySync(UUID uuid, SyncInfoBase updatedSyncInfo)
        {
            SyncInfoBase thisSyncInfo = null;
            bool found = false;
            lock (m_syncLock)
            {
                found = m_syncedUUIDs.TryGetValue(uuid, out thisSyncInfo);
            }
            if (found)
            {
                // DebugLog.WarnFormat("[SYNC INFO MANAGER] UpdateSyncInfoBySync SyncInfo for {0} FOUND.", uuid);
                // Update properties listed in updatedSyncInfo
                //return m_syncedUUIDs[uuid].UpdatePropertiesBySync(uuid, new HashSet<SyncedProperty>(updatedSyncInfo.CurrentlySyncedProperties.Values));
                return thisSyncInfo.UpdatePropertiesBySync(uuid, new HashSet<SyncedProperty>(updatedSyncInfo.CurrentlySyncedProperties.Values));
            }

            //This should not happen, as we should only receive UpdatedPrimProperties after receiving a NewObject message
            // m_log.WarnFormat("[SYNC INFO MANAGER] UpdateSyncInfoBySync SyncInfo for {0} NOT FOUND.", uuid);
            return new HashSet<SyncableProperties.Type>();
        }

        public OSDMap EncodeProperties(UUID uuid, HashSet<SyncableProperties.Type> propertiesToEncode)
        {
            // m_log.WarnFormat("[SYNC INFO MANAGER] EncodeProperties SyncInfo for {0}", uuid);
            SyncInfoBase thisSyncInfo = null;
            lock (m_syncLock)
            {
                m_syncedUUIDs.TryGetValue(uuid, out thisSyncInfo);
            }
            if (thisSyncInfo != null)
            {
                OSDMap data = new OSDMap();
                data["uuid"] = OSD.FromUUID(uuid);
                data["properties"] = thisSyncInfo.EncodeSyncedProperties(propertiesToEncode);
                return data;
            }

            // DebugLog.WarnFormat("[SYNC INFO MANAGER] EncodeProperties SyncInfo for {0} not in m_syncedUUIDs.", uuid);
            return null;
        }

        public HashSet<string> GetLastUpdatedSyncIDs(UUID uuid, HashSet<SyncableProperties.Type> properties)
        {
            HashSet<string> syncIDs = null;
            SyncInfoBase thisSyncInfo = null;
            bool found = false;
            lock (m_syncLock)
            {
                found = m_syncedUUIDs.TryGetValue(uuid, out thisSyncInfo);
            }
            if (found)
            {
                syncIDs = thisSyncInfo.GetLastUpdateSyncIDs(properties);
            }

            return syncIDs;
        }

        /// <summary>
        /// For a newly synced object or avatar, create a SyncInfoBase for it. 
        /// Assume the timestamp for each property is at least T ticks old, T=m_ageOutThreshold. 
        /// </summary>
        /// <param name="uuid"></param>
        /// <param name="syncInfoInitTime"></param>
        /// <param name="syncID"></param>
        public void InsertSyncInfoLocal(UUID uuid, long syncInfoInitTime, string syncID)
        {
            // m_log.WarnFormat("[SYNC INFO MANAGER] InsertSyncInfoLocal: uuid={0}, syncID={1}", uuid, syncID);
            long lastUpdateTimeStamp = syncInfoInitTime - m_ageOutThreshold;
            SyncInfoBase sib = SyncInfoBase.SyncInfoFactory(uuid, Scene, lastUpdateTimeStamp, syncID);
            lock (m_syncLock)
            {
                if (sib == null)
                {
                    DebugLog.ErrorFormat("{0}: Could not create SyncInfo for syncID {1}", "[SYNC INFO MANAGER]", syncID);
                    return;
                }
                //else if (UpdateInActiveQuark(sib))
                // Allow local insertions outside of quark. This will result in new object being sent to relevant connectors. 
                m_syncedUUIDs[uuid] = sib;
            }
        }

        /// <summary>
        /// Insert a new SyncInfoBase based on information from a remote actor. 
        /// Assumes source is an active quark.
        /// </summary>
        /// <param name="uuid"></param>
        /// <param name="syncInfo"></param>
        public void InsertSyncInfoRemote(UUID uuid, SyncInfoBase syncinfo)
        {
            // bool isPrim = syncinfo is SyncInfoPrim;
            // m_log.WarnFormat("[SYNC INFO MANAGER] InsertSyncInfoLocal for uuid {0}, type={1}", uuid, isPrim?"Prim":"Presence");
            lock (m_syncLock)
                m_syncedUUIDs[uuid] = syncinfo;
        }

        public void RemoveSyncInfo(UUID uuid)
        {
            // m_log.WarnFormat("[SYNC INFO MANAGER] RemoveSyncInfo for uuid {0}", uuid);
            lock (m_syncLock)
                m_syncedUUIDs.Remove(uuid);
        }

        public SyncInfoBase GetSyncInfo(UUID uuid)
        {
            // m_log.WarnFormat("[SYNC INFO MANAGER] GetSyncInfo for uuid {0}", uuid);
            // Should never be called unless SyncInfo has already been added
            //lock (m_syncLock)
            //    return m_syncedUUIDs[uuid];
            SyncInfoBase thisSyncInfo = null;
            bool found = false;
            lock (m_syncLock)
            {
                found = m_syncedUUIDs.TryGetValue(uuid, out thisSyncInfo);
            }
            if (found)
                return thisSyncInfo;
            else
                return null;
        }

        public bool UpdateInActiveQuark(SyncInfoBase syncInfo)
        {
            // When the region starts, old parts are inserted into sync info. We assume they are all active quark parts for now.
            if (m_regionSyncModule.QuarkManager == null)
                return true;
            else if (m_regionSyncModule.QuarkManager.IsInActiveQuark(syncInfo.CurQuark.QuarkName))
                return true;
            else
                return false;
        }
    }
}
