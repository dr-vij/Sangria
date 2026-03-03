using Unity.Collections;
using Unity.Mathematics;

namespace PropellerheadMesh
{
    public static class NativeSphereGenerator
    {
        /// <summary>
        /// Generates a sphere with a specified subdivision level
        /// </summary>
        /// <param name="detail">The detail object to add the sphere to</param>
        /// <param name="radius">Radius of the sphere</param>
        /// <param name="subdivisionLevel">Subdivision level (0 and higher)</param>
        public static void GenerateSphere(ref NativeDetail detail, float radius, int subdivisionLevel = 2)
        {
            subdivisionLevel = math.max(0, subdivisionLevel);

            // Golden ratio
            var t = (1.0f + math.sqrt(5.0f)) / 2.0f;

            // Initial 12 vertices of icosahedron
            var vertices = new NativeList<float3>(Allocator.Temp);
            vertices.Add(math.normalize(new float3(-1, t, 0)) * radius);
            vertices.Add(math.normalize(new float3(1, t, 0)) * radius);
            vertices.Add(math.normalize(new float3(-1, -t, 0)) * radius);
            vertices.Add(math.normalize(new float3(1, -t, 0)) * radius);
            vertices.Add(math.normalize(new float3(0, -1, t)) * radius);
            vertices.Add(math.normalize(new float3(0, 1, t)) * radius);
            vertices.Add(math.normalize(new float3(0, -1, -t)) * radius);
            vertices.Add(math.normalize(new float3(0, 1, -t)) * radius);
            vertices.Add(math.normalize(new float3(t, 0, -1)) * radius);
            vertices.Add(math.normalize(new float3(t, 0, 1)) * radius);
            vertices.Add(math.normalize(new float3(-t, 0, -1)) * radius);
            vertices.Add(math.normalize(new float3(-t, 0, 1)) * radius);

            // Initial 20 faces - correct winding order for outward-facing normals
            var faces = new NativeList<int3>(Allocator.Temp);
            faces.Add(new int3(0, 11, 5)); faces.Add(new int3(0, 5, 1)); faces.Add(new int3(0, 1, 7)); faces.Add(new int3(0, 7, 10)); faces.Add(new int3(0, 10, 11));
            faces.Add(new int3(1, 5, 9)); faces.Add(new int3(5, 11, 4)); faces.Add(new int3(11, 10, 2)); faces.Add(new int3(10, 7, 6)); faces.Add(new int3(7, 1, 8));
            faces.Add(new int3(3, 9, 4)); faces.Add(new int3(3, 4, 2)); faces.Add(new int3(3, 2, 6)); faces.Add(new int3(3, 6, 8)); faces.Add(new int3(3, 8, 9));
            faces.Add(new int3(4, 9, 5)); faces.Add(new int3(2, 4, 11)); faces.Add(new int3(6, 2, 10)); faces.Add(new int3(8, 6, 7)); faces.Add(new int3(9, 8, 1));

            // Subdivide
            for (var i = 0; i < subdivisionLevel; i++)
            {
                var newFaces = new NativeList<int3>(Allocator.Temp);
                var midpointCache = new NativeHashMap<long, int>(vertices.Length * 2, Allocator.Temp);

                foreach (var face in faces)
                {
                    // Get midpoints of each edge
                    var a = GetMidpointIndex(face.x, face.y, vertices, midpointCache, radius);
                    var b = GetMidpointIndex(face.y, face.z, vertices, midpointCache, radius);
                    var c = GetMidpointIndex(face.z, face.x, vertices, midpointCache, radius);

                    // Create 4 new triangles maintaining winding order
                    // Corner triangles
                    newFaces.Add(new int3(face.x, a, c));  // Corner at face.x
                    newFaces.Add(new int3(face.y, b, a));  // Corner at face.y  
                    newFaces.Add(new int3(face.z, c, b));  // Corner at face.z
                    
                    // Center triangle
                    newFaces.Add(new int3(a, b, c));       // Center triangle
                }

                faces.Dispose();
                faces = newFaces;
                midpointCache.Dispose();
            }

            // Add points to detail
            var pointIndices = new NativeArray<int>(vertices.Length, Allocator.Temp);
            for (var i = 0; i < vertices.Length; i++)
            {
                pointIndices[i] = detail.AddPoint(vertices[i]);
            }

            // Create vertices for each point and add faces to detail
            foreach (var face in faces)
            {
                var vertexIndices = new NativeArray<int>(3, Allocator.Temp);
                
                // Create vertices that reference the points
                vertexIndices[0] = detail.AddVertex(pointIndices[face.x]);
                vertexIndices[1] = detail.AddVertex(pointIndices[face.y]);
                vertexIndices[2] = detail.AddVertex(pointIndices[face.z]);
                
                // Add primitive with vertex indices
                detail.AddPrimitive(vertexIndices);
                
                vertexIndices.Dispose();
            }

            // Cleanup
            vertices.Dispose();
            faces.Dispose();
            pointIndices.Dispose();
        }

        private static int GetMidpointIndex(int p1, int p2, NativeList<float3> vertices, NativeHashMap<long, int> cache, float radius)
        {
            // Create unique key for edge
            var key = ((long)math.min(p1, p2) << 32) | (long)math.max(p1, p2);
            
            if (cache.TryGetValue(key, out var index))
                return index;

            // Calculate midpoint and project to a sphere
            var midpoint = (vertices[p1] + vertices[p2]) * 0.5f;
            midpoint = math.normalize(midpoint) * radius;

            // Add to a vertices list
            vertices.Add(midpoint);
            index = vertices.Length - 1;
            cache[key] = index;

            return index;
        }
    }
}