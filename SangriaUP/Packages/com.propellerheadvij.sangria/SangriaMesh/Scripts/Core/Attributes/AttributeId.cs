// Core: Global registry that maps attribute names to stable integer ids.
using System;
using System.Collections.Generic;

namespace SangriaMesh
{
    /// <summary>
    /// Global attribute id registry for SangriaMesh.
    /// </summary>
    public static class AttributeID
    {
        private static readonly object s_Lock = new();
        private static readonly Dictionary<string, int> s_NameToId = new();
        private static readonly Dictionary<int, string> s_IdToName = new();
        private static int s_NextId;

        // Common mesh attributes.
        public static readonly int Position = Register("Position");
        public static readonly int Normal = Register("Normal");
        public static readonly int Tangent = Register("Tangent");
        public static readonly int Color = Register("Color");
        public static readonly int UV0 = Register("UV0");
        public static readonly int UV1 = Register("UV1");
        public static readonly int UV2 = Register("UV2");
        public static readonly int UV3 = Register("UV3");
        public static readonly int UV4 = Register("UV4");
        public static readonly int UV5 = Register("UV5");
        public static readonly int UV6 = Register("UV6");
        public static readonly int UV7 = Register("UV7");
        public static readonly int BoneWeights = Register("BoneWeights");
        public static readonly int BlendIndices = Register("BlendIndices");

        public static int Count
        {
            get
            {
                lock (s_Lock)
                    return s_NameToId.Count;
            }
        }

        public static int Register(string name)
        {
            ValidateAttributeName(name);
            name = name.Trim();

            lock (s_Lock)
            {
                if (s_NameToId.TryGetValue(name, out int existingId))
                    return existingId;

                int id = s_NextId++;
                s_NameToId[name] = id;
                s_IdToName[id] = name;
                return id;
            }
        }

        public static string GetName(int id)
        {
            lock (s_Lock)
                return s_IdToName.GetValueOrDefault(id);
        }

        public static int GetId(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return -1;

            lock (s_Lock)
                return s_NameToId.GetValueOrDefault(name.Trim(), -1);
        }

        public static bool IsRegistered(int id)
        {
            lock (s_Lock)
                return s_IdToName.ContainsKey(id);
        }

        public static bool IsRegistered(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            lock (s_Lock)
                return s_NameToId.ContainsKey(name.Trim());
        }

        private static void ValidateAttributeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Attribute name cannot be null, empty, or whitespace.", nameof(name));

            if (name.Length > 64)
                throw new ArgumentException("Attribute name cannot exceed 64 characters.", nameof(name));

            if (name.Contains('\0'))
                throw new ArgumentException("Attribute name cannot contain null characters.", nameof(name));
        }
    }
}
