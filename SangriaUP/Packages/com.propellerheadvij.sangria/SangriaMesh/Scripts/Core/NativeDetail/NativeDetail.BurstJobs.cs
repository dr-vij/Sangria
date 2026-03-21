// Core: Burst jobs used by NativeDetail for remap, packing, and adjacency build stages.
using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace SangriaMesh
{
    public partial struct NativeDetail : IDisposable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RemapPointIndex(int sparsePoint, bool pointsIdentityRemap, int pointCount, NativeArray<int> pointRemap)
        {
            if (pointsIdentityRemap)
            {
                if ((uint)sparsePoint < (uint)pointCount)
                    return sparsePoint;

                return -1;
            }

            if ((uint)sparsePoint >= (uint)pointRemap.Length)
                return -1;

            int remappedPoint = pointRemap[sparsePoint];
            return remappedPoint != 0 ? remappedPoint - 1 : -1;
        }

        private unsafe struct AttributePackPlan
        {
            public int AttributeId;
            public int TypeHash;
            public int Stride;
            public int Count;
            public int OffsetBytes;

            [NativeDisableUnsafePtrRestriction] public byte* SourcePtr;
        }

        [BurstCompile]
        private struct DenseTriangleOffsetsJob : IJobParallelFor
        {
            public NativeArray<int> PrimitiveOffsets;

            public void Execute(int index)
            {
                PrimitiveOffsets[index] = index * 3;
            }
        }

        [BurstCompile]
        private struct BuildPointRemapJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> AlivePoints;
            [NativeDisableParallelForRestriction] public NativeArray<int> PointRemap;

            public void Execute(int index)
            {
                PointRemap[AlivePoints[index]] = index + 1;
            }
        }

        [BurstCompile]
        private struct BuildVertexRemapDenseJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> VertexToPointSparse;
            [ReadOnly] public NativeArray<int> PointRemap;

            public bool PointsIdentityRemap;
            public int PointCount;

            public NativeArray<int> VertexRemap;
            public NativeArray<int> VertexToPointDense;

            public void Execute(int sparseVertex)
            {
                VertexRemap[sparseVertex] = sparseVertex + 1;
                VertexToPointDense[sparseVertex] = RemapPointIndex(VertexToPointSparse[sparseVertex], PointsIdentityRemap, PointCount, PointRemap);
            }
        }

        [BurstCompile]
        private struct BuildVertexRemapSparseJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> AliveVertices;
            [ReadOnly] public NativeArray<int> VertexToPointSparse;
            [ReadOnly] public NativeArray<int> PointRemap;

            public bool PointsIdentityRemap;
            public int PointCount;

            [NativeDisableParallelForRestriction] public NativeArray<int> VertexRemap;
            public NativeArray<int> VertexToPointDense;

            public void Execute(int denseVertex)
            {
                int sparseVertex = AliveVertices[denseVertex];
                VertexRemap[sparseVertex] = denseVertex + 1;
                VertexToPointDense[denseVertex] = RemapPointIndex(VertexToPointSparse[sparseVertex], PointsIdentityRemap, PointCount, PointRemap);
            }
        }

        [BurstCompile]
        private struct CountSparsePrimitivesJob : IJob
        {
            [ReadOnly] public NativeArray<PrimitiveRecord> PrimitiveRecords;
            [ReadOnly] public NativeArray<int> PrimitiveData;
            [ReadOnly] public NativeArray<int> VertexRemap;
            [ReadOnly] public NativeArray<int> AlivePrimitives;

            public bool DenseTrianglePrimitives;
            public bool PrimitivesDenseContiguous;
            public int PrimitiveCount;

            public NativeArray<int> TotalPrimitiveVertexCountOut;
            public NativeArray<int> TriangleOnlyOut;

            public void Execute()
            {
                int totalPrimitiveVertexCount = 0;
                int triangleOnly = 1;
                int vertexRemapLength = VertexRemap.Length;

                if (DenseTrianglePrimitives)
                {
                    for (int primitiveIndex = 0; primitiveIndex < PrimitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        int validCount = 0;

                        int v0 = PrimitiveData[triStart];
                        int v1 = PrimitiveData[triStart + 1];
                        int v2 = PrimitiveData[triStart + 2];

                        if ((uint)v0 < (uint)vertexRemapLength && VertexRemap[v0] != 0)
                            validCount++;
                        if ((uint)v1 < (uint)vertexRemapLength && VertexRemap[v1] != 0)
                            validCount++;
                        if ((uint)v2 < (uint)vertexRemapLength && VertexRemap[v2] != 0)
                            validCount++;

                        totalPrimitiveVertexCount += validCount;
                        if (validCount != 3)
                            triangleOnly = 0;
                    }
                }
                else if (PrimitivesDenseContiguous)
                {
                    for (int sparsePrimitive = 0; sparsePrimitive < PrimitiveCount; sparsePrimitive++)
                    {
                        PrimitiveRecord record = PrimitiveRecords[sparsePrimitive];
                        int validCount = 0;
                        int dataCursor = record.Start;

                        for (int k = 0; k < record.Length; k++)
                        {
                            int sparseVertex = PrimitiveData[dataCursor + k];
                            if ((uint)sparseVertex < (uint)vertexRemapLength && VertexRemap[sparseVertex] != 0)
                                validCount++;
                        }

                        totalPrimitiveVertexCount += validCount;
                        if (validCount != 3)
                            triangleOnly = 0;
                    }
                }
                else
                {
                    int alivePrimitiveCount = AlivePrimitives.Length;
                    for (int i = 0; i < alivePrimitiveCount; i++)
                    {
                        int sparsePrimitive = AlivePrimitives[i];
                        PrimitiveRecord record = PrimitiveRecords[sparsePrimitive];
                        int validCount = 0;
                        int dataCursor = record.Start;

                        for (int k = 0; k < record.Length; k++)
                        {
                            int sparseVertex = PrimitiveData[dataCursor + k];
                            if ((uint)sparseVertex < (uint)vertexRemapLength && VertexRemap[sparseVertex] != 0)
                                validCount++;
                        }

                        totalPrimitiveVertexCount += validCount;
                        if (validCount != 3)
                            triangleOnly = 0;
                    }
                }

                TotalPrimitiveVertexCountOut[0] = totalPrimitiveVertexCount;
                TriangleOnlyOut[0] = triangleOnly;
            }
        }

        [BurstCompile]
        private struct FillSparsePrimitivesJob : IJob
        {
            [ReadOnly] public NativeArray<PrimitiveRecord> PrimitiveRecords;
            [ReadOnly] public NativeArray<int> PrimitiveData;
            [ReadOnly] public NativeArray<int> VertexRemap;
            [ReadOnly] public NativeArray<int> AlivePrimitives;

            public bool DenseTrianglePrimitives;
            public bool PrimitivesDenseContiguous;
            public int PrimitiveCount;

            public NativeArray<int> PrimitiveOffsets;
            public NativeArray<int> PrimitiveVertices;

            public void Execute()
            {
                int runningOffset = 0;
                int vertexRemapLength = VertexRemap.Length;

                if (DenseTrianglePrimitives)
                {
                    for (int primitiveIndex = 0; primitiveIndex < PrimitiveCount; primitiveIndex++)
                    {
                        PrimitiveOffsets[primitiveIndex] = runningOffset;
                        int triStart = primitiveIndex * 3;

                        int v0 = PrimitiveData[triStart];
                        int v1 = PrimitiveData[triStart + 1];
                        int v2 = PrimitiveData[triStart + 2];

                        if ((uint)v0 < (uint)vertexRemapLength)
                        {
                            int remapped = VertexRemap[v0];
                            if (remapped != 0)
                                PrimitiveVertices[runningOffset++] = remapped - 1;
                        }

                        if ((uint)v1 < (uint)vertexRemapLength)
                        {
                            int remapped = VertexRemap[v1];
                            if (remapped != 0)
                                PrimitiveVertices[runningOffset++] = remapped - 1;
                        }

                        if ((uint)v2 < (uint)vertexRemapLength)
                        {
                            int remapped = VertexRemap[v2];
                            if (remapped != 0)
                                PrimitiveVertices[runningOffset++] = remapped - 1;
                        }
                    }
                }
                else if (PrimitivesDenseContiguous)
                {
                    for (int sparsePrimitive = 0; sparsePrimitive < PrimitiveCount; sparsePrimitive++)
                    {
                        PrimitiveOffsets[sparsePrimitive] = runningOffset;

                        PrimitiveRecord record = PrimitiveRecords[sparsePrimitive];
                        int dataCursor = record.Start;
                        for (int k = 0; k < record.Length; k++)
                        {
                            int sparseVertex = PrimitiveData[dataCursor + k];
                            if ((uint)sparseVertex >= (uint)vertexRemapLength)
                                continue;

                            int remapped = VertexRemap[sparseVertex];
                            if (remapped != 0)
                                PrimitiveVertices[runningOffset++] = remapped - 1;
                        }
                    }
                }
                else
                {
                    int alivePrimitiveCount = AlivePrimitives.Length;
                    for (int i = 0; i < alivePrimitiveCount; i++)
                    {
                        PrimitiveOffsets[i] = runningOffset;

                        int sparsePrimitive = AlivePrimitives[i];
                        PrimitiveRecord record = PrimitiveRecords[sparsePrimitive];
                        int dataCursor = record.Start;
                        for (int k = 0; k < record.Length; k++)
                        {
                            int sparseVertex = PrimitiveData[dataCursor + k];
                            if ((uint)sparseVertex >= (uint)vertexRemapLength)
                                continue;

                            int remapped = VertexRemap[sparseVertex];
                            if (remapped != 0)
                                PrimitiveVertices[runningOffset++] = remapped - 1;
                        }
                    }
                }

                PrimitiveOffsets[PrimitiveCount] = runningOffset;
            }
        }

        [BurstCompile]
        private struct FillDenseGeneralPrimitivesJob : IJob
        {
            [ReadOnly] public NativeArray<PrimitiveRecord> PrimitiveRecords;
            [ReadOnly] public NativeArray<int> PrimitiveData;

            public int PrimitiveCount;
            public NativeArray<int> PrimitiveOffsets;
            public NativeArray<int> PrimitiveVertices;
            public NativeArray<int> TriangleOnlyOut;

            public void Execute()
            {
                int runningOffset = 0;
                int triangleOnly = 1;

                for (int primitiveIndex = 0; primitiveIndex < PrimitiveCount; primitiveIndex++)
                {
                    PrimitiveOffsets[primitiveIndex] = runningOffset;

                    PrimitiveRecord record = PrimitiveRecords[primitiveIndex];
                    int dataCursor = record.Start;
                    if (record.Length != 3)
                        triangleOnly = 0;

                    for (int k = 0; k < record.Length; k++)
                        PrimitiveVertices[runningOffset++] = PrimitiveData[dataCursor + k];
                }

                PrimitiveOffsets[PrimitiveCount] = runningOffset;
                TriangleOnlyOut[0] = triangleOnly;
            }
        }

        [BurstCompile]
        private struct BuildAliveMaskJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> AliveIndices;
            [NativeDisableParallelForRestriction] public NativeArray<byte> AliveMask;

            public void Execute(int index)
            {
                AliveMask[AliveIndices[index]] = 1;
            }
        }

        [BurstCompile]
        private struct BuildAdjacencyDenseJob : IJob
        {
            [ReadOnly] public NativeArray<int> VertexToPoint;
            [ReadOnly] public NativeArray<PrimitiveRecord> PrimitiveRecords;
            [ReadOnly] public NativeArray<int> PrimitiveData;

            public int PointCount;
            public int VertexCount;
            public int PrimitiveCount;
            public bool DenseTriangleLayout;

            public NativeParallelMultiHashMap<int, int> PointToVertices;
            public NativeParallelMultiHashMap<int, int> VertexToPrimitives;

            public void Execute()
            {
                for (int vertexIndex = 0; vertexIndex < VertexCount; vertexIndex++)
                {
                    int pointIndex = VertexToPoint[vertexIndex];
                    if ((uint)pointIndex < (uint)PointCount)
                        PointToVertices.Add(pointIndex, vertexIndex);
                }

                if (DenseTriangleLayout)
                {
                    for (int primitiveIndex = 0; primitiveIndex < PrimitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        VertexToPrimitives.Add(PrimitiveData[triStart], primitiveIndex);
                        VertexToPrimitives.Add(PrimitiveData[triStart + 1], primitiveIndex);
                        VertexToPrimitives.Add(PrimitiveData[triStart + 2], primitiveIndex);
                    }

                    return;
                }

                for (int primitiveIndex = 0; primitiveIndex < PrimitiveCount; primitiveIndex++)
                {
                    PrimitiveRecord record = PrimitiveRecords[primitiveIndex];
                    int dataCursor = record.Start;
                    for (int k = 0; k < record.Length; k++)
                        VertexToPrimitives.Add(PrimitiveData[dataCursor + k], primitiveIndex);
                }
            }
        }

        [BurstCompile]
        private struct BuildAdjacencySparseJob : IJob
        {
            [ReadOnly] public NativeArray<int> AliveVertices;
            [ReadOnly] public NativeArray<int> AlivePrimitives;
            [ReadOnly] public NativeArray<int> VertexToPoint;
            [ReadOnly] public NativeArray<byte> PointAliveMask;
            [ReadOnly] public NativeArray<byte> VertexAliveMask;
            [ReadOnly] public NativeArray<PrimitiveRecord> PrimitiveRecords;
            [ReadOnly] public NativeArray<int> PrimitiveData;

            public NativeParallelMultiHashMap<int, int> PointToVertices;
            public NativeParallelMultiHashMap<int, int> VertexToPrimitives;

            public void Execute()
            {
                for (int i = 0; i < AliveVertices.Length; i++)
                {
                    int vertexIndex = AliveVertices[i];
                    int pointIndex = VertexToPoint[vertexIndex];

                    if ((uint)pointIndex >= (uint)PointAliveMask.Length)
                        continue;
                    if (PointAliveMask[pointIndex] == 0)
                        continue;

                    PointToVertices.Add(pointIndex, vertexIndex);
                }

                for (int i = 0; i < AlivePrimitives.Length; i++)
                {
                    int primitiveIndex = AlivePrimitives[i];
                    PrimitiveRecord record = PrimitiveRecords[primitiveIndex];
                    int dataCursor = record.Start;

                    for (int k = 0; k < record.Length; k++)
                    {
                        int vertexIndex = PrimitiveData[dataCursor + k];
                        if ((uint)vertexIndex >= (uint)VertexAliveMask.Length)
                            continue;
                        if (VertexAliveMask[vertexIndex] == 0)
                            continue;

                        VertexToPrimitives.Add(vertexIndex, primitiveIndex);
                    }
                }
            }
        }

        [BurstCompile]
        private unsafe struct PackDenseAttributesJob : IJob
        {
            [ReadOnly] public NativeArray<AttributePackPlan> Plans;
            public NativeArray<CompiledAttributeDescriptor> Descriptors;

            [NativeDisableUnsafePtrRestriction] public byte* DestinationPtr;

            public void Execute()
            {
                for (int i = 0; i < Plans.Length; i++)
                {
                    AttributePackPlan plan = Plans[i];

                    Descriptors[i] = new CompiledAttributeDescriptor
                    {
                        AttributeId = plan.AttributeId,
                        TypeHash = plan.TypeHash,
                        Stride = plan.Stride,
                        Count = plan.Count,
                        OffsetBytes = plan.OffsetBytes
                    };

                    int copyBytes = plan.Stride * plan.Count;
                    if (copyBytes > 0)
                        UnsafeUtility.MemCpy(DestinationPtr + plan.OffsetBytes, plan.SourcePtr, copyBytes);
                }
            }
        }

        [BurstCompile]
        private unsafe struct PackSparseAttributesJob : IJob
        {
            [ReadOnly] public NativeArray<AttributePackPlan> Plans;
            [ReadOnly] public NativeArray<int> AliveSparseIndices;
            public NativeArray<CompiledAttributeDescriptor> Descriptors;

            [NativeDisableUnsafePtrRestriction] public byte* DestinationPtr;

            public void Execute()
            {
                int elementCount = AliveSparseIndices.Length;
                for (int i = 0; i < Plans.Length; i++)
                {
                    AttributePackPlan plan = Plans[i];

                    Descriptors[i] = new CompiledAttributeDescriptor
                    {
                        AttributeId = plan.AttributeId,
                        TypeHash = plan.TypeHash,
                        Stride = plan.Stride,
                        Count = plan.Count,
                        OffsetBytes = plan.OffsetBytes
                    };

                    for (int k = 0; k < elementCount; k++)
                    {
                        int sparseIndex = AliveSparseIndices[k];
                        byte* src = plan.SourcePtr + sparseIndex * plan.Stride;
                        byte* dst = DestinationPtr + plan.OffsetBytes + k * plan.Stride;
                        UnsafeUtility.MemCpy(dst, src, plan.Stride);
                    }
                }
            }
        }
    }
}
