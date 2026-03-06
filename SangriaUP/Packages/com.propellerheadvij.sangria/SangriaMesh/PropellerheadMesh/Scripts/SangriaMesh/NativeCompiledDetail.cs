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
            int primitiveCount)
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

            m_IsDisposed = false;
        }

        public CoreResult TryGetAttributeAccessor<T>(MeshDomain domain, int attributeId, out CompiledAttributeAccessor<T> accessor)
            where T : unmanaged
        {
            accessor = default;

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
        }
    }
}
