using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public readonly unsafe struct CompiledAttributeAccessor<T> where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction] private readonly byte* m_BasePtr;
        private readonly int m_Stride;
        private readonly int m_Count;

        internal CompiledAttributeAccessor(byte* basePtr, int stride, int count)
        {
            m_BasePtr = basePtr;
            m_Stride = stride;
            m_Count = count;
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_Count;
        }

        public ref T this[int index]
        {
            get
            {
                CheckBounds(index);
                return ref GetRefUnchecked(index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRefUnchecked(int index)
        {
            return ref UnsafeUtility.AsRef<T>((void*)(m_BasePtr + index * m_Stride));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T* GetPointerUnchecked(int index)
        {
            return (T*)(m_BasePtr + index * m_Stride);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T* GetBasePointer() => (T*)m_BasePtr;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckBounds(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if ((uint)index >= (uint)m_Count)
                throw new IndexOutOfRangeException();
#endif
        }
    }
}
