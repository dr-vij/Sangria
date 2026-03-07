using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace ViJMeshTools
{
    public static partial class MeshAnalizers
    {
        /// <summary>
        /// We currently support meshes with ushort index format and with 4 attributes:
        /// Position, Normal, Tangent and UV (TexCoord0)
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public static bool IsMeshSupportedWithCutter(Mesh mesh)
        {
            var attributes = new List<VertexAttributeDescriptor>();
            mesh.GetVertexAttributes(attributes);
            bool result;

            if (attributes.Any(c => c.attribute == VertexAttribute.Position && c.dimension == 3) &&
                attributes.Any(c => c.attribute == VertexAttribute.Normal && c.dimension == 3) &&
                attributes.Any(c => c.attribute == VertexAttribute.Tangent && c.dimension == 4) &&
                attributes.Any(c => c.attribute == VertexAttribute.TexCoord0 && c.dimension == 2) &&
                attributes.Count == 4)
            {
                result = true;
            }
            else
            {
                Debug.LogError("I support meshes with Position, Normal, Tangent and TexCoord0");
                var debugString = "";
                foreach (var attr in attributes)
                    debugString += $"Attribute: {attr.attribute} + Format: {attr.format}\n";
                Debug.LogError(debugString);
                result = false;
            }
            return result;
        }
    }
}