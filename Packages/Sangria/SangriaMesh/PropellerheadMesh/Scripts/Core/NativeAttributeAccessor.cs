using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace PropellerheadMesh
{
    public readonly struct NativeAttributeAccessor<T> where T : unmanaged
    {
        private readonly UnsafeList<byte> m_Buffer;
        private readonly int m_Stride;
        private readonly int m_ElementCount;

        internal NativeAttributeAccessor(UnsafeList<byte> buffer, int stride, int elementCount)
        {
            m_Buffer = buffer;
            m_Stride = stride;
            m_ElementCount = elementCount;
        }

        public int Length => m_ElementCount;

        public ref T this[int index]
        {
            get
            {
                CheckBounds(index);
                return ref GetRefUnchecked(index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T GetRefUnchecked(int index)
        {
            return ref UnsafeUtility.AsRef<T>((T*)(m_Buffer.Ptr + index * m_Stride));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe T* GetPointerUnchecked(int index)
        {
            return (T*)(m_Buffer.Ptr + index * m_Stride);
        }

        public unsafe T* GetBasePointer() => (T*)m_Buffer.Ptr;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckBounds(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if ((uint)index >= (uint)m_ElementCount)
                throw new IndexOutOfRangeException();
#endif
        }
    }
}