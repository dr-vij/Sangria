using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public unsafe struct SparseHandleSet : IDisposable
    {
        private readonly Allocator m_Allocator;
        private bool m_IsDisposed;

        private NativeBitArray m_Alive;
        private NativeList<uint> m_Generations;
        private NativeList<int> m_FreeIndices;

        private int m_Capacity;
        private int m_NextUnusedIndex;
        private int m_Count;

        public int Count => m_Count;
        public int Capacity => m_Capacity;
        public int MaxIndexExclusive => m_NextUnusedIndex;
        public bool IsDenseContiguous => m_Count == m_NextUnusedIndex && m_FreeIndices.Length == 0;

        public SparseHandleSet(int initialCapacity, Allocator allocator)
        {
            m_Allocator = allocator;
            m_IsDisposed = false;

            m_Capacity = math_max(1, initialCapacity);
            m_Alive = new NativeBitArray(m_Capacity, allocator);
            m_Generations = new NativeList<uint>(m_Capacity, allocator);
            m_FreeIndices = new NativeList<int>(allocator);
            m_Generations.Resize(m_Capacity, NativeArrayOptions.ClearMemory);

            m_NextUnusedIndex = 0;
            m_Count = 0;
        }

        public int Allocate(out ElementHandle handle)
        {
            int index;

            if (m_FreeIndices.Length > 0)
            {
                int freeIndex = m_FreeIndices.Length - 1;
                index = m_FreeIndices[freeIndex];
                m_FreeIndices.RemoveAtSwapBack(freeIndex);
            }
            else
            {
                index = m_NextUnusedIndex;
                EnsureCapacity(index + 1);
                m_NextUnusedIndex++;
            }

            uint generation = m_Generations[index];
            if (generation == 0)
                generation = 1;

            m_Generations[index] = generation;
            m_Alive.Set(index, true);
            m_Count++;

            handle = new ElementHandle(index, generation);
            return index;
        }

        public void AllocateDenseRange(int count)
        {
            if (count <= 0)
                return;

            if (m_FreeIndices.Length > 0)
                throw new InvalidOperationException("AllocateDenseRange requires empty free list.");

            int start = m_NextUnusedIndex;
            int endExclusive = start + count;

            EnsureCapacity(endExclusive);
            m_Alive.SetBits(start, true, count);
            WriteGenerationRange(start, count, 1u);

            m_NextUnusedIndex = endExclusive;
            m_Count += count;
        }

        public bool Release(ElementHandle handle)
        {
            if (!IsHandleValid(handle))
                return false;

            return Release(handle.Index);
        }

        public bool Release(int index)
        {
            if (!IsAlive(index))
                return false;

            m_Alive.Set(index, false);
            m_Generations[index] = NextGeneration(m_Generations[index]);
            m_FreeIndices.Add(index);
            m_Count--;
            return true;
        }

        public bool IsAlive(int index)
        {
            return index >= 0 && index < m_Capacity && m_Alive.IsSet(index);
        }

        public bool IsHandleValid(ElementHandle handle)
        {
            if (!handle.IsValid)
                return false;

            if (!IsAlive(handle.Index))
                return false;

            return m_Generations[handle.Index] == handle.Generation;
        }

        public uint GetGeneration(int index)
        {
            if ((uint)index >= (uint)m_Capacity)
                return 0;

            return m_Generations[index];
        }

        public void GetAliveIndices(NativeList<int> output)
        {
            output.Clear();
            for (int i = 0; i < m_NextUnusedIndex; i++)
            {
                if (m_Alive.IsSet(i))
                    output.Add(i);
            }
        }

        public void EnsureCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= m_Capacity)
                return;

            int newCapacity = m_Capacity;
            while (newCapacity < requiredCapacity)
                newCapacity *= 2;

            var newAlive = new NativeBitArray(newCapacity, m_Allocator);
            if (m_NextUnusedIndex > 0)
                newAlive.Copy(0, ref m_Alive, 0, m_NextUnusedIndex);

            m_Alive.Dispose();
            m_Alive = newAlive;

            int oldLength = m_Generations.Length;
            m_Generations.Resize(newCapacity, NativeArrayOptions.UninitializedMemory);
            if (newCapacity > oldLength)
                ClearGenerationRange(oldLength, newCapacity - oldLength);

            m_Capacity = newCapacity;
        }

        public void Clear()
        {
            int usedCount = m_NextUnusedIndex;
            if (usedCount > 0)
            {
                m_Alive.SetBits(0, false, usedCount);
                IncrementGenerationRange(0, usedCount);
            }

            m_FreeIndices.Clear();
            m_NextUnusedIndex = 0;
            m_Count = 0;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            if (m_Alive.IsCreated)
                m_Alive.Dispose();
            if (m_Generations.IsCreated)
                m_Generations.Dispose();
            if (m_FreeIndices.IsCreated)
                m_FreeIndices.Dispose();

            m_IsDisposed = true;
        }

        private static uint NextGeneration(uint current)
        {
            uint next = current + 1;
            return next == 0 ? 1 : next;
        }

        private void WriteGenerationRange(int start, int count, uint value)
        {
            if (count <= 0)
                return;

            var generationsArray = m_Generations.AsArray();
            uint* dst = (uint*)NativeArrayUnsafeUtility.GetUnsafePtr(generationsArray) + start;
            UnsafeUtility.MemCpyReplicate(dst, &value, UnsafeUtility.SizeOf<uint>(), count);
        }

        private void IncrementGenerationRange(int start, int count)
        {
            if (count <= 0)
                return;

            var generationsArray = m_Generations.AsArray();
            uint* ptr = (uint*)NativeArrayUnsafeUtility.GetUnsafePtr(generationsArray) + start;
            for (int i = 0; i < count; i++)
            {
                uint generation = ptr[i];
                if (generation != 0)
                    ptr[i] = NextGeneration(generation);
            }
        }

        private void ClearGenerationRange(int start, int count)
        {
            if (count <= 0)
                return;

            var generationsArray = m_Generations.AsArray();
            byte* dst = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(generationsArray) + start * UnsafeUtility.SizeOf<uint>();
            UnsafeUtility.MemClear(dst, count * UnsafeUtility.SizeOf<uint>());
        }

        private static int math_max(int a, int b) => a > b ? a : b;
    }
}
