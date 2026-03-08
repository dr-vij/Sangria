using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SangriaMesh
{
    public partial struct NativeDetail : IDisposable
    {
        #region Point

        public int AddPoint(float3 position)
        {
            return AddPoint(position, out _);
        }

        public int AddPoint(float3 position, out ElementHandle handle)
        {
            int oldCapacity = m_Points.Capacity;
            int pointIndex = m_Points.Allocate(out handle);

            if (m_Points.Capacity != oldCapacity)
                m_PointAttributes.EnsureCapacity(m_Points.Capacity);

            m_PointAttributes.ClearElement(pointIndex);
            m_PointAttributes.TrySet(m_PointPositionHandle, pointIndex, position);

            m_TopologyVersion++;
            m_AttributeVersion++;
            return pointIndex;
        }

        public bool CanRemovePoint(
            int pointIndex,
            PointDeletePolicy pointPolicy = PointDeletePolicy.DeleteIncidentVertices,
            VertexDeletePolicy vertexPolicy = VertexDeletePolicy.RemoveFromIncidentPrimitives)
        {
            if (!m_Points.IsAlive(pointIndex))
                return false;

            if (pointPolicy != PointDeletePolicy.DeleteIncidentVertices &&
                pointPolicy != PointDeletePolicy.FailIfIncidentVerticesExist)
                return false;

            if (vertexPolicy != VertexDeletePolicy.RemoveFromIncidentPrimitives &&
                vertexPolicy != VertexDeletePolicy.DeleteIncidentPrimitives &&
                vertexPolicy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return false;

            if (m_AdjacencyDirty)
                return CanRemovePointWhenAdjacencyDirty(pointIndex, pointPolicy, vertexPolicy);

            EnsureAdjacencyUpToDate();
            using var pointVertices = new NativeList<int>(Allocator.Temp);
            CollectMultiMapValues(in m_PointToVertices, pointIndex, pointVertices);

            if (pointPolicy == PointDeletePolicy.FailIfIncidentVerticesExist)
            {
                for (int i = 0; i < pointVertices.Length; i++)
                {
                    if (m_Vertices.IsAlive(pointVertices[i]))
                        return false;
                }

                return true;
            }

            if (vertexPolicy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return true;

            for (int i = 0; i < pointVertices.Length; i++)
            {
                int vertexIndex = pointVertices[i];
                if (!m_Vertices.IsAlive(vertexIndex))
                    continue;
                if (HasIncidentPrimitivesNoPrepare(vertexIndex))
                    return false;
            }

            return true;
        }

        public bool RemovePoint(int pointIndex)
        {
            return RemovePoint(pointIndex, PointDeletePolicy.DeleteIncidentVertices, VertexDeletePolicy.RemoveFromIncidentPrimitives);
        }

        public bool RemovePoint(
            int pointIndex,
            PointDeletePolicy pointPolicy,
            VertexDeletePolicy vertexPolicy = VertexDeletePolicy.RemoveFromIncidentPrimitives)
        {
            if (!m_Points.IsAlive(pointIndex))
                return false;

            if (pointPolicy != PointDeletePolicy.DeleteIncidentVertices &&
                pointPolicy != PointDeletePolicy.FailIfIncidentVerticesExist)
                return false;

            if (vertexPolicy != VertexDeletePolicy.RemoveFromIncidentPrimitives &&
                vertexPolicy != VertexDeletePolicy.DeleteIncidentPrimitives &&
                vertexPolicy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return false;

            if (m_AdjacencyDirty)
                return RemovePointWhenAdjacencyDirty(pointIndex, pointPolicy, vertexPolicy);

            EnsureAdjacencyUpToDate();
            using var pointVertices = new NativeList<int>(Allocator.Temp);
            CollectMultiMapValues(in m_PointToVertices, pointIndex, pointVertices);

            if (pointPolicy == PointDeletePolicy.FailIfIncidentVerticesExist)
            {
                for (int i = 0; i < pointVertices.Length; i++)
                {
                    if (m_Vertices.IsAlive(pointVertices[i]))
                        return false;
                }
            }
            else if (vertexPolicy == VertexDeletePolicy.FailIfIncidentPrimitivesExist)
            {
                for (int i = 0; i < pointVertices.Length; i++)
                {
                    int vertexIndex = pointVertices[i];
                    if (!m_Vertices.IsAlive(vertexIndex))
                        continue;
                    if (HasIncidentPrimitivesNoPrepare(vertexIndex))
                        return false;
                }
            }

            if (pointPolicy == PointDeletePolicy.DeleteIncidentVertices)
            {
                for (int i = 0; i < pointVertices.Length; i++)
                {
                    int vertexIndex = pointVertices[i];
                    if (!m_Vertices.IsAlive(vertexIndex))
                        continue;
                    if (!RemoveVertexInternal(vertexIndex, vertexPolicy, adjacencyPrepared: true))
                        return false;
                }
            }

            m_PointToVertices.Remove(pointIndex);
            bool removed = m_Points.Release(pointIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        public bool IsPointAlive(int pointIndex) => m_Points.IsAlive(pointIndex);
        public bool IsPointHandleValid(ElementHandle handle) => m_Points.IsHandleValid(handle);
        public ElementHandle GetPointHandle(int pointIndex)
            => m_Points.IsAlive(pointIndex) ? new ElementHandle(pointIndex, m_Points.GetGeneration(pointIndex)) : default;

        public float3 GetPointPosition(int pointIndex)
        {
            if (!m_Points.IsAlive(pointIndex))
                return default;

            return m_PointAttributes.TryGet(m_PointPositionHandle, pointIndex, out float3 value) == CoreResult.Success ? value : default;
        }

        public bool SetPointPosition(int pointIndex, float3 position)
        {
            if (!m_Points.IsAlive(pointIndex))
                return false;

            if (m_PointAttributes.TrySet(m_PointPositionHandle, pointIndex, position) != CoreResult.Success)
                return false;

            m_AttributeVersion++;
            return true;
        }

        public void GetAllValidPoints(NativeList<int> output)
        {
            m_Points.GetAliveIndices(output);
        }

        #endregion

        #region Vertex

        public int AddVertex(int pointIndex)
        {
            return AddVertex(pointIndex, out _);
        }

        public int AddVertex(int pointIndex, out ElementHandle handle)
        {
            if (!m_Points.IsAlive(pointIndex))
            {
                handle = default;
                return -1;
            }

            EnsureAdjacencyUpToDate();

            int oldCapacity = m_Vertices.Capacity;
            int vertexIndex = m_Vertices.Allocate(out handle);

            if (m_Vertices.Capacity != oldCapacity)
            {
                EnsureVertexToPointCapacity(m_Vertices.Capacity);
                m_VertexAttributes.EnsureCapacity(m_Vertices.Capacity);
            }
            else
            {
                EnsureVertexToPointCapacity(vertexIndex + 1);
            }

            m_VertexToPoint[vertexIndex] = pointIndex;
            m_VertexAttributes.ClearElement(vertexIndex);
            EnsureMultiMapCapacity(ref m_PointToVertices, 1);
            m_PointToVertices.Add(pointIndex, vertexIndex);

            m_TopologyVersion++;
            m_AttributeVersion++;
            return vertexIndex;
        }

        internal unsafe int* GetVertexToPointPointerUnchecked()
        {
            var vertexToPointArray = m_VertexToPoint.AsArray();
            return (int*)NativeArrayUnsafeUtility.GetUnsafePtr(vertexToPointArray);
        }

        public bool CanRemoveVertex(
            int vertexIndex,
            VertexDeletePolicy policy = VertexDeletePolicy.RemoveFromIncidentPrimitives)
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return false;

            if (policy == VertexDeletePolicy.RemoveFromIncidentPrimitives ||
                policy == VertexDeletePolicy.DeleteIncidentPrimitives)
                return true;
            if (policy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return false;

            if (m_AdjacencyDirty)
                return !HasIncidentPrimitivesByScan(vertexIndex);

            EnsureAdjacencyUpToDate();
            return !HasIncidentPrimitivesNoPrepare(vertexIndex);
        }

        public bool RemoveVertex(int vertexIndex)
        {
            return RemoveVertex(vertexIndex, VertexDeletePolicy.RemoveFromIncidentPrimitives);
        }

        public bool RemoveVertex(
            int vertexIndex,
            VertexDeletePolicy policy = VertexDeletePolicy.RemoveFromIncidentPrimitives)
        {
            return RemoveVertexInternal(vertexIndex, policy, adjacencyPrepared: false);
        }

        public int GetVertexPoint(int vertexIndex)
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return -1;

            return m_VertexToPoint[vertexIndex];
        }

        public bool IsVertexAlive(int vertexIndex) => m_Vertices.IsAlive(vertexIndex);
        public bool IsVertexHandleValid(ElementHandle handle) => m_Vertices.IsHandleValid(handle);
        public ElementHandle GetVertexHandle(int vertexIndex)
            => m_Vertices.IsAlive(vertexIndex) ? new ElementHandle(vertexIndex, m_Vertices.GetGeneration(vertexIndex)) : default;

        public void GetAllValidVertices(NativeList<int> output)
        {
            m_Vertices.GetAliveIndices(output);
        }

        #endregion
    }
}
