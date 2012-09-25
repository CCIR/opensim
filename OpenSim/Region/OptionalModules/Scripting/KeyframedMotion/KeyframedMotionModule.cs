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
using System.Reflection;

using log4net;
using Mono.Addins;
using Nini.Config;

using OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.OptionalModules.Scripting.KeyframedMotion
{
    /// <summary>
    /// Playback command for controlling keyframed animation.
    /// </summary>
    public enum Command
    {
        /// <summary>
        /// Starts animation.
        /// </summary>
        Play = 0,

        /// <summary>
        /// Stops animating and resets animation.
        /// </summary>
        Stop,

        /// <summary>
        /// Pauses animation.
        /// </summary>
        Pause
    }

    /// <summary>
    /// Indicates how a keyframe list should be processed for animation.
    /// </summary>
    public enum Mode
    {
        /// <summary>
        /// Specifies that the animation should proceed from start to end.
        /// </summary>
        /// <remarks>
        /// Default playback mode.
        /// </remarks>
        Forward = 0,

        /// <summary>
        /// Specifies that the animation should repeat continuously from start
        /// to end.
        /// </summary>
        Loop,

        /// <summary>
        /// Specifies that the animation should repeat continuously from start
        /// to end to start.
        /// </summary>
        Ping_Pong,

        /// <summary>
        /// Specifies that the animation should proceed from end to start.
        /// </summary>
        Reverse
    }

    [Flags]
    public enum Data
    {
        /// <summary>
        /// Keyframe list contains rotation instructions.
        /// </summary>
        Rotation = 1,

        /// <summary>
        /// Keyframe list contains position instructions.
        /// </summary>
        Translation
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "KeyframedMotionModule")]
    class KeyframedMotionModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        #region INonSharedRegionModule

        private Scene m_scene;

        public string Name
        {
            get { return "KeyframedMotionModule"; }
        }

        public void Initialise(IConfigSource configSource)
        {
        }

        public void AddRegion(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            m_scene.EventManager.OnFrame -= OnSceneFrame;
            m_scene.EventManager.OnObjectBeingRemovedFromScene -= OnObjectBeingRemovedFromScene;

            m_animations.Clear();
        }

        public void RegionLoaded(Scene scene)
        {
            IScriptModuleComms comms = scene.RequestModuleInterface<IScriptModuleComms>();

            if (comms == null)
            {
                m_log.Error("Could not get IScriptModuleComms, cannot register constants and functions.");
                return;
            }

            m_scene = scene;

            comms.RegisterConstants(this);
            comms.RegisterScriptInvocations(this);

            comms.RegisterConstant("KFM_CMD_PLAY", (int)Command.Play);
            comms.RegisterConstant("KFM_CMD_STOP", (int)Command.Stop);
            comms.RegisterConstant("KFM_CMD_PAUSE", (int)Command.Pause);

            comms.RegisterConstant("KFM_FORWARD", (int)Mode.Forward);
            comms.RegisterConstant("KFM_LOOP", (int)Mode.Loop);
            comms.RegisterConstant("KFM_PING_PONG", (int)Mode.Ping_Pong);
            comms.RegisterConstant("KFM_REVERSE", (int)Mode.Reverse);

            comms.RegisterConstant("KFM_ROTATION", (int)Data.Rotation);
            comms.RegisterConstant("KFM_TRANSLATION", (int)Data.Translation);

            m_scene.EventManager.OnFrame += OnSceneFrame;
            m_scene.EventManager.OnObjectBeingRemovedFromScene += OnObjectBeingRemovedFromScene;
        }

        public void Close()
        {
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        #region ScriptComms

        /// <summary>
        /// Subsequent integer in a list should be one of
        /// <see cref="KFM_CMD_STOP"/>, <see cref="KFM_CMD_PLAY"/> or
        /// <see cref="KFM_CMD_PAUSE"/>
        /// </summary>
        [ScriptConstant]
        public const int KFM_COMMAND = 0;

        /// <summary>
        /// Subsequent integer in a list specifies playback mode.
        /// </summary>
        /// <remarks>
        /// Should be followed by one of
        /// <see cref="KFM_FORWARD"/>, <see cref="KFM_LOOP"/>,
        /// <see cref="KFM_PING_PONG"/> or <see cref="KFM_REVERSE"/>.
        /// Default value is <see cref="KFM_FORWARD"/>
        /// </remarks>
        [ScriptConstant]
        public const int KFM_MODE = 1;

        /// <summary>
        /// Subsequent integer in a list should be a bitwise combination of
        /// <see cref="KFM_TRANSLATION"/> and <see cref="KFM_ROTATION"/>.
        /// </summary>
        /// <remarks>
        /// Must be specified with a keyframe list, if only one option is on
        /// bitfield then keyframe list should only contain that type.
        /// </remarks>
        [ScriptConstant]
        public const int KFM_DATA = 2;

        private Dictionary<UUID, KeyframedMotionAnimation> m_animations =
                new Dictionary<UUID, KeyframedMotionAnimation>();

        private void ScriptError(UUID host, string msg)
        {
            SceneObjectGroup hostObject;

            if (m_scene.TryGetSceneObjectGroup(host, out hostObject))
            {
                m_scene.SimChat(msg, ChatTypeEnum.DebugChannel,
                        hostObject.AbsolutePosition, hostObject.Name, host,
                        false);
            }
            else
            {
                // Since Mantis 6273 is not in core at time of writing, this
                // temporary variable has the same length as the property will
                // so that the line lengths won't have to change if/when it
                // goes in.
                Vector3 m_scene_Center = new Vector3(
                        Constants.RegionSize * 0.5f,
                        Constants.RegionSize * 0.5f,
                        Constants.RegionHeight * 0.5f);
                m_scene.SimChat(msg, ChatTypeEnum.DebugChannel,
                        m_scene_Center, "Unknown", host, false);
            }
        }

        /// <summary>
        /// Animates a non-physical object through a specified list of
        /// keyframes.
        /// </summary>
        /// <remarks>
        /// See http://wiki.secondlife.com/wiki/LlSetKeyframedMotion
        /// </remarks>
        /// <param name="host"></param>
        /// <param name="script"></param>
        /// <param name="keyframes"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        [ScriptInvocation]
        public int llSetKeyframedMotion(UUID host, UUID script,
                object[] keyframes, object[] options)
        {
            SetKeyFramedMotion(host, keyframes, options, true);
            return 0;
        }

        /// <summary>
        /// Animates a non-physical object through a specified list of
        /// keyframes.
        /// </summary>
        /// <remarks>
        /// Similar to <see cref="llSetKeyframedMotion"/> but deviates from
        /// the strict specification:
        /// *   Allows <see cref="KFM_COMMAND"/> to be specified with a 
        ///     keyframes list, allowing the user to specify an animation to
        ///     play later.
        /// *   Allows llSetKeyframedMotion to be specified at the same time
        ///     as other options.
        /// *   Allows <see cref="KFM_MODE"/> to be changed mid-animation.
        /// *   Allows <see cref="KFM_DATA"/> to be changed mid-animation.
        /// </remarks>
        /// <param name="host"></param>
        /// <param name="script"></param>
        /// <param name="keyframes"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        [ScriptInvocation]
        public int osSetKeyframedMotion(UUID host, UUID script,
                object[] keyframes, object[] options)
        {
            SetKeyFramedMotion(host, keyframes, options, false);
            return 0;
        }

        /// <summary>
        /// Specifies a list of animation keyframes to apply to an object when
        /// <see cref="Command.Play">played</see>
        /// </summary>
        /// <param name="keyframes">
        /// A strided list of <see cref="OpenMetaverse.Vector3">vector</see>
        /// (optional via <see cref="Data.Translation">KFM_TRANSLATION</see>
        /// with <see cref="KFM_DATA"/>), <see cref="OpenMetaverse.Quaternion">rotation</see>
        /// (optional via <see cref="Data.Rotation">KFM_ROTATION</see> with
        /// <see cref="KFM_DATA"/>, <see cref="System.Double">float</see>
        /// frame duration.
        /// See <see cref="KeyframedMotionFrame"/>.
        /// </param>
        /// <param name="options"></param>
        /// <param name="followLLSpec"></param>
        private void SetKeyFramedMotion(UUID host, object[] keyframes, object[] options, bool followLLSpec)
        {
            int i = 0;
            int remaining;

            bool commandSpecified = false;
            Command command = Command.Stop;

            bool modeSpecified = false;
            Mode mode = Mode.Forward;

            bool dataSpecified = false;
            Data data = (Data)0;

            while (i < options.Length)
            {
                remaining = options.Length - i;
                if (options[i] is int)
                {
                    switch ((int)options[i])
                    {
                        case KFM_COMMAND:
                            if (remaining < 1)
                            {
                                ScriptError(host, "No command specified.");
                                return;
                            }
                            else if (!(options[i + 1] is int))
                            {
                                ScriptError(host,
                                        "Command not specified as integer.");
                                return;
                            }
                            switch ((int)options[i + 1])
                            {
                                case (int)Command.Play:
                                case (int)Command.Stop:
                                case (int)Command.Pause:
                                    command = (Command)options[i + 1];
                                    commandSpecified = true;
                                    break;
                                default:
                                    ScriptError(host,
                                            "Unsupported command specified.");
                                    return;
                            }
                            i += 2;
                            break;
                        case KFM_MODE:
                            if (remaining < 1)
                            {
                                ScriptError(host, "No mode specified.");
                                return;
                            }
                            else if (!(options[i + 1] is int))
                            {
                                ScriptError(host,
                                        "Mode not specified as integer.");
                                return;
                            }
                            switch ((int)options[i + 1])
                            {
                                case (int)Mode.Forward:
                                case (int)Mode.Loop:
                                case (int)Mode.Ping_Pong:
                                case (int)Mode.Reverse:
                                    mode = (Mode)options[i + 1];
                                    modeSpecified = true;
                                    break;
                                default:
                                    ScriptError(host,
                                            "Unsupported mode specified.");
                                    return;
                            }
                            i += 2;
                            break;
                        case KFM_DATA:
                            if (remaining < 1)
                            {
                                ScriptError(host, "No data specified.");
                                return;
                            }
                            else if (!(options[i + 1] is int))
                            {
                                ScriptError(host,
                                        "Data not specified as integer.");
                                return;
                            }
                            if (((int)options[i + 1] & (int)Data.Rotation) == (int)Data.Rotation)
                            {
                                dataSpecified = true;
                                data |= Data.Rotation;
                            }
                            if (((int)options[i + 1] & (int)Data.Translation) == (int)Data.Translation)
                            {
                                dataSpecified = true;
                                data |= Data.Translation;
                            }
                            if (data == 0)
                            {
                                ScriptError(host, "No valid data specified.");
                                return;
                            }
                            i += 2;
                            break;
                        default:
                            ScriptError(host, string.Format(
                                        "Unsupported option specified at pos {0}",
                                        i));
                            return;
                    }
                }
            }

            if (!m_animations.ContainsKey(host))
            {
                if (keyframes.Length > 0 && !dataSpecified)
                    data = Data.Rotation | Data.Translation;

                if (keyframes.Length > 0 && !modeSpecified)
                    mode = Mode.Forward;
            }

            if (followLLSpec)
            {
                if (commandSpecified)
                {
                    if (keyframes.Length > 0)
                    {
                        ScriptError(host,
                                "Cannot specify a command at the same time as a keyframes list");
                        return;
                    }
                    else if (m_animations.ContainsKey(host))
                    {
                        m_animations[host].Playback(command);
                        return;
                    }
                }

                if (modeSpecified && keyframes.Length <= 0)
                {
                    ScriptError(host,
                            "Playback mode specified when no keyframes specified.");
                    return;
                }
                else if (dataSpecified && keyframes.Length <= 0)
                {
                    ScriptError(host,
                            "Animation data specified when no keyframes specified.");
                    return;
                }
            }
            else if (keyframes.Length <= 0 && m_animations.ContainsKey(host))
            {
                if (dataSpecified)
                    m_animations[host].AnimationData = data;

                if (modeSpecified)
                    m_animations[host].AnimationMode = mode;

                return;
            }

            if (keyframes.Length > 0)
            {
                List<KeyframedMotionFrame> frames = new List<KeyframedMotionFrame>();

                if ((data & (Data.Rotation | Data.Translation)) == (Data.Rotation | Data.Translation))
                {
                    if (keyframes.Length % 3 != 0)
                    {
                        ScriptError(host,
                                "Translation and Rotation specified but keyframes list of invalid length.");
                        return;
                    }

                    for (i = 0; i < keyframes.Length; i += 3)
                    {
                        if (!(keyframes[i] is Vector3))
                        {
                            ScriptError(host, string.Format(
                                    "Frame {0} has an invalid vector offset",
                                    Math.Floor(i / 3.0)));
                            return;
                        }
                        else if (!(keyframes[i + 1] is Quaternion))
                        {
                            ScriptError(host, string.Format(
                                    "Frame {0} has an invalid rotation",
                                    Math.Floor(i / 3.0)));
                            return;
                        }
                        else if (!(keyframes[i + 2] is int) &&
                                !(keyframes[i + 2] is float) &&
                                !(keyframes[i + 2] is double))
                        {
                            ScriptError(host, string.Format(
                                    "Frame {0} has an invalid duration",
                                    Math.Floor(i / 3.0)));
                            return;
                        }
                        float duration;
                        if (keyframes[i + 2] is int)
                            duration = (float)((int)keyframes[i + 2]);
                        else if (keyframes[i + 2] is double)
                            duration = (float)((double)keyframes[i + 2]);
                        else
                            duration = (float)keyframes[i + 2];
                        frames.Add(new KeyframedMotionFrame
                        {
                            Translation = (Vector3)keyframes[i],
                            Rotation = (Quaternion)keyframes[i + 1],
                            Duration = duration
                        });
                    }
                }
                else if ((data & Data.Rotation) == Data.Rotation || (data & Data.Translation) == Data.Translation)
                {
                    if (keyframes.Length % 2 != 0)
                    {
                        if ((data & Data.Rotation) == Data.Rotation)
                        {
                            ScriptError(host,
                                    "Rotation-only animation specified but no keyframes list of invalid length.");
                        }
                        else if ((data & Data.Translation) == Data.Translation)
                        {
                            ScriptError(host,
                                    "Translation-only animation specified but no keyframes list of invalid length.");
                        }
                    }


                    if ((data & Data.Rotation) == Data.Rotation)
                    {
                        for (i = 0; i < keyframes.Length; i += 2)
                        {
                            if (!(keyframes[i] is Quaternion))
                            {
                                ScriptError(host, string.Format(
                                        "Frame {0} has an invalid rotation",
                                        Math.Floor(i / 3.0)));
                                return;
                            }
                            else if (!(keyframes[i + 1] is int) &&
                                    !(keyframes[i + 1] is float) &&
                                    !(keyframes[i + 1] is double))
                            {
                                ScriptError(host, string.Format(
                                        "Frame {0} has an invalid duration",
                                        Math.Floor(i / 3.0)));
                                return;
                            }
                            float duration;
                            if (keyframes[i + 1] is int)
                                duration = (float)((int)keyframes[i + 1]);
                            else if (keyframes[i + 1] is double)
                                duration = (float)((double)keyframes[i + 1]);
                            else
                                duration = (float)keyframes[i + 1];
                            frames.Add(new KeyframedMotionFrame
                            {
                                Rotation = (Quaternion)keyframes[i],
                                Duration = duration
                            });
                        }
                    }
                    else
                    {
                        for (i = 0; i < keyframes.Length; i += 2)
                        {
                            if (!(keyframes[i] is Vector3))
                            {
                                ScriptError(host, string.Format(
                                        "Frame {0} has an invalid offset",
                                        Math.Floor(i / 3.0)));
                                return;
                            }
                            else if (!(keyframes[i + 1] is int) &&
                                    !(keyframes[i + 1] is float) &&
                                    !(keyframes[i + 1] is double))
                            {
                                ScriptError(host, string.Format(
                                        "Frame {0} has an invalid duration",
                                        Math.Floor(i / 3.0)));
                                return;
                            }
                            float duration;
                            if (keyframes[i + 1] is int)
                                duration = (float)((int)keyframes[i + 1]);
                            else if (keyframes[i + 1] is double)
                                duration = (float)((double)keyframes[i + 1]);
                            else
                                duration = (float)keyframes[i + 1];

                            frames.Add(new KeyframedMotionFrame
                            {
                                Translation = (Vector3)keyframes[i],
                                Duration = duration
                            });
                        }
                    }
                }
                else
                {
                    ScriptError(host,
                            "Keyframes list specified with unsupported animation data.");
                }

                SceneObjectGroup hostObject;

                // In the event the object was deleted very quickly.
                if (!m_scene.TryGetSceneObjectGroup(host, out hostObject))
                    ScriptError(host, "Object not found.");

                m_animations[host] = new KeyframedMotionAnimation(
                        hostObject, data, mode, frames.ToArray());

                if (followLLSpec)
                    m_animations[host].Playback(Command.Play);
            }
        }

        #endregion

        #region Event handlers

        private void OnSceneFrame()
        {
            foreach (KeyValuePair<UUID, KeyframedMotionAnimation> kvp in m_animations)
            {
                kvp.Value.Animate();
            }
        }

        private void OnObjectBeingRemovedFromScene(SceneObjectGroup sog)
        {
            m_animations.Remove(sog.UUID);
        }

        #endregion
    }
}
