using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace PropellerheadMesh
{
    [NativeContainer]
    public struct NativeArray2D<T> : IDisposable where T : unmanaged
    {
        private NativeList<PageInfo> m_Pages;
        private NativeList<T> m_DataRecords;
        private readonly int m_DefaultPageSize;
        private int m_LastRecordIndex;

        public int Count => m_Pages.Length;

        public T this[int row, int column]
        {
            get
            {
                var page = m_Pages[row];
                var linearIndex = page.StartIndex + column;
                return m_DataRecords[linearIndex];
            }
            set
            {
                var page = m_Pages[row];
                var linearIndex = page.StartIndex + column;
                m_DataRecords[linearIndex] = value;
            }
        }

        public NativeArray2D(int initialCapacity, int defaultPageSize = 8, Allocator allocator = Allocator.Persistent)
        {
            m_DefaultPageSize = defaultPageSize;
            m_Pages = new NativeList<PageInfo>(initialCapacity, allocator);
            m_DataRecords = new NativeList<T>(initialCapacity * 4, allocator);
            m_LastRecordIndex = -1;
        }

        public ActivePageEnumerator GetActivePageEnumerator()
        {
            return new ActivePageEnumerator(m_Pages);
        }

        public PageInfo GetPageInfo(int index) => m_Pages[index];

        public int CreateArrayRecord(int pageSize = -1)
        {
            int startAddress = m_DataRecords.Length;
            int actualPageSize = pageSize < 0 ? m_DefaultPageSize : pageSize;

            var page = new PageInfo
            {
                StartIndex = startAddress,
                DataLength = 0,
                Capacity = actualPageSize
            };

            m_Pages.Add(page);
            m_LastRecordIndex = m_Pages.Length - 1;

            for (int i = 0; i < actualPageSize; i++)
            {
                m_DataRecords.Add(default(T));
            }

            return m_LastRecordIndex;
        }

        public void Append(T element)
        {
            var page = m_Pages[m_LastRecordIndex];

            if (page.DataLength >= page.Capacity)
            {
                var newCapacity = page.Capacity + m_DefaultPageSize;

                for (int i = 0; i < m_DefaultPageSize; i++)
                    m_DataRecords.Add(default);

                page.Capacity = newCapacity;
            }

            var elementIndex = page.StartIndex + page.DataLength;
            m_DataRecords[elementIndex] = element;

            page.DataLength++;
            m_Pages[m_LastRecordIndex] = page;
        }

        public int AddArray(NativeArray<T> rowData)
        {
            int recordIndex = CreateArrayRecord();

            for (int i = 0; i < rowData.Length; i++)
                Append(rowData[i]);

            return recordIndex;
        }

        public bool AppendAt(int recordIndex, T element)
        {
            if (recordIndex < 0 || recordIndex >= m_Pages.Length)
                return false;

            var page = m_Pages[recordIndex];

            if (page.DataLength < page.Capacity)
            {
                var elementIndex = page.StartIndex + page.DataLength;
                m_DataRecords[elementIndex] = element;

                page.DataLength++;
                m_Pages[recordIndex] = page;

                return true;
            }

            if (recordIndex == m_LastRecordIndex)
            {
                Append(element);
                return true;
            }

            int newStartIndex = m_DataRecords.Length;
            int newCapacity = page.Capacity * 2;

            for (int i = 0; i < newCapacity; i++)
                m_DataRecords.Add(default);

            for (int i = 0; i < page.DataLength; i++)
                m_DataRecords[newStartIndex + i] = m_DataRecords[page.StartIndex + i];

            m_DataRecords[newStartIndex + page.DataLength] = element;

            page.StartIndex = newStartIndex;
            page.DataLength++;
            page.Capacity = newCapacity;
            m_Pages[recordIndex] = page;

            m_LastRecordIndex = recordIndex;

            return true;
        }

        public bool RemoveAtArray(int pageIndex, int elementIndex)
        {
            if (pageIndex < 0 || pageIndex >= m_Pages.Length)
                return false;

            var page = m_Pages[pageIndex];

            if (elementIndex < 0 || elementIndex >= page.DataLength)
                return false;

            for (int i = elementIndex; i < page.DataLength - 1; i++)
            {
                int currentPos = page.StartIndex + i;
                int nextPos = page.StartIndex + i + 1;
                m_DataRecords[currentPos] = m_DataRecords[nextPos];
            }

            page.DataLength--;
            m_Pages[pageIndex] = page;
            return true;
        }

        public int GetLength(int rowIndex)
        {
            return m_Pages[rowIndex].DataLength;
        }

        public NativeSlice<T> GetRowSlice(int rowIndex)
        {
            var page = m_Pages[rowIndex];
            return new NativeSlice<T>(m_DataRecords.AsArray(), page.StartIndex, page.DataLength);
        }

        public NativeArray<T> GetRowArray(int rowIndex)
        {
            var page = m_Pages[rowIndex];
            return m_DataRecords.AsArray().GetSubArray(page.StartIndex, page.DataLength);
        }
        
        /// <summary>
        /// Creates a complete copy of this NativeArray2D with all pages and data
        /// </summary>
        /// <param name="allocator">The allocator to use for the new copy</param>
        /// <returns>A new NativeArray2D containing all the same data</returns>
        public NativeArray2D<T> GetCopy(Allocator allocator = Allocator.Persistent)
        {
            var copy = new NativeArray2D<T>(m_Pages.Length, m_DefaultPageSize, allocator);
            for (int i = 0; i < m_Pages.Length; i++)
            {
                var originalPage = m_Pages[i];
                int newRecordIndex = copy.CreateArrayRecord(originalPage.Capacity);
                for (int j = 0; j < originalPage.DataLength; j++)
                {
                    var element = m_DataRecords[originalPage.StartIndex + j];
                    copy.m_DataRecords[copy.m_Pages[newRecordIndex].StartIndex + j] = element;
                }
                var copiedPage = copy.m_Pages[newRecordIndex];
                copiedPage.DataLength = originalPage.DataLength;
                copy.m_Pages[newRecordIndex] = copiedPage;
            }
            copy.m_LastRecordIndex = m_LastRecordIndex;
            return copy;
        }

        public void Dispose()
        {
            m_Pages.Dispose();
            m_DataRecords.Dispose();
        }

        public JobHandle Dispose(JobHandle dependencies)
        {
            dependencies = m_Pages.Dispose(dependencies);
            dependencies = m_DataRecords.Dispose(dependencies);
            return dependencies;
        }

        #region Managed Implementations

        public int AddArray(T[] rowData)
        {
            using var nativeData = new NativeArray<T>(rowData, Allocator.Temp);
            return AddArray(nativeData);
        }

        public void ForEachActivePage(Action<int> action)
        {
            for (int i = 0; i < m_Pages.Length; i++)
                action(i);
        }

        public void ForEachActivePageSlice(Action<int, NativeSlice<T>> action)
        {
            for (int i = 0; i < m_Pages.Length; i++)
            {
                var page = m_Pages[i];
                var slice = new NativeSlice<T>(m_DataRecords.AsArray(), page.StartIndex, page.DataLength);
                action(i, slice);
            }
        }

        #endregion
    }
}