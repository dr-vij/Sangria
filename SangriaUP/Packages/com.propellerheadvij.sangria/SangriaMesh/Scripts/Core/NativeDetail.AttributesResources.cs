using System;

namespace SangriaMesh
{
    public partial struct NativeDetail : IDisposable
    {
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
    }
}
