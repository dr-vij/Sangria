// Core: Main mutable mesh container managing topology, attributes, resources, and versioning.
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace SangriaMesh
{
    [BurstCompile]
    public partial struct NativeDetail : IDisposable
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
            EnsureJobSafeAllocator(allocator);

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

        private static void EnsureJobSafeAllocator(Allocator allocator)
        {
            if (allocator == Allocator.Temp)
                throw new InvalidOperationException("Allocator.Temp is not supported for job-scheduled SangriaMesh core. Use Allocator.TempJob or Allocator.Persistent.");
        }
    }
}
