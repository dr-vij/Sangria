using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public unsafe struct CompiledResourceSet : IDisposable
    {
        private bool m_IsDisposed;
        private NativeArray<CompiledResourceDescriptor> m_Descriptors;
        private NativeParallelHashMap<int, int> m_IdToDescriptor;
        private NativeArray<byte> m_Data;

        public int Count => m_Descriptors.IsCreated ? m_Descriptors.Length : 0;
        public bool IsCreated => m_Descriptors.IsCreated;

        public CompiledResourceSet(
            NativeArray<CompiledResourceDescriptor> descriptors,
            NativeParallelHashMap<int, int> idToDescriptor,
            NativeArray<byte> data)
        {
            m_Descriptors = descriptors;
            m_IdToDescriptor = idToDescriptor;
            m_Data = data;
            m_IsDisposed = false;
        }

        public bool TryGetDescriptor(int resourceId, out CompiledResourceDescriptor descriptor)
        {
            descriptor = default;
            if (!m_IdToDescriptor.IsCreated)
                return false;

            if (!m_IdToDescriptor.TryGetValue(resourceId, out int index))
                return false;

            descriptor = m_Descriptors[index];
            return true;
        }

        public CoreResult TryGetResource<T>(int resourceId, out T value) where T : unmanaged
        {
            value = default;

            if (!TryGetDescriptor(resourceId, out var descriptor))
                return CoreResult.NotFound;

            int typeHash = BurstRuntime.GetHashCode32<T>();
            if (descriptor.TypeHash != typeHash)
                return CoreResult.TypeMismatch;

            value = UnsafeUtility.ReadArrayElement<T>((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(m_Data) + descriptor.OffsetBytes, 0);
            return CoreResult.Success;
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
