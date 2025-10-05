using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using System;

namespace ViJMeshTools
{
    /// <summary>
    /// This struct is used inside cut job. I use ID to check instances for quicker search in hashmaps
    /// </summary>
    public struct CutSegment : IEquatable<CutSegment>
    {
        public float3 Start;
        public float3 End;
        public int Id;

        public CutSegment(float3 start, float3 end, int id)
        {
            Id = id;
            Start = start;
            End = end;
        }

        public bool Equals(CutSegment other) => Id == other.Id;

        public override bool Equals(object obj) => obj is CutSegment otherSeg && Id == otherSeg.Id;

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() => $"ID {Id} Start: {Start} End: {End}";
    }
}