using System;
using System.Collections.Generic;
using UnityEngine;

namespace PropellerheadMesh
{
    /// <summary>
    /// Manages attribute IDs with registration and lookup
    /// Features:
    /// - Performance monitoring and metrics
    /// - Comprehensive validation and error handling
    /// </summary>
    public static partial class AttributeID
    {
        private static readonly Dictionary<string, int> s_NameToId = new();
        private static readonly Dictionary<int, string> s_IdToName = new();
        private static int s_NextId;

        // Standard Unity Mesh Attributes
        public static readonly int Position = Register("Position"); // Vertex positions
        public static readonly int Normal = Register("Normal"); // Vertex normals
        public static readonly int Tangent = Register("Tangent"); // Vertex tangents
        public static readonly int Color = Register("Color"); // Vertex colors
        public static readonly int UV0 = Register("UV0"); // Primary texture coordinates
        public static readonly int UV1 = Register("UV1"); // Secondary texture coordinates
        public static readonly int UV2 = Register("UV2"); // Third texture coordinates
        public static readonly int UV3 = Register("UV3"); // Fourth texture coordinates
        public static readonly int UV4 = Register("UV4"); // Fifth texture coordinates
        public static readonly int UV5 = Register("UV5"); // Sixth texture coordinates
        public static readonly int UV6 = Register("UV6"); // Seventh texture coordinates
        public static readonly int UV7 = Register("UV7"); // Eighth texture coordinates
        public static readonly int BoneWeights = Register("BoneWeights"); // Skinned mesh bone weights
        public static readonly int BlendIndices = Register("BlendIndices"); // Skinned mesh bone indices

        /// <summary>
        /// Gets performance metrics for monitoring
        /// </summary>
        public static AttribIDMetrics GetMetrics()
        {
            return new AttribIDMetrics(
                s_NameToId.Count,
                s_NextId
            );
        }

        /// <summary>
        /// Registers a new attribute name and returns its ID
        /// </summary>
        /// <param name="name">The name of the attribute</param>
        /// <returns>The attribute ID</returns>
        /// <exception cref="ArgumentException">Thrown if the name is invalid</exception>
        public static int Register(string name)
        {
            ValidateAttributeName(name);

            name = name.Trim();

            // Try to get existing ID first
            if (s_NameToId.TryGetValue(name, out int existingId))
                return existingId;

            // Generate new ID
            int newId = s_NextId++;

            // Register the mapping
            s_NameToId[name] = newId;
            s_IdToName[newId] = name;

            // Debug.Log($"Registered attribute: {name} -> ID {newId}");

            return newId;
        }

        private static void ValidateAttributeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Attribute name cannot be null, empty, or whitespace", nameof(name));

            if (name.Length > 32)
                throw new ArgumentException("Attribute name cannot exceed 64 characters", nameof(name));

            if (name.Contains('\0'))
                throw new ArgumentException("Attribute name cannot contain null characters", nameof(name));
        }

        /// <summary>
        /// Gets the name associated with an attribute ID
        /// </summary>
        /// <param name="id">The attribute ID</param>
        /// <returns>The attribute name, or null if not found</returns>
        public static string GetName(int id)
        {
            return s_IdToName.GetValueOrDefault(id);
        }

        /// <summary>
        /// Gets the ID associated with an attribute name
        /// </summary>
        /// <param name="name">The attribute name</param>
        /// <returns>The attribute ID, or -1 if not found</returns>
        public static int GetId(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return -1;
            return s_NameToId.GetValueOrDefault(name.Trim(), -1);
        }

        /// <summary>
        /// Checks if an attribute ID is registered
        /// </summary>
        /// <param name="id">The attribute ID to check</param>
        /// <returns>True if the ID is registered, false otherwise</returns>
        public static bool IsRegistered(int id) => s_IdToName.ContainsKey(id);

        /// <summary>
        /// Checks if an attribute name is registered
        /// </summary>
        /// <param name="name">The attribute name to check</param>
        /// <returns>True if the name is registered, false otherwise</returns>
        public static bool IsRegistered(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && s_NameToId.ContainsKey(name.Trim());
        }

        /// <summary>
        /// Gets all registered attribute names
        /// </summary>
        /// <returns>A collection of all registered attribute names</returns>
        public static IEnumerable<string> GetAllNames() => s_NameToId.Keys;

        /// <summary>
        /// Gets all registered attribute IDs
        /// </summary>
        /// <returns>A collection of all registered attribute IDs</returns>
        public static IEnumerable<int> GetAllIds()
        {
            return s_IdToName.Keys;
        }

        /// <summary>
        /// Gets the total number of registered attributes
        /// </summary>
        public static int Count => s_NameToId.Count;

        /// <summary>
        /// Validates the internal consistency of the attribute registry
        /// </summary>
        /// <returns>True if the internal state is consistent</returns>
        public static bool ValidateIntegrity()
        {
            // Check that both dictionaries have the same count
            if (s_NameToId.Count != s_IdToName.Count)
                return false;

            // Check bidirectional mapping consistency
            foreach (var kvp in s_NameToId)
            {
                if (!s_IdToName.TryGetValue(kvp.Value, out string name) || name != kvp.Key)
                    return false;
            }

            foreach (var kvp in s_IdToName)
            {
                if (!s_NameToId.TryGetValue(kvp.Value, out int id) || id != kvp.Key)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Clears all registered attributes (for testing purposes)
        /// </summary>
        internal static void ClearForTesting()
        {
            s_NameToId.Clear();
            s_IdToName.Clear();
            s_NextId = 0;
        }
    }

    /// <summary>
    /// Performance metrics for AttribID monitoring
    /// </summary>
    public readonly struct AttribIDMetrics
    {
        public int RegisteredCount { get; }
        public int NextId { get; }

        public AttribIDMetrics(int registeredCount, int nextId)
        {
            RegisteredCount = registeredCount;
            NextId = nextId;
        }

        public override string ToString()
        {
            return $"Registered: {RegisteredCount},  NextID: {NextId}";
        }
    }
}