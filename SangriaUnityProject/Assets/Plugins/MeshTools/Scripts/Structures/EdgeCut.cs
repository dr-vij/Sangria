using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;

namespace ViJMeshTools
{
    public struct Edge : IEquatable<Edge>
    {
        public float3 A;
        public float3 B;

        public Edge(float3 a, float3 b)
        {
            A = a;
            B = b;
        }

        public static bool operator ==(Edge lhs, Edge rhs) => lhs.Equals(rhs);

        public static bool operator !=(Edge lsh, Edge rhs) => !lsh.Equals(rhs);

        public bool Equals(Edge other) => A.Equals(other.A) && B.Equals(other.B) || A.Equals(other.B) && B.Equals(other.A);

        public override bool Equals(object obj) => obj is Edge edge && Equals(edge);

        public override int GetHashCode() => A.GetHashCode() | B.GetHashCode();
    }
}