// Core: Columnar native storage for per-domain attributes with typed handles and accessors.
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public struct AttributeColumn
    {
        public int AttributeId;
        public int TypeHash;
        public int Stride;
        public UnsafeList<byte> Buffer;
    }

    public struct AttributeStore : IDisposable
    {
        private readonly Allocator m_Allocator;
        private bool m_IsDisposed;

        private NativeList<AttributeColumn> m_Columns;
        private NativeParallelHashMap<int, int> m_IdToColumn;

        private int m_ElementCapacity;

        public int Count => m_Columns.Length;
        public int ElementCapacity => m_ElementCapacity;

        public AttributeStore(int estimatedAttributeCount, int elementCapacity, Allocator allocator)
        {
            m_Allocator = allocator;
            m_IsDisposed = false;

            m_Columns = new NativeList<AttributeColumn>(math_max(1, estimatedAttributeCount), allocator);
            m_IdToColumn = new NativeParallelHashMap<int, int>(math_max(1, estimatedAttributeCount), allocator);
            m_ElementCapacity = math_max(1, elementCapacity);
        }

        public CoreResult RegisterAttribute<T>(int attributeId) where T : unmanaged
        {
            return RegisterAttribute<T>(attributeId, m_ElementCapacity);
        }

        public unsafe CoreResult RegisterAttribute<T>(int attributeId, int minimumElementCapacity) where T : unmanaged
        {
            if (m_IdToColumn.ContainsKey(attributeId))
                return CoreResult.AlreadyExists;

            if (minimumElementCapacity > m_ElementCapacity)
                EnsureCapacity(minimumElementCapacity);

            EnsureIdMapCapacityForInsert();

            int stride = UnsafeUtility.SizeOf<T>();
            int typeHash = BurstRuntime.GetHashCode32<T>();
            int elementCapacity = m_ElementCapacity;
            int bytes = stride * elementCapacity;

            var buffer = new UnsafeList<byte>(bytes, m_Allocator);
            buffer.Length = bytes;
            UnsafeUtility.MemClear(buffer.Ptr, bytes);

            int columnIndex = m_Columns.Length;
            m_Columns.Add(new AttributeColumn
            {
                AttributeId = attributeId,
                TypeHash = typeHash,
                Stride = stride,
                Buffer = buffer
            });

            m_IdToColumn[attributeId] = columnIndex;
            return CoreResult.Success;
        }

        public unsafe CoreResult RegisterAttributeRaw(int attributeId, int stride, int typeHash, int minimumElementCapacity)
        {
            if (m_IdToColumn.ContainsKey(attributeId))
                return CoreResult.AlreadyExists;

            if (minimumElementCapacity > m_ElementCapacity)
                EnsureCapacity(minimumElementCapacity);

            EnsureIdMapCapacityForInsert();

            int elementCapacity = m_ElementCapacity;
            int bytes = stride * elementCapacity;

            var buffer = new UnsafeList<byte>(bytes, m_Allocator);
            buffer.Length = bytes;
            UnsafeUtility.MemClear(buffer.Ptr, bytes);

            int columnIndex = m_Columns.Length;
            m_Columns.Add(new AttributeColumn
            {
                AttributeId = attributeId,
                TypeHash = typeHash,
                Stride = stride,
                Buffer = buffer
            });

            m_IdToColumn[attributeId] = columnIndex;
            return CoreResult.Success;
        }

        public CoreResult RemoveAttribute(int attributeId)
        {
            if (!m_IdToColumn.TryGetValue(attributeId, out int index))
                return CoreResult.NotFound;

            var removed = m_Columns[index];
            if (removed.Buffer.IsCreated)
                removed.Buffer.Dispose();

            int last = m_Columns.Length - 1;
            if (index != last)
            {
                var moved = m_Columns[last];
                m_Columns[index] = moved;
                m_IdToColumn[moved.AttributeId] = index;
            }

            m_Columns.RemoveAt(last);
            m_IdToColumn.Remove(attributeId);
            return CoreResult.Success;
        }

        public bool ContainsAttribute(int attributeId)
        {
            return m_IdToColumn.ContainsKey(attributeId);
        }

        public void EnsureCapacity(int newElementCapacity)
        {
            if (newElementCapacity <= m_ElementCapacity)
                return;

            for (int i = 0; i < m_Columns.Length; i++)
            {
                var column = m_Columns[i];
                int newSizeBytes = newElementCapacity * column.Stride;
                column.Buffer.Resize(newSizeBytes);
                m_Columns[i] = column;
            }

            m_ElementCapacity = newElementCapacity;
        }

        public CoreResult TryResolveHandle<T>(int attributeId, out AttributeHandle<T> handle) where T : unmanaged
        {
            handle = default;

            if (!m_IdToColumn.TryGetValue(attributeId, out int columnIndex))
                return CoreResult.NotFound;

            var column = m_Columns[columnIndex];
            int typeHash = BurstRuntime.GetHashCode32<T>();
            if (column.TypeHash != typeHash)
                return CoreResult.TypeMismatch;

            handle = new AttributeHandle<T>(attributeId, columnIndex, typeHash);
            return CoreResult.Success;
        }

        public CoreResult TryGetAccessor<T>(int attributeId, out NativeAttributeAccessor<T> accessor) where T : unmanaged
        {
            accessor = default;

            CoreResult result = TryResolveHandle<T>(attributeId, out var handle);
            if (result != CoreResult.Success)
                return result;

            return TryGetAccessor(handle, out accessor);
        }

        public CoreResult TryGetAccessor<T>(AttributeHandle<T> handle, out NativeAttributeAccessor<T> accessor) where T : unmanaged
        {
            accessor = default;

            if ((uint)handle.ColumnIndex >= (uint)m_Columns.Length)
                return CoreResult.InvalidHandle;

            var column = m_Columns[handle.ColumnIndex];
            if (column.AttributeId != handle.AttributeId)
                return CoreResult.InvalidHandle;
            if (column.TypeHash != handle.TypeHash)
                return CoreResult.InvalidHandle;

            accessor = new NativeAttributeAccessor<T>(column.Buffer, column.Stride, m_ElementCapacity);
            return CoreResult.Success;
        }

        public unsafe CoreResult TrySet<T>(AttributeHandle<T> handle, int elementIndex, T value) where T : unmanaged
        {
            if ((uint)elementIndex >= (uint)m_ElementCapacity)
                return CoreResult.IndexOutOfRange;

            if ((uint)handle.ColumnIndex >= (uint)m_Columns.Length)
                return CoreResult.InvalidHandle;

            var column = m_Columns[handle.ColumnIndex];
            if (column.AttributeId != handle.AttributeId)
                return CoreResult.InvalidHandle;
            if (column.TypeHash != handle.TypeHash)
                return CoreResult.InvalidHandle;

            UnsafeUtility.WriteArrayElement(column.Buffer.Ptr, elementIndex, value);
            return CoreResult.Success;
        }

        public unsafe CoreResult TryGet<T>(AttributeHandle<T> handle, int elementIndex, out T value) where T : unmanaged
        {
            value = default;

            if ((uint)elementIndex >= (uint)m_ElementCapacity)
                return CoreResult.IndexOutOfRange;

            if ((uint)handle.ColumnIndex >= (uint)m_Columns.Length)
                return CoreResult.InvalidHandle;

            var column = m_Columns[handle.ColumnIndex];
            if (column.AttributeId != handle.AttributeId)
                return CoreResult.InvalidHandle;
            if (column.TypeHash != handle.TypeHash)
                return CoreResult.InvalidHandle;

            value = UnsafeUtility.ReadArrayElement<T>(column.Buffer.Ptr, elementIndex);
            return CoreResult.Success;
        }

        public unsafe void ClearElement(int elementIndex)
        {
            if ((uint)elementIndex >= (uint)m_ElementCapacity)
                return;

            for (int i = 0; i < m_Columns.Length; i++)
            {
                var column = m_Columns[i];
                byte* ptr = column.Buffer.Ptr + elementIndex * column.Stride;
                UnsafeUtility.MemClear(ptr, column.Stride);
            }
        }

        public unsafe void ClearRange(int startElementIndex, int count)
        {
            if (count <= 0)
                return;
            if (startElementIndex >= m_ElementCapacity)
                return;

            int start = startElementIndex < 0 ? 0 : startElementIndex;
            int maxCount = m_ElementCapacity - start;
            if (maxCount <= 0)
                return;

            if (count > maxCount)
                count = maxCount;

            for (int i = 0; i < m_Columns.Length; i++)
            {
                var column = m_Columns[i];
                byte* ptr = column.Buffer.Ptr + start * column.Stride;
                UnsafeUtility.MemClear(ptr, column.Stride * count);
            }
        }

        public int GetColumnCount() => m_Columns.Length;

        public AttributeColumn GetColumnAt(int index)
        {
            return m_Columns[index];
        }

        public void Clear()
        {
            for (int i = 0; i < m_Columns.Length; i++)
            {
                var column = m_Columns[i];
                if (column.Buffer.IsCreated)
                    column.Buffer.Dispose();
            }

            m_Columns.Clear();
            m_IdToColumn.Clear();
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            for (int i = 0; i < m_Columns.Length; i++)
            {
                var column = m_Columns[i];
                if (column.Buffer.IsCreated)
                    column.Buffer.Dispose();
            }

            if (m_Columns.IsCreated)
                m_Columns.Dispose();
            if (m_IdToColumn.IsCreated)
                m_IdToColumn.Dispose();

            m_IsDisposed = true;
        }

        private void EnsureIdMapCapacityForInsert()
        {
            int required = m_IdToColumn.Count() + 1;
            if (required <= m_IdToColumn.Capacity)
                return;

            int newCapacity = math_max(1, m_IdToColumn.Capacity);
            while (newCapacity < required)
                newCapacity *= 2;

            m_IdToColumn.Capacity = newCapacity;
        }

        private static int math_max(int a, int b) => a > b ? a : b;
    }
}
