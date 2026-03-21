// Core: Primitive topology operations and dense topology allocation helpers.
using System;
using Unity.Collections;

namespace SangriaMesh
{
    public partial struct NativeDetail : IDisposable
    {
        #region Primitive

        public int AddPrimitive(NativeArray<int> vertexIndices)
        {
            return AddPrimitive(vertexIndices, out _);
        }

        public int AddPrimitive(NativeArray<int> vertexIndices, out ElementHandle handle)
        {
            return AddPrimitive(new NativeSlice<int>(vertexIndices), out handle);
        }

        public int AddPrimitive(NativeSlice<int> vertexIndices)
        {
            return AddPrimitive(vertexIndices, out _);
        }

        public int AddPrimitive(NativeSlice<int> vertexIndices, out ElementHandle handle)
        {
            if (vertexIndices.Length < 3)
            {
                handle = default;
                return -1;
            }

            for (int i = 0; i < vertexIndices.Length; i++)
            {
                if (!m_Vertices.IsAlive(vertexIndices[i]))
                {
                    handle = default;
                    return -1;
                }
            }

            EnsureAdjacencyUpToDate();

            int oldCapacity = m_Primitives.Capacity;
            int primitiveIndex = m_Primitives.Allocate(out handle);

            if (m_Primitives.Capacity != oldCapacity)
                m_PrimitiveAttributes.EnsureCapacity(m_Primitives.Capacity);

            m_PrimitiveAttributes.ClearElement(primitiveIndex);
            m_PrimitiveStorage.SetVertices(primitiveIndex, vertexIndices);
            EnsureMultiMapCapacity(ref m_VertexToPrimitives, vertexIndices.Length);
            for (int i = 0; i < vertexIndices.Length; i++)
            {
                m_VertexToPrimitives.Add(vertexIndices[i], primitiveIndex);
            }

            m_TopologyVersion++;
            m_AttributeVersion++;
            return primitiveIndex;
        }

        internal unsafe int* GetPrimitiveTriangleDataPointerUnchecked()
        {
            return m_PrimitiveStorage.GetDataPointerUnchecked();
        }

        internal void AllocateDenseTopologyUnchecked(
            int pointCount,
            int vertexCount,
            int primitiveCount,
            bool prepareTriangleStorage,
            bool initializeVertexToPoint = true)
        {
            if (m_Points.Count != 0 || m_Vertices.Count != 0 || m_Primitives.Count != 0)
                throw new InvalidOperationException("AllocateDenseTopologyUnchecked requires empty detail.");

            if (pointCount > 0)
            {
                int oldPointCapacity = m_Points.Capacity;
                m_Points.AllocateDenseRange(pointCount);
                if (m_Points.Capacity != oldPointCapacity)
                    m_PointAttributes.EnsureCapacity(m_Points.Capacity);

                m_PointAttributes.ClearRange(0, pointCount);
            }

            if (vertexCount > 0)
            {
                int oldVertexCapacity = m_Vertices.Capacity;
                m_Vertices.AllocateDenseRange(vertexCount);

                if (m_Vertices.Capacity != oldVertexCapacity)
                    m_VertexAttributes.EnsureCapacity(m_Vertices.Capacity);

                EnsureVertexToPointCapacity(vertexCount);
                if (initializeVertexToPoint)
                    FillNativeListWithMinusOne(m_VertexToPoint, vertexCount);

                m_VertexAttributes.ClearRange(0, vertexCount);
            }

            if (primitiveCount > 0)
            {
                int oldPrimitiveCapacity = m_Primitives.Capacity;
                m_Primitives.AllocateDenseRange(primitiveCount);
                if (m_Primitives.Capacity != oldPrimitiveCapacity)
                    m_PrimitiveAttributes.EnsureCapacity(m_Primitives.Capacity);

                m_PrimitiveAttributes.ClearRange(0, primitiveCount);
            }

            if (prepareTriangleStorage)
                m_PrimitiveStorage.PrepareDenseTriangleRecords(primitiveCount);
            else if (primitiveCount > 0)
                m_PrimitiveStorage.EnsureRecordSlot(primitiveCount - 1);

            m_PointToVertices.Clear();
            m_VertexToPrimitives.Clear();
            InvalidateAdjacency();
        }

        internal void MarkTopologyAndAttributeChanged()
        {
            m_TopologyVersion++;
            m_AttributeVersion++;
            m_PointToVertices.Clear();
            m_VertexToPrimitives.Clear();
            InvalidateAdjacency();
        }

        public bool RemovePrimitive(int primitiveIndex)
        {
            return RemovePrimitiveInternal(primitiveIndex, adjacencyPrepared: false);
        }

        public bool AddVertexToPrimitive(int primitiveIndex, int vertexIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex) || !m_Vertices.IsAlive(vertexIndex))
                return false;

            EnsureAdjacencyUpToDate();

            bool result = m_PrimitiveStorage.AppendVertex(primitiveIndex, vertexIndex);
            if (result)
            {
                EnsureMultiMapCapacity(ref m_VertexToPrimitives, 1);
                m_VertexToPrimitives.Add(vertexIndex, primitiveIndex);
                m_TopologyVersion++;
            }

            return result;
        }

        public bool RemoveVertexFromPrimitive(int primitiveIndex, int vertexOffset)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return false;

            EnsureAdjacencyUpToDate();

            int removedVertexIndex = m_PrimitiveStorage.GetVertex(primitiveIndex, vertexOffset);
            bool removed = m_PrimitiveStorage.RemoveVertexAt(primitiveIndex, vertexOffset);
            if (!removed)
                return false;

            if (removedVertexIndex >= 0)
                RemoveValueFromMultiMap(ref m_VertexToPrimitives, removedVertexIndex, primitiveIndex, removeAllMatches: false);

            if (m_PrimitiveStorage.GetLength(primitiveIndex) < 3)
                return RemovePrimitiveInternal(primitiveIndex, adjacencyPrepared: true);

            m_TopologyVersion++;
            return true;
        }

        public NativeSlice<int> GetPrimitiveVertices(int primitiveIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return default;

            return m_PrimitiveStorage.GetVertices(primitiveIndex);
        }

        public int GetPrimitiveVertexCount(int primitiveIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return 0;

            return m_PrimitiveStorage.GetLength(primitiveIndex);
        }

        public bool CollectGarbage(float minGarbageRatio = 0f)
        {
            return m_PrimitiveStorage.CollectGarbage(minGarbageRatio);
        }

        public bool IsPrimitiveAlive(int primitiveIndex) => m_Primitives.IsAlive(primitiveIndex);
        public bool IsPrimitiveHandleValid(ElementHandle handle) => m_Primitives.IsHandleValid(handle);
        public ElementHandle GetPrimitiveHandle(int primitiveIndex)
            => m_Primitives.IsAlive(primitiveIndex) ? new ElementHandle(primitiveIndex, m_Primitives.GetGeneration(primitiveIndex)) : default;

        public void GetAllValidPrimitives(NativeList<int> output)
        {
            m_Primitives.GetAliveIndices(output);
        }

        #endregion
    }
}
