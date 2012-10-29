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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using log4net;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Serialization.External;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.Framework.Scenes.Serialization
{
    /// <summary>
    /// Serialize and deserialize scene objects.
    /// </summary>
    /// This should really be in OpenSim.Framework.Serialization but this would mean circular dependency problems
    /// right now - hopefully this isn't forever.
    public class SceneObjectSerializer
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static IUserManagement m_UserManagement;
        
        /// <summary>
        /// Deserialize a scene object from the original xml format
        /// </summary>
        /// <param name="xmlData"></param>
        /// <returns>The scene object deserialized.  Null on failure.</returns>
        public static SceneObjectGroup FromOriginalXmlFormat(string xmlData)
        {
            //m_log.DebugFormat("[SOG]: Starting deserialization of SOG");
            //int time = System.Environment.TickCount;

            try
            {
                StringReader  sr;
                XmlTextReader reader;
                XmlNodeList   parts;
                XmlDocument   doc;
                int           linkNum;

                doc = new XmlDocument();
                doc.LoadXml(xmlData);
                parts = doc.GetElementsByTagName("RootPart");

                if (parts.Count == 0)
                    throw new Exception("Invalid Xml format - no root part");

                sr = new StringReader(parts[0].InnerXml);
                reader = new XmlTextReader(sr);
                SceneObjectGroup sceneObject = new SceneObjectGroup(SceneObjectPart.FromXml(reader));
                reader.Close();
                sr.Close();

                parts = doc.GetElementsByTagName("Part");

                for (int i = 0; i < parts.Count; i++)
                {
                    sr = new StringReader(parts[i].InnerXml);
                    reader = new XmlTextReader(sr);
                    SceneObjectPart part = SceneObjectPart.FromXml(reader);
                    linkNum = part.LinkNum;
                    sceneObject.AddPart(part);
                    part.LinkNum = linkNum;
                    part.TrimPermissions();
                    reader.Close();
                    sr.Close();
                }

                // Script state may, or may not, exist. Not having any, is NOT
                // ever a problem.
                sceneObject.LoadScriptState(doc);

                return sceneObject;
            }
            catch (Exception e)
            {
                m_log.ErrorFormat(
                    "[SERIALIZER]: Deserialization of xml failed with {0}.  xml was {1}", e, xmlData);
                return null;
            }
        }

        /// <summary>
        /// Serialize a scene object to the original xml format
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        public static string ToOriginalXmlFormat(SceneObjectGroup sceneObject)
        {
            return ToOriginalXmlFormat(sceneObject, true);
        }

        /// <summary>
        /// Serialize a scene object to the original xml format
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <param name="doScriptStates">Control whether script states are also serialized.</param>
        /// <returns></returns>
        public static string ToOriginalXmlFormat(SceneObjectGroup sceneObject, bool doScriptStates)
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter writer = new XmlTextWriter(sw))
                {
                    ToOriginalXmlFormat(sceneObject, writer, doScriptStates);
                }

                return sw.ToString();
            }
        }

        /// <summary>
        /// Serialize a scene object to the original xml format
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        public static void ToOriginalXmlFormat(SceneObjectGroup sceneObject, XmlTextWriter writer, bool doScriptStates)
        {
            ToOriginalXmlFormat(sceneObject, writer, doScriptStates, false);
        }
        
        public static string ToOriginalXmlFormat(SceneObjectGroup sceneObject, string scriptedState)
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter writer = new XmlTextWriter(sw))
                {
                    writer.WriteStartElement(String.Empty, "SceneObjectGroup", String.Empty);

                    ToOriginalXmlFormat(sceneObject, writer, false, true);

                    writer.WriteRaw(scriptedState);

                    writer.WriteEndElement();
                }
                return sw.ToString();
            }
        }

        /// <summary>
        /// Serialize a scene object to the original xml format
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <param name="writer"></param>
        /// <param name="noRootElement">If false, don't write the enclosing SceneObjectGroup element</param>
        /// <returns></returns>
        public static void ToOriginalXmlFormat(
            SceneObjectGroup sceneObject, XmlTextWriter writer, bool doScriptStates, bool noRootElement)
        {
//            m_log.DebugFormat("[SERIALIZER]: Starting serialization of {0}", sceneObject.Name);
//            int time = System.Environment.TickCount;

            if (!noRootElement)
                writer.WriteStartElement(String.Empty, "SceneObjectGroup", String.Empty);
            
            writer.WriteStartElement(String.Empty, "RootPart", String.Empty);
            ToXmlFormat(sceneObject.RootPart, writer);
            writer.WriteEndElement();
            writer.WriteStartElement(String.Empty, "OtherParts", String.Empty);

            SceneObjectPart[] parts = sceneObject.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                SceneObjectPart part = parts[i];
                if (part.UUID != sceneObject.RootPart.UUID)
                {
                    writer.WriteStartElement(String.Empty, "Part", String.Empty);
                    ToXmlFormat(part, writer);
                    writer.WriteEndElement();
                }
            }

            writer.WriteEndElement(); // OtherParts

            if (doScriptStates)
                sceneObject.SaveScriptedState(writer);
            
            if (!noRootElement)
                writer.WriteEndElement(); // SceneObjectGroup

//            m_log.DebugFormat("[SERIALIZER]: Finished serialization of SOG {0}, {1}ms", sceneObject.Name, System.Environment.TickCount - time);
        }        

        protected static void ToXmlFormat(SceneObjectPart part, XmlTextWriter writer)
        {
            SOPToXml2(writer, part, new Dictionary<string, object>());
        }
        
        public static SceneObjectGroup FromXml2Format(string xmlData)
        {
            //m_log.DebugFormat("[SOG]: Starting deserialization of SOG");
            //int time = System.Environment.TickCount;
            
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlData);

                XmlNodeList parts = doc.GetElementsByTagName("SceneObjectPart");

                if (parts.Count == 0)
                {
                    m_log.ErrorFormat("[SERIALIZER]: Deserialization of xml failed: No SceneObjectPart nodes. xml was " + xmlData);
                    return null;
                }

                StringReader sr = new StringReader(parts[0].OuterXml);
                XmlTextReader reader = new XmlTextReader(sr);
                SceneObjectGroup sceneObject = new SceneObjectGroup(SceneObjectPart.FromXml(reader));
                reader.Close();
                sr.Close();

                // Then deal with the rest
                for (int i = 1; i < parts.Count; i++)
                {
                    sr = new StringReader(parts[i].OuterXml);
                    reader = new XmlTextReader(sr);
                    SceneObjectPart part = SceneObjectPart.FromXml(reader);

                    int originalLinkNum = part.LinkNum;

                    sceneObject.AddPart(part);

                    // SceneObjectGroup.AddPart() tries to be smart and automatically set the LinkNum.
                    // We override that here
                    if (originalLinkNum != 0)
                        part.LinkNum = originalLinkNum;

                    reader.Close();
                    sr.Close();
                }

                // Script state may, or may not, exist. Not having any, is NOT
                // ever a problem.
                sceneObject.LoadScriptState(doc);
                
                return sceneObject;
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[SERIALIZER]: Deserialization of xml failed with {0}.  xml was {1}", e, xmlData);
                return null;
            }
        }

        /// <summary>
        /// Serialize a scene object to the 'xml2' format.
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        public static string ToXml2Format(SceneObjectGroup sceneObject)
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter writer = new XmlTextWriter(sw))
                {
                    SOGToXml2(writer, sceneObject, new Dictionary<string,object>());
                }

                return sw.ToString();
            }
        }


        #region manual serialization

        private static Dictionary<string, Action<SceneObjectPart, XmlTextReader>> m_SOPXmlProcessors
            = new Dictionary<string, Action<SceneObjectPart, XmlTextReader>>();

        private static Dictionary<string, Action<TaskInventoryItem, XmlTextReader>> m_TaskInventoryXmlProcessors
            = new Dictionary<string, Action<TaskInventoryItem, XmlTextReader>>();

        private static Dictionary<string, Action<PrimitiveBaseShape, XmlTextReader>> m_ShapeXmlProcessors
            = new Dictionary<string, Action<PrimitiveBaseShape, XmlTextReader>>();

        static SceneObjectSerializer()
        {
            #region SOPXmlProcessors initialization
            m_SOPXmlProcessors.Add("AllowedDrop", ProcessAllowedDrop);
            m_SOPXmlProcessors.Add("CreatorID", ProcessCreatorID);
            m_SOPXmlProcessors.Add("CreatorData", ProcessCreatorData);
            m_SOPXmlProcessors.Add("FolderID", ProcessFolderID);
            m_SOPXmlProcessors.Add("InventorySerial", ProcessInventorySerial);
            m_SOPXmlProcessors.Add("TaskInventory", ProcessTaskInventory);
            m_SOPXmlProcessors.Add("UUID", ProcessUUID);
            m_SOPXmlProcessors.Add("LocalId", ProcessLocalId);
            m_SOPXmlProcessors.Add("Name", ProcessName);
            m_SOPXmlProcessors.Add("Material", ProcessMaterial);
            m_SOPXmlProcessors.Add("PassTouches", ProcessPassTouches);
            m_SOPXmlProcessors.Add("PassCollisions", ProcessPassCollisions);
            m_SOPXmlProcessors.Add("RegionHandle", ProcessRegionHandle);
            m_SOPXmlProcessors.Add("ScriptAccessPin", ProcessScriptAccessPin);
            m_SOPXmlProcessors.Add("GroupPosition", ProcessGroupPosition);
            m_SOPXmlProcessors.Add("OffsetPosition", ProcessOffsetPosition);
            m_SOPXmlProcessors.Add("RotationOffset", ProcessRotationOffset);
            m_SOPXmlProcessors.Add("Velocity", ProcessVelocity);
            m_SOPXmlProcessors.Add("AngularVelocity", ProcessAngularVelocity);
            m_SOPXmlProcessors.Add("Acceleration", ProcessAcceleration);
            m_SOPXmlProcessors.Add("Description", ProcessDescription);
            m_SOPXmlProcessors.Add("Color", ProcessColor);
            m_SOPXmlProcessors.Add("Text", ProcessText);
            m_SOPXmlProcessors.Add("SitName", ProcessSitName);
            m_SOPXmlProcessors.Add("TouchName", ProcessTouchName);
            m_SOPXmlProcessors.Add("LinkNum", ProcessLinkNum);
            m_SOPXmlProcessors.Add("ClickAction", ProcessClickAction);
            m_SOPXmlProcessors.Add("Shape", ProcessShape);
            m_SOPXmlProcessors.Add("Scale", ProcessScale);
            m_SOPXmlProcessors.Add("SitTargetOrientation", ProcessSitTargetOrientation);
            m_SOPXmlProcessors.Add("SitTargetPosition", ProcessSitTargetPosition);
            m_SOPXmlProcessors.Add("SitTargetPositionLL", ProcessSitTargetPositionLL);
            m_SOPXmlProcessors.Add("SitTargetOrientationLL", ProcessSitTargetOrientationLL);
            m_SOPXmlProcessors.Add("ParentID", ProcessParentID);
            m_SOPXmlProcessors.Add("CreationDate", ProcessCreationDate);
            m_SOPXmlProcessors.Add("Category", ProcessCategory);
            m_SOPXmlProcessors.Add("SalePrice", ProcessSalePrice);
            m_SOPXmlProcessors.Add("ObjectSaleType", ProcessObjectSaleType);
            m_SOPXmlProcessors.Add("OwnershipCost", ProcessOwnershipCost);
            m_SOPXmlProcessors.Add("GroupID", ProcessGroupID);
            m_SOPXmlProcessors.Add("OwnerID", ProcessOwnerID);
            m_SOPXmlProcessors.Add("LastOwnerID", ProcessLastOwnerID);
            m_SOPXmlProcessors.Add("BaseMask", ProcessBaseMask);
            m_SOPXmlProcessors.Add("OwnerMask", ProcessOwnerMask);
            m_SOPXmlProcessors.Add("GroupMask", ProcessGroupMask);
            m_SOPXmlProcessors.Add("EveryoneMask", ProcessEveryoneMask);
            m_SOPXmlProcessors.Add("NextOwnerMask", ProcessNextOwnerMask);
            m_SOPXmlProcessors.Add("Flags", ProcessFlags);
            m_SOPXmlProcessors.Add("CollisionSound", ProcessCollisionSound);
            m_SOPXmlProcessors.Add("CollisionSoundVolume", ProcessCollisionSoundVolume);
            m_SOPXmlProcessors.Add("MediaUrl", ProcessMediaUrl);
            m_SOPXmlProcessors.Add("TextureAnimation", ProcessTextureAnimation);
            m_SOPXmlProcessors.Add("ParticleSystem", ProcessParticleSystem);
            m_SOPXmlProcessors.Add("PayPrice0", ProcessPayPrice0);
            m_SOPXmlProcessors.Add("PayPrice1", ProcessPayPrice1);
            m_SOPXmlProcessors.Add("PayPrice2", ProcessPayPrice2);
            m_SOPXmlProcessors.Add("PayPrice3", ProcessPayPrice3);
            m_SOPXmlProcessors.Add("PayPrice4", ProcessPayPrice4);
            #endregion

            #region TaskInventoryXmlProcessors initialization
            m_TaskInventoryXmlProcessors.Add("AssetID", ProcessTIAssetID);
            m_TaskInventoryXmlProcessors.Add("BasePermissions", ProcessTIBasePermissions);
            m_TaskInventoryXmlProcessors.Add("CreationDate", ProcessTICreationDate);
            m_TaskInventoryXmlProcessors.Add("CreatorID", ProcessTICreatorID);
            m_TaskInventoryXmlProcessors.Add("CreatorData", ProcessTICreatorData);
            m_TaskInventoryXmlProcessors.Add("Description", ProcessTIDescription);
            m_TaskInventoryXmlProcessors.Add("EveryonePermissions", ProcessTIEveryonePermissions);
            m_TaskInventoryXmlProcessors.Add("Flags", ProcessTIFlags);
            m_TaskInventoryXmlProcessors.Add("GroupID", ProcessTIGroupID);
            m_TaskInventoryXmlProcessors.Add("GroupPermissions", ProcessTIGroupPermissions);
            m_TaskInventoryXmlProcessors.Add("InvType", ProcessTIInvType);
            m_TaskInventoryXmlProcessors.Add("ItemID", ProcessTIItemID);
            m_TaskInventoryXmlProcessors.Add("OldItemID", ProcessTIOldItemID);
            m_TaskInventoryXmlProcessors.Add("LastOwnerID", ProcessTILastOwnerID);
            m_TaskInventoryXmlProcessors.Add("Name", ProcessTIName);
            m_TaskInventoryXmlProcessors.Add("NextPermissions", ProcessTINextPermissions);
            m_TaskInventoryXmlProcessors.Add("OwnerID", ProcessTIOwnerID);
            m_TaskInventoryXmlProcessors.Add("CurrentPermissions", ProcessTICurrentPermissions);
            m_TaskInventoryXmlProcessors.Add("ParentID", ProcessTIParentID);
            m_TaskInventoryXmlProcessors.Add("ParentPartID", ProcessTIParentPartID);
            m_TaskInventoryXmlProcessors.Add("PermsGranter", ProcessTIPermsGranter);
            m_TaskInventoryXmlProcessors.Add("PermsMask", ProcessTIPermsMask);
            m_TaskInventoryXmlProcessors.Add("Type", ProcessTIType);
            m_TaskInventoryXmlProcessors.Add("OwnerChanged", ProcessTIOwnerChanged);
            
            #endregion

            #region ShapeXmlProcessors initialization
            m_ShapeXmlProcessors.Add("ProfileCurve", ProcessShpProfileCurve);
            m_ShapeXmlProcessors.Add("TextureEntry", ProcessShpTextureEntry);
            m_ShapeXmlProcessors.Add("ExtraParams", ProcessShpExtraParams);
            m_ShapeXmlProcessors.Add("PathBegin", ProcessShpPathBegin);
            m_ShapeXmlProcessors.Add("PathCurve", ProcessShpPathCurve);
            m_ShapeXmlProcessors.Add("PathEnd", ProcessShpPathEnd);
            m_ShapeXmlProcessors.Add("PathRadiusOffset", ProcessShpPathRadiusOffset);
            m_ShapeXmlProcessors.Add("PathRevolutions", ProcessShpPathRevolutions);
            m_ShapeXmlProcessors.Add("PathScaleX", ProcessShpPathScaleX);
            m_ShapeXmlProcessors.Add("PathScaleY", ProcessShpPathScaleY);
            m_ShapeXmlProcessors.Add("PathShearX", ProcessShpPathShearX);
            m_ShapeXmlProcessors.Add("PathShearY", ProcessShpPathShearY);
            m_ShapeXmlProcessors.Add("PathSkew", ProcessShpPathSkew);
            m_ShapeXmlProcessors.Add("PathTaperX", ProcessShpPathTaperX);
            m_ShapeXmlProcessors.Add("PathTaperY", ProcessShpPathTaperY);
            m_ShapeXmlProcessors.Add("PathTwist", ProcessShpPathTwist);
            m_ShapeXmlProcessors.Add("PathTwistBegin", ProcessShpPathTwistBegin);
            m_ShapeXmlProcessors.Add("PCode", ProcessShpPCode);
            m_ShapeXmlProcessors.Add("ProfileBegin", ProcessShpProfileBegin);
            m_ShapeXmlProcessors.Add("ProfileEnd", ProcessShpProfileEnd);
            m_ShapeXmlProcessors.Add("ProfileHollow", ProcessShpProfileHollow);
            m_ShapeXmlProcessors.Add("Scale", ProcessShpScale);
            m_ShapeXmlProcessors.Add("State", ProcessShpState);
            m_ShapeXmlProcessors.Add("ProfileShape", ProcessShpProfileShape);
            m_ShapeXmlProcessors.Add("HollowShape", ProcessShpHollowShape);
            m_ShapeXmlProcessors.Add("SculptTexture", ProcessShpSculptTexture);
            m_ShapeXmlProcessors.Add("SculptType", ProcessShpSculptType);
            m_ShapeXmlProcessors.Add("SculptData", ProcessShpSculptData);
            m_ShapeXmlProcessors.Add("FlexiSoftness", ProcessShpFlexiSoftness);
            m_ShapeXmlProcessors.Add("FlexiTension", ProcessShpFlexiTension);
            m_ShapeXmlProcessors.Add("FlexiDrag", ProcessShpFlexiDrag);
            m_ShapeXmlProcessors.Add("FlexiGravity", ProcessShpFlexiGravity);
            m_ShapeXmlProcessors.Add("FlexiWind", ProcessShpFlexiWind);
            m_ShapeXmlProcessors.Add("FlexiForceX", ProcessShpFlexiForceX);
            m_ShapeXmlProcessors.Add("FlexiForceY", ProcessShpFlexiForceY);
            m_ShapeXmlProcessors.Add("FlexiForceZ", ProcessShpFlexiForceZ);
            m_ShapeXmlProcessors.Add("LightColorR", ProcessShpLightColorR);
            m_ShapeXmlProcessors.Add("LightColorG", ProcessShpLightColorG);
            m_ShapeXmlProcessors.Add("LightColorB", ProcessShpLightColorB);
            m_ShapeXmlProcessors.Add("LightColorA", ProcessShpLightColorA);
            m_ShapeXmlProcessors.Add("LightRadius", ProcessShpLightRadius);
            m_ShapeXmlProcessors.Add("LightCutoff", ProcessShpLightCutoff);
            m_ShapeXmlProcessors.Add("LightFalloff", ProcessShpLightFalloff);
            m_ShapeXmlProcessors.Add("LightIntensity", ProcessShpLightIntensity);
            m_ShapeXmlProcessors.Add("FlexiEntry", ProcessShpFlexiEntry);
            m_ShapeXmlProcessors.Add("LightEntry", ProcessShpLightEntry);
            m_ShapeXmlProcessors.Add("SculptEntry", ProcessShpSculptEntry);
            m_ShapeXmlProcessors.Add("Media", ProcessShpMedia);
            #endregion
        }

        #region SOPXmlProcessors
        private static void ProcessAllowedDrop(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.AllowedDrop = Util.ReadBoolean(reader);
        }

        private static void ProcessCreatorID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CreatorID = Util.ReadUUID(reader, "CreatorID");
        }

        private static void ProcessCreatorData(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CreatorData = reader.ReadElementContentAsString("CreatorData", String.Empty);
        }

        private static void ProcessFolderID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.FolderID = Util.ReadUUID(reader, "FolderID");
        }

        private static void ProcessInventorySerial(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.InventorySerial = (uint)reader.ReadElementContentAsInt("InventorySerial", String.Empty);
        }

        private static void ProcessTaskInventory(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.TaskInventory = ReadTaskInventory(reader, "TaskInventory");
        }

        private static void ProcessUUID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.UUID = Util.ReadUUID(reader, "UUID");
        }

        private static void ProcessLocalId(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.LocalId = (uint)reader.ReadElementContentAsLong("LocalId", String.Empty);
        }

        private static void ProcessName(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Name = reader.ReadElementString("Name");
        }

        private static void ProcessMaterial(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Material = (byte)reader.ReadElementContentAsInt("Material", String.Empty);
        }

        private static void ProcessPassTouches(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PassTouches = Util.ReadBoolean(reader);
        }

        private static void ProcessPassCollisions(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PassCollisions = Util.ReadBoolean(reader);
        }

        private static void ProcessRegionHandle(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.RegionHandle = (ulong)reader.ReadElementContentAsLong("RegionHandle", String.Empty);
        }

        private static void ProcessScriptAccessPin(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.ScriptAccessPin = reader.ReadElementContentAsInt("ScriptAccessPin", String.Empty);
        }

        private static void ProcessGroupPosition(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.GroupPosition = Util.ReadVector(reader, "GroupPosition");
        }

        private static void ProcessOffsetPosition(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.OffsetPosition = Util.ReadVector(reader, "OffsetPosition"); ;
        }

        private static void ProcessRotationOffset(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.RotationOffset = Util.ReadQuaternion(reader, "RotationOffset");
        }

        private static void ProcessVelocity(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Velocity = Util.ReadVector(reader, "Velocity");
        }

        private static void ProcessAngularVelocity(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.AngularVelocity = Util.ReadVector(reader, "AngularVelocity");
        }

        private static void ProcessAcceleration(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Acceleration = Util.ReadVector(reader, "Acceleration");
        }

        private static void ProcessDescription(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Description = reader.ReadElementString("Description");
        }

        private static void ProcessColor(SceneObjectPart obj, XmlTextReader reader)
        {
            reader.ReadStartElement("Color");
            if (reader.Name == "R")
            {
                float r = reader.ReadElementContentAsFloat("R", String.Empty);
                float g = reader.ReadElementContentAsFloat("G", String.Empty);
                float b = reader.ReadElementContentAsFloat("B", String.Empty);
                float a = reader.ReadElementContentAsFloat("A", String.Empty);
                obj.Color = Color.FromArgb((int)a, (int)r, (int)g, (int)b);
                reader.ReadEndElement();
            }
        }

        private static void ProcessText(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Text = reader.ReadElementString("Text", String.Empty);
        }

        private static void ProcessSitName(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitName = reader.ReadElementString("SitName", String.Empty);
        }

        private static void ProcessTouchName(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.TouchName = reader.ReadElementString("TouchName", String.Empty);
        }

        private static void ProcessLinkNum(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.LinkNum = reader.ReadElementContentAsInt("LinkNum", String.Empty);
        }

        private static void ProcessClickAction(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.ClickAction = (byte)reader.ReadElementContentAsInt("ClickAction", String.Empty);
        }

        private static void ProcessShape(SceneObjectPart obj, XmlTextReader reader)
        {
            List<string> errorNodeNames;
            obj.Shape = ReadShape(reader, "Shape", out errorNodeNames);

            if (errorNodeNames != null)
            {
                m_log.DebugFormat(
                    "[SceneObjectSerializer]: Parsing PrimitiveBaseShape for object part {0} {1} encountered errors in properties {2}.",
                    obj.Name, obj.UUID, string.Join(", ", errorNodeNames.ToArray()));
            }
        }

        private static void ProcessScale(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Scale = Util.ReadVector(reader, "Scale");
        }

        private static void ProcessSitTargetOrientation(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitTargetOrientation = Util.ReadQuaternion(reader, "SitTargetOrientation");
        }

        private static void ProcessSitTargetPosition(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitTargetPosition = Util.ReadVector(reader, "SitTargetPosition");
        }

        private static void ProcessSitTargetPositionLL(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitTargetPositionLL = Util.ReadVector(reader, "SitTargetPositionLL");
        }

        private static void ProcessSitTargetOrientationLL(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitTargetOrientationLL = Util.ReadQuaternion(reader, "SitTargetOrientationLL");
        }

        private static void ProcessParentID(SceneObjectPart obj, XmlTextReader reader)
        {
            string str = reader.ReadElementContentAsString("ParentID", String.Empty);
            obj.ParentID = Convert.ToUInt32(str);
        }

        private static void ProcessCreationDate(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CreationDate = reader.ReadElementContentAsInt("CreationDate", String.Empty);
        }

        private static void ProcessCategory(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Category = (uint)reader.ReadElementContentAsInt("Category", String.Empty);
        }

        private static void ProcessSalePrice(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SalePrice = reader.ReadElementContentAsInt("SalePrice", String.Empty);
        }

        private static void ProcessObjectSaleType(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.ObjectSaleType = (byte)reader.ReadElementContentAsInt("ObjectSaleType", String.Empty);
        }

        private static void ProcessOwnershipCost(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.OwnershipCost = reader.ReadElementContentAsInt("OwnershipCost", String.Empty);
        }

        private static void ProcessGroupID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.GroupID = Util.ReadUUID(reader, "GroupID");
        }

        private static void ProcessOwnerID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.OwnerID = Util.ReadUUID(reader, "OwnerID");
        }

        private static void ProcessLastOwnerID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.LastOwnerID = Util.ReadUUID(reader, "LastOwnerID");
        }

        private static void ProcessBaseMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.BaseMask = (uint)reader.ReadElementContentAsInt("BaseMask", String.Empty);
        }

        private static void ProcessOwnerMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.OwnerMask = (uint)reader.ReadElementContentAsInt("OwnerMask", String.Empty);
        }

        private static void ProcessGroupMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.GroupMask = (uint)reader.ReadElementContentAsInt("GroupMask", String.Empty);
        }

        private static void ProcessEveryoneMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.EveryoneMask = (uint)reader.ReadElementContentAsInt("EveryoneMask", String.Empty);
        }

        private static void ProcessNextOwnerMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.NextOwnerMask = (uint)reader.ReadElementContentAsInt("NextOwnerMask", String.Empty);
        }

        private static void ProcessFlags(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Flags = Util.ReadEnum<PrimFlags>(reader, "Flags");
        }

        private static void ProcessCollisionSound(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CollisionSound = Util.ReadUUID(reader, "CollisionSound");
        }

        private static void ProcessCollisionSoundVolume(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CollisionSoundVolume = reader.ReadElementContentAsFloat("CollisionSoundVolume", String.Empty);
        }

        private static void ProcessMediaUrl(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.MediaUrl = reader.ReadElementContentAsString("MediaUrl", String.Empty);
        }

        private static void ProcessTextureAnimation(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.TextureAnimation = Convert.FromBase64String(reader.ReadElementContentAsString("TextureAnimation", String.Empty));
        }

        private static void ProcessParticleSystem(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.ParticleSystem = Convert.FromBase64String(reader.ReadElementContentAsString("ParticleSystem", String.Empty));
        }

        private static void ProcessPayPrice0(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[0] = (int)reader.ReadElementContentAsInt("PayPrice0", String.Empty);
        }

        private static void ProcessPayPrice1(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[1] = (int)reader.ReadElementContentAsInt("PayPrice1", String.Empty);
        }

        private static void ProcessPayPrice2(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[2] = (int)reader.ReadElementContentAsInt("PayPrice2", String.Empty);
        }

        private static void ProcessPayPrice3(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[3] = (int)reader.ReadElementContentAsInt("PayPrice3", String.Empty);
        }

        private static void ProcessPayPrice4(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[4] = (int)reader.ReadElementContentAsInt("PayPrice4", String.Empty);
        }

        #endregion

        #region TaskInventoryXmlProcessors
        private static void ProcessTIAssetID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.AssetID = Util.ReadUUID(reader, "AssetID");
        }

        private static void ProcessTIBasePermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.BasePermissions = (uint)reader.ReadElementContentAsInt("BasePermissions", String.Empty);
        }

        private static void ProcessTICreationDate(TaskInventoryItem item, XmlTextReader reader)
        {
            item.CreationDate = (uint)reader.ReadElementContentAsInt("CreationDate", String.Empty);
        }

        private static void ProcessTICreatorID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.CreatorID = Util.ReadUUID(reader, "CreatorID");
        }

        private static void ProcessTICreatorData(TaskInventoryItem item, XmlTextReader reader)
        {
            item.CreatorData = reader.ReadElementContentAsString("CreatorData", String.Empty);
        }

        private static void ProcessTIDescription(TaskInventoryItem item, XmlTextReader reader)
        {
            item.Description = reader.ReadElementContentAsString("Description", String.Empty);
        }

        private static void ProcessTIEveryonePermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.EveryonePermissions = (uint)reader.ReadElementContentAsInt("EveryonePermissions", String.Empty);
        }

        private static void ProcessTIFlags(TaskInventoryItem item, XmlTextReader reader)
        {
            item.Flags = (uint)reader.ReadElementContentAsInt("Flags", String.Empty);
        }

        private static void ProcessTIGroupID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.GroupID = Util.ReadUUID(reader, "GroupID");
        }

        private static void ProcessTIGroupPermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.GroupPermissions = (uint)reader.ReadElementContentAsInt("GroupPermissions", String.Empty);
        }

        private static void ProcessTIInvType(TaskInventoryItem item, XmlTextReader reader)
        {
            item.InvType = reader.ReadElementContentAsInt("InvType", String.Empty);
        }

        private static void ProcessTIItemID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.ItemID = Util.ReadUUID(reader, "ItemID");
        }

        private static void ProcessTIOldItemID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.OldItemID = Util.ReadUUID(reader, "OldItemID");
        }

        private static void ProcessTILastOwnerID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.LastOwnerID = Util.ReadUUID(reader, "LastOwnerID");
        }

        private static void ProcessTIName(TaskInventoryItem item, XmlTextReader reader)
        {
            item.Name = reader.ReadElementContentAsString("Name", String.Empty);
        }

        private static void ProcessTINextPermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.NextPermissions = (uint)reader.ReadElementContentAsInt("NextPermissions", String.Empty);
        }

        private static void ProcessTIOwnerID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.OwnerID = Util.ReadUUID(reader, "OwnerID");
        }

        private static void ProcessTICurrentPermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.CurrentPermissions = (uint)reader.ReadElementContentAsInt("CurrentPermissions", String.Empty);
        }

        private static void ProcessTIParentID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.ParentID = Util.ReadUUID(reader, "ParentID");
        }

        private static void ProcessTIParentPartID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.ParentPartID = Util.ReadUUID(reader, "ParentPartID");
        }

        private static void ProcessTIPermsGranter(TaskInventoryItem item, XmlTextReader reader)
        {
            item.PermsGranter = Util.ReadUUID(reader, "PermsGranter");
        }

        private static void ProcessTIPermsMask(TaskInventoryItem item, XmlTextReader reader)
        {
            item.PermsMask = reader.ReadElementContentAsInt("PermsMask", String.Empty);
        }

        private static void ProcessTIType(TaskInventoryItem item, XmlTextReader reader)
        {
            item.Type = reader.ReadElementContentAsInt("Type", String.Empty);
        }

        private static void ProcessTIOwnerChanged(TaskInventoryItem item, XmlTextReader reader)
        {
            item.OwnerChanged = Util.ReadBoolean(reader);
        }

        #endregion

        #region ShapeXmlProcessors
        private static void ProcessShpProfileCurve(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ProfileCurve = (byte)reader.ReadElementContentAsInt("ProfileCurve", String.Empty);
        }

        private static void ProcessShpTextureEntry(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            byte[] teData = Convert.FromBase64String(reader.ReadElementString("TextureEntry"));
            shp.Textures = new Primitive.TextureEntry(teData, 0, teData.Length);
        }

        private static void ProcessShpExtraParams(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ExtraParams = Convert.FromBase64String(reader.ReadElementString("ExtraParams"));
        }

        private static void ProcessShpPathBegin(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathBegin = (ushort)reader.ReadElementContentAsInt("PathBegin", String.Empty);
        }

        private static void ProcessShpPathCurve(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathCurve = (byte)reader.ReadElementContentAsInt("PathCurve", String.Empty);
        }

        private static void ProcessShpPathEnd(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathEnd = (ushort)reader.ReadElementContentAsInt("PathEnd", String.Empty);
        }

        private static void ProcessShpPathRadiusOffset(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathRadiusOffset = (sbyte)reader.ReadElementContentAsInt("PathRadiusOffset", String.Empty);
        }

        private static void ProcessShpPathRevolutions(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathRevolutions = (byte)reader.ReadElementContentAsInt("PathRevolutions", String.Empty);
        }

        private static void ProcessShpPathScaleX(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathScaleX = (byte)reader.ReadElementContentAsInt("PathScaleX", String.Empty);
        }

        private static void ProcessShpPathScaleY(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathScaleY = (byte)reader.ReadElementContentAsInt("PathScaleY", String.Empty);
        }

        private static void ProcessShpPathShearX(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathShearX = (byte)reader.ReadElementContentAsInt("PathShearX", String.Empty);
        }

        private static void ProcessShpPathShearY(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathShearY = (byte)reader.ReadElementContentAsInt("PathShearY", String.Empty);
        }

        private static void ProcessShpPathSkew(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathSkew = (sbyte)reader.ReadElementContentAsInt("PathSkew", String.Empty);
        }

        private static void ProcessShpPathTaperX(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathTaperX = (sbyte)reader.ReadElementContentAsInt("PathTaperX", String.Empty);
        }

        private static void ProcessShpPathTaperY(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathTaperY = (sbyte)reader.ReadElementContentAsInt("PathTaperY", String.Empty);
        }

        private static void ProcessShpPathTwist(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathTwist = (sbyte)reader.ReadElementContentAsInt("PathTwist", String.Empty);
        }

        private static void ProcessShpPathTwistBegin(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathTwistBegin = (sbyte)reader.ReadElementContentAsInt("PathTwistBegin", String.Empty);
        }

        private static void ProcessShpPCode(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PCode = (byte)reader.ReadElementContentAsInt("PCode", String.Empty);
        }

        private static void ProcessShpProfileBegin(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ProfileBegin = (ushort)reader.ReadElementContentAsInt("ProfileBegin", String.Empty);
        }

        private static void ProcessShpProfileEnd(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ProfileEnd = (ushort)reader.ReadElementContentAsInt("ProfileEnd", String.Empty);
        }

        private static void ProcessShpProfileHollow(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ProfileHollow = (ushort)reader.ReadElementContentAsInt("ProfileHollow", String.Empty);
        }

        private static void ProcessShpScale(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.Scale = Util.ReadVector(reader, "Scale");
        }

        private static void ProcessShpState(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.State = (byte)reader.ReadElementContentAsInt("State", String.Empty);
        }

        private static void ProcessShpProfileShape(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ProfileShape = Util.ReadEnum<ProfileShape>(reader, "ProfileShape");
        }

        private static void ProcessShpHollowShape(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.HollowShape = Util.ReadEnum<HollowShape>(reader, "HollowShape");
        }
        
        private static void ProcessShpSculptTexture(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.SculptTexture = Util.ReadUUID(reader, "SculptTexture");
        }

        private static void ProcessShpSculptType(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.SculptType = (byte)reader.ReadElementContentAsInt("SculptType", String.Empty);
        }

        private static void ProcessShpSculptData(PrimitiveBaseShape shp, XmlTextReader reader)
        {
//            m_log.DebugFormat("[SCENE OBJECT SERIALIZER]: Setting sculpt data length {0}", shp.SculptData.Length);

            shp.SculptData = Convert.FromBase64String(reader.ReadElementString("SculptData"));
        }

        private static void ProcessShpFlexiSoftness(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiSoftness = reader.ReadElementContentAsInt("FlexiSoftness", String.Empty);
        }

        private static void ProcessShpFlexiTension(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiTension = reader.ReadElementContentAsFloat("FlexiTension", String.Empty);
        }

        private static void ProcessShpFlexiDrag(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiDrag = reader.ReadElementContentAsFloat("FlexiDrag", String.Empty);
        }

        private static void ProcessShpFlexiGravity(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiGravity = reader.ReadElementContentAsFloat("FlexiGravity", String.Empty);
        }

        private static void ProcessShpFlexiWind(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiWind = reader.ReadElementContentAsFloat("FlexiWind", String.Empty);
        }

        private static void ProcessShpFlexiForceX(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiForceX = reader.ReadElementContentAsFloat("FlexiForceX", String.Empty);
        }

        private static void ProcessShpFlexiForceY(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiForceY = reader.ReadElementContentAsFloat("FlexiForceY", String.Empty);
        }

        private static void ProcessShpFlexiForceZ(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiForceZ = reader.ReadElementContentAsFloat("FlexiForceZ", String.Empty);
        }

        private static void ProcessShpLightColorR(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightColorR = reader.ReadElementContentAsFloat("LightColorR", String.Empty);
        }

        private static void ProcessShpLightColorG(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightColorG = reader.ReadElementContentAsFloat("LightColorG", String.Empty);
        }

        private static void ProcessShpLightColorB(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightColorB = reader.ReadElementContentAsFloat("LightColorB", String.Empty);
        }

        private static void ProcessShpLightColorA(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightColorA = reader.ReadElementContentAsFloat("LightColorA", String.Empty);
        }

        private static void ProcessShpLightRadius(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightRadius = reader.ReadElementContentAsFloat("LightRadius", String.Empty);
        }

        private static void ProcessShpLightCutoff(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightCutoff = reader.ReadElementContentAsFloat("LightCutoff", String.Empty);
        }

        private static void ProcessShpLightFalloff(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightFalloff = reader.ReadElementContentAsFloat("LightFalloff", String.Empty);
        }

        private static void ProcessShpLightIntensity(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightIntensity = reader.ReadElementContentAsFloat("LightIntensity", String.Empty);
        }

        private static void ProcessShpFlexiEntry(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiEntry = Util.ReadBoolean(reader);
        }

        private static void ProcessShpLightEntry(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightEntry = Util.ReadBoolean(reader);
        }

        private static void ProcessShpSculptEntry(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.SculptEntry = Util.ReadBoolean(reader);
        }

        private static void ProcessShpMedia(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            string value = reader.ReadElementContentAsString("Media", String.Empty);
            shp.Media = PrimitiveBaseShape.MediaList.FromXml(value);
        }

        #endregion

        ////////// Write /////////

        public static void SOGToXml2(XmlTextWriter writer, SceneObjectGroup sog, Dictionary<string, object>options)
        {
            writer.WriteStartElement(String.Empty, "SceneObjectGroup", String.Empty);
            SOPToXml2(writer, sog.RootPart, options);
            writer.WriteStartElement(String.Empty, "OtherParts", String.Empty);

            sog.ForEachPart(delegate(SceneObjectPart sop)
            {
                if (sop.UUID != sog.RootPart.UUID)
                    SOPToXml2(writer, sop, options);
            });

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        public static void SOPToXml2(XmlTextWriter writer, SceneObjectPart sop, Dictionary<string, object> options)
        {
            writer.WriteStartElement("SceneObjectPart");
            writer.WriteAttributeString("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
            writer.WriteAttributeString("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");

            writer.WriteElementString("AllowedDrop", sop.AllowedDrop.ToString().ToLower());

            WriteUUID(writer, "CreatorID", sop.CreatorID, options);

            if (sop.CreatorData != null && sop.CreatorData != string.Empty)
                writer.WriteElementString("CreatorData", sop.CreatorData);
            else if (options.ContainsKey("home"))
            {
                if (m_UserManagement == null)
                    m_UserManagement = sop.ParentGroup.Scene.RequestModuleInterface<IUserManagement>();
                string name = m_UserManagement.GetUserName(sop.CreatorID);
                writer.WriteElementString("CreatorData", (string)options["home"] + ";" + name);
            }

            WriteUUID(writer, "FolderID", sop.FolderID, options);
            writer.WriteElementString("InventorySerial", sop.InventorySerial.ToString());

            WriteTaskInventory(writer, sop.TaskInventory, options, sop.ParentGroup.Scene);

            WriteUUID(writer, "UUID", sop.UUID, options);
            writer.WriteElementString("LocalId", sop.LocalId.ToString());
            writer.WriteElementString("Name", sop.Name);
            writer.WriteElementString("Material", sop.Material.ToString());
            writer.WriteElementString("PassTouches", sop.PassTouches.ToString().ToLower());
            writer.WriteElementString("PassCollisions", sop.PassCollisions.ToString().ToLower());
            writer.WriteElementString("RegionHandle", sop.RegionHandle.ToString());
            writer.WriteElementString("ScriptAccessPin", sop.ScriptAccessPin.ToString());

            WriteVector(writer, "GroupPosition", sop.GroupPosition);
            WriteVector(writer, "OffsetPosition", sop.OffsetPosition);

            WriteQuaternion(writer, "RotationOffset", sop.RotationOffset);
            WriteVector(writer, "Velocity", sop.Velocity);
            WriteVector(writer, "AngularVelocity", sop.AngularVelocity);
            WriteVector(writer, "Acceleration", sop.Acceleration);
            writer.WriteElementString("Description", sop.Description);

            writer.WriteStartElement("Color");
            writer.WriteElementString("R", sop.Color.R.ToString(Utils.EnUsCulture));
            writer.WriteElementString("G", sop.Color.G.ToString(Utils.EnUsCulture));
            writer.WriteElementString("B", sop.Color.B.ToString(Utils.EnUsCulture));
            writer.WriteElementString("A", sop.Color.A.ToString(Utils.EnUsCulture));
            writer.WriteEndElement();

            writer.WriteElementString("Text", sop.Text);
            writer.WriteElementString("SitName", sop.SitName);
            writer.WriteElementString("TouchName", sop.TouchName);

            writer.WriteElementString("LinkNum", sop.LinkNum.ToString());
            writer.WriteElementString("ClickAction", sop.ClickAction.ToString());

            WriteShape(writer, sop.Shape, options);

            WriteVector(writer, "Scale", sop.Scale);
            WriteQuaternion(writer, "SitTargetOrientation", sop.SitTargetOrientation); 
            WriteVector(writer, "SitTargetPosition", sop.SitTargetPosition);
            WriteVector(writer, "SitTargetPositionLL", sop.SitTargetPositionLL);
            WriteQuaternion(writer, "SitTargetOrientationLL", sop.SitTargetOrientationLL);
            writer.WriteElementString("ParentID", sop.ParentID.ToString());
            writer.WriteElementString("CreationDate", sop.CreationDate.ToString());
            writer.WriteElementString("Category", sop.Category.ToString());
            writer.WriteElementString("SalePrice", sop.SalePrice.ToString());
            writer.WriteElementString("ObjectSaleType", sop.ObjectSaleType.ToString());
            writer.WriteElementString("OwnershipCost", sop.OwnershipCost.ToString());
            WriteUUID(writer, "GroupID", sop.GroupID, options);

            UUID ownerID = options.ContainsKey("wipe-owners") ? UUID.Zero : sop.OwnerID;
            WriteUUID(writer, "OwnerID", ownerID, options);

            UUID lastOwnerID = options.ContainsKey("wipe-owners") ? UUID.Zero : sop.LastOwnerID;
            WriteUUID(writer, "LastOwnerID", lastOwnerID, options);

            writer.WriteElementString("BaseMask", sop.BaseMask.ToString());
            writer.WriteElementString("OwnerMask", sop.OwnerMask.ToString());
            writer.WriteElementString("GroupMask", sop.GroupMask.ToString());
            writer.WriteElementString("EveryoneMask", sop.EveryoneMask.ToString());
            writer.WriteElementString("NextOwnerMask", sop.NextOwnerMask.ToString());
            WriteFlags(writer, "Flags", sop.Flags.ToString(), options);
            WriteUUID(writer, "CollisionSound", sop.CollisionSound, options);
            writer.WriteElementString("CollisionSoundVolume", sop.CollisionSoundVolume.ToString());
            if (sop.MediaUrl != null)
                writer.WriteElementString("MediaUrl", sop.MediaUrl.ToString());
            WriteBytes(writer, "TextureAnimation", sop.TextureAnimation);
            WriteBytes(writer, "ParticleSystem", sop.ParticleSystem);
            writer.WriteElementString("PayPrice0", sop.PayPrice[0].ToString());
            writer.WriteElementString("PayPrice1", sop.PayPrice[1].ToString());
            writer.WriteElementString("PayPrice2", sop.PayPrice[2].ToString());
            writer.WriteElementString("PayPrice3", sop.PayPrice[3].ToString());
            writer.WriteElementString("PayPrice4", sop.PayPrice[4].ToString());

            writer.WriteEndElement();
        }

        static void WriteUUID(XmlTextWriter writer, string name, UUID id, Dictionary<string, object> options)
        {
            writer.WriteStartElement(name);
            if (options.ContainsKey("old-guids"))
                writer.WriteElementString("Guid", id.ToString());
            else
                writer.WriteElementString("UUID", id.ToString());
            writer.WriteEndElement();
        }

        static void WriteVector(XmlTextWriter writer, string name, Vector3 vec)
        {
            writer.WriteStartElement(name);
            writer.WriteElementString("X", vec.X.ToString(Utils.EnUsCulture));
            writer.WriteElementString("Y", vec.Y.ToString(Utils.EnUsCulture));
            writer.WriteElementString("Z", vec.Z.ToString(Utils.EnUsCulture));
            writer.WriteEndElement();
        }

        static void WriteQuaternion(XmlTextWriter writer, string name, Quaternion quat)
        {
            writer.WriteStartElement(name);
            writer.WriteElementString("X", quat.X.ToString(Utils.EnUsCulture));
            writer.WriteElementString("Y", quat.Y.ToString(Utils.EnUsCulture));
            writer.WriteElementString("Z", quat.Z.ToString(Utils.EnUsCulture));
            writer.WriteElementString("W", quat.W.ToString(Utils.EnUsCulture));
            writer.WriteEndElement();
        }

        static void WriteBytes(XmlTextWriter writer, string name, byte[] data)
        {
            writer.WriteStartElement(name);
            byte[] d;
            if (data != null)
                d = data;
            else
                d = Utils.EmptyBytes;
            writer.WriteBase64(d, 0, d.Length);
            writer.WriteEndElement(); // name

        }

        static void WriteFlags(XmlTextWriter writer, string name, string flagsStr, Dictionary<string, object> options)
        {
            // Older versions of serialization can't cope with commas, so we eliminate the commas
            writer.WriteElementString(name, flagsStr.Replace(",", ""));
        }

        public static void WriteTaskInventory(XmlTextWriter writer, TaskInventoryDictionary tinv, Dictionary<string, object> options, Scene scene)
        {
            if (tinv.Count > 0) // otherwise skip this
            {
                writer.WriteStartElement("TaskInventory");

                foreach (TaskInventoryItem item in tinv.Values)
                {
                    writer.WriteStartElement("TaskInventoryItem");

                    WriteUUID(writer, "AssetID", item.AssetID, options);
                    writer.WriteElementString("BasePermissions", item.BasePermissions.ToString());
                    writer.WriteElementString("CreationDate", item.CreationDate.ToString());

                    WriteUUID(writer, "CreatorID", item.CreatorID, options);

                    if (item.CreatorData != null && item.CreatorData != string.Empty)
                        writer.WriteElementString("CreatorData", item.CreatorData);
                    else if (options.ContainsKey("home"))
                    {
                        if (m_UserManagement == null)
                            m_UserManagement = scene.RequestModuleInterface<IUserManagement>();
                        string name = m_UserManagement.GetUserName(item.CreatorID);
                        writer.WriteElementString("CreatorData", (string)options["home"] + ";" + name);
                    }

                    writer.WriteElementString("Description", item.Description);
                    writer.WriteElementString("EveryonePermissions", item.EveryonePermissions.ToString());
                    writer.WriteElementString("Flags", item.Flags.ToString());
                    WriteUUID(writer, "GroupID", item.GroupID, options);
                    writer.WriteElementString("GroupPermissions", item.GroupPermissions.ToString());
                    writer.WriteElementString("InvType", item.InvType.ToString());
                    WriteUUID(writer, "ItemID", item.ItemID, options);
                    WriteUUID(writer, "OldItemID", item.OldItemID, options);

                    UUID lastOwnerID = options.ContainsKey("wipe-owners") ? UUID.Zero : item.LastOwnerID;
                    WriteUUID(writer, "LastOwnerID", lastOwnerID, options);

                    writer.WriteElementString("Name", item.Name);
                    writer.WriteElementString("NextPermissions", item.NextPermissions.ToString());

                    UUID ownerID = options.ContainsKey("wipe-owners") ? UUID.Zero : item.OwnerID;
                    WriteUUID(writer, "OwnerID", ownerID, options);

                    writer.WriteElementString("CurrentPermissions", item.CurrentPermissions.ToString());
                    WriteUUID(writer, "ParentID", item.ParentID, options);
                    WriteUUID(writer, "ParentPartID", item.ParentPartID, options);
                    WriteUUID(writer, "PermsGranter", item.PermsGranter, options);
                    writer.WriteElementString("PermsMask", item.PermsMask.ToString());
                    writer.WriteElementString("Type", item.Type.ToString());
                    writer.WriteElementString("OwnerChanged", item.OwnerChanged.ToString().ToLower());

                    writer.WriteEndElement(); // TaskInventoryItem
                }

                writer.WriteEndElement(); // TaskInventory
            }
        }

        public static void WriteShape(XmlTextWriter writer, PrimitiveBaseShape shp, Dictionary<string, object> options)
        {
            if (shp != null)
            {
                writer.WriteStartElement("Shape");

                writer.WriteElementString("ProfileCurve", shp.ProfileCurve.ToString());

                writer.WriteStartElement("TextureEntry");
                byte[] te;
                if (shp.TextureEntry != null)
                    te = shp.TextureEntry;
                else
                    te = Utils.EmptyBytes;
                writer.WriteBase64(te, 0, te.Length);
                writer.WriteEndElement(); // TextureEntry

                writer.WriteStartElement("ExtraParams");
                byte[] ep;
                if (shp.ExtraParams != null)
                    ep = shp.ExtraParams;
                else
                    ep = Utils.EmptyBytes;
                writer.WriteBase64(ep, 0, ep.Length);
                writer.WriteEndElement(); // ExtraParams

                writer.WriteElementString("PathBegin", shp.PathBegin.ToString());
                writer.WriteElementString("PathCurve", shp.PathCurve.ToString());
                writer.WriteElementString("PathEnd", shp.PathEnd.ToString());
                writer.WriteElementString("PathRadiusOffset", shp.PathRadiusOffset.ToString());
                writer.WriteElementString("PathRevolutions", shp.PathRevolutions.ToString());
                writer.WriteElementString("PathScaleX", shp.PathScaleX.ToString());
                writer.WriteElementString("PathScaleY", shp.PathScaleY.ToString());
                writer.WriteElementString("PathShearX", shp.PathShearX.ToString());
                writer.WriteElementString("PathShearY", shp.PathShearY.ToString());
                writer.WriteElementString("PathSkew", shp.PathSkew.ToString());
                writer.WriteElementString("PathTaperX", shp.PathTaperX.ToString());
                writer.WriteElementString("PathTaperY", shp.PathTaperY.ToString());
                writer.WriteElementString("PathTwist", shp.PathTwist.ToString());
                writer.WriteElementString("PathTwistBegin", shp.PathTwistBegin.ToString());
                writer.WriteElementString("PCode", shp.PCode.ToString());
                writer.WriteElementString("ProfileBegin", shp.ProfileBegin.ToString());
                writer.WriteElementString("ProfileEnd", shp.ProfileEnd.ToString());
                writer.WriteElementString("ProfileHollow", shp.ProfileHollow.ToString());
                writer.WriteElementString("State", shp.State.ToString());

                WriteFlags(writer, "ProfileShape", shp.ProfileShape.ToString(), options);
                WriteFlags(writer, "HollowShape", shp.HollowShape.ToString(), options);

                WriteUUID(writer, "SculptTexture", shp.SculptTexture, options);
                writer.WriteElementString("SculptType", shp.SculptType.ToString());
                writer.WriteStartElement("SculptData");
                byte[] sd;
                if (shp.SculptData != null)
                    sd = shp.SculptData;
                else
                    sd = Utils.EmptyBytes;
                writer.WriteBase64(sd, 0, sd.Length);
                writer.WriteEndElement(); // SculptData

                writer.WriteElementString("FlexiSoftness", shp.FlexiSoftness.ToString());
                writer.WriteElementString("FlexiTension", shp.FlexiTension.ToString());
                writer.WriteElementString("FlexiDrag", shp.FlexiDrag.ToString());
                writer.WriteElementString("FlexiGravity", shp.FlexiGravity.ToString());
                writer.WriteElementString("FlexiWind", shp.FlexiWind.ToString());
                writer.WriteElementString("FlexiForceX", shp.FlexiForceX.ToString());
                writer.WriteElementString("FlexiForceY", shp.FlexiForceY.ToString());
                writer.WriteElementString("FlexiForceZ", shp.FlexiForceZ.ToString());

                writer.WriteElementString("LightColorR", shp.LightColorR.ToString());
                writer.WriteElementString("LightColorG", shp.LightColorG.ToString());
                writer.WriteElementString("LightColorB", shp.LightColorB.ToString());
                writer.WriteElementString("LightColorA", shp.LightColorA.ToString());
                writer.WriteElementString("LightRadius", shp.LightRadius.ToString());
                writer.WriteElementString("LightCutoff", shp.LightCutoff.ToString());
                writer.WriteElementString("LightFalloff", shp.LightFalloff.ToString());
                writer.WriteElementString("LightIntensity", shp.LightIntensity.ToString());

                writer.WriteElementString("FlexiEntry", shp.FlexiEntry.ToString().ToLower());
                writer.WriteElementString("LightEntry", shp.LightEntry.ToString().ToLower());
                writer.WriteElementString("SculptEntry", shp.SculptEntry.ToString().ToLower());

                if (shp.Media != null)
                    writer.WriteElementString("Media", shp.Media.ToXml());

                writer.WriteEndElement(); // Shape
            }
        }

        public static SceneObjectPart Xml2ToSOP(XmlTextReader reader)
        {
            SceneObjectPart obj = new SceneObjectPart();

            reader.ReadStartElement("SceneObjectPart");

            ExternalRepresentationUtils.ExecuteReadProcessors(
                obj,
                m_SOPXmlProcessors,
                reader,
                (o, nodeName, e)
                    => m_log.DebugFormat(
                        "[SceneObjectSerializer]: Exception while parsing {0} in object {1} {2}: {3}{4}",
                        ((SceneObjectPart)o).Name, ((SceneObjectPart)o).UUID, nodeName, e.Message, e.StackTrace));

            reader.ReadEndElement(); // SceneObjectPart

            //m_log.DebugFormat("[XXX]: parsed SOP {0} - {1}", obj.Name, obj.UUID);
            return obj;
        }

        public static TaskInventoryDictionary ReadTaskInventory(XmlTextReader reader, string name)
        {
            TaskInventoryDictionary tinv = new TaskInventoryDictionary();

            if (reader.IsEmptyElement)
            {
                reader.Read();
                return tinv;
            }

            reader.ReadStartElement(name, String.Empty);

            while (reader.Name == "TaskInventoryItem")
            {
                reader.ReadStartElement("TaskInventoryItem", String.Empty); // TaskInventory

                TaskInventoryItem item = new TaskInventoryItem();

                ExternalRepresentationUtils.ExecuteReadProcessors(
                    item,
                    m_TaskInventoryXmlProcessors,
                    reader);

                reader.ReadEndElement(); // TaskInventoryItem
                tinv.Add(item.ItemID, item);

            }

            if (reader.NodeType == XmlNodeType.EndElement)
                reader.ReadEndElement(); // TaskInventory

            return tinv;
        }

        /// <summary>
        /// Read a shape from xml input
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="name">The name of the xml element containing the shape</param>
        /// <param name="errors">a list containing the failing node names.  If no failures then null.</param>
        /// <returns>The shape parsed</returns>
        public static PrimitiveBaseShape ReadShape(XmlTextReader reader, string name, out List<string> errorNodeNames)
        {
            List<string> internalErrorNodeNames = null;

            PrimitiveBaseShape shape = new PrimitiveBaseShape();

            if (reader.IsEmptyElement)
            {
                reader.Read();
                errorNodeNames = null;
                return shape;
            }

            reader.ReadStartElement(name, String.Empty); // Shape

            ExternalRepresentationUtils.ExecuteReadProcessors(
                shape,
                m_ShapeXmlProcessors,
                reader,
                (o, nodeName, e)
                    =>
                    {
//                        m_log.DebugFormat(
//                            "[SceneObjectSerializer]: Exception while parsing Shape property {0}: {1}{2}",
//                            nodeName, e.Message, e.StackTrace);
                        if (internalErrorNodeNames == null)
                            internalErrorNodeNames = new List<string>();

                        internalErrorNodeNames.Add(nodeName);
                    }
            );

            reader.ReadEndElement(); // Shape

            errorNodeNames = internalErrorNodeNames;

            return shape;
        }

        #endregion
    }
}
