
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace PropellerheadMesh
{
    /// <summary>
    /// Ultra-fast direct access normals calculation job
    /// </summary>
    [BurstCompile]
    public struct FastNormalsCalculationJob : IJob
    {
        [ReadOnly] public NativeArray<int> ValidPrimitives;
        [ReadOnly] public NativeArray<int> ValidVertices;
        [ReadOnly] public float SmoothAngleCos; // Precomputed cosine for faster comparison

        // Direct unsafe accessors to NativeDetail data
        [ReadOnly] [NativeDisableUnsafePtrRestriction]
        public unsafe void* DetailPtr;

        [ReadOnly] [NativeDisableUnsafePtrRestriction]
        public NativeAttributeAccessor<float3> PositionAccessor;

        [NativeDisableUnsafePtrRestriction] public NativeAttributeAccessor<float3> VertexNormalAccessor;

        // Pre-allocated working arrays
        [NativeDisableParallelForRestriction] public NativeArray<float3> FaceNormals;
        [NativeDisableParallelForRestriction] public NativeArray<float> FaceAreas;

        // Point-to-vertices mapping
        [NativeDisableParallelForRestriction] public NativeArray<int> PointVertexData;
        [NativeDisableParallelForRestriction] public NativeArray<int> PointVertexOffsets;
        [NativeDisableParallelForRestriction] public NativeArray<int> PointVertexCounts;

        public unsafe void Execute()
        {
            var detail = (NativeDetail*)DetailPtr;

            // Step 1: Calculate face normals
            CalculateFaceNormals(detail);

            // Step 2: Build point-to-vertices mapping
            BuildPointVertexMapping(detail);

            // Step 3: Calculate vertex normals using point groups
            CalculateVertexNormalsFromPointGroups(detail);
        }

        private unsafe void CalculateFaceNormals(NativeDetail* detail)
        {
            for (int i = 0; i < ValidPrimitives.Length; i++)
            {
                var primIndex = ValidPrimitives[i];
                var vertexSlice = detail->GetPrimitiveVertices(primIndex);

                if (vertexSlice.Length < 3)
                {
                    FaceNormals[primIndex] = float3.zero;
                    FaceAreas[primIndex] = 0f;
                    continue;
                }

                // Get the first three vertices for normal calculation
                var v0 = vertexSlice[0];
                var v1 = vertexSlice[1];
                var v2 = vertexSlice[2];

                // Get positions
                var p0 = detail->GetVertexPoint(v0);
                var p1 = detail->GetVertexPoint(v1);
                var p2 = detail->GetVertexPoint(v2);

                var pos0 = PositionAccessor[p0];
                var pos1 = PositionAccessor[p1];
                var pos2 = PositionAccessor[p2];

                // Calculate face normal and area
                var edge1 = pos1 - pos0;
                var edge2 = pos2 - pos0;
                var cross = math.cross(edge1, edge2);
                var crossLengthSq = math.lengthsq(cross);

                if (crossLengthSq < 1e-12f)
                {
                    FaceNormals[primIndex] = float3.zero;
                    FaceAreas[primIndex] = 0f;
                    continue;
                }

                var crossLength = math.sqrt(crossLengthSq);
                var area = crossLength * 0.5f;
                var normal = cross / crossLength;

                FaceNormals[primIndex] = normal;
                FaceAreas[primIndex] = area;
            }
        }

        private unsafe void BuildPointVertexMapping(NativeDetail* detail)
        {
            // Clear counts
            UnsafeUtility.MemClear(PointVertexCounts.GetUnsafePtr(), PointVertexCounts.Length * sizeof(int));

            // First pass: count vertices per point
            for (int i = 0; i < ValidVertices.Length; i++)
            {
                var vertexIndex = ValidVertices[i];
                var pointIndex = detail->GetVertexPoint(vertexIndex);
                
                if (pointIndex >= 0 && pointIndex < PointVertexCounts.Length)
                {
                    PointVertexCounts[pointIndex]++;
                }
            }

            // Calculate offsets
            int currentOffset = 0;
            for (int i = 0; i < PointVertexOffsets.Length; i++)
            {
                PointVertexOffsets[i] = currentOffset;
                currentOffset += PointVertexCounts[i];
                PointVertexCounts[i] = 0; // Reset for second pass
            }

            // Second pass: fill vertex data
            for (int i = 0; i < ValidVertices.Length; i++)
            {
                var vertexIndex = ValidVertices[i];
                var pointIndex = detail->GetVertexPoint(vertexIndex);
                
                if (pointIndex >= 0 && pointIndex < PointVertexOffsets.Length)
                {
                    var offset = PointVertexOffsets[pointIndex];
                    var count = PointVertexCounts[pointIndex];

                    if (offset + count < PointVertexData.Length)
                    {
                        PointVertexData[offset + count] = vertexIndex;
                        PointVertexCounts[pointIndex]++;
                    }
                }
            }
        }

        private unsafe void CalculateVertexNormalsFromPointGroups(NativeDetail* detail)
        {
            var defaultNormal = new float3(0, 1, 0);

            // Process each point and its vertices
            for (int pointIndex = 0; pointIndex < PointVertexOffsets.Length; pointIndex++)
            {
                var offset = PointVertexOffsets[pointIndex];
                var count = PointVertexCounts[pointIndex];

                if (count == 0)
                    continue;

                if (count == 1)
                {
                    // Single vertex - use face normal
                    var vertexIndex = PointVertexData[offset];
                    var primitiveIndex = FindVertexOwningPrimitive(detail, vertexIndex);
                    
                    if (primitiveIndex >= 0)
                    {
                        var faceNormal = FaceNormals[primitiveIndex];
                        VertexNormalAccessor[vertexIndex] = math.lengthsq(faceNormal) > 1e-12f ? faceNormal : defaultNormal;
                    }
                    else
                    {
                        VertexNormalAccessor[vertexIndex] = defaultNormal;
                    }
                    continue;
                }

                // Multiple vertices - calculate normals for this point group
                CalculatePointGroupNormals(detail, offset, count, pointIndex, defaultNormal);
            }
        }

        private unsafe void CalculatePointGroupNormals(NativeDetail* detail, int offset, int count, int pointIndex, float3 defaultNormal)
        {
            // Collect all face normals for vertices at this point
            var vertexPrimitives = new NativeList<VertexPrimitiveData>(count, Allocator.Temp);
            
            for (int i = 0; i < count; i++)
            {
                var vertexIndex = PointVertexData[offset + i];
                var primitiveIndex = FindVertexOwningPrimitive(detail, vertexIndex);
                
                if (primitiveIndex >= 0)
                {
                    var faceNormal = FaceNormals[primitiveIndex];
                    var faceArea = FaceAreas[primitiveIndex];
                    
                    if (math.lengthsq(faceNormal) > 1e-12f && faceArea > 0f)
                    {
                        vertexPrimitives.Add(new VertexPrimitiveData
                        {
                            VertexIndex = vertexIndex,
                            PrimitiveIndex = primitiveIndex,
                            Normal = faceNormal,
                            Area = faceArea
                        });
                    }
                }
            }

            if (vertexPrimitives.Length == 0)
            {
                // No valid primitives - use default normal for all vertices
                for (int i = 0; i < count; i++)
                {
                    var vertexIndex = PointVertexData[offset + i];
                    VertexNormalAccessor[vertexIndex] = defaultNormal;
                }
                vertexPrimitives.Dispose();
                return;
            }

            if (vertexPrimitives.Length == 1)
            {
                // Single primitive - all vertices get face normal
                var faceNormal = vertexPrimitives[0].Normal;
                for (int i = 0; i < count; i++)
                {
                    var vertexIndex = PointVertexData[offset + i];
                    VertexNormalAccessor[vertexIndex] = faceNormal;
                }
                vertexPrimitives.Dispose();
                return;
            }

            // Multiple primitives - check smoothing groups
            ProcessSmoothingGroups(vertexPrimitives, defaultNormal);
            vertexPrimitives.Dispose();
        }

        private void ProcessSmoothingGroups(NativeList<VertexPrimitiveData> vertexPrimitives, float3 defaultNormal)
        {
            var processed = new NativeArray<bool>(vertexPrimitives.Length, Allocator.Temp);
            
            for (int i = 0; i < vertexPrimitives.Length; i++)
            {
                if (processed[i])
                    continue;

                var currentData = vertexPrimitives[i];
                var smoothGroup = new NativeList<VertexPrimitiveData>(vertexPrimitives.Length, Allocator.Temp);
                smoothGroup.Add(currentData);
                processed[i] = true;

                // Find all primitives that can be smoothed with current one
                for (int j = i + 1; j < vertexPrimitives.Length; j++)
                {
                    if (processed[j])
                        continue;

                    var otherData = vertexPrimitives[j];
                    var dot = math.dot(currentData.Normal, otherData.Normal);

                    if (dot >= SmoothAngleCos) // Can smooth
                    {
                        smoothGroup.Add(otherData);
                        processed[j] = true;
                    }
                }

                // Calculate smoothed normal for this group
                var smoothedNormal = CalculateGroupSmoothedNormal(smoothGroup, defaultNormal);

                // Apply smoothed normal to all vertices in group
                for (int j = 0; j < smoothGroup.Length; j++)
                {
                    var vertexIndex = smoothGroup[j].VertexIndex;
                    VertexNormalAccessor[vertexIndex] = smoothedNormal;
                }

                smoothGroup.Dispose();
            }

            processed.Dispose();
        }

        private float3 CalculateGroupSmoothedNormal(NativeList<VertexPrimitiveData> smoothGroup, float3 defaultNormal)
        {
            if (smoothGroup.Length == 0)
                return defaultNormal;

            if (smoothGroup.Length == 1)
                return smoothGroup[0].Normal;

            // Calculate area-weighted average
            var smoothNormal = float3.zero;
            var totalWeight = 0f;

            for (int i = 0; i < smoothGroup.Length; i++)
            {
                var data = smoothGroup[i];
                smoothNormal += data.Normal * data.Area;
                totalWeight += data.Area;
            }

            if (totalWeight > 1e-12f)
            {
                smoothNormal /= totalWeight;
                var normalLengthSq = math.lengthsq(smoothNormal);
                
                if (normalLengthSq > 1e-12f)
                {
                    return smoothNormal / math.sqrt(normalLengthSq);
                }
            }

            return smoothGroup[0].Normal;
        }

        private unsafe int FindVertexOwningPrimitive(NativeDetail* detail, int vertexIndex)
        {
            for (int i = 0; i < ValidPrimitives.Length; i++)
            {
                var primIndex = ValidPrimitives[i];
                var vertexSlice = detail->GetPrimitiveVertices(primIndex);

                for (int j = 0; j < vertexSlice.Length; j++)
                {
                    if (vertexSlice[j] == vertexIndex)
                    {
                        return primIndex;
                    }
                }
            }
            return -1;
        }

        private struct VertexPrimitiveData
        {
            public int VertexIndex;
            public int PrimitiveIndex;
            public float3 Normal;
            public float Area;
        }
    }

    /// <summary>
    /// Optimized native normals calculation operator
    /// </summary>
    public static class NativeNormalsOperators
    {
        /// <summary>
        /// Ultra-fast normals calculation using direct memory access
        /// </summary>
        public static unsafe JobHandle CalculateNormals(
            ref NativeDetail detail,
            float smoothAngle = math.PI / 3f,
            JobHandle dependency = default)
        {
            // Validate input
            if (detail.PrimitiveCount == 0 || detail.VertexCount == 0)
                return dependency;

            // Ensure normal attributes exist
            if (!detail.HasVertexAttribute(AttributeID.Normal))
                detail.AddVertexAttribute<float3>(AttributeID.Normal);

            // Get required accessors
            if (detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor) !=
                AttributeMapResult.Success)
                return dependency;

            if (detail.GetVertexAttributeAccessor<float3>(AttributeID.Normal, out var vertexNormalAccessor) !=
                AttributeMapResult.Success)
                return dependency;

            // Get valid elements
            var validPrimitives = CollectValidElements(detail, true);
            var validVertices = CollectValidElements(detail, false);

            if (validPrimitives.Length == 0)
            {
                validPrimitives.Dispose();
                validVertices.Dispose();
                return dependency;
            }

            // Estimate adjacency data size
            var maxVerticesPerPoint = 6; // Conservative estimate
            var maxAdjacencySize = math.max(validVertices.Length * maxVerticesPerPoint, 1024);

            // Create working arrays
            var faceNormals = new NativeArray<float3>(detail.PrimitiveCount, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var faceAreas = new NativeArray<float>(detail.PrimitiveCount, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var pointVertexData = new NativeArray<int>(maxAdjacencySize, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var pointVertexOffsets = new NativeArray<int>(detail.PointCount, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var pointVertexCounts = new NativeArray<int>(detail.PointCount, Allocator.TempJob,
                NativeArrayOptions.ClearMemory);

            var detailPtr = UnsafeUtility.AddressOf(ref detail);
            var smoothAngleCos = math.cos(smoothAngle);

            var job = new FastNormalsCalculationJob
            {
                ValidPrimitives = validPrimitives,
                ValidVertices = validVertices,
                SmoothAngleCos = smoothAngleCos,
                DetailPtr = detailPtr,
                PositionAccessor = positionAccessor,
                VertexNormalAccessor = vertexNormalAccessor,
                FaceNormals = faceNormals,
                FaceAreas = faceAreas,
                PointVertexData = pointVertexData,
                PointVertexOffsets = pointVertexOffsets,
                PointVertexCounts = pointVertexCounts
            };

            var handle = job.Schedule(dependency);
            handle.Complete();

            // Cleanup
            validPrimitives.Dispose();
            validVertices.Dispose();
            faceNormals.Dispose();
            faceAreas.Dispose();
            pointVertexData.Dispose();
            pointVertexOffsets.Dispose();
            pointVertexCounts.Dispose();

            return handle;
        }

        private static unsafe NativeArray<int> CollectValidElements(NativeDetail detail, bool collectPrimitives)
        {
            var count = collectPrimitives ? detail.PrimitiveCount : detail.VertexCount;
            var tempList = new NativeList<int>(count, Allocator.TempJob);

            for (int i = 0; i < count; i++)
            {
                bool isValid = collectPrimitives ? detail.IsPrimitiveValid(i) : detail.IsVertexValid(i);
                if (isValid)
                {
                    tempList.Add(i);
                }
            }

            var result = new NativeArray<int>(tempList.Length, Allocator.TempJob);
            UnsafeUtility.MemCpy(result.GetUnsafePtr(), tempList.GetUnsafePtr(),
                tempList.Length * sizeof(int));

            tempList.Dispose();
            return result;
        }

        public static float DegreesToRadians(float degrees)
        {
            return degrees * math.PI / 180f;
        }

        public static float3 GetVertexNormal(ref NativeDetail detail, int vertexIndex)
        {
            if (detail.HasVertexAttribute(AttributeID.Normal))
            {
                return detail.GetVertexAttribute<float3>(vertexIndex, AttributeID.Normal);
            }
            return new float3(0, 1, 0);
        }
    }
}