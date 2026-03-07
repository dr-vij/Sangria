using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SangriaMesh
{
    [BurstCompile]
    public unsafe struct NativeDetail : IDisposable
    {
        private readonly Allocator m_Allocator;
        private bool m_IsDisposed;

        private SparseHandleSet m_Points;
        private SparseHandleSet m_Vertices;
        private SparseHandleSet m_Primitives;

        private NativeList<int> m_VertexToPoint;
        private PrimitiveStorage m_PrimitiveStorage;

        private NativeParallelMultiHashMap<int, int> m_PointToVertices;
        private NativeParallelMultiHashMap<int, int> m_VertexToPrimitives;
        private bool m_AdjacencyDirty;

        private AttributeStore m_PointAttributes;
        private AttributeStore m_VertexAttributes;
        private AttributeStore m_PrimitiveAttributes;
        private AttributeHandle<float3> m_PointPositionHandle;

        private ResourceRegistry m_Resources;

        private uint m_TopologyVersion;
        private uint m_AttributeVersion;

        public int PointCount => m_Points.Count;
        public int VertexCount => m_Vertices.Count;
        public int PrimitiveCount => m_Primitives.Count;

        public int PointCapacity => m_Points.Capacity;
        public int VertexCapacity => m_Vertices.Capacity;
        public int PrimitiveCapacity => m_Primitives.Capacity;
        public int PrimitiveDataLength => m_PrimitiveStorage.DataLength;

        public uint TopologyVersion => m_TopologyVersion;
        public uint AttributeVersion => m_AttributeVersion;

        public NativeDetail(int initialCapacity, Allocator allocator)
            : this(initialCapacity, initialCapacity, initialCapacity, allocator)
        {
        }

        public NativeDetail(int pointCapacity, int vertexCapacity, int primitiveCapacity, Allocator allocator)
        {
            int pointCap = math_max(1, pointCapacity);
            int vertexCap = math_max(1, vertexCapacity);
            int primitiveCap = math_max(1, primitiveCapacity);

            m_Allocator = allocator;
            m_IsDisposed = false;

            m_Points = new SparseHandleSet(pointCap, allocator);
            m_Vertices = new SparseHandleSet(vertexCap, allocator);
            m_Primitives = new SparseHandleSet(primitiveCap, allocator);

            m_VertexToPoint = new NativeList<int>(vertexCap, allocator);
            m_VertexToPoint.Resize(vertexCap, NativeArrayOptions.UninitializedMemory);
            FillNativeArrayWithMinusOne(m_VertexToPoint.AsArray());
            m_PrimitiveStorage = new PrimitiveStorage(primitiveCap, 4, allocator);
            m_PointToVertices = new NativeParallelMultiHashMap<int, int>(vertexCap, allocator);
            m_VertexToPrimitives = new NativeParallelMultiHashMap<int, int>(primitiveCap * 4, allocator);
            m_AdjacencyDirty = false;

            m_PointAttributes = new AttributeStore(8, pointCap, allocator);
            m_VertexAttributes = new AttributeStore(8, vertexCap, allocator);
            m_PrimitiveAttributes = new AttributeStore(8, primitiveCap, allocator);

            m_Resources = new ResourceRegistry(16, allocator);

            m_TopologyVersion = 1;
            m_AttributeVersion = 1;

            // Mandatory position attribute on point domain.
            m_PointAttributes.RegisterAttribute<float3>(AttributeID.Position);
            if (m_PointAttributes.TryResolveHandle<float3>(AttributeID.Position, out m_PointPositionHandle) != CoreResult.Success)
                throw new InvalidOperationException("Failed to initialize mandatory point position handle.");
        }

        #region Attributes

        public CoreResult AddPointAttribute<T>(int attributeId) where T : unmanaged
        {
            CoreResult result = m_PointAttributes.RegisterAttribute<T>(attributeId, m_Points.Capacity);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult AddVertexAttribute<T>(int attributeId) where T : unmanaged
        {
            CoreResult result = m_VertexAttributes.RegisterAttribute<T>(attributeId, m_Vertices.Capacity);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult AddPrimitiveAttribute<T>(int attributeId) where T : unmanaged
        {
            CoreResult result = m_PrimitiveAttributes.RegisterAttribute<T>(attributeId, m_Primitives.Capacity);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult RemovePointAttribute(int attributeId)
        {
            if (attributeId == AttributeID.Position)
                return CoreResult.InvalidOperation;

            CoreResult result = m_PointAttributes.RemoveAttribute(attributeId);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult RemoveVertexAttribute(int attributeId)
        {
            CoreResult result = m_VertexAttributes.RemoveAttribute(attributeId);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult RemovePrimitiveAttribute(int attributeId)
        {
            CoreResult result = m_PrimitiveAttributes.RemoveAttribute(attributeId);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public bool HasPointAttribute(int attributeId) => m_PointAttributes.ContainsAttribute(attributeId);
        public bool HasVertexAttribute(int attributeId) => m_VertexAttributes.ContainsAttribute(attributeId);
        public bool HasPrimitiveAttribute(int attributeId) => m_PrimitiveAttributes.ContainsAttribute(attributeId);

        public CoreResult TryGetPointAttributeHandle<T>(int attributeId, out AttributeHandle<T> handle) where T : unmanaged
            => m_PointAttributes.TryResolveHandle(attributeId, out handle);

        public CoreResult TryGetVertexAttributeHandle<T>(int attributeId, out AttributeHandle<T> handle) where T : unmanaged
            => m_VertexAttributes.TryResolveHandle(attributeId, out handle);

        public CoreResult TryGetPrimitiveAttributeHandle<T>(int attributeId, out AttributeHandle<T> handle) where T : unmanaged
            => m_PrimitiveAttributes.TryResolveHandle(attributeId, out handle);

        public CoreResult TryGetPointAccessor<T>(int attributeId, out NativeAttributeAccessor<T> accessor) where T : unmanaged
            => m_PointAttributes.TryGetAccessor(attributeId, out accessor);

        public CoreResult TryGetVertexAccessor<T>(int attributeId, out NativeAttributeAccessor<T> accessor) where T : unmanaged
            => m_VertexAttributes.TryGetAccessor(attributeId, out accessor);

        public CoreResult TryGetPrimitiveAccessor<T>(int attributeId, out NativeAttributeAccessor<T> accessor) where T : unmanaged
            => m_PrimitiveAttributes.TryGetAccessor(attributeId, out accessor);

        public CoreResult TrySetPointAttribute<T>(int pointIndex, AttributeHandle<T> handle, T value) where T : unmanaged
        {
            if (!m_Points.IsAlive(pointIndex))
                return CoreResult.InvalidHandle;

            CoreResult result = m_PointAttributes.TrySet(handle, pointIndex, value);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult TryGetPointAttribute<T>(int pointIndex, AttributeHandle<T> handle, out T value) where T : unmanaged
        {
            value = default;
            if (!m_Points.IsAlive(pointIndex))
                return CoreResult.InvalidHandle;

            return m_PointAttributes.TryGet(handle, pointIndex, out value);
        }

        public CoreResult TrySetVertexAttribute<T>(int vertexIndex, AttributeHandle<T> handle, T value) where T : unmanaged
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return CoreResult.InvalidHandle;

            CoreResult result = m_VertexAttributes.TrySet(handle, vertexIndex, value);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult TryGetVertexAttribute<T>(int vertexIndex, AttributeHandle<T> handle, out T value) where T : unmanaged
        {
            value = default;
            if (!m_Vertices.IsAlive(vertexIndex))
                return CoreResult.InvalidHandle;

            return m_VertexAttributes.TryGet(handle, vertexIndex, out value);
        }

        public CoreResult TrySetPrimitiveAttribute<T>(int primitiveIndex, AttributeHandle<T> handle, T value) where T : unmanaged
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return CoreResult.InvalidHandle;

            CoreResult result = m_PrimitiveAttributes.TrySet(handle, primitiveIndex, value);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult TryGetPrimitiveAttribute<T>(int primitiveIndex, AttributeHandle<T> handle, out T value) where T : unmanaged
        {
            value = default;
            if (!m_Primitives.IsAlive(primitiveIndex))
                return CoreResult.InvalidHandle;

            return m_PrimitiveAttributes.TryGet(handle, primitiveIndex, out value);
        }

        #endregion

        #region Resources

        public CoreResult SetResource<T>(int resourceId, in T value) where T : unmanaged
        {
            CoreResult result = m_Resources.SetResource(resourceId, value);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult TryGetResource<T>(int resourceId, out T value) where T : unmanaged
            => m_Resources.TryGetResource(resourceId, out value);

        public CoreResult RemoveResource(int resourceId)
        {
            CoreResult result = m_Resources.RemoveResource(resourceId);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        #endregion

        #region Point

        public int AddPoint(float3 position)
        {
            return AddPoint(position, out _);
        }

        public int AddPoint(float3 position, out ElementHandle handle)
        {
            int oldCapacity = m_Points.Capacity;
            int pointIndex = m_Points.Allocate(out handle);

            if (m_Points.Capacity != oldCapacity)
                m_PointAttributes.EnsureCapacity(m_Points.Capacity);

            m_PointAttributes.ClearElement(pointIndex);
            m_PointAttributes.TrySet(m_PointPositionHandle, pointIndex, position);

            m_TopologyVersion++;
            m_AttributeVersion++;
            return pointIndex;
        }

        public bool RemovePoint(int pointIndex)
        {
            if (!m_Points.IsAlive(pointIndex))
                return false;

            EnsureAdjacencyUpToDate();
            using var pointVertices = new NativeList<int>(Allocator.Temp);
            CollectMultiMapValues(in m_PointToVertices, pointIndex, pointVertices);

            for (int i = 0; i < pointVertices.Length; i++)
            {
                int vertexIndex = pointVertices[i];
                if (m_Vertices.IsAlive(vertexIndex))
                    RemoveVertex(vertexIndex);
            }

            if (!m_AdjacencyDirty)
                m_PointToVertices.Remove(pointIndex);

            bool removed = m_Points.Release(pointIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        public bool IsPointAlive(int pointIndex) => m_Points.IsAlive(pointIndex);
        public bool IsPointHandleValid(ElementHandle handle) => m_Points.IsHandleValid(handle);
        public ElementHandle GetPointHandle(int pointIndex)
            => m_Points.IsAlive(pointIndex) ? new ElementHandle(pointIndex, m_Points.GetGeneration(pointIndex)) : default;

        public float3 GetPointPosition(int pointIndex)
        {
            if (!m_Points.IsAlive(pointIndex))
                return default;

            return m_PointAttributes.TryGet(m_PointPositionHandle, pointIndex, out float3 value) == CoreResult.Success ? value : default;
        }

        public bool SetPointPosition(int pointIndex, float3 position)
        {
            if (!m_Points.IsAlive(pointIndex))
                return false;

            if (m_PointAttributes.TrySet(m_PointPositionHandle, pointIndex, position) != CoreResult.Success)
                return false;

            m_AttributeVersion++;
            return true;
        }

        public void GetAllValidPoints(NativeList<int> output)
        {
            m_Points.GetAliveIndices(output);
        }

        #endregion

        #region Vertex

        public int AddVertex(int pointIndex)
        {
            return AddVertex(pointIndex, out _);
        }

        public int AddVertex(int pointIndex, out ElementHandle handle)
        {
            if (!m_Points.IsAlive(pointIndex))
            {
                handle = default;
                return -1;
            }

            int oldCapacity = m_Vertices.Capacity;
            int vertexIndex = m_Vertices.Allocate(out handle);

            if (m_Vertices.Capacity != oldCapacity)
            {
                EnsureVertexToPointCapacity(m_Vertices.Capacity);
                m_VertexAttributes.EnsureCapacity(m_Vertices.Capacity);
            }
            else
            {
                EnsureVertexToPointCapacity(vertexIndex + 1);
            }

            m_VertexToPoint[vertexIndex] = pointIndex;
            m_VertexAttributes.ClearElement(vertexIndex);
            if (!m_AdjacencyDirty)
            {
                EnsureMultiMapCapacity(ref m_PointToVertices, 1);
                m_PointToVertices.Add(pointIndex, vertexIndex);
            }

            m_TopologyVersion++;
            m_AttributeVersion++;
            return vertexIndex;
        }

        internal int* GetVertexToPointPointerUnchecked()
        {
            var vertexToPointArray = m_VertexToPoint.AsArray();
            return (int*)NativeArrayUnsafeUtility.GetUnsafePtr(vertexToPointArray);
        }

        public bool RemoveVertex(int vertexIndex)
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return false;

            EnsureAdjacencyUpToDate();
            int pointIndex = m_VertexToPoint[vertexIndex];

            using var incidentPrimitives = new NativeList<int>(Allocator.Temp);
            CollectMultiMapValues(in m_VertexToPrimitives, vertexIndex, incidentPrimitives);

            using var uniquePrimitiveGuard = new NativeHashSet<int>(math_max(1, incidentPrimitives.Length), Allocator.Temp);
            for (int i = 0; i < incidentPrimitives.Length; i++)
            {
                int primitiveIndex = incidentPrimitives[i];
                if (!uniquePrimitiveGuard.Add(primitiveIndex))
                    continue;
                if (!m_Primitives.IsAlive(primitiveIndex))
                    continue;

                RemoveVertexFromPrimitiveAllOccurrencesInternal(primitiveIndex, vertexIndex);
            }

            if (!m_AdjacencyDirty)
            {
                if (pointIndex >= 0)
                    RemoveValueFromMultiMap(ref m_PointToVertices, pointIndex, vertexIndex, removeAllMatches: true);
                m_VertexToPrimitives.Remove(vertexIndex);
            }

            m_VertexToPoint[vertexIndex] = -1;
            bool removed = m_Vertices.Release(vertexIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        public int GetVertexPoint(int vertexIndex)
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return -1;

            return m_VertexToPoint[vertexIndex];
        }

        public bool IsVertexAlive(int vertexIndex) => m_Vertices.IsAlive(vertexIndex);
        public bool IsVertexHandleValid(ElementHandle handle) => m_Vertices.IsHandleValid(handle);
        public ElementHandle GetVertexHandle(int vertexIndex)
            => m_Vertices.IsAlive(vertexIndex) ? new ElementHandle(vertexIndex, m_Vertices.GetGeneration(vertexIndex)) : default;

        public void GetAllValidVertices(NativeList<int> output)
        {
            m_Vertices.GetAliveIndices(output);
        }

        #endregion

        #region Primitive

        public int AddPrimitive(NativeArray<int> vertexIndices)
        {
            return AddPrimitive(vertexIndices, out _);
        }

        public int AddPrimitive(NativeArray<int> vertexIndices, out ElementHandle handle)
        {
            return AddPrimitive(new NativeSlice<int>(vertexIndices), out handle);
        }

        public int AddPrimitive(NativeSlice<int> vertexIndices)
        {
            return AddPrimitive(vertexIndices, out _);
        }

        public int AddPrimitive(NativeSlice<int> vertexIndices, out ElementHandle handle)
        {
            if (vertexIndices.Length < 3)
            {
                handle = default;
                return -1;
            }

            for (int i = 0; i < vertexIndices.Length; i++)
            {
                if (!m_Vertices.IsAlive(vertexIndices[i]))
                {
                    handle = default;
                    return -1;
                }
            }

            int oldCapacity = m_Primitives.Capacity;
            int primitiveIndex = m_Primitives.Allocate(out handle);

            if (m_Primitives.Capacity != oldCapacity)
                m_PrimitiveAttributes.EnsureCapacity(m_Primitives.Capacity);

            m_PrimitiveAttributes.ClearElement(primitiveIndex);
            m_PrimitiveStorage.SetVertices(primitiveIndex, vertexIndices);
            if (!m_AdjacencyDirty)
            {
                EnsureMultiMapCapacity(ref m_VertexToPrimitives, vertexIndices.Length);
                for (int i = 0; i < vertexIndices.Length; i++)
                    m_VertexToPrimitives.Add(vertexIndices[i], primitiveIndex);
            }

            m_TopologyVersion++;
            m_AttributeVersion++;
            return primitiveIndex;
        }

        internal int* GetPrimitiveTriangleDataPointerUnchecked()
        {
            return m_PrimitiveStorage.GetDataPointerUnchecked();
        }

        internal void AllocateDenseTopologyUnchecked(
            int pointCount,
            int vertexCount,
            int primitiveCount,
            bool prepareTriangleStorage,
            bool initializeVertexToPoint = true)
        {
            if (m_Points.Count != 0 || m_Vertices.Count != 0 || m_Primitives.Count != 0)
                throw new InvalidOperationException("AllocateDenseTopologyUnchecked requires empty detail.");

            if (pointCount > 0)
            {
                int oldPointCapacity = m_Points.Capacity;
                m_Points.AllocateDenseRange(pointCount);
                if (m_Points.Capacity != oldPointCapacity)
                    m_PointAttributes.EnsureCapacity(m_Points.Capacity);

                m_PointAttributes.ClearRange(0, pointCount);
            }

            if (vertexCount > 0)
            {
                int oldVertexCapacity = m_Vertices.Capacity;
                m_Vertices.AllocateDenseRange(vertexCount);

                if (m_Vertices.Capacity != oldVertexCapacity)
                    m_VertexAttributes.EnsureCapacity(m_Vertices.Capacity);

                EnsureVertexToPointCapacity(vertexCount);
                if (initializeVertexToPoint)
                    FillNativeListWithMinusOne(m_VertexToPoint, vertexCount);

                m_VertexAttributes.ClearRange(0, vertexCount);
            }

            if (primitiveCount > 0)
            {
                int oldPrimitiveCapacity = m_Primitives.Capacity;
                m_Primitives.AllocateDenseRange(primitiveCount);
                if (m_Primitives.Capacity != oldPrimitiveCapacity)
                    m_PrimitiveAttributes.EnsureCapacity(m_Primitives.Capacity);

                m_PrimitiveAttributes.ClearRange(0, primitiveCount);
            }

            if (prepareTriangleStorage)
                m_PrimitiveStorage.PrepareDenseTriangleRecords(primitiveCount);
            else if (primitiveCount > 0)
                m_PrimitiveStorage.EnsureRecordSlot(primitiveCount - 1);

            m_PointToVertices.Clear();
            m_VertexToPrimitives.Clear();
            m_AdjacencyDirty = true;
        }

        internal void MarkTopologyAndAttributeChanged()
        {
            m_TopologyVersion++;
            m_AttributeVersion++;
            m_AdjacencyDirty = true;
        }

        public bool RemovePrimitive(int primitiveIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return false;

            if (!m_AdjacencyDirty)
            {
                var vertices = m_PrimitiveStorage.GetVertices(primitiveIndex);
                using var uniqueVertices = new NativeHashSet<int>(math_max(1, vertices.Length), Allocator.Temp);
                for (int i = 0; i < vertices.Length; i++)
                {
                    int vertexIndex = vertices[i];
                    if (!uniqueVertices.Add(vertexIndex))
                        continue;

                    RemoveValueFromMultiMap(ref m_VertexToPrimitives, vertexIndex, primitiveIndex, removeAllMatches: true);
                }
            }

            m_PrimitiveStorage.ClearRecord(primitiveIndex);
            bool removed = m_Primitives.Release(primitiveIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        public bool AddVertexToPrimitive(int primitiveIndex, int vertexIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex) || !m_Vertices.IsAlive(vertexIndex))
                return false;

            bool result = m_PrimitiveStorage.AppendVertex(primitiveIndex, vertexIndex);
            if (result)
            {
                if (!m_AdjacencyDirty)
                {
                    EnsureMultiMapCapacity(ref m_VertexToPrimitives, 1);
                    m_VertexToPrimitives.Add(vertexIndex, primitiveIndex);
                }
                m_TopologyVersion++;
            }

            return result;
        }

        public bool RemoveVertexFromPrimitive(int primitiveIndex, int vertexOffset)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return false;

            int removedVertexIndex = m_PrimitiveStorage.GetVertex(primitiveIndex, vertexOffset);
            bool removed = m_PrimitiveStorage.RemoveVertexAt(primitiveIndex, vertexOffset);
            if (!removed)
                return false;

            if (!m_AdjacencyDirty && removedVertexIndex >= 0)
            {
                RemoveValueFromMultiMap(ref m_VertexToPrimitives, removedVertexIndex, primitiveIndex, removeAllMatches: false);
            }

            if (m_PrimitiveStorage.GetLength(primitiveIndex) < 3)
                return RemovePrimitive(primitiveIndex);

            m_TopologyVersion++;
            return true;
        }

        public NativeSlice<int> GetPrimitiveVertices(int primitiveIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return default;

            return m_PrimitiveStorage.GetVertices(primitiveIndex);
        }

        public int GetPrimitiveVertexCount(int primitiveIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return 0;

            return m_PrimitiveStorage.GetLength(primitiveIndex);
        }

        public bool IsPrimitiveAlive(int primitiveIndex) => m_Primitives.IsAlive(primitiveIndex);
        public bool IsPrimitiveHandleValid(ElementHandle handle) => m_Primitives.IsHandleValid(handle);
        public ElementHandle GetPrimitiveHandle(int primitiveIndex)
            => m_Primitives.IsAlive(primitiveIndex) ? new ElementHandle(primitiveIndex, m_Primitives.GetGeneration(primitiveIndex)) : default;

        public void GetAllValidPrimitives(NativeList<int> output)
        {
            m_Primitives.GetAliveIndices(output);
        }

        #endregion

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

        private NativeCompiledDetail CompileSparse(Allocator allocator)
        {
            using var alivePoints = new NativeList<int>(Allocator.Temp);
            using var aliveVertices = new NativeList<int>(Allocator.Temp);
            using var alivePrimitives = new NativeList<int>(Allocator.Temp);

            m_Points.GetAliveIndices(alivePoints);
            m_Vertices.GetAliveIndices(aliveVertices);
            m_Primitives.GetAliveIndices(alivePrimitives);

            int pointCount = alivePoints.Length;
            int vertexCount = aliveVertices.Length;
            int primitiveCount = alivePrimitives.Length;

            var pointRemap = new NativeArray<int>(
                math_max(1, m_Points.MaxIndexExclusive),
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            var vertexRemap = new NativeArray<int>(
                math_max(1, m_Vertices.MaxIndexExclusive),
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            try
            {
                FillWithMinusOne(pointRemap);
                FillWithMinusOne(vertexRemap);

                for (int i = 0; i < pointCount; i++)
                    pointRemap[alivePoints[i]] = i;

                var vertexToPointDense = new NativeArray<int>(vertexCount, allocator, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < vertexCount; i++)
                {
                    int sparseVertex = aliveVertices[i];
                    vertexRemap[sparseVertex] = i;

                    int sparsePoint = m_VertexToPoint[sparseVertex];
                    int densePoint = sparsePoint >= 0 && sparsePoint < pointRemap.Length ? pointRemap[sparsePoint] : -1;
                    vertexToPointDense[i] = densePoint;
                }

                int totalPrimitiveVertexCount = 0;
                bool triangleOnlyTopology = true;
                for (int i = 0; i < primitiveCount; i++)
                {
                    int sparsePrimitive = alivePrimitives[i];
                    int primitiveLength = m_PrimitiveStorage.GetLength(sparsePrimitive);
                    int validCount = 0;

                    for (int k = 0; k < primitiveLength; k++)
                    {
                        int sparseVertex = m_PrimitiveStorage.GetVertexUnchecked(sparsePrimitive, k);
                        if ((uint)sparseVertex < (uint)vertexRemap.Length && vertexRemap[sparseVertex] >= 0)
                            validCount++;
                    }

                    totalPrimitiveVertexCount += validCount;
                    if (validCount != 3)
                        triangleOnlyTopology = false;
                }

                var primitiveOffsetsDense = new NativeArray<int>(primitiveCount + 1, allocator, NativeArrayOptions.UninitializedMemory);
                var primitiveVerticesDense = new NativeArray<int>(totalPrimitiveVertexCount, allocator, NativeArrayOptions.UninitializedMemory);

                int runningOffset = 0;
                for (int i = 0; i < primitiveCount; i++)
                {
                    primitiveOffsetsDense[i] = runningOffset;

                    int sparsePrimitive = alivePrimitives[i];
                    int primitiveLength = m_PrimitiveStorage.GetLength(sparsePrimitive);
                    for (int k = 0; k < primitiveLength; k++)
                    {
                        int sparseVertex = m_PrimitiveStorage.GetVertexUnchecked(sparsePrimitive, k);
                        if ((uint)sparseVertex >= (uint)vertexRemap.Length)
                            continue;

                        int denseVertex = vertexRemap[sparseVertex];
                        if (denseVertex >= 0)
                            primitiveVerticesDense[runningOffset++] = denseVertex;
                    }
                }

                primitiveOffsetsDense[primitiveCount] = runningOffset;

                var pointAttributesCompiled = CompileAttributes(m_PointAttributes, alivePoints, allocator);
                var vertexAttributesCompiled = CompileAttributes(m_VertexAttributes, aliveVertices, allocator);
                var primitiveAttributesCompiled = CompileAttributes(m_PrimitiveAttributes, alivePrimitives, allocator);
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

        #region Utility

        public void Clear()
        {
            m_Points.Clear();
            m_Vertices.Clear();
            m_Primitives.Clear();

            m_PrimitiveStorage.Clear();
            FillNativeListWithMinusOne(m_VertexToPoint, m_VertexToPoint.Length);
            m_PointToVertices.Clear();
            m_VertexToPrimitives.Clear();
            m_AdjacencyDirty = false;

            m_Resources.Clear();

            m_TopologyVersion++;
            m_AttributeVersion++;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_Points.Dispose();
            m_Vertices.Dispose();
            m_Primitives.Dispose();

            if (m_VertexToPoint.IsCreated)
                m_VertexToPoint.Dispose();

            m_PrimitiveStorage.Dispose();
            if (m_PointToVertices.IsCreated)
                m_PointToVertices.Dispose();
            if (m_VertexToPrimitives.IsCreated)
                m_VertexToPrimitives.Dispose();

            m_PointAttributes.Dispose();
            m_VertexAttributes.Dispose();
            m_PrimitiveAttributes.Dispose();

            m_Resources.Dispose();

            m_IsDisposed = true;
        }

        private void EnsureVertexToPointCapacity(int required)
        {
            while (m_VertexToPoint.Length < required)
                m_VertexToPoint.Add(-1);
        }

        private void EnsureAdjacencyUpToDate()
        {
            if (!m_AdjacencyDirty)
                return;

            m_PointToVertices.Clear();
            m_VertexToPrimitives.Clear();

            EnsureMultiMapCapacity(ref m_PointToVertices, m_Vertices.Count);
            EnsureMultiMapCapacity(ref m_VertexToPrimitives, m_PrimitiveStorage.TotalVertexCount);

            using var aliveVertices = new NativeList<int>(Allocator.Temp);
            m_Vertices.GetAliveIndices(aliveVertices);
            for (int i = 0; i < aliveVertices.Length; i++)
            {
                int vertexIndex = aliveVertices[i];
                int pointIndex = m_VertexToPoint[vertexIndex];
                if (!m_Points.IsAlive(pointIndex))
                    continue;

                m_PointToVertices.Add(pointIndex, vertexIndex);
            }

            using var alivePrimitives = new NativeList<int>(Allocator.Temp);
            m_Primitives.GetAliveIndices(alivePrimitives);
            for (int i = 0; i < alivePrimitives.Length; i++)
            {
                int primitiveIndex = alivePrimitives[i];
                var vertices = m_PrimitiveStorage.GetVertices(primitiveIndex);
                for (int k = 0; k < vertices.Length; k++)
                {
                    int vertexIndex = vertices[k];
                    if (!m_Vertices.IsAlive(vertexIndex))
                        continue;

                    m_VertexToPrimitives.Add(vertexIndex, primitiveIndex);
                }
            }

            m_AdjacencyDirty = false;
        }

        private void RemoveVertexFromPrimitiveAllOccurrencesInternal(int primitiveIndex, int vertexIndex)
        {
            var vertices = m_PrimitiveStorage.GetVertices(primitiveIndex);
            bool removedAny = false;
            for (int k = vertices.Length - 1; k >= 0; k--)
            {
                if (vertices[k] != vertexIndex)
                    continue;

                m_PrimitiveStorage.RemoveVertexAt(primitiveIndex, k);
                removedAny = true;
            }

            if (!removedAny)
                return;

            if (!m_AdjacencyDirty)
                RemoveValueFromMultiMap(ref m_VertexToPrimitives, vertexIndex, primitiveIndex, removeAllMatches: true);

            if (m_PrimitiveStorage.GetLength(primitiveIndex) < 3)
                RemovePrimitive(primitiveIndex);
        }

        private static void CollectMultiMapValues(
            in NativeParallelMultiHashMap<int, int> map,
            int key,
            NativeList<int> output)
        {
            output.Clear();

            if (!map.TryGetFirstValue(key, out int value, out NativeParallelMultiHashMapIterator<int> iterator))
                return;

            do
            {
                output.Add(value);
            }
            while (map.TryGetNextValue(out value, ref iterator));
        }

        private static void RemoveValueFromMultiMap(
            ref NativeParallelMultiHashMap<int, int> map,
            int key,
            int valueToRemove,
            bool removeAllMatches)
        {
            if (!map.TryGetFirstValue(key, out int value, out NativeParallelMultiHashMapIterator<int> iterator))
                return;

            using var valuesToKeep = new NativeList<int>(Allocator.Temp);
            bool removed = false;

            do
            {
                if (value == valueToRemove && (removeAllMatches || !removed))
                {
                    removed = true;
                    continue;
                }

                valuesToKeep.Add(value);
            }
            while (map.TryGetNextValue(out value, ref iterator));

            map.Remove(key);
            if (valuesToKeep.Length <= 0)
                return;

            EnsureMultiMapCapacity(ref map, valuesToKeep.Length);
            for (int i = 0; i < valuesToKeep.Length; i++)
                map.Add(key, valuesToKeep[i]);
        }

        private static void EnsureMultiMapCapacity(ref NativeParallelMultiHashMap<int, int> map, int additionalEntries)
        {
            if (additionalEntries <= 0)
                return;

            int required = map.Count() + additionalEntries;
            if (required <= map.Capacity)
                return;

            int newCapacity = math_max(1, map.Capacity);
            while (newCapacity < required)
                newCapacity *= 2;

            map.Capacity = newCapacity;
        }

        private static unsafe CompiledAttributeSet CompileAttributes(
            AttributeStore source,
            NativeList<int> aliveSparseIndices,
            Allocator allocator)
        {
            int attributeCount = source.GetColumnCount();
            int elementCount = aliveSparseIndices.Length;

            var descriptors = new NativeArray<CompiledAttributeDescriptor>(attributeCount, allocator);
            var idToDescriptor = new NativeParallelHashMap<int, int>(math_max(1, attributeCount), allocator);

            int totalBytes = 0;
            for (int i = 0; i < attributeCount; i++)
            {
                var column = source.GetColumnAt(i);
                totalBytes += column.Stride * elementCount;
            }

            var data = new NativeArray<byte>(math_max(1, totalBytes), allocator, NativeArrayOptions.UninitializedMemory);
            byte* basePtr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(data);

            int runningOffset = 0;
            for (int i = 0; i < attributeCount; i++)
            {
                var column = source.GetColumnAt(i);

                descriptors[i] = new CompiledAttributeDescriptor
                {
                    AttributeId = column.AttributeId,
                    TypeHash = column.TypeHash,
                    Stride = column.Stride,
                    Count = elementCount,
                    OffsetBytes = runningOffset
                };

                idToDescriptor[column.AttributeId] = i;

                for (int k = 0; k < elementCount; k++)
                {
                    int sparseIndex = aliveSparseIndices[k];
                    byte* src = column.Buffer.Ptr + sparseIndex * column.Stride;
                    byte* dst = basePtr + runningOffset + k * column.Stride;
                    UnsafeUtility.MemCpy(dst, src, column.Stride);
                }

                runningOffset += column.Stride * elementCount;
            }

            return new CompiledAttributeSet(descriptors, idToDescriptor, data);
        }

        private static unsafe CompiledAttributeSet CompileAttributesDense(
            AttributeStore source,
            int elementCount,
            Allocator allocator)
        {
            int attributeCount = source.GetColumnCount();

            var descriptors = new NativeArray<CompiledAttributeDescriptor>(attributeCount, allocator);
            var idToDescriptor = new NativeParallelHashMap<int, int>(math_max(1, attributeCount), allocator);

            int totalBytes = 0;
            for (int i = 0; i < attributeCount; i++)
            {
                var column = source.GetColumnAt(i);
                totalBytes += column.Stride * elementCount;
            }

            var data = new NativeArray<byte>(math_max(1, totalBytes), allocator, NativeArrayOptions.UninitializedMemory);
            byte* basePtr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(data);

            int runningOffset = 0;
            for (int i = 0; i < attributeCount; i++)
            {
                var column = source.GetColumnAt(i);
                int copyBytes = column.Stride * elementCount;

                descriptors[i] = new CompiledAttributeDescriptor
                {
                    AttributeId = column.AttributeId,
                    TypeHash = column.TypeHash,
                    Stride = column.Stride,
                    Count = elementCount,
                    OffsetBytes = runningOffset
                };

                idToDescriptor[column.AttributeId] = i;

                if (copyBytes > 0)
                    UnsafeUtility.MemCpy(basePtr + runningOffset, column.Buffer.Ptr, copyBytes);

                runningOffset += copyBytes;
            }

            return new CompiledAttributeSet(descriptors, idToDescriptor, data);
        }

        private static void FillWithMinusOne(NativeArray<int> array)
        {
            for (int i = 0; i < array.Length; i++)
                array[i] = -1;
        }

        private static unsafe void FillNativeArrayWithMinusOne(NativeArray<int> array)
        {
            if (array.Length <= 0)
                return;

            UnsafeUtility.MemSet(NativeArrayUnsafeUtility.GetUnsafePtr(array), 0xFF, array.Length * UnsafeUtility.SizeOf<int>());
        }

        private static unsafe void FillNativeListWithMinusOne(NativeList<int> list, int length)
        {
            if (length <= 0)
                return;

            var array = list.AsArray();
            UnsafeUtility.MemSet(NativeArrayUnsafeUtility.GetUnsafePtr(array), 0xFF, length * UnsafeUtility.SizeOf<int>());
        }

        private static int math_max(int a, int b) => a > b ? a : b;

        #endregion
    }
}
