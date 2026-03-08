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
        public int PrimitiveGarbageLength => m_PrimitiveStorage.GarbageLength;
        public bool PrimitiveHasGarbage => m_PrimitiveStorage.HasGarbage;

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

        public bool CanRemovePoint(
            int pointIndex,
            PointDeletePolicy pointPolicy = PointDeletePolicy.DeleteIncidentVertices,
            VertexDeletePolicy vertexPolicy = VertexDeletePolicy.RemoveFromIncidentPrimitives)
        {
            if (!m_Points.IsAlive(pointIndex))
                return false;

            if (pointPolicy != PointDeletePolicy.DeleteIncidentVertices &&
                pointPolicy != PointDeletePolicy.FailIfIncidentVerticesExist)
                return false;

            if (vertexPolicy != VertexDeletePolicy.RemoveFromIncidentPrimitives &&
                vertexPolicy != VertexDeletePolicy.DeleteIncidentPrimitives &&
                vertexPolicy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return false;

            if (m_AdjacencyDirty)
                return CanRemovePointWhenAdjacencyDirty(pointIndex, pointPolicy, vertexPolicy);

            EnsureAdjacencyUpToDate();
            using var pointVertices = new NativeList<int>(Allocator.Temp);
            CollectMultiMapValues(in m_PointToVertices, pointIndex, pointVertices);

            if (pointPolicy == PointDeletePolicy.FailIfIncidentVerticesExist)
            {
                for (int i = 0; i < pointVertices.Length; i++)
                {
                    if (m_Vertices.IsAlive(pointVertices[i]))
                        return false;
                }

                return true;
            }

            if (vertexPolicy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return true;

            for (int i = 0; i < pointVertices.Length; i++)
            {
                int vertexIndex = pointVertices[i];
                if (!m_Vertices.IsAlive(vertexIndex))
                    continue;
                if (HasIncidentPrimitivesNoPrepare(vertexIndex))
                    return false;
            }

            return true;
        }

        public bool RemovePoint(int pointIndex)
        {
            return RemovePoint(pointIndex, PointDeletePolicy.DeleteIncidentVertices, VertexDeletePolicy.RemoveFromIncidentPrimitives);
        }

        public bool RemovePoint(
            int pointIndex,
            PointDeletePolicy pointPolicy,
            VertexDeletePolicy vertexPolicy = VertexDeletePolicy.RemoveFromIncidentPrimitives)
        {
            if (!m_Points.IsAlive(pointIndex))
                return false;

            if (pointPolicy != PointDeletePolicy.DeleteIncidentVertices &&
                pointPolicy != PointDeletePolicy.FailIfIncidentVerticesExist)
                return false;

            if (vertexPolicy != VertexDeletePolicy.RemoveFromIncidentPrimitives &&
                vertexPolicy != VertexDeletePolicy.DeleteIncidentPrimitives &&
                vertexPolicy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return false;

            if (m_AdjacencyDirty)
                return RemovePointWhenAdjacencyDirty(pointIndex, pointPolicy, vertexPolicy);

            EnsureAdjacencyUpToDate();
            using var pointVertices = new NativeList<int>(Allocator.Temp);
            CollectMultiMapValues(in m_PointToVertices, pointIndex, pointVertices);

            if (pointPolicy == PointDeletePolicy.FailIfIncidentVerticesExist)
            {
                for (int i = 0; i < pointVertices.Length; i++)
                {
                    if (m_Vertices.IsAlive(pointVertices[i]))
                        return false;
                }
            }
            else if (vertexPolicy == VertexDeletePolicy.FailIfIncidentPrimitivesExist)
            {
                for (int i = 0; i < pointVertices.Length; i++)
                {
                    int vertexIndex = pointVertices[i];
                    if (!m_Vertices.IsAlive(vertexIndex))
                        continue;
                    if (HasIncidentPrimitivesNoPrepare(vertexIndex))
                        return false;
                }
            }

            if (pointPolicy == PointDeletePolicy.DeleteIncidentVertices)
            {
                for (int i = 0; i < pointVertices.Length; i++)
                {
                    int vertexIndex = pointVertices[i];
                    if (!m_Vertices.IsAlive(vertexIndex))
                        continue;
                    if (!RemoveVertexInternal(vertexIndex, vertexPolicy, adjacencyPrepared: true))
                        return false;
                }
            }

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

            EnsureAdjacencyUpToDate();

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
            EnsureMultiMapCapacity(ref m_PointToVertices, 1);
            m_PointToVertices.Add(pointIndex, vertexIndex);

            m_TopologyVersion++;
            m_AttributeVersion++;
            return vertexIndex;
        }

        internal int* GetVertexToPointPointerUnchecked()
        {
            var vertexToPointArray = m_VertexToPoint.AsArray();
            return (int*)NativeArrayUnsafeUtility.GetUnsafePtr(vertexToPointArray);
        }

        public bool CanRemoveVertex(
            int vertexIndex,
            VertexDeletePolicy policy = VertexDeletePolicy.RemoveFromIncidentPrimitives)
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return false;

            if (policy == VertexDeletePolicy.RemoveFromIncidentPrimitives ||
                policy == VertexDeletePolicy.DeleteIncidentPrimitives)
                return true;
            if (policy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return false;

            if (m_AdjacencyDirty)
                return !HasIncidentPrimitivesByScan(vertexIndex);

            EnsureAdjacencyUpToDate();
            return !HasIncidentPrimitivesNoPrepare(vertexIndex);
        }

        public bool RemoveVertex(int vertexIndex)
        {
            return RemoveVertex(vertexIndex, VertexDeletePolicy.RemoveFromIncidentPrimitives);
        }

        public bool RemoveVertex(
            int vertexIndex,
            VertexDeletePolicy policy = VertexDeletePolicy.RemoveFromIncidentPrimitives)
        {
            return RemoveVertexInternal(vertexIndex, policy, adjacencyPrepared: false);
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

            EnsureAdjacencyUpToDate();

            int oldCapacity = m_Primitives.Capacity;
            int primitiveIndex = m_Primitives.Allocate(out handle);

            if (m_Primitives.Capacity != oldCapacity)
                m_PrimitiveAttributes.EnsureCapacity(m_Primitives.Capacity);

            m_PrimitiveAttributes.ClearElement(primitiveIndex);
            m_PrimitiveStorage.SetVertices(primitiveIndex, vertexIndices);
            EnsureMultiMapCapacity(ref m_VertexToPrimitives, vertexIndices.Length);
            for (int i = 0; i < vertexIndices.Length; i++)
            {
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
            InvalidateAdjacency();
        }

        internal void MarkTopologyAndAttributeChanged()
        {
            m_TopologyVersion++;
            m_AttributeVersion++;
            m_PointToVertices.Clear();
            m_VertexToPrimitives.Clear();
            InvalidateAdjacency();
        }

        public bool RemovePrimitive(int primitiveIndex)
        {
            return RemovePrimitiveInternal(primitiveIndex, adjacencyPrepared: false);
        }

        public bool AddVertexToPrimitive(int primitiveIndex, int vertexIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex) || !m_Vertices.IsAlive(vertexIndex))
                return false;

            EnsureAdjacencyUpToDate();

            bool result = m_PrimitiveStorage.AppendVertex(primitiveIndex, vertexIndex);
            if (result)
            {
                EnsureMultiMapCapacity(ref m_VertexToPrimitives, 1);
                m_VertexToPrimitives.Add(vertexIndex, primitiveIndex);
                m_TopologyVersion++;
            }

            return result;
        }

        public bool RemoveVertexFromPrimitive(int primitiveIndex, int vertexOffset)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return false;

            EnsureAdjacencyUpToDate();

            int removedVertexIndex = m_PrimitiveStorage.GetVertex(primitiveIndex, vertexOffset);
            bool removed = m_PrimitiveStorage.RemoveVertexAt(primitiveIndex, vertexOffset);
            if (!removed)
                return false;

            if (removedVertexIndex >= 0)
                RemoveValueFromMultiMap(ref m_VertexToPrimitives, removedVertexIndex, primitiveIndex, removeAllMatches: false);

            if (m_PrimitiveStorage.GetLength(primitiveIndex) < 3)
                return RemovePrimitiveInternal(primitiveIndex, adjacencyPrepared: true);

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

        public bool CollectGarbage(float minGarbageRatio = 0f)
        {
            return m_PrimitiveStorage.CollectGarbage(minGarbageRatio);
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

        private unsafe NativeCompiledDetail CompileSparse(Allocator allocator)
        {
            using var alivePoints = new NativeList<int>(Allocator.Temp);
            using var aliveVertices = new NativeList<int>(Allocator.Temp);
            using var alivePrimitives = new NativeList<int>(Allocator.Temp);

            if (m_Points.IsDenseContiguous)
                FillSequentialIndices(alivePoints, m_Points.Count);
            else
                m_Points.GetAliveIndices(alivePoints);

            if (m_Vertices.IsDenseContiguous)
                FillSequentialIndices(aliveVertices, m_Vertices.Count);
            else
                m_Vertices.GetAliveIndices(aliveVertices);

            if (m_Primitives.IsDenseContiguous)
                FillSequentialIndices(alivePrimitives, m_Primitives.Count);
            else
                m_Primitives.GetAliveIndices(alivePrimitives);

            int pointCount = alivePoints.Length;
            int vertexCount = aliveVertices.Length;
            int primitiveCount = alivePrimitives.Length;

            bool pointsIdentityRemap = m_Points.IsDenseContiguous && m_Points.MaxIndexExclusive == pointCount;
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
                if (!pointsIdentityRemap)
                {
                    for (int i = 0; i < pointCount; i++)
                        pointRemap[alivePoints[i]] = i + 1;
                }

                var vertexToPointDense = new NativeArray<int>(vertexCount, allocator, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < vertexCount; i++)
                {
                    int sparseVertex = aliveVertices[i];
                    vertexRemap[sparseVertex] = i + 1;

                    int sparsePoint = m_VertexToPoint[sparseVertex];
                    int densePoint = -1;
                    if (pointsIdentityRemap)
                    {
                        if ((uint)sparsePoint < (uint)pointCount)
                            densePoint = sparsePoint;
                    }
                    else if ((uint)sparsePoint < (uint)pointRemap.Length)
                    {
                        int remappedPoint = pointRemap[sparsePoint];
                        if (remappedPoint != 0)
                            densePoint = remappedPoint - 1;
                    }

                    vertexToPointDense[i] = densePoint;
                }

                int totalPrimitiveVertexCount = 0;
                bool triangleOnlyTopology = true;
                if (m_Primitives.IsDenseContiguous && m_PrimitiveStorage.IsDenseTriangleLayout)
                {
                    int* primitiveDataPtr = m_PrimitiveStorage.GetDataPointerUnchecked();
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        int validCount = 0;

                        int v0 = primitiveDataPtr[triStart];
                        int v1 = primitiveDataPtr[triStart + 1];
                        int v2 = primitiveDataPtr[triStart + 2];

                        if ((uint)v0 < (uint)vertexRemap.Length && vertexRemap[v0] != 0)
                            validCount++;
                        if ((uint)v1 < (uint)vertexRemap.Length && vertexRemap[v1] != 0)
                            validCount++;
                        if ((uint)v2 < (uint)vertexRemap.Length && vertexRemap[v2] != 0)
                            validCount++;

                        totalPrimitiveVertexCount += validCount;
                        if (validCount != 3)
                            triangleOnlyTopology = false;
                    }
                }
                else
                {
                    for (int i = 0; i < primitiveCount; i++)
                    {
                        int sparsePrimitive = alivePrimitives[i];
                        PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(sparsePrimitive);
                        int validCount = 0;

                        for (int k = 0; k < record.Length; k++)
                        {
                            int sparseVertex = m_PrimitiveStorage.GetVertexUnchecked(record, k);
                            if ((uint)sparseVertex < (uint)vertexRemap.Length && vertexRemap[sparseVertex] != 0)
                                validCount++;
                        }

                        totalPrimitiveVertexCount += validCount;
                        if (validCount != 3)
                            triangleOnlyTopology = false;
                    }
                }

                var primitiveOffsetsDense = new NativeArray<int>(primitiveCount + 1, allocator, NativeArrayOptions.UninitializedMemory);
                var primitiveVerticesDense = new NativeArray<int>(totalPrimitiveVertexCount, allocator, NativeArrayOptions.UninitializedMemory);

                int runningOffset = 0;
                if (m_Primitives.IsDenseContiguous && m_PrimitiveStorage.IsDenseTriangleLayout)
                {
                    int* primitiveDataPtr = m_PrimitiveStorage.GetDataPointerUnchecked();
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        primitiveOffsetsDense[primitiveIndex] = runningOffset;
                        int triStart = primitiveIndex * 3;

                        int v0 = primitiveDataPtr[triStart];
                        int v1 = primitiveDataPtr[triStart + 1];
                        int v2 = primitiveDataPtr[triStart + 2];

                        if ((uint)v0 < (uint)vertexRemap.Length)
                        {
                            int remapped = vertexRemap[v0];
                            if (remapped != 0)
                                primitiveVerticesDense[runningOffset++] = remapped - 1;
                        }

                        if ((uint)v1 < (uint)vertexRemap.Length)
                        {
                            int remapped = vertexRemap[v1];
                            if (remapped != 0)
                                primitiveVerticesDense[runningOffset++] = remapped - 1;
                        }

                        if ((uint)v2 < (uint)vertexRemap.Length)
                        {
                            int remapped = vertexRemap[v2];
                            if (remapped != 0)
                                primitiveVerticesDense[runningOffset++] = remapped - 1;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < primitiveCount; i++)
                    {
                        primitiveOffsetsDense[i] = runningOffset;

                        int sparsePrimitive = alivePrimitives[i];
                        PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(sparsePrimitive);
                        for (int k = 0; k < record.Length; k++)
                        {
                            int sparseVertex = m_PrimitiveStorage.GetVertexUnchecked(record, k);
                            if ((uint)sparseVertex >= (uint)vertexRemap.Length)
                                continue;

                            int remapped = vertexRemap[sparseVertex];
                            if (remapped != 0)
                                primitiveVerticesDense[runningOffset++] = remapped - 1;
                        }
                    }
                }

                primitiveOffsetsDense[primitiveCount] = runningOffset;

                var pointAttributesCompiled = m_Points.IsDenseContiguous
                    ? CompileAttributesDense(m_PointAttributes, pointCount, allocator)
                    : CompileAttributes(m_PointAttributes, alivePoints, allocator);
                var vertexAttributesCompiled = m_Vertices.IsDenseContiguous
                    ? CompileAttributesDense(m_VertexAttributes, vertexCount, allocator)
                    : CompileAttributes(m_VertexAttributes, aliveVertices, allocator);
                var primitiveAttributesCompiled = m_Primitives.IsDenseContiguous
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

        private void InvalidateAdjacency()
        {
            m_AdjacencyDirty = true;
        }

        private void CollectPointVerticesByScan(int pointIndex, NativeList<int> output)
        {
            output.Clear();

            if (m_Vertices.IsDenseContiguous)
            {
                int vertexCount = m_Vertices.Count;
                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    if (m_VertexToPoint[vertexIndex] == pointIndex)
                        output.Add(vertexIndex);
                }

                return;
            }

            using var aliveVertices = new NativeList<int>(Allocator.Temp);
            m_Vertices.GetAliveIndices(aliveVertices);
            for (int i = 0; i < aliveVertices.Length; i++)
            {
                int vertexIndex = aliveVertices[i];
                if (m_VertexToPoint[vertexIndex] == pointIndex)
                    output.Add(vertexIndex);
            }
        }

        private void CollectIncidentPrimitivesByScan(int vertexIndex, NativeList<int> output)
        {
            output.Clear();

            if (m_Primitives.IsDenseContiguous)
            {
                int primitiveCount = m_Primitives.Count;
                if (m_PrimitiveStorage.IsDenseTriangleLayout)
                {
                    int* primitiveData = m_PrimitiveStorage.GetDataPointerUnchecked();
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        if (primitiveData[triStart] == vertexIndex ||
                            primitiveData[triStart + 1] == vertexIndex ||
                            primitiveData[triStart + 2] == vertexIndex)
                        {
                            output.Add(primitiveIndex);
                        }
                    }

                    return;
                }

                for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                {
                    PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
                    for (int k = 0; k < record.Length; k++)
                    {
                        if (m_PrimitiveStorage.GetVertexUnchecked(record, k) != vertexIndex)
                            continue;

                        output.Add(primitiveIndex);
                        break;
                    }
                }

                return;
            }

            using var alivePrimitives = new NativeList<int>(Allocator.Temp);
            m_Primitives.GetAliveIndices(alivePrimitives);
            for (int i = 0; i < alivePrimitives.Length; i++)
            {
                int primitiveIndex = alivePrimitives[i];
                var vertices = m_PrimitiveStorage.GetVertices(primitiveIndex);
                for (int k = 0; k < vertices.Length; k++)
                {
                    if (vertices[k] != vertexIndex)
                        continue;

                    output.Add(primitiveIndex);
                    break;
                }
            }
        }

        private bool HasIncidentPrimitivesByScan(int vertexIndex)
        {
            if (m_Primitives.IsDenseContiguous)
            {
                int primitiveCount = m_Primitives.Count;
                if (m_PrimitiveStorage.IsDenseTriangleLayout)
                {
                    int* primitiveData = m_PrimitiveStorage.GetDataPointerUnchecked();
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        if (primitiveData[triStart] == vertexIndex ||
                            primitiveData[triStart + 1] == vertexIndex ||
                            primitiveData[triStart + 2] == vertexIndex)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                {
                    PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
                    for (int k = 0; k < record.Length; k++)
                    {
                        if (m_PrimitiveStorage.GetVertexUnchecked(record, k) == vertexIndex)
                            return true;
                    }
                }

                return false;
            }

            using var alivePrimitives = new NativeList<int>(Allocator.Temp);
            m_Primitives.GetAliveIndices(alivePrimitives);
            for (int i = 0; i < alivePrimitives.Length; i++)
            {
                int primitiveIndex = alivePrimitives[i];
                var vertices = m_PrimitiveStorage.GetVertices(primitiveIndex);
                for (int k = 0; k < vertices.Length; k++)
                {
                    if (vertices[k] == vertexIndex)
                        return true;
                }
            }

            return false;
        }

        private bool CanRemovePointWhenAdjacencyDirty(
            int pointIndex,
            PointDeletePolicy pointPolicy,
            VertexDeletePolicy vertexPolicy)
        {
            using var pointVertices = new NativeList<int>(Allocator.Temp);
            CollectPointVerticesByScan(pointIndex, pointVertices);

            if (pointPolicy == PointDeletePolicy.FailIfIncidentVerticesExist)
                return pointVertices.Length <= 0;

            if (vertexPolicy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return true;

            for (int i = 0; i < pointVertices.Length; i++)
            {
                if (HasIncidentPrimitivesByScan(pointVertices[i]))
                    return false;
            }

            return true;
        }

        private bool RemovePointWhenAdjacencyDirty(
            int pointIndex,
            PointDeletePolicy pointPolicy,
            VertexDeletePolicy vertexPolicy)
        {
            using var pointVertices = new NativeList<int>(Allocator.Temp);
            CollectPointVerticesByScan(pointIndex, pointVertices);

            if (pointPolicy == PointDeletePolicy.FailIfIncidentVerticesExist && pointVertices.Length > 0)
                return false;

            if (pointPolicy == PointDeletePolicy.DeleteIncidentVertices)
            {
                if (vertexPolicy == VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                {
                    for (int i = 0; i < pointVertices.Length; i++)
                    {
                        if (HasIncidentPrimitivesByScan(pointVertices[i]))
                            return false;
                    }
                }

                for (int i = 0; i < pointVertices.Length; i++)
                {
                    int vertexIndex = pointVertices[i];
                    if (!m_Vertices.IsAlive(vertexIndex))
                        continue;

                    if (!RemoveVertexWhenAdjacencyDirty(vertexIndex, vertexPolicy))
                        return false;
                }
            }

            bool removed = m_Points.Release(pointIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private bool RemoveVertexWhenAdjacencyDirty(int vertexIndex, VertexDeletePolicy policy)
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return false;

            if (policy != VertexDeletePolicy.RemoveFromIncidentPrimitives &&
                policy != VertexDeletePolicy.DeleteIncidentPrimitives &&
                policy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return false;

            if (policy == VertexDeletePolicy.FailIfIncidentPrimitivesExist && HasIncidentPrimitivesByScan(vertexIndex))
                return false;

            if (policy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
            {
                using var incidentPrimitives = new NativeList<int>(Allocator.Temp);
                CollectIncidentPrimitivesByScan(vertexIndex, incidentPrimitives);

                for (int i = 0; i < incidentPrimitives.Length; i++)
                {
                    int primitiveIndex = incidentPrimitives[i];
                    if (!m_Primitives.IsAlive(primitiveIndex))
                        continue;

                    if (policy == VertexDeletePolicy.DeleteIncidentPrimitives)
                        RemovePrimitiveWhenAdjacencyDirty(primitiveIndex);
                    else
                        RemoveVertexFromPrimitiveAllOccurrencesWhenAdjacencyDirty(primitiveIndex, vertexIndex);
                }
            }

            m_VertexToPoint[vertexIndex] = -1;
            bool removed = m_Vertices.Release(vertexIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private bool RemovePrimitiveWhenAdjacencyDirty(int primitiveIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return false;

            m_PrimitiveStorage.ClearRecord(primitiveIndex);
            bool removed = m_Primitives.Release(primitiveIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private void RemoveVertexFromPrimitiveAllOccurrencesWhenAdjacencyDirty(int primitiveIndex, int vertexIndex)
        {
            PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
            bool removedAny = false;
            for (int k = record.Length - 1; k >= 0; k--)
            {
                if (m_PrimitiveStorage.GetVertexUnchecked(record, k) != vertexIndex)
                    continue;

                m_PrimitiveStorage.RemoveVertexAt(primitiveIndex, k);
                removedAny = true;
            }

            if (!removedAny)
                return;

            if (m_PrimitiveStorage.GetLength(primitiveIndex) < 3)
                RemovePrimitiveWhenAdjacencyDirty(primitiveIndex);
        }

        private bool HasIncidentPrimitivesNoPrepare(int vertexIndex)
        {
            if (!m_VertexToPrimitives.TryGetFirstValue(vertexIndex, out int primitiveIndex, out NativeParallelMultiHashMapIterator<int> iterator))
                return false;

            do
            {
                if (m_Primitives.IsAlive(primitiveIndex))
                    return true;
            }
            while (m_VertexToPrimitives.TryGetNextValue(out primitiveIndex, ref iterator));

            return false;
        }

        private bool RemoveVertexInternal(int vertexIndex, VertexDeletePolicy policy, bool adjacencyPrepared)
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return false;

            if (!adjacencyPrepared && m_AdjacencyDirty)
                return RemoveVertexWhenAdjacencyDirty(vertexIndex, policy);

            if (!adjacencyPrepared)
                EnsureAdjacencyUpToDate();

            if (policy == VertexDeletePolicy.FailIfIncidentPrimitivesExist && HasIncidentPrimitivesNoPrepare(vertexIndex))
                return false;

            if (policy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
            {
                if (policy != VertexDeletePolicy.DeleteIncidentPrimitives &&
                    policy != VertexDeletePolicy.RemoveFromIncidentPrimitives)
                    return false;

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

                    if (policy == VertexDeletePolicy.DeleteIncidentPrimitives)
                        RemovePrimitiveInternal(primitiveIndex, adjacencyPrepared: true);
                    else
                        RemoveVertexFromPrimitiveAllOccurrencesInternal(primitiveIndex, vertexIndex, adjacencyPrepared: true);
                }
            }

            int pointIndex = m_VertexToPoint[vertexIndex];
            if (pointIndex >= 0)
                RemoveValueFromMultiMap(ref m_PointToVertices, pointIndex, vertexIndex, removeAllMatches: true);

            m_VertexToPrimitives.Remove(vertexIndex);
            m_VertexToPoint[vertexIndex] = -1;
            bool removed = m_Vertices.Release(vertexIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private bool RemovePrimitiveInternal(int primitiveIndex, bool adjacencyPrepared)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return false;

            if (!adjacencyPrepared)
                EnsureAdjacencyUpToDate();

            var vertices = m_PrimitiveStorage.GetVertices(primitiveIndex);
            using var uniqueVertices = new NativeHashSet<int>(math_max(1, vertices.Length), Allocator.Temp);
            for (int i = 0; i < vertices.Length; i++)
            {
                int vertexIndex = vertices[i];
                if (!uniqueVertices.Add(vertexIndex))
                    continue;

                RemoveValueFromMultiMap(ref m_VertexToPrimitives, vertexIndex, primitiveIndex, removeAllMatches: true);
            }

            m_PrimitiveStorage.ClearRecord(primitiveIndex);
            bool removed = m_Primitives.Release(primitiveIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private void EnsureAdjacencyUpToDate()
        {
            if (!m_AdjacencyDirty)
                return;

            m_PointToVertices.Clear();
            m_VertexToPrimitives.Clear();

            EnsureMultiMapCapacity(ref m_PointToVertices, m_Vertices.Count);
            EnsureMultiMapCapacity(ref m_VertexToPrimitives, m_PrimitiveStorage.TotalVertexCount);

            if (m_Points.IsDenseContiguous && m_Vertices.IsDenseContiguous && m_Primitives.IsDenseContiguous)
            {
                int pointCount = m_Points.Count;
                int vertexCount = m_Vertices.Count;
                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    int pointIndex = m_VertexToPoint[vertexIndex];
                    if ((uint)pointIndex < (uint)pointCount)
                        m_PointToVertices.Add(pointIndex, vertexIndex);
                }

                int primitiveCount = m_Primitives.Count;
                if (m_PrimitiveStorage.IsDenseTriangleLayout)
                {
                    int* primitiveData = m_PrimitiveStorage.GetDataPointerUnchecked();
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        m_VertexToPrimitives.Add(primitiveData[triStart], primitiveIndex);
                        m_VertexToPrimitives.Add(primitiveData[triStart + 1], primitiveIndex);
                        m_VertexToPrimitives.Add(primitiveData[triStart + 2], primitiveIndex);
                    }
                }
                else
                {
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
                        for (int k = 0; k < record.Length; k++)
                            m_VertexToPrimitives.Add(m_PrimitiveStorage.GetVertexUnchecked(record, k), primitiveIndex);
                    }
                }

                m_AdjacencyDirty = false;
                return;
            }

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
                PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
                for (int k = 0; k < record.Length; k++)
                {
                    int vertexIndex = m_PrimitiveStorage.GetVertexUnchecked(record, k);
                    if (!m_Vertices.IsAlive(vertexIndex))
                        continue;

                    m_VertexToPrimitives.Add(vertexIndex, primitiveIndex);
                }
            }

            m_AdjacencyDirty = false;
        }

        private void RemoveVertexFromPrimitiveAllOccurrencesInternal(int primitiveIndex, int vertexIndex, bool adjacencyPrepared)
        {
            if (!adjacencyPrepared)
                EnsureAdjacencyUpToDate();

            PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
            bool removedAny = false;
            for (int k = record.Length - 1; k >= 0; k--)
            {
                if (m_PrimitiveStorage.GetVertexUnchecked(record, k) != vertexIndex)
                    continue;

                m_PrimitiveStorage.RemoveVertexAt(primitiveIndex, k);
                removedAny = true;
            }

            if (!removedAny)
                return;

            RemoveValueFromMultiMap(ref m_VertexToPrimitives, vertexIndex, primitiveIndex, removeAllMatches: true);

            if (m_PrimitiveStorage.GetLength(primitiveIndex) < 3)
                RemovePrimitiveInternal(primitiveIndex, adjacencyPrepared: true);
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

        private static int RemoveValueFromMultiMap(
            ref NativeParallelMultiHashMap<int, int> map,
            int key,
            int valueToRemove,
            bool removeAllMatches)
        {
            int removed = 0;
            while (map.TryGetFirstValue(key, out int value, out NativeParallelMultiHashMapIterator<int> iterator))
            {
                bool removedOne = false;
                do
                {
                    if (value != valueToRemove)
                        continue;

                    map.Remove(iterator);
                    removed++;
                    removedOne = true;
                    break;
                }
                while (map.TryGetNextValue(out value, ref iterator));

                if (!removedOne || !removeAllMatches)
                    break;
            }

            return removed;
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

        private static void FillSequentialIndices(NativeList<int> output, int count)
        {
            output.Clear();
            if (count <= 0)
                return;

            output.Resize(count, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < count; i++)
                output[i] = i;
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
