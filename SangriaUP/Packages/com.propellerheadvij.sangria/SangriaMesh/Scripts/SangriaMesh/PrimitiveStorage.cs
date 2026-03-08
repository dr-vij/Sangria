using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public struct PrimitiveRecord
    {
        public int Start;
        public int Length;
        public int Capacity;
    }

    public unsafe struct PrimitiveStorage : IDisposable
    {
        private readonly Allocator m_Allocator;
        private bool m_IsDisposed;

        private NativeList<PrimitiveRecord> m_Records;
        private NativeList<int> m_Data;
        private readonly int m_DefaultPrimitiveCapacity;
        private int m_TotalLength;
        private bool m_IsDenseTriangleLayout;
        private int m_GarbageLength;

        public int RecordCapacity => m_Records.Length;
        public int DataLength => m_Data.Length;
        public int TotalVertexCount => m_TotalLength;
        public bool IsDenseTriangleLayout => m_IsDenseTriangleLayout;
        public int GarbageLength => m_GarbageLength;
        public bool HasGarbage => m_GarbageLength > 0;

        public PrimitiveStorage(int initialPrimitiveCapacity, int defaultPrimitiveCapacity, Allocator allocator)
        {
            m_Allocator = allocator;
            m_IsDisposed = false;

            int primCapacity = math_max(1, initialPrimitiveCapacity);
            m_DefaultPrimitiveCapacity = math_max(3, defaultPrimitiveCapacity);

            m_Records = new NativeList<PrimitiveRecord>(primCapacity, allocator);
            m_Data = new NativeList<int>(primCapacity * m_DefaultPrimitiveCapacity, allocator);
            m_TotalLength = 0;
            m_IsDenseTriangleLayout = false;
            m_GarbageLength = 0;
        }

        public void EnsureRecordSlot(int primitiveIndex)
        {
            while (m_Records.Length <= primitiveIndex)
            {
                m_Records.Add(new PrimitiveRecord
                {
                    Start = -1,
                    Length = 0,
                    Capacity = 0
                });
            }
        }

        public void SetVertices(int primitiveIndex, NativeArray<int> vertices)
        {
            SetVertices(primitiveIndex, new NativeSlice<int>(vertices));
        }

        public void SetVertices(int primitiveIndex, NativeSlice<int> vertices)
        {
            EnsureRecordSlot(primitiveIndex);
            int oldLength = m_Records[primitiveIndex].Length;
            EnsureRecordCapacity(primitiveIndex, vertices.Length);

            var record = m_Records[primitiveIndex];
            if (vertices.Length > 0)
            {
                for (int i = 0; i < vertices.Length; i++)
                    m_Data[record.Start + i] = vertices[i];
            }

            record.Length = vertices.Length;
            m_Records[primitiveIndex] = record;
            m_TotalLength += vertices.Length - oldLength;
            m_IsDenseTriangleLayout = false;
        }

        public void PrepareDenseTriangleRecords(int primitiveCount)
        {
            if (primitiveCount <= 0)
            {
                m_Data.Resize(0, NativeArrayOptions.ClearMemory);
                m_Records.Resize(0, NativeArrayOptions.ClearMemory);
                m_TotalLength = 0;
                m_GarbageLength = 0;
                return;
            }

            int requiredDataLength = primitiveCount * 3;
            m_Data.Resize(requiredDataLength, NativeArrayOptions.UninitializedMemory);
            m_Records.Resize(primitiveCount, NativeArrayOptions.UninitializedMemory);

            var recordsArray = m_Records.AsArray();
            PrimitiveRecord* recordsPtr = (PrimitiveRecord*)NativeArrayUnsafeUtility.GetUnsafePtr(recordsArray);

            for (int i = 0; i < primitiveCount; i++)
            {
                recordsPtr[i] = new PrimitiveRecord
                {
                    Start = i * 3,
                    Length = 3,
                    Capacity = 3
                };
            }

            m_TotalLength = requiredDataLength;
            m_IsDenseTriangleLayout = true;
            m_GarbageLength = 0;
        }

        public int* GetDataPointerUnchecked()
        {
            var dataArray = m_Data.AsArray();
            return (int*)NativeArrayUnsafeUtility.GetUnsafePtr(dataArray);
        }

        public bool AppendVertex(int primitiveIndex, int vertexIndex)
        {
            EnsureRecordSlot(primitiveIndex);
            EnsureRecordCapacity(primitiveIndex, GetLength(primitiveIndex) + 1);

            var record = m_Records[primitiveIndex];
            m_Data[record.Start + record.Length] = vertexIndex;
            record.Length++;
            m_Records[primitiveIndex] = record;
            m_TotalLength++;
            m_IsDenseTriangleLayout = false;
            return true;
        }

        public bool RemoveVertexAt(int primitiveIndex, int vertexOffset)
        {
            if ((uint)primitiveIndex >= (uint)m_Records.Length)
                return false;

            var record = m_Records[primitiveIndex];
            if (record.Start < 0)
                return false;
            if ((uint)vertexOffset >= (uint)record.Length)
                return false;

            int remaining = record.Length - vertexOffset - 1;
            if (remaining > 0)
            {
                int src = record.Start + vertexOffset + 1;
                int dst = record.Start + vertexOffset;
                NativeArray<int>.Copy(m_Data.AsArray(), src, m_Data.AsArray(), dst, remaining);
            }

            record.Length--;
            m_Records[primitiveIndex] = record;
            m_TotalLength--;
            m_IsDenseTriangleLayout = false;
            return true;
        }

        public int GetLength(int primitiveIndex)
        {
            if ((uint)primitiveIndex >= (uint)m_Records.Length)
                return 0;

            return m_Records[primitiveIndex].Length;
        }

        public bool TryGetRecord(int primitiveIndex, out PrimitiveRecord record)
        {
            record = default;
            if ((uint)primitiveIndex >= (uint)m_Records.Length)
                return false;

            record = m_Records[primitiveIndex];
            return record.Start >= 0;
        }

        public int GetVertex(int primitiveIndex, int vertexOffset)
        {
            if (!TryGetRecord(primitiveIndex, out var record))
                return -1;

            if ((uint)vertexOffset >= (uint)record.Length)
                return -1;

            return m_Data[record.Start + vertexOffset];
        }

        public int GetVertexUnchecked(int primitiveIndex, int vertexOffset)
        {
            var record = m_Records[primitiveIndex];
            return m_Data[record.Start + vertexOffset];
        }

        public PrimitiveRecord GetRecordUnchecked(int primitiveIndex)
        {
            return m_Records[primitiveIndex];
        }

        public int GetVertexUnchecked(in PrimitiveRecord record, int vertexOffset)
        {
            return m_Data[record.Start + vertexOffset];
        }

        public void CopyVerticesUnchecked(int primitiveIndex, NativeArray<int> destination, int destinationStartIndex)
        {
            CopyVerticesUnchecked(m_Records[primitiveIndex], destination, destinationStartIndex);
        }

        public void CopyVerticesUnchecked(in PrimitiveRecord record, NativeArray<int> destination, int destinationStartIndex)
        {
            if (record.Length <= 0)
                return;

            NativeArray<int>.Copy(m_Data.AsArray(), record.Start, destination, destinationStartIndex, record.Length);
        }

        public NativeSlice<int> GetVertices(int primitiveIndex)
        {
            if ((uint)primitiveIndex >= (uint)m_Records.Length)
                return default;

            var record = m_Records[primitiveIndex];
            if (record.Start < 0 || record.Length <= 0)
                return default;

            return new NativeSlice<int>(m_Data.AsArray(), record.Start, record.Length);
        }

        public void ClearRecord(int primitiveIndex)
        {
            if ((uint)primitiveIndex >= (uint)m_Records.Length)
                return;

            var record = m_Records[primitiveIndex];
            if (record.Start < 0)
                return;

            m_TotalLength -= record.Length;
            m_GarbageLength += record.Capacity;
            record.Start = -1;
            record.Length = 0;
            record.Capacity = 0;
            m_Records[primitiveIndex] = record;
            m_IsDenseTriangleLayout = false;
        }

        public bool CollectGarbage(float minGarbageRatio = 0f)
        {
            if (m_GarbageLength <= 0)
                return false;

            if (m_Data.Length <= 0)
            {
                m_GarbageLength = 0;
                return false;
            }

            if (minGarbageRatio > 0f)
            {
                float garbageRatio = (float)m_GarbageLength / m_Data.Length;
                if (garbageRatio < minGarbageRatio)
                    return false;
            }

            CompactGarbage();
            return true;
        }

        public void Clear()
        {
            m_Data.Resize(0, NativeArrayOptions.ClearMemory);
            m_Records.Resize(0, NativeArrayOptions.ClearMemory);
            m_TotalLength = 0;
            m_IsDenseTriangleLayout = false;
            m_GarbageLength = 0;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            if (m_Records.IsCreated)
                m_Records.Dispose();
            if (m_Data.IsCreated)
                m_Data.Dispose();

            m_IsDisposed = true;
        }

        private void EnsureRecordCapacity(int primitiveIndex, int requiredLength)
        {
            var record = m_Records[primitiveIndex];

            if (record.Start < 0)
            {
                int cap = math_max(m_DefaultPrimitiveCapacity, requiredLength);
                int start = m_Data.Length;
                m_Data.Resize(start + cap, NativeArrayOptions.UninitializedMemory);

                record.Start = start;
                record.Length = 0;
                record.Capacity = cap;
                m_Records[primitiveIndex] = record;
                return;
            }

            if (requiredLength <= record.Capacity)
                return;

            int newCap = record.Capacity;
            while (newCap < requiredLength)
                newCap *= 2;

            int newStart = m_Data.Length;
            m_Data.Resize(newStart + newCap, NativeArrayOptions.UninitializedMemory);

            if (record.Length > 0)
                NativeArray<int>.Copy(m_Data.AsArray(), record.Start, m_Data.AsArray(), newStart, record.Length);

            m_GarbageLength += record.Capacity;
            record.Start = newStart;
            record.Capacity = newCap;
            m_Records[primitiveIndex] = record;
        }

        private void CompactGarbage()
        {
            if (m_GarbageLength <= 0)
                return;

            int recordCount = m_Records.Length;
            int requiredLength = 0;
            for (int i = 0; i < recordCount; i++)
            {
                var record = m_Records[i];
                if (record.Start < 0 || record.Capacity <= 0)
                    continue;

                requiredLength += record.Capacity;
            }

            var newData = new NativeList<int>(math_max(1, requiredLength), m_Allocator);
            if (requiredLength > 0)
                newData.Resize(requiredLength, NativeArrayOptions.UninitializedMemory);

            int writeCursor = 0;
            for (int i = 0; i < recordCount; i++)
            {
                var record = m_Records[i];
                if (record.Start < 0 || record.Capacity <= 0)
                {
                    record.Start = -1;
                    record.Length = 0;
                    record.Capacity = 0;
                    m_Records[i] = record;
                    continue;
                }

                if (record.Length > 0)
                    NativeArray<int>.Copy(m_Data.AsArray(), record.Start, newData.AsArray(), writeCursor, record.Length);

                record.Start = writeCursor;
                m_Records[i] = record;
                writeCursor += record.Capacity;
            }

            m_Data.Dispose();
            m_Data = newData;
            m_GarbageLength = 0;
        }

        private static int math_max(int a, int b) => a > b ? a : b;
    }
}
