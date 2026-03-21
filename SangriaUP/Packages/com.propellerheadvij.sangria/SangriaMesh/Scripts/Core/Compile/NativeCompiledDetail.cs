// Core: Read-only compiled mesh snapshot with packed topology, attributes, and resources.
using System;
using Unity.Collections;

namespace SangriaMesh
{
    /// <summary>
    /// Owner of compiled native mesh memory.
    /// Do not copy by value. Pass by <c>in</c>/<c>ref</c> to avoid ownership aliasing.
    /// </summary>
    public struct NativeCompiledDetail : IDisposable
    {
        private bool m_IsDisposed;

        private NativeArray<int> m_VertexToPointDense;
        private NativeArray<int> m_PrimitiveOffsetsDense;
        private NativeArray<int> m_PrimitiveVerticesDense;

        private CompiledAttributeSet m_PointAttributes;
        private CompiledAttributeSet m_VertexAttributes;
        private CompiledAttributeSet m_PrimitiveAttributes;
        private CompiledResourceSet m_Resources;

        private int m_PointCount;
        private int m_VertexCount;
        private int m_PrimitiveCount;
        private bool m_IsTriangleOnlyTopology;

        public NativeArray<int>.ReadOnly VertexToPointDense => m_VertexToPointDense.AsReadOnly();
        public NativeArray<int>.ReadOnly PrimitiveOffsetsDense => m_PrimitiveOffsetsDense.AsReadOnly();
        public NativeArray<int>.ReadOnly PrimitiveVerticesDense => m_PrimitiveVerticesDense.AsReadOnly();

        public readonly int PointCount => m_PointCount;
        public readonly int VertexCount => m_VertexCount;
        public readonly int PrimitiveCount => m_PrimitiveCount;
        public readonly bool IsTriangleOnlyTopology => m_IsTriangleOnlyTopology;
        public readonly bool IsDisposed => m_IsDisposed;
        public readonly bool IsCreated => !m_IsDisposed && m_VertexToPointDense.IsCreated;

        internal NativeCompiledDetail(
            NativeArray<int> vertexToPointDense,
            NativeArray<int> primitiveOffsetsDense,
            NativeArray<int> primitiveVerticesDense,
            CompiledAttributeSet pointAttributes,
            CompiledAttributeSet vertexAttributes,
            CompiledAttributeSet primitiveAttributes,
            CompiledResourceSet resources,
            int pointCount,
            int vertexCount,
            int primitiveCount,
            bool isTriangleOnlyTopology)
        {
            m_VertexToPointDense = vertexToPointDense;
            m_PrimitiveOffsetsDense = primitiveOffsetsDense;
            m_PrimitiveVerticesDense = primitiveVerticesDense;

            m_PointAttributes = pointAttributes;
            m_VertexAttributes = vertexAttributes;
            m_PrimitiveAttributes = primitiveAttributes;
            m_Resources = resources;

            m_PointCount = pointCount;
            m_VertexCount = vertexCount;
            m_PrimitiveCount = primitiveCount;
            m_IsTriangleOnlyTopology = isTriangleOnlyTopology;

            m_IsDisposed = false;
        }

        public CoreResult TryGetAttributeAccessor<T>(MeshDomain domain, int attributeId, out CompiledAttributeAccessor<T> accessor)
            where T : unmanaged
        {
            accessor = default;
            ThrowIfDisposed();

            return domain switch
            {
                MeshDomain.Point => m_PointAttributes.TryGetAccessor(attributeId, out accessor),
                MeshDomain.Vertex => m_VertexAttributes.TryGetAccessor(attributeId, out accessor),
                MeshDomain.Primitive => m_PrimitiveAttributes.TryGetAccessor(attributeId, out accessor),
                _ => CoreResult.InvalidOperation
            };
        }

        public CoreResult TryGetResource<T>(int resourceId, out T value) where T : unmanaged
        {
            ThrowIfDisposed();
            return m_Resources.TryGetResource(resourceId, out value);
        }

        internal readonly NativeArray<int> GetVertexToPointDenseArrayUnsafe()
        {
            ThrowIfDisposed();
            return m_VertexToPointDense;
        }

        internal readonly NativeArray<int> GetPrimitiveVerticesDenseArrayUnsafe()
        {
            ThrowIfDisposed();
            return m_PrimitiveVerticesDense;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            if (m_VertexToPointDense.IsCreated)
                m_VertexToPointDense.Dispose();
            if (m_PrimitiveOffsetsDense.IsCreated)
                m_PrimitiveOffsetsDense.Dispose();
            if (m_PrimitiveVerticesDense.IsCreated)
                m_PrimitiveVerticesDense.Dispose();

            m_PointAttributes.Dispose();
            m_VertexAttributes.Dispose();
            m_PrimitiveAttributes.Dispose();
            m_Resources.Dispose();

            m_IsDisposed = true;
            m_VertexToPointDense = default;
            m_PrimitiveOffsetsDense = default;
            m_PrimitiveVerticesDense = default;
            m_PointAttributes = default;
            m_VertexAttributes = default;
            m_PrimitiveAttributes = default;
            m_Resources = default;
            m_PointCount = 0;
            m_VertexCount = 0;
            m_PrimitiveCount = 0;
            m_IsTriangleOnlyTopology = false;
        }

        private readonly void ThrowIfDisposed()
        {
            if (m_IsDisposed)
                throw new ObjectDisposedException(nameof(NativeCompiledDetail));
        }
    }
}
