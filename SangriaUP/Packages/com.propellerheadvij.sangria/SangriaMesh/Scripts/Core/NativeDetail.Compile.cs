using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public unsafe partial struct NativeDetail : IDisposable
    {
        #region Compile

        public NativeCompiledDetail Compile(Allocator allocator = Allocator.Persistent)
        {
            bool denseContiguous =
                m_Points.IsDenseContiguous &&
                m_Vertices.IsDenseContiguous &&
                m_Primitives.IsDenseContiguous;

            return denseContiguous
                ? CompileDenseContiguous(allocator)
                : CompileSparse(allocator);
        }

        private unsafe NativeCompiledDetail CompileDenseContiguous(Allocator allocator)
        {
            int pointCount = m_Points.Count;
            int vertexCount = m_Vertices.Count;
            int primitiveCount = m_Primitives.Count;

            var vertexToPointDense = new NativeArray<int>(vertexCount, allocator, NativeArrayOptions.UninitializedMemory);
            if (vertexCount > 0)
            {
                var vertexToPointSource = m_VertexToPoint.AsArray();
                UnsafeUtility.MemCpy(
                    NativeArrayUnsafeUtility.GetUnsafePtr(vertexToPointDense),
                    NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(vertexToPointSource),
                    vertexCount * UnsafeUtility.SizeOf<int>());
            }

            int totalPrimitiveVertexCount = m_PrimitiveStorage.TotalVertexCount;
            bool triangleOnlyTopology = primitiveCount == 0;

            var primitiveOffsetsDense = new NativeArray<int>(primitiveCount + 1, allocator, NativeArrayOptions.UninitializedMemory);
            var primitiveVerticesDense = new NativeArray<int>(totalPrimitiveVertexCount, allocator, NativeArrayOptions.UninitializedMemory);
            if (primitiveCount == 0)
            {
                primitiveOffsetsDense[0] = 0;
            }
            else if (m_PrimitiveStorage.IsDenseTriangleLayout && totalPrimitiveVertexCount == primitiveCount * 3)
            {
                triangleOnlyTopology = true;
                int* offsetsPtr = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(primitiveOffsetsDense);
                for (int i = 0; i <= primitiveCount; i++)
                    offsetsPtr[i] = i * 3;

                if (totalPrimitiveVertexCount > 0)
                {
                    UnsafeUtility.MemCpy(
                        NativeArrayUnsafeUtility.GetUnsafePtr(primitiveVerticesDense),
                        m_PrimitiveStorage.GetDataPointerUnchecked(),
                        totalPrimitiveVertexCount * UnsafeUtility.SizeOf<int>());
                }
            }
            else
            {
                triangleOnlyTopology = true;
                int runningOffset = 0;
                for (int i = 0; i < primitiveCount; i++)
                {
                    primitiveOffsetsDense[i] = runningOffset;
                    PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(i);
                    if (record.Length != 3)
                        triangleOnlyTopology = false;

                    m_PrimitiveStorage.CopyVerticesUnchecked(record, primitiveVerticesDense, runningOffset);
                    runningOffset += record.Length;
                }

                primitiveOffsetsDense[primitiveCount] = runningOffset;
            }

            var pointAttributesCompiled = CompileAttributesDense(m_PointAttributes, pointCount, allocator);
            var vertexAttributesCompiled = CompileAttributesDense(m_VertexAttributes, vertexCount, allocator);
            var primitiveAttributesCompiled = CompileAttributesDense(m_PrimitiveAttributes, primitiveCount, allocator);
            var compiledResources = m_Resources.Compile(allocator);

            return new NativeCompiledDetail(
                vertexToPointDense,
                primitiveOffsetsDense,
                primitiveVerticesDense,
                pointAttributesCompiled,
                vertexAttributesCompiled,
                primitiveAttributesCompiled,
                compiledResources,
                pointCount,
                vertexCount,
                primitiveCount,
                triangleOnlyTopology);
        }

        private unsafe NativeCompiledDetail CompileSparse(Allocator allocator)
        {
            int pointCount = m_Points.Count;
            int vertexCount = m_Vertices.Count;
            int primitiveCount = m_Primitives.Count;

            bool pointsIdentityRemap = m_Points.IsDenseContiguous && m_Points.MaxIndexExclusive == pointCount;
            bool verticesDenseContiguous = m_Vertices.IsDenseContiguous && m_Vertices.MaxIndexExclusive == vertexCount;
            bool primitivesDenseContiguous = m_Primitives.IsDenseContiguous && m_Primitives.MaxIndexExclusive == primitiveCount;

            using var alivePoints = new NativeList<int>(math_max(1, pointCount), Allocator.Temp);
            using var aliveVertices = new NativeList<int>(math_max(1, vertexCount), Allocator.Temp);
            using var alivePrimitives = new NativeList<int>(math_max(1, primitiveCount), Allocator.Temp);

            NativeArray<int> pointRemap = default;
            if (!pointsIdentityRemap)
            {
                pointRemap = new NativeArray<int>(
                    math_max(1, m_Points.MaxIndexExclusive),
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            }

            var vertexRemap = new NativeArray<int>(
                math_max(1, m_Vertices.MaxIndexExclusive),
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);

            try
            {
                int* pointRemapPtr = pointRemap.IsCreated
                    ? (int*)NativeArrayUnsafeUtility.GetUnsafePtr(pointRemap)
                    : null;
                int pointRemapLength = pointRemap.IsCreated ? pointRemap.Length : 0;

                int* vertexRemapPtr = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(vertexRemap);
                int vertexRemapLength = vertexRemap.Length;
                var vertexToPointSparse = m_VertexToPoint.AsArray();
                int* vertexToPointSparsePtr = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(vertexToPointSparse);

                if (!pointsIdentityRemap)
                {
                    m_Points.GetAliveIndices(alivePoints);

                    var alivePointsArray = alivePoints.AsArray();
                    int alivePointCount = alivePointsArray.Length;
                    if (alivePointCount > 0)
                    {
                        int* alivePointsPtr = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(alivePointsArray);
                        for (int densePoint = 0; densePoint < alivePointCount; densePoint++)
                        {
                            int sparsePoint = alivePointsPtr[densePoint];
                            pointRemapPtr[sparsePoint] = densePoint + 1;
                        }
                    }
                }

                var vertexToPointDense = new NativeArray<int>(vertexCount, allocator, NativeArrayOptions.UninitializedMemory);
                int* vertexToPointDensePtr = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(vertexToPointDense);

                if (verticesDenseContiguous)
                {
                    for (int sparseVertex = 0; sparseVertex < vertexCount; sparseVertex++)
                    {
                        vertexRemapPtr[sparseVertex] = sparseVertex + 1;

                        int sparsePoint = vertexToPointSparsePtr[sparseVertex];
                        int mappedPoint = -1;
                        if (pointsIdentityRemap)
                        {
                            if ((uint)sparsePoint < (uint)pointCount)
                                mappedPoint = sparsePoint;
                        }
                        else if ((uint)sparsePoint < (uint)pointRemapLength)
                        {
                            int remappedPoint = pointRemapPtr[sparsePoint];
                            if (remappedPoint != 0)
                                mappedPoint = remappedPoint - 1;
                        }

                        vertexToPointDensePtr[sparseVertex] = mappedPoint;
                    }
                }
                else
                {
                    m_Vertices.GetAliveIndices(aliveVertices);

                    var aliveVerticesArray = aliveVertices.AsArray();
                    int aliveVertexCount = aliveVerticesArray.Length;
                    if (aliveVertexCount > 0)
                    {
                        int* aliveVerticesPtr = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(aliveVerticesArray);
                        for (int denseVertex = 0; denseVertex < aliveVertexCount; denseVertex++)
                        {
                            int sparseVertex = aliveVerticesPtr[denseVertex];
                            vertexRemapPtr[sparseVertex] = denseVertex + 1;

                            int sparsePoint = vertexToPointSparsePtr[sparseVertex];
                            int mappedPoint = -1;
                            if (pointsIdentityRemap)
                            {
                                if ((uint)sparsePoint < (uint)pointCount)
                                    mappedPoint = sparsePoint;
                            }
                            else if ((uint)sparsePoint < (uint)pointRemapLength)
                            {
                                int remappedPoint = pointRemapPtr[sparsePoint];
                                if (remappedPoint != 0)
                                    mappedPoint = remappedPoint - 1;
                            }

                            vertexToPointDensePtr[denseVertex] = mappedPoint;
                        }
                    }
                }

                if (!primitivesDenseContiguous)
                    m_Primitives.GetAliveIndices(alivePrimitives);

                int* alivePrimitivesPtr = null;
                int alivePrimitiveCount = 0;
                if (!primitivesDenseContiguous)
                {
                    var alivePrimitivesArray = alivePrimitives.AsArray();
                    alivePrimitiveCount = alivePrimitivesArray.Length;
                    if (alivePrimitiveCount > 0)
                        alivePrimitivesPtr = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(alivePrimitivesArray);
                }

                int totalPrimitiveVertexCount = 0;
                bool triangleOnlyTopology = true;
                bool denseTrianglePrimitives = primitivesDenseContiguous && m_PrimitiveStorage.IsDenseTriangleLayout;
                int* primitiveDataPtr = m_PrimitiveStorage.GetDataPointerUnchecked();
                PrimitiveRecord* primitiveRecordsPtr = m_PrimitiveStorage.GetRecordPointerUnchecked();

                if (denseTrianglePrimitives)
                {
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        int validCount = 0;

                        int v0 = primitiveDataPtr[triStart];
                        int v1 = primitiveDataPtr[triStart + 1];
                        int v2 = primitiveDataPtr[triStart + 2];

                        if ((uint)v0 < (uint)vertexRemapLength && vertexRemapPtr[v0] != 0)
                            validCount++;
                        if ((uint)v1 < (uint)vertexRemapLength && vertexRemapPtr[v1] != 0)
                            validCount++;
                        if ((uint)v2 < (uint)vertexRemapLength && vertexRemapPtr[v2] != 0)
                            validCount++;

                        totalPrimitiveVertexCount += validCount;
                        if (validCount != 3)
                            triangleOnlyTopology = false;
                    }
                }
                else
                {
                    if (primitivesDenseContiguous)
                    {
                        for (int sparsePrimitive = 0; sparsePrimitive < primitiveCount; sparsePrimitive++)
                        {
                            PrimitiveRecord record = primitiveRecordsPtr[sparsePrimitive];
                            int validCount = 0;
                            int dataCursor = record.Start;

                            for (int k = 0; k < record.Length; k++)
                            {
                                int sparseVertex = primitiveDataPtr[dataCursor + k];
                                if ((uint)sparseVertex < (uint)vertexRemapLength && vertexRemapPtr[sparseVertex] != 0)
                                    validCount++;
                            }

                            totalPrimitiveVertexCount += validCount;
                            if (validCount != 3)
                                triangleOnlyTopology = false;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < alivePrimitiveCount; i++)
                        {
                            int sparsePrimitive = alivePrimitivesPtr[i];
                            PrimitiveRecord record = primitiveRecordsPtr[sparsePrimitive];
                            int validCount = 0;
                            int dataCursor = record.Start;

                            for (int k = 0; k < record.Length; k++)
                            {
                                int sparseVertex = primitiveDataPtr[dataCursor + k];
                                if ((uint)sparseVertex < (uint)vertexRemapLength && vertexRemapPtr[sparseVertex] != 0)
                                    validCount++;
                            }

                            totalPrimitiveVertexCount += validCount;
                            if (validCount != 3)
                                triangleOnlyTopology = false;
                        }
                    }
                }

                var primitiveOffsetsDense = new NativeArray<int>(primitiveCount + 1, allocator, NativeArrayOptions.UninitializedMemory);
                var primitiveVerticesDense = new NativeArray<int>(totalPrimitiveVertexCount, allocator, NativeArrayOptions.UninitializedMemory);
                int* primitiveOffsetsPtr = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(primitiveOffsetsDense);
                int* primitiveVerticesPtr = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(primitiveVerticesDense);

                int runningOffset = 0;
                if (denseTrianglePrimitives)
                {
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        primitiveOffsetsPtr[primitiveIndex] = runningOffset;
                        int triStart = primitiveIndex * 3;

                        int v0 = primitiveDataPtr[triStart];
                        int v1 = primitiveDataPtr[triStart + 1];
                        int v2 = primitiveDataPtr[triStart + 2];

                        if ((uint)v0 < (uint)vertexRemapLength)
                        {
                            int remapped = vertexRemapPtr[v0];
                            if (remapped != 0)
                                primitiveVerticesPtr[runningOffset++] = remapped - 1;
                        }

                        if ((uint)v1 < (uint)vertexRemapLength)
                        {
                            int remapped = vertexRemapPtr[v1];
                            if (remapped != 0)
                                primitiveVerticesPtr[runningOffset++] = remapped - 1;
                        }

                        if ((uint)v2 < (uint)vertexRemapLength)
                        {
                            int remapped = vertexRemapPtr[v2];
                            if (remapped != 0)
                                primitiveVerticesPtr[runningOffset++] = remapped - 1;
                        }
                    }
                }
                else
                {
                    if (primitivesDenseContiguous)
                    {
                        for (int sparsePrimitive = 0; sparsePrimitive < primitiveCount; sparsePrimitive++)
                        {
                            primitiveOffsetsPtr[sparsePrimitive] = runningOffset;

                            PrimitiveRecord record = primitiveRecordsPtr[sparsePrimitive];
                            int dataCursor = record.Start;
                            for (int k = 0; k < record.Length; k++)
                            {
                                int sparseVertex = primitiveDataPtr[dataCursor + k];
                                if ((uint)sparseVertex >= (uint)vertexRemapLength)
                                    continue;

                                int remapped = vertexRemapPtr[sparseVertex];
                                if (remapped != 0)
                                    primitiveVerticesPtr[runningOffset++] = remapped - 1;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < alivePrimitiveCount; i++)
                        {
                            primitiveOffsetsPtr[i] = runningOffset;

                            int sparsePrimitive = alivePrimitivesPtr[i];
                            PrimitiveRecord record = primitiveRecordsPtr[sparsePrimitive];
                            int dataCursor = record.Start;
                            for (int k = 0; k < record.Length; k++)
                            {
                                int sparseVertex = primitiveDataPtr[dataCursor + k];
                                if ((uint)sparseVertex >= (uint)vertexRemapLength)
                                    continue;

                                int remapped = vertexRemapPtr[sparseVertex];
                                if (remapped != 0)
                                    primitiveVerticesPtr[runningOffset++] = remapped - 1;
                            }
                        }
                    }
                }

                primitiveOffsetsPtr[primitiveCount] = runningOffset;

                var pointAttributesCompiled = pointsIdentityRemap
                    ? CompileAttributesDense(m_PointAttributes, pointCount, allocator)
                    : CompileAttributes(m_PointAttributes, alivePoints, allocator);
                var vertexAttributesCompiled = verticesDenseContiguous
                    ? CompileAttributesDense(m_VertexAttributes, vertexCount, allocator)
                    : CompileAttributes(m_VertexAttributes, aliveVertices, allocator);
                var primitiveAttributesCompiled = primitivesDenseContiguous
                    ? CompileAttributesDense(m_PrimitiveAttributes, primitiveCount, allocator)
                    : CompileAttributes(m_PrimitiveAttributes, alivePrimitives, allocator);
                var compiledResources = m_Resources.Compile(allocator);

                return new NativeCompiledDetail(
                    vertexToPointDense,
                    primitiveOffsetsDense,
                    primitiveVerticesDense,
                    pointAttributesCompiled,
                    vertexAttributesCompiled,
                    primitiveAttributesCompiled,
                    compiledResources,
                    pointCount,
                    vertexCount,
                    primitiveCount,
                    triangleOnlyTopology);
            }
            finally
            {
                if (pointRemap.IsCreated)
                    pointRemap.Dispose();
                if (vertexRemap.IsCreated)
                    vertexRemap.Dispose();
            }
        }

        #endregion
    }
}
