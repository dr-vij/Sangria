using Unity.Collections;
using Unity.Mathematics;

namespace PropellerheadMesh
{
    public static class NativeCubeGenerator
    {
        public static void GenerateCube(ref NativeDetail detail, float3 size, bool calcNormals = false)
        {
            detail.Clear();
            
            var half = size * 0.5f;

            // Create 8 vertices of a cube
            var cubeVertices = new NativeArray<float3>(8, Allocator.Temp)
            {
                [0] = new(-half.x, -half.y, -half.z), // 0: bottom-left-back
                [1] = new(half.x, -half.y, -half.z), // 1: bottom-right-back
                [2] = new(half.x, half.y, -half.z), // 2: top-right-back
                [3] = new(-half.x, half.y, -half.z), // 3: top-left-back
                [4] = new(-half.x, -half.y, half.z), // 4: bottom-left-front
                [5] = new(half.x, -half.y, half.z), // 5: bottom-right-front
                [6] = new(half.x, half.y, half.z), // 6: top-right-front
                [7] = new(-half.x, half.y, half.z) // 7: top-left-front
            };

            // Add points to detail
            var pointIndices = new NativeArray<int>(8, Allocator.Temp);
            for (var i = 0; i < cubeVertices.Length; i++)
                pointIndices[i] = detail.AddPoint(cubeVertices[i]);

            // Create 6 faces (quads) of the cube - CW winding (clockwise)
            var cubeFaces = new NativeArray<NativeArray<int>>(6, Allocator.Temp)
            {
                [0] = new NativeArray<int>(new[] { 0, 3, 2, 1 }, Allocator.Temp), // Back face
                [1] = new NativeArray<int>(new[] { 4, 5, 6, 7 }, Allocator.Temp), // Front face
                [2] = new NativeArray<int>(new[] { 0, 1, 5, 4 }, Allocator.Temp), // Bottom face
                [3] = new NativeArray<int>(new[] { 3, 7, 6, 2 }, Allocator.Temp), // Top face
                [4] = new NativeArray<int>(new[] { 0, 4, 7, 3 }, Allocator.Temp), // Left face
                [5] = new NativeArray<int>(new[] { 1, 2, 6, 5 }, Allocator.Temp)  // Right face
            };

            // Face normals for each face
            var faceNormals = new NativeArray<float3>(6, Allocator.Temp)
            {
                [0] = new(0, 0, -1), // Back face
                [1] = new(0, 0, 1), // Front face
                [2] = new(0, -1, 0), // Bottom face
                [3] = new(0, 1, 0), // Top face
                [4] = new(-1, 0, 0), // Left face
                [5] = new(1, 0, 0) // Right face
            };

            // Add primitives (faces) to detail - create separate vertices for each face
            for (var faceIndex = 0; faceIndex < cubeFaces.Length; faceIndex++)
            {
                var face = cubeFaces[faceIndex];
                var faceVertexIndices = new NativeArray<int>(face.Length, Allocator.Temp);

                // Create a unique vertex for each point on this face
                for (var i = 0; i < face.Length; i++)
                {
                    var pointIndex = pointIndices[face[i]];
                    var vertexIndex = detail.AddVertex(pointIndex);
                    faceVertexIndices[i] = vertexIndex;

                    // Add a normal attribute to vertex if requested
                    if (calcNormals)
                    {
                        if (!detail.HasVertexAttribute(AttributeID.Normal))
                            detail.AddVertexAttribute<float3>(AttributeID.Normal);
                        detail.SetVertexAttribute(vertexIndex, AttributeID.Normal, faceNormals[faceIndex]);
                    }
                }

                // Add the primitive
                var primitiveIndex = detail.AddPrimitive(faceVertexIndices);

                // Add normal attribute to primitive if requested
                if (calcNormals)
                {
                    if (!detail.HasPrimitiveAttribute(AttributeID.Normal))
                        detail.AddPrimitiveAttribute<float3>(AttributeID.Normal);

                    detail.SetPrimitiveAttribute(primitiveIndex, AttributeID.Normal, faceNormals[faceIndex]);
                }

                faceVertexIndices.Dispose();
            }

            // Dispose of native arrays
            cubeVertices.Dispose();
            pointIndices.Dispose();
            faceNormals.Dispose();

            for (var i = 0; i < cubeFaces.Length; i++)
                cubeFaces[i].Dispose();
            cubeFaces.Dispose();
        }
    }
}