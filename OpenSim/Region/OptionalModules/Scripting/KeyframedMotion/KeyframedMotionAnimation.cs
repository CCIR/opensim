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

using OpenMetaverse;

using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.OptionalModules.Scripting.KeyframedMotion
{
    class KeyframedMotionAnimation
    {
        /// <summary>
        /// The object being animated.
        /// </summary>
        private SceneObjectGroup m_object;

        /// <summary>
        /// The position to return to when the animation is
        /// <see cref="Command.Stop">stopped</see>.
        /// </summary>
        private Vector3 m_startPos;

        /// <summary>
        /// The rotation to return to when the animation is
        /// <see cref="Command.Stop">stopped</see>.
        /// </summary>
        private Quaternion m_startRot;

        /// <summary>
        /// Specifies the type of <see cref="Data"/> to process in the
        /// animation frames.
        /// </summary>
        public Data AnimationData { get; set; }

        /// <summary>
        /// Specifies what type of animation to perform with the keyframe list.
        /// </summary>
        public Mode AnimationMode { get; set; }

        /// <summary>
        /// True when activate, False when
        /// <see cref="Command.Pause">paused</see> or
        /// <see cref="Command.Stop">stopped</see>.
        /// </summary>
        private bool m_enabled;

        /// <summary>
        /// Specifies whether the <see cref="KeyframedMotionModule"/> should
        /// process the animation.
        /// </summary>
        public bool Enabled { get { return m_enabled; } }

        /// <summary>
        /// Animation frames.
        /// </summary>
        private KeyframedMotionFrame[] m_frames;

        /// <summary>
        /// Indicates which is the current frame.
        /// </summary>
        private int m_currentFrame = 0;

        /// <summary>
        /// Should toggle between 1 and -1 to enable forward, reverse and
        /// ping-pong.
        /// </summary>
        private int m_advanceByFrames = 1;

        /// <summary>
        /// Translations add up rather than being relative to the start
        /// position.
        /// </summary>
        private Vector3 m_referenceTranslation;

        /// <summary>
        /// Rotations add up rather than being relative to the start rotation.
        /// </summary>
        private Quaternion m_referenceRotation;

        public KeyframedMotionAnimation(SceneObjectGroup sog, Data data, Mode mode, KeyframedMotionFrame[] frames)
        {
            if (frames.Length < 1)
                throw new ArgumentException("At least one animation frame should be specified.", "frames");

            AnimationData = data;
            AnimationMode = mode;

            m_frames = frames;
            m_object = sog;
        }

        /// <summary>
        /// Previous command used.
        /// </summary>
        private Command m_previousCommand = Command.Stop;

        private DateTime m_frameStarted;

        public void Playback(Command command)
        {
            switch (command)
            {
                case Command.Play:
                    if (!m_enabled && m_previousCommand == Command.Stop)
                    {
                        m_startPos = m_object.AbsolutePosition;
                        m_startRot = m_object.GroupRotation;
                        m_referenceTranslation = Vector3.Zero;
                        m_referenceRotation = Quaternion.Identity;
                        m_frameStarted = DateTime.Now;
                    }
                    m_enabled = true;
                    break;
                case Command.Pause:
                    m_enabled = false;
                    break;
                case Command.Stop:
                    m_enabled = false;
                    if (AnimationMode == Mode.Reverse)
                    {
                        m_advanceByFrames = -1;
                        m_currentFrame = m_frames.Length - 1;
                        m_object.UpdateGroupRotationPR(m_startPos, m_startRot);
                    }
                    break;
            }

            m_previousCommand = command;
        }

        public KeyframedMotionFrame Current()
        {
            KeyframedMotionFrame currentFrame = m_frames[m_currentFrame];

            TimeSpan sinceLastFrame = DateTime.Now - m_frameStarted;

            if ((float)sinceLastFrame.TotalSeconds >= currentFrame.Duration)
            {
                m_referenceTranslation += currentFrame.Translation;
                m_referenceRotation += currentFrame.Rotation;

                m_currentFrame += m_advanceByFrames;

                if (m_currentFrame >= m_frames.Length)
                {
                    switch (AnimationMode)
                    {
                        case Mode.Forward:
                            Playback(Command.Stop);
                            break;
                        case Mode.Loop:
                            m_referenceTranslation = Vector3.Zero;
                            m_referenceRotation = Quaternion.Identity;
                            m_currentFrame = 0;
                            m_advanceByFrames = 1;
                            break;
                        case Mode.Ping_Pong:
                            m_currentFrame = m_frames.Length - 1;
                            m_advanceByFrames = -1;
                            break;
                        case Mode.Reverse:
                            m_referenceTranslation = Vector3.Zero;
                            m_referenceRotation = Quaternion.Identity;
                            m_currentFrame = m_frames.Length - 1;
                            m_advanceByFrames = -1;
                            break;
                    }
                }
                else if (m_currentFrame <= 0)
                {
                    switch (AnimationMode)
                    {
                        case Mode.Forward:
                        case Mode.Loop:
                        case Mode.Ping_Pong:
                            m_referenceTranslation = Vector3.Zero;
                            m_referenceRotation = Quaternion.Identity;
                            m_currentFrame = 0;
                            m_advanceByFrames = 1;
                            break;
                        case Mode.Reverse:
                            Playback(Command.Stop);
                            break;
                    }
                }
                currentFrame = m_frames[m_currentFrame];
                m_frameStarted = DateTime.Now;
            }

            return currentFrame;
        }

        public void Animate()
        {
            // Stop animations on attachments or physical objects.
            if (m_object.IsAttachment || m_object.UsesPhysics)
                Playback(Command.Stop);

            // exit early if disabled (stopped or paused)
            if (!Enabled)
                return;

            KeyframedMotionFrame currentFrame = Current();

            // we do Math.Min so that if it's ran over time then we don't translate too far.
            float delta = (float)Math.Min(currentFrame.Duration,
                    (DateTime.Now - m_frameStarted).TotalSeconds) /
                    currentFrame.Duration;

            if ((AnimationData & Data.Translation) == Data.Translation)
                m_object.AbsolutePosition = m_startPos + m_referenceTranslation + (currentFrame.Translation * delta);

            if ((AnimationData & Data.Rotation) == Data.Rotation)
                m_object.UpdateGroupRotationR(m_startRot + m_referenceRotation + (currentFrame.Rotation * delta));

            m_object.SendGroupRootTerseUpdate();
        }
    }

    public struct KeyframedMotionFrame
    {
        public Quaternion Rotation { get; set; }

        public Vector3 Translation { get; set; }

        public float Duration { get; set; }
    }
}
