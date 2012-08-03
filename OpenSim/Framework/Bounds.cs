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
using OpenMetaverse;

namespace OpenSim.Framework
{
    public enum BoundsType
    {
        Sphere = 0,
        Box = 1
    }

    public interface IBounds
    {
        BoundsType BoundingType { get; }

        Vector3 Center { get; set; }

        bool Contains(Vector3 point);
    }

    public class BoundingBox : IBounds
    {
        public BoundsType BoundingType { get { return BoundsType.Box; } }

        public Vector3 Center { get; set; }
        public Vector3 Size { get; set; }

        public Vector3 Min
        {
            get { return Center - (Size / 2.0f); }
        }

        public Vector3 Max
        {
            get { return Center + (Size / 2.0f); }
        }

        public BoundingBox(Vector3 center, Vector3 size)
        {
            Center = center;
            Size = size;
        }

        public bool Contains(Vector3 point)
        {
            Vector3 min = Min;
            Vector3 max = Max;
            return (
                point.X >= Min.X && point.Y >= Min.Y && point.Z >= Min.Z &&
                point.X <= Max.X && point.Y <= Max.Y && point.Z <= Max.Z
            );
        }

        /*
        public bool Contains(Vector3 point, Quaternion translate)
        {
            Vector3 relative = (point - Center) / translate;

            return (
                relative.X >= 0 && relative.Y >= 0 && relative.Z >= 0 &&
                relative.X <= Size.X && relative.Y <= Size.Y && relative.Z <= Size.Z
            );
        }
        */
    }

    public class BoundingSphere : IBounds
    {
        public BoundsType BoundingType { get { return BoundsType.Sphere; } }

        public Vector3 Center { get; set; }
        public float Radius { get; set; }

        public BoundingSphere(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public bool Contains(Vector3 point)
        {
            return Vector3.Distance(Center, point) <= Math.Abs(Radius);
        }
    }
}