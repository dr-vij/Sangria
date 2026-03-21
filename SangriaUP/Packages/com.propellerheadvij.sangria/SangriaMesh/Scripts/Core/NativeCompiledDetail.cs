// Core: Read-only compiled mesh snapshot with packed topology, attributes, and resources.
using System;
using Unity.Collections;

namespace SangriaMesh
{
    public struct NativeCompiledDetail : IDisposable
    {
        private bool m_IsDisposed;

        public NativeArray<int> VertexToPointDense;
        public NativeArray<int> PrimitiveOffsetsDense;
        public NativeArray<int> PrimitiveVerticesDense;

        public CompiledAttributeSet PointAttributes;
        public CompiledAttributeSet VertexAttributes;
        public CompiledAttributeSet PrimitiveAttributes;
        public CompiledResourceSet Resources;

        public int PointCount;
        public int VertexCount;
        public int PrimitiveCount;
        public bool IsTriangleOnlyTopology;
        public bool IsDisposed => m_IsDisposed;
        public bool IsCreated => !m_IsDisposed && VertexToPointDense.IsCreated;

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
            VertexToPointDense = vertexToPointDense;
            PrimitiveOffsetsDense = primitiveOffsetsDense;
            PrimitiveVerticesDense = primitiveVerticesDense;

            PointAttributes = pointAttributes;
            VertexAttributes = vertexAttributes;
            PrimitiveAttributes = primitiveAttributes;
            Resources = resources;

            PointCount = pointCount;
            VertexCount = vertexCount;
            PrimitiveCount = primitiveCount;
            IsTriangleOnlyTopology = isTriangleOnlyTopology;

            m_IsDisposed = false;
        }

        public CoreResult TryGetAttributeAccessor<T>(MeshDomain domain, int attributeId, out CompiledAttributeAccessor<T> accessor)
            where T : unmanaged
        {
            accessor = default;
            ThrowIfDisposed();

            return domain switch
            {
                MeshDomain.Point => PointAttributes.TryGetAccessor(attributeId, out accessor),
                MeshDomain.Vertex => VertexAttributes.TryGetAccessor(attributeId, out accessor),
                MeshDomain.Primitive => PrimitiveAttributes.TryGetAccessor(attributeId, out accessor),
                _ => CoreResult.InvalidOperation
            };
        }

        public CoreResult TryGetResource<T>(int resourceId, out T value) where T : unmanaged
        {
            ThrowIfDisposed();
            return Resources.TryGetResource(resourceId, out value);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            if (VertexToPointDense.IsCreated)
                VertexToPointDense.Dispose();
            if (PrimitiveOffsetsDense.IsCreated)
                PrimitiveOffsetsDense.Dispose();
            if (PrimitiveVerticesDense.IsCreated)
                PrimitiveVerticesDense.Dispose();

            PointAttributes.Dispose();
            VertexAttributes.Dispose();
            PrimitiveAttributes.Dispose();
            Resources.Dispose();

            m_IsDisposed = true;
            VertexToPointDense = default;
            PrimitiveOffsetsDense = default;
            PrimitiveVerticesDense = default;
            PointAttributes = default;
            VertexAttributes = default;
            PrimitiveAttributes = default;
            Resources = default;
            PointCount = 0;
            VertexCount = 0;
            PrimitiveCount = 0;
            IsTriangleOnlyTopology = false;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
                throw new ObjectDisposedException(nameof(NativeCompiledDetail));
        }
    }
}
