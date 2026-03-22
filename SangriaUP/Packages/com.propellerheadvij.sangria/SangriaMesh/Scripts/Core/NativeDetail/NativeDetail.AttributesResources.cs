// Core: Attribute and resource CRUD API surface for NativeDetail.
using System;

namespace SangriaMesh
{
    public partial struct NativeDetail
    {
        #region Attributes

        public CoreResult AddPointAttribute<T>(int attributeId) where T : unmanaged
        {
            ThrowIfDisposed();
            CoreResult result = m_PointAttributes.RegisterAttribute<T>(attributeId, m_Points.Capacity);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult AddVertexAttribute<T>(int attributeId) where T : unmanaged
        {
            ThrowIfDisposed();
            CoreResult result = m_VertexAttributes.RegisterAttribute<T>(attributeId, m_Vertices.Capacity);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult AddPrimitiveAttribute<T>(int attributeId) where T : unmanaged
        {
            ThrowIfDisposed();
            CoreResult result = m_PrimitiveAttributes.RegisterAttribute<T>(attributeId, m_Primitives.Capacity);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult RemovePointAttribute(int attributeId)
        {
            ThrowIfDisposed();

            if (attributeId == AttributeID.Position)
                return CoreResult.InvalidOperation;

            CoreResult result = m_PointAttributes.RemoveAttribute(attributeId);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult RemoveVertexAttribute(int attributeId)
        {
            ThrowIfDisposed();
            CoreResult result = m_VertexAttributes.RemoveAttribute(attributeId);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult RemovePrimitiveAttribute(int attributeId)
        {
            ThrowIfDisposed();
            CoreResult result = m_PrimitiveAttributes.RemoveAttribute(attributeId);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public bool HasPointAttribute(int attributeId)
        {
            ThrowIfDisposed();
            return m_PointAttributes.ContainsAttribute(attributeId);
        }

        public bool HasVertexAttribute(int attributeId)
        {
            ThrowIfDisposed();
            return m_VertexAttributes.ContainsAttribute(attributeId);
        }

        public bool HasPrimitiveAttribute(int attributeId)
        {
            ThrowIfDisposed();
            return m_PrimitiveAttributes.ContainsAttribute(attributeId);
        }

        public CoreResult TryGetPointAttributeHandle<T>(int attributeId, out AttributeHandle<T> handle) where T : unmanaged
        {
            ThrowIfDisposed();
            return m_PointAttributes.TryResolveHandle(attributeId, out handle);
        }

        public CoreResult TryGetVertexAttributeHandle<T>(int attributeId, out AttributeHandle<T> handle) where T : unmanaged
        {
            ThrowIfDisposed();
            return m_VertexAttributes.TryResolveHandle(attributeId, out handle);
        }

        public CoreResult TryGetPrimitiveAttributeHandle<T>(int attributeId, out AttributeHandle<T> handle) where T : unmanaged
        {
            ThrowIfDisposed();
            return m_PrimitiveAttributes.TryResolveHandle(attributeId, out handle);
        }

        public CoreResult TryGetPointAccessor<T>(int attributeId, out NativeAttributeAccessor<T> accessor) where T : unmanaged
        {
            ThrowIfDisposed();
            return m_PointAttributes.TryGetAccessor(attributeId, out accessor);
        }

        public CoreResult TryGetVertexAccessor<T>(int attributeId, out NativeAttributeAccessor<T> accessor) where T : unmanaged
        {
            ThrowIfDisposed();
            return m_VertexAttributes.TryGetAccessor(attributeId, out accessor);
        }

        public CoreResult TryGetPrimitiveAccessor<T>(int attributeId, out NativeAttributeAccessor<T> accessor) where T : unmanaged
        {
            ThrowIfDisposed();
            return m_PrimitiveAttributes.TryGetAccessor(attributeId, out accessor);
        }

        public CoreResult TrySetPointAttribute<T>(int pointIndex, AttributeHandle<T> handle, T value) where T : unmanaged
        {
            ThrowIfDisposed();

            if (!m_Points.IsAlive(pointIndex))
                return CoreResult.InvalidHandle;

            CoreResult result = m_PointAttributes.TrySet(handle, pointIndex, value);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult TryGetPointAttribute<T>(int pointIndex, AttributeHandle<T> handle, out T value) where T : unmanaged
        {
            ThrowIfDisposed();
            value = default;
            if (!m_Points.IsAlive(pointIndex))
                return CoreResult.InvalidHandle;

            return m_PointAttributes.TryGet(handle, pointIndex, out value);
        }

        public CoreResult TrySetVertexAttribute<T>(int vertexIndex, AttributeHandle<T> handle, T value) where T : unmanaged
        {
            ThrowIfDisposed();

            if (!m_Vertices.IsAlive(vertexIndex))
                return CoreResult.InvalidHandle;

            CoreResult result = m_VertexAttributes.TrySet(handle, vertexIndex, value);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult TryGetVertexAttribute<T>(int vertexIndex, AttributeHandle<T> handle, out T value) where T : unmanaged
        {
            ThrowIfDisposed();
            value = default;
            if (!m_Vertices.IsAlive(vertexIndex))
                return CoreResult.InvalidHandle;

            return m_VertexAttributes.TryGet(handle, vertexIndex, out value);
        }

        public CoreResult TrySetPrimitiveAttribute<T>(int primitiveIndex, AttributeHandle<T> handle, T value) where T : unmanaged
        {
            ThrowIfDisposed();

            if (!m_Primitives.IsAlive(primitiveIndex))
                return CoreResult.InvalidHandle;

            CoreResult result = m_PrimitiveAttributes.TrySet(handle, primitiveIndex, value);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult TryGetPrimitiveAttribute<T>(int primitiveIndex, AttributeHandle<T> handle, out T value) where T : unmanaged
        {
            ThrowIfDisposed();
            value = default;
            if (!m_Primitives.IsAlive(primitiveIndex))
                return CoreResult.InvalidHandle;

            return m_PrimitiveAttributes.TryGet(handle, primitiveIndex, out value);
        }

        #endregion

        #region Resources

        public CoreResult SetResource<T>(int resourceId, in T value) where T : unmanaged
        {
            ThrowIfDisposed();
            CoreResult result = m_Resources.SetResource(resourceId, value);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        public CoreResult TryGetResource<T>(int resourceId, out T value) where T : unmanaged
        {
            ThrowIfDisposed();
            return m_Resources.TryGetResource(resourceId, out value);
        }

        public CoreResult RemoveResource(int resourceId)
        {
            ThrowIfDisposed();
            CoreResult result = m_Resources.RemoveResource(resourceId);
            if (result == CoreResult.Success)
                m_AttributeVersion++;

            return result;
        }

        #endregion

        #region Attribute Transfer Support

        public int GetPointAttributeColumnCount()
        {
            ThrowIfDisposed();
            return m_PointAttributes.GetColumnCount();
        }

        public AttributeColumn GetPointAttributeColumnAt(int index)
        {
            ThrowIfDisposed();
            return m_PointAttributes.GetColumnAt(index);
        }

        public AttributeColumn GetPointAttributeColumnByIdUnchecked(int attributeId)
        {
            ThrowIfDisposed();
            int count = m_PointAttributes.GetColumnCount();
            for (int i = 0; i < count; i++)
            {
                var col = m_PointAttributes.GetColumnAt(i);
                if (col.AttributeId == attributeId)
                    return col;
            }
            return default;
        }

        public CoreResult AddPointAttributeRaw(int attributeId, int stride, int typeHash)
        {
            ThrowIfDisposed();
            CoreResult result = m_PointAttributes.RegisterAttributeRaw(attributeId, stride, typeHash, m_Points.Capacity);
            if (result == CoreResult.Success)
                m_AttributeVersion++;
            return result;
        }

        #endregion
    }
}
