using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public unsafe struct CompiledAttributeSet : IDisposable
    {
        private bool m_IsDisposed;
        private NativeArray<CompiledAttributeDescriptor> m_Descriptors;
        private NativeParallelHashMap<int, int> m_IdToDescriptor;
        private NativeArray<byte> m_Data;

        public int Count => m_Descriptors.IsCreated ? m_Descriptors.Length : 0;
        public bool IsCreated => m_Descriptors.IsCreated;

        public CompiledAttributeSet(
            NativeArray<CompiledAttributeDescriptor> descriptors,
            NativeParallelHashMap<int, int> idToDescriptor,
            NativeArray<byte> data)
        {
            m_Descriptors = descriptors;
            m_IdToDescriptor = idToDescriptor;
            m_Data = data;
            m_IsDisposed = false;
        }

        public bool TryGetDescriptor(int attributeId, out CompiledAttributeDescriptor descriptor)
        {
            descriptor = default;
            if (!m_IdToDescriptor.IsCreated)
                return false;

            if (!m_IdToDescriptor.TryGetValue(attributeId, out int descriptorIndex))
                return false;

            descriptor = m_Descriptors[descriptorIndex];
            return true;
        }

        public CoreResult TryGetAccessor<T>(int attributeId, out CompiledAttributeAccessor<T> accessor) where T : unmanaged
        {
            accessor = default;

            if (!TryGetDescriptor(attributeId, out var descriptor))
                return CoreResult.NotFound;

            int typeHash = BurstRuntime.GetHashCode32<T>();
            if (descriptor.TypeHash != typeHash)
                return CoreResult.TypeMismatch;

            byte* basePtr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(m_Data) + descriptor.OffsetBytes;
            accessor = new CompiledAttributeAccessor<T>(basePtr, descriptor.Stride, descriptor.Count);
            return CoreResult.Success;
        }

        public NativeArray<CompiledAttributeDescriptor>.ReadOnly GetDescriptors()
        {
            return m_Descriptors.AsReadOnly();
        }

        public NativeArray<byte>.ReadOnly GetRawData()
        {
            return m_Data.AsReadOnly();
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            if (m_Descriptors.IsCreated)
                m_Descriptors.Dispose();
            if (m_IdToDescriptor.IsCreated)
                m_IdToDescriptor.Dispose();
            if (m_Data.IsCreated)
                m_Data.Dispose();

            m_IsDisposed = true;
        }
    }
}
