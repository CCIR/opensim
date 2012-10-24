/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Monitoring;
using OpenSim.Region.ClientStack.LindenUDP;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.OptionalModules.Avatar.Attachments
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "TempAttachmentsModule")]
    public class TempAttachmentsModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;
        private IRegionConsole m_console;

        public void Initialise(IConfigSource configSource)
        {
        }

        public void AddRegion(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
            m_scene = scene;

            IScriptModuleComms comms = scene.RequestModuleInterface<IScriptModuleComms>();
            if (comms != null)
            {
                comms.RegisterScriptInvocations(this);
                m_log.DebugFormat("[TEMP ATTACHS]: Registered script functions");
                m_console = scene.RequestModuleInterface<IRegionConsole>();

                if (m_console != null)
                {
                    m_console.AddCommand("TempATtachModule", false, "set auto_grant_attach_perms", "set auto_grant_attach_perms true|false", "Allow objects owned by the region owner os estate managers to obtain attach permissions without asking the user", SetAutoGrantAttachPerms);
                }
            }
            else
            {
                m_log.ErrorFormat("[TEMP ATTACHS]: Failed to register script functions");
            }
        }

        public void Close()
        {
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "TempAttachmentsModule"; }
        }

        private void SendConsoleOutput(UUID agentID, string text)
        {
            if (m_console == null)
                return;

            m_console.SendConsoleOutput(agentID, text);
        }

        private void SetAutoGrantAttachPerms(string module, string[] parms)
        {
            UUID agentID = new UUID(parms[parms.Length - 1]);
            Array.Resize(ref parms, parms.Length - 1);

            if (parms.Length != 3)
            {
                SendConsoleOutput(agentID, "Command parameter error");
                return;
            }

            string val = parms[2];
            if (val != "true" && val != "false")
            {
                SendConsoleOutput(agentID, "Command parameter error");
                return;
            }

            m_scene.StoreExtraSetting("auto_grant_attach_perms", val);

            SendConsoleOutput(agentID, String.Format("auto_grant_attach_perms set to {0}", val));
        }

        /// <summary>
        /// Temporarily attach the current object to the permissive avatar.
        /// </summary>
        /// <param name="host">
        /// UUID of the object executing the script.
        /// </param>
        /// <param name="script">UUID of the executing script.</param>
        /// <param name="attachmentPoint">
        /// Attachment point to attach to.
        /// </param>
        /// <returns>zero</returns>
        /// <remarks>
        /// The return type strictly should be void, but practice is for
        /// script comms funcs to return int instead of void.
        /// see http://wiki.secondlife.com/wiki/LlAttachToAvatarTemp
        /// </remarks>
        [ScriptInvocation]
        public int llAttachToAvatarTemp(UUID host, UUID script, int attachmentPoint)
        {
            SceneObjectPart hostPart = m_scene.GetSceneObjectPart(host);

            if (hostPart == null)
                return 0;

            if (hostPart.ParentGroup.IsAttachment)
                return 0;

            TaskInventoryItem item = hostPart.Inventory.GetInventoryItem(script);
            if (item == null)
                return 0;

            if ((item.PermsMask & 32) == 0) // PERMISSION_ATTACH
                return 0;

            ScenePresence target;
            if (!m_scene.TryGetScenePresence(item.PermsGranter, out target))
                return 0;

            return AttachToOtherAvatarTemp(hostPart.ParentGroup, target, attachmentPoint);
        }

        /// <summary>
        /// Temporarily attach the current object to the specified avatar.
        /// </summary>
        /// <param name="host">
        /// UUID of the object executing the script.
        /// </param>
        /// <param name="script">UUID of the executing script.</param>
        /// <param name="other">UUID of the avatar to attach to.</param>
        /// <param name="attachmentPoint">
        /// Attachment point to attach to.
        /// </param>
        /// <returns>zero</returns>
        /// <remarks>
        /// The return type strictly should be void, but practice is for
        /// script comms funcs to return int instead of void.
        /// </remarks>
        [ScriptInvocation]
        public int osForceAttachToOtherAvatarTemp(UUID host, UUID script, string other, int attachmentPoint)
        {
            SceneObjectPart hostPart = m_scene.GetSceneObjectPart(host);

            if (hostPart == null)
                return 0;

            if (hostPart.ParentGroup.IsAttachment)
                return 0;

            UUID otherAvatarID;
            if (!UUID.TryParse(other, out otherAvatarID))
                return 0;

            ScenePresence target;
            if (!m_scene.TryGetScenePresence(otherAvatarID, out target))
                return 0;

            return AttachToOtherAvatarTemp(hostPart.ParentGroup, target, attachmentPoint);
        }

        /// <summary>
        /// Temporarily attaches the specified object contents to the
        /// specified avatar.
        /// </summary>
        /// <param name="host">
        /// UUID of the object executing the script.
        /// </param>
        /// <param name="script">UUID of the executing script.</param>
        /// <param name="attachmentPoint">
        /// Attachment point to attach to.
        /// </param>
        /// <param name="args">
        /// A strided list of attachment points and attachment objects.
        /// </param>
        /// <returns>zero</returns>
        /// <remarks>
        /// The return type strictly should be void, but practice is for
        /// script comms funcs to return int instead of void.
        /// </remarks>
        /// <example>
        /// default{
        ///     touch_start(integer t){
        ///         osForceAttachToOtherAvatarFromInventoryTemp(llDetectedKey(0), [
        ///             (integer)ATTACH_HEAD, "Box",
        ///             (integer)ATTACH_RHAND, "Box",
        ///             (integer)ATTACH_LHAND, "Box"
        ///         ]);
        ///     }
        /// }
        /// </example>
        [ScriptInvocation]
        public int osForceAttachToOtherAvatarFromInventoryTemp(UUID host, UUID script, string other, object[] args)
        {
            if (args.Length < 2)
                return 0;

            SceneObjectPart hostPart = m_scene.GetSceneObjectPart(host);

            if (hostPart == null)
                return 0;

            if (hostPart.ParentGroup.IsAttachment)
                return 0;

            UUID otherAvatarID;
            if (!UUID.TryParse(other, out otherAvatarID))
                return 0;

            ScenePresence target;
            if (!m_scene.TryGetScenePresence(otherAvatarID, out target))
                return 0;

            // this structure enforces the current lack of support for
            // multi-object attachment points without requiring change of
            // LSL syntax when support is added.
            Dictionary<int, TaskInventoryItem> attachments = new Dictionary<int, TaskInventoryItem>();
            int i = 0;
            int remaining = args.Length;

            for (i = 0; i < args.Length; i += 2)
            {
                if (args[i] is int && args[i + 1] is string)
                {
                    TaskInventoryItem item = hostPart.Inventory.GetInventoryItem((string)args[i + 1]);
                    if (item != null)
                    {
                        attachments[(int)args[i]] = item;
                    }
                }
                remaining -= 2;
            }

            if (attachments.Count < 1)
                return 0;

            foreach (KeyValuePair<int, TaskInventoryItem> kvp in attachments)
            {
                SceneObjectGroup sog = hostPart.Inventory.GetRezReadySceneObject(kvp.Value);
                if (sog != null && AttachToOtherAvatarTemp(sog, target, kvp.Key) == 1)
                {
                    m_scene.AddNewSceneObject(sog, false);
                    sog.CreateScriptInstances(0, true,
                            m_scene.DefaultScriptEngine, 3);
                    sog.ResumeScripts();
                    m_scene.EventManager.TriggerOnAttach(sog.LocalId, sog.FromItemID, target.UUID);
                }
            }

            return 0;
        }

        private int AttachToOtherAvatarTemp(SceneObjectGroup attachment, ScenePresence target, int attachmentPoint)
        {
            IAttachmentsModule attachmentsModule = m_scene.RequestModuleInterface<IAttachmentsModule>();
            if (attachmentsModule == null)
                return 0;

            if (target.UUID != attachment.OwnerID)
            {
                uint effectivePerms = attachment.GetEffectivePermissions();

                if ((effectivePerms & (uint)PermissionMask.Transfer) == 0)
                    return 0;

                attachment.SetOwnerId(target.UUID);
                attachment.SetRootPartOwner(attachment.RootPart, target.UUID, target.ControllingClient.ActiveGroupId);

                if (m_scene.Permissions.PropagatePermissions())
                {
                    foreach (SceneObjectPart child in attachment.Parts)
                    {
                        child.Inventory.ChangeInventoryOwner(target.UUID);
                        child.TriggerScriptChangedEvent(Changed.OWNER);
                        child.ApplyNextOwnerPermissions();
                    }
                }

                attachment.RootPart.ObjectSaleType = 0;
                attachment.RootPart.SalePrice = 10;

                attachment.HasGroupChanged = true;
                attachment.RootPart.SendPropertiesToClient(target.ControllingClient);
                attachment.RootPart.ScheduleFullUpdate();
            }

            return attachmentsModule.AttachObject(target, attachment, (uint)attachmentPoint, false, true) ? 1 : 0;
        }
    }
}
