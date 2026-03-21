// Core: NativeDetail compilation pipeline from mutable sparse state to packed runtime data.
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace SangriaMesh
{
    public partial struct NativeDetail : IDisposable
    {
        #region Compile

        public NativeCompiledDetail Compile(Allocator allocator = Allocator.Persistent)
        {
            EnsureJobSafeAllocator(allocator);

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

                var offsetsJob = new DenseTriangleOffsetsJob
                {
                    PrimitiveOffsets = primitiveOffsetsDense
                };
                offsetsJob.Schedule(primitiveCount + 1, 64).Complete();

                if (totalPrimitiveVertexCount > 0)
                {
                    var primitiveData = m_PrimitiveStorage.GetDataArray();
                    UnsafeUtility.MemCpy(
                        NativeArrayUnsafeUtility.GetUnsafePtr(primitiveVerticesDense),
                        NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(primitiveData),
                        totalPrimitiveVertexCount * UnsafeUtility.SizeOf<int>());
                }
            }
            else
            {
                var primitiveRecords = m_PrimitiveStorage.GetRecordsArray();
                var primitiveData = m_PrimitiveStorage.GetDataArray();
                using var triangleOnlyFlag = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var fillJob = new FillDenseGeneralPrimitivesJob
                {
                    PrimitiveRecords = primitiveRecords,
                    PrimitiveData = primitiveData,
                    PrimitiveCount = primitiveCount,
                    PrimitiveOffsets = primitiveOffsetsDense,
                    PrimitiveVertices = primitiveVerticesDense,
                    TriangleOnlyOut = triangleOnlyFlag
                };
                fillJob.Schedule().Complete();

                triangleOnlyTopology = triangleOnlyFlag[0] != 0;
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

        private NativeCompiledDetail CompileSparse(Allocator allocator)
        {
            int pointCount = m_Points.Count;
            int vertexCount = m_Vertices.Count;
            int primitiveCount = m_Primitives.Count;

            bool pointsIdentityRemap = m_Points.IsDenseContiguous && m_Points.MaxIndexExclusive == pointCount;
            bool verticesDenseContiguous = m_Vertices.IsDenseContiguous && m_Vertices.MaxIndexExclusive == vertexCount;
            bool primitivesDenseContiguous = m_Primitives.IsDenseContiguous && m_Primitives.MaxIndexExclusive == primitiveCount;

            using var alivePoints = new NativeList<int>(math_max(1, pointCount), Allocator.TempJob);
            using var aliveVertices = new NativeList<int>(math_max(1, vertexCount), Allocator.TempJob);
            using var alivePrimitives = new NativeList<int>(math_max(1, primitiveCount), Allocator.TempJob);
            using var emptyIntArray = new NativeArray<int>(0, Allocator.TempJob);

            NativeArray<int> pointRemap = default;
            var vertexRemap = new NativeArray<int>(
                math_max(1, m_Vertices.MaxIndexExclusive),
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);

            try
            {
                if (!pointsIdentityRemap)
                {
                    pointRemap = new NativeArray<int>(
                        math_max(1, m_Points.MaxIndexExclusive),
                        Allocator.TempJob,
                        NativeArrayOptions.ClearMemory);

                    m_Points.GetAliveIndices(alivePoints);
                    if (alivePoints.Length > 0)
                    {
                        var pointRemapJob = new BuildPointRemapJob
                        {
                            AlivePoints = alivePoints.AsArray(),
                            PointRemap = pointRemap
                        };
                        pointRemapJob.Schedule(alivePoints.Length, 64).Complete();
                    }
                }

                var pointRemapForJob = pointsIdentityRemap ? emptyIntArray : pointRemap;
                var vertexToPointSparse = m_VertexToPoint.AsArray();
                var vertexToPointDense = new NativeArray<int>(vertexCount, allocator, NativeArrayOptions.UninitializedMemory);

                if (verticesDenseContiguous)
                {
                    if (vertexCount > 0)
                    {
                        var vertexRemapJob = new BuildVertexRemapDenseJob
                        {
                            VertexToPointSparse = vertexToPointSparse,
                            PointRemap = pointRemapForJob,
                            PointsIdentityRemap = pointsIdentityRemap,
                            PointCount = pointCount,
                            VertexRemap = vertexRemap,
                            VertexToPointDense = vertexToPointDense
                        };
                        vertexRemapJob.Schedule(vertexCount, 64).Complete();
                    }
                }
                else
                {
                    m_Vertices.GetAliveIndices(aliveVertices);

                    if (aliveVertices.Length > 0)
                    {
                        var vertexRemapJob = new BuildVertexRemapSparseJob
                        {
                            AliveVertices = aliveVertices.AsArray(),
                            VertexToPointSparse = vertexToPointSparse,
                            PointRemap = pointRemapForJob,
                            PointsIdentityRemap = pointsIdentityRemap,
                            PointCount = pointCount,
                            VertexRemap = vertexRemap,
                            VertexToPointDense = vertexToPointDense
                        };
                        vertexRemapJob.Schedule(aliveVertices.Length, 64).Complete();
                    }
                }

                if (!primitivesDenseContiguous)
                    m_Primitives.GetAliveIndices(alivePrimitives);

                var alivePrimitivesForJob = primitivesDenseContiguous ? emptyIntArray : alivePrimitives.AsArray();
                bool denseTrianglePrimitives = primitivesDenseContiguous && m_PrimitiveStorage.IsDenseTriangleLayout;

                var primitiveRecords = m_PrimitiveStorage.GetRecordsArray();
                var primitiveData = m_PrimitiveStorage.GetDataArray();

                using var totalPrimitiveVertexCountOut = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                using var triangleOnlyOut = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var countJob = new CountSparsePrimitivesJob
                {
                    PrimitiveRecords = primitiveRecords,
                    PrimitiveData = primitiveData,
                    VertexRemap = vertexRemap,
                    AlivePrimitives = alivePrimitivesForJob,
                    DenseTrianglePrimitives = denseTrianglePrimitives,
                    PrimitivesDenseContiguous = primitivesDenseContiguous,
                    PrimitiveCount = primitiveCount,
                    TotalPrimitiveVertexCountOut = totalPrimitiveVertexCountOut,
                    TriangleOnlyOut = triangleOnlyOut
                };
                countJob.Schedule().Complete();

                int totalPrimitiveVertexCount = totalPrimitiveVertexCountOut[0];
                bool triangleOnlyTopology = triangleOnlyOut[0] != 0;

                var primitiveOffsetsDense = new NativeArray<int>(primitiveCount + 1, allocator, NativeArrayOptions.UninitializedMemory);
                var primitiveVerticesDense = new NativeArray<int>(totalPrimitiveVertexCount, allocator, NativeArrayOptions.UninitializedMemory);

                var fillJob = new FillSparsePrimitivesJob
                {
                    PrimitiveRecords = primitiveRecords,
                    PrimitiveData = primitiveData,
                    VertexRemap = vertexRemap,
                    AlivePrimitives = alivePrimitivesForJob,
                    DenseTrianglePrimitives = denseTrianglePrimitives,
                    PrimitivesDenseContiguous = primitivesDenseContiguous,
                    PrimitiveCount = primitiveCount,
                    PrimitiveOffsets = primitiveOffsetsDense,
                    PrimitiveVertices = primitiveVerticesDense
                };
                fillJob.Schedule().Complete();

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
