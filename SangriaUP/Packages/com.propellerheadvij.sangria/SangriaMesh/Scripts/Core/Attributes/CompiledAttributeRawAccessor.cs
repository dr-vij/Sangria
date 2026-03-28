// Core: Pointer-based untyped accessor over compiled attribute memory.
using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public readonly unsafe struct CompiledAttributeRawAccessor
    {
        [NativeDisableUnsafePtrRestriction] 
        private readonly byte* m_BasePtr;
        private readonly int m_Stride;
        private readonly int m_Count;

        public int Stride => m_Stride;
        public int Count => m_Count;
        public void* BasePointer => m_BasePtr;

        internal CompiledAttributeRawAccessor(byte* basePtr, int stride, int count)
        {
            m_BasePtr = basePtr;
            m_Stride = stride;
            m_Count = count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void* GetPointerUnchecked(int index)
        {
            CheckBounds(index);
            return m_BasePtr + (long)index * m_Stride;
        }
        
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
