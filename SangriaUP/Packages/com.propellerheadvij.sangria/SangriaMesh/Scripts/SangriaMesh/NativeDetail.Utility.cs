using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public unsafe partial struct NativeDetail : IDisposable
    {
        #region Utility

        public void Clear()
        {
            m_Points.Clear();
            m_Vertices.Clear();
            m_Primitives.Clear();

            m_PrimitiveStorage.Clear();
            FillNativeListWithMinusOne(m_VertexToPoint, m_VertexToPoint.Length);
            m_PointToVertices.Clear();
            m_VertexToPrimitives.Clear();
            m_AdjacencyDirty = false;

            m_Resources.Clear();

            m_TopologyVersion++;
            m_AttributeVersion++;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_Points.Dispose();
            m_Vertices.Dispose();
            m_Primitives.Dispose();

            if (m_VertexToPoint.IsCreated)
                m_VertexToPoint.Dispose();

            m_PrimitiveStorage.Dispose();
            if (m_PointToVertices.IsCreated)
                m_PointToVertices.Dispose();
            if (m_VertexToPrimitives.IsCreated)
                m_VertexToPrimitives.Dispose();

            m_PointAttributes.Dispose();
            m_VertexAttributes.Dispose();
            m_PrimitiveAttributes.Dispose();

            m_Resources.Dispose();

            m_IsDisposed = true;
        }

        private void EnsureVertexToPointCapacity(int required)
        {
            while (m_VertexToPoint.Length < required)
                m_VertexToPoint.Add(-1);
        }

        private void InvalidateAdjacency()
        {
            m_AdjacencyDirty = true;
        }

        private void CollectPointVerticesByScan(int pointIndex, NativeList<int> output)
        {
            output.Clear();

            if (m_Vertices.IsDenseContiguous)
            {
                int vertexCount = m_Vertices.Count;
                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    if (m_VertexToPoint[vertexIndex] == pointIndex)
                        output.Add(vertexIndex);
                }

                return;
            }

            using var aliveVertices = new NativeList<int>(Allocator.Temp);
            m_Vertices.GetAliveIndices(aliveVertices);
            for (int i = 0; i < aliveVertices.Length; i++)
            {
                int vertexIndex = aliveVertices[i];
                if (m_VertexToPoint[vertexIndex] == pointIndex)
                    output.Add(vertexIndex);
            }
        }

        private void CollectIncidentPrimitivesByScan(int vertexIndex, NativeList<int> output)
        {
            output.Clear();

            if (m_Primitives.IsDenseContiguous)
            {
                int primitiveCount = m_Primitives.Count;
                if (m_PrimitiveStorage.IsDenseTriangleLayout)
                {
                    int* primitiveData = m_PrimitiveStorage.GetDataPointerUnchecked();
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        if (primitiveData[triStart] == vertexIndex ||
                            primitiveData[triStart + 1] == vertexIndex ||
                            primitiveData[triStart + 2] == vertexIndex)
                        {
                            output.Add(primitiveIndex);
                        }
                    }

                    return;
                }

                for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                {
                    PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
                    for (int k = 0; k < record.Length; k++)
                    {
                        if (m_PrimitiveStorage.GetVertexUnchecked(record, k) != vertexIndex)
                            continue;

                        output.Add(primitiveIndex);
                        break;
                    }
                }

                return;
            }

            using var alivePrimitives = new NativeList<int>(Allocator.Temp);
            m_Primitives.GetAliveIndices(alivePrimitives);
            for (int i = 0; i < alivePrimitives.Length; i++)
            {
                int primitiveIndex = alivePrimitives[i];
                var vertices = m_PrimitiveStorage.GetVertices(primitiveIndex);
                for (int k = 0; k < vertices.Length; k++)
                {
                    if (vertices[k] != vertexIndex)
                        continue;

                    output.Add(primitiveIndex);
                    break;
                }
            }
        }

        private bool HasIncidentPrimitivesByScan(int vertexIndex)
        {
            if (m_Primitives.IsDenseContiguous)
            {
                int primitiveCount = m_Primitives.Count;
                if (m_PrimitiveStorage.IsDenseTriangleLayout)
                {
                    int* primitiveData = m_PrimitiveStorage.GetDataPointerUnchecked();
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        if (primitiveData[triStart] == vertexIndex ||
                            primitiveData[triStart + 1] == vertexIndex ||
                            primitiveData[triStart + 2] == vertexIndex)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                {
                    PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
                    for (int k = 0; k < record.Length; k++)
                    {
                        if (m_PrimitiveStorage.GetVertexUnchecked(record, k) == vertexIndex)
                            return true;
                    }
                }

                return false;
            }

            using var alivePrimitives = new NativeList<int>(Allocator.Temp);
            m_Primitives.GetAliveIndices(alivePrimitives);
            for (int i = 0; i < alivePrimitives.Length; i++)
            {
                int primitiveIndex = alivePrimitives[i];
                var vertices = m_PrimitiveStorage.GetVertices(primitiveIndex);
                for (int k = 0; k < vertices.Length; k++)
                {
                    if (vertices[k] == vertexIndex)
                        return true;
                }
            }

            return false;
        }

        private bool CanRemovePointWhenAdjacencyDirty(
            int pointIndex,
            PointDeletePolicy pointPolicy,
            VertexDeletePolicy vertexPolicy)
        {
            using var pointVertices = new NativeList<int>(Allocator.Temp);
            CollectPointVerticesByScan(pointIndex, pointVertices);

            if (pointPolicy == PointDeletePolicy.FailIfIncidentVerticesExist)
                return pointVertices.Length <= 0;

            if (vertexPolicy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return true;

            for (int i = 0; i < pointVertices.Length; i++)
            {
                if (HasIncidentPrimitivesByScan(pointVertices[i]))
                    return false;
            }

            return true;
        }

        private bool RemovePointWhenAdjacencyDirty(
            int pointIndex,
            PointDeletePolicy pointPolicy,
            VertexDeletePolicy vertexPolicy)
        {
            using var pointVertices = new NativeList<int>(Allocator.Temp);
            CollectPointVerticesByScan(pointIndex, pointVertices);

            if (pointPolicy == PointDeletePolicy.FailIfIncidentVerticesExist && pointVertices.Length > 0)
                return false;

            if (pointPolicy == PointDeletePolicy.DeleteIncidentVertices)
            {
                if (vertexPolicy == VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                {
                    for (int i = 0; i < pointVertices.Length; i++)
                    {
                        if (HasIncidentPrimitivesByScan(pointVertices[i]))
                            return false;
                    }
                }

                for (int i = 0; i < pointVertices.Length; i++)
                {
                    int vertexIndex = pointVertices[i];
                    if (!m_Vertices.IsAlive(vertexIndex))
                        continue;

                    if (!RemoveVertexWhenAdjacencyDirty(vertexIndex, vertexPolicy))
                        return false;
                }
            }

            bool removed = m_Points.Release(pointIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private bool RemoveVertexWhenAdjacencyDirty(int vertexIndex, VertexDeletePolicy policy)
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return false;

            if (policy != VertexDeletePolicy.RemoveFromIncidentPrimitives &&
                policy != VertexDeletePolicy.DeleteIncidentPrimitives &&
                policy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
                return false;

            if (policy == VertexDeletePolicy.FailIfIncidentPrimitivesExist && HasIncidentPrimitivesByScan(vertexIndex))
                return false;

            if (policy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
            {
                using var incidentPrimitives = new NativeList<int>(Allocator.Temp);
                CollectIncidentPrimitivesByScan(vertexIndex, incidentPrimitives);

                for (int i = 0; i < incidentPrimitives.Length; i++)
                {
                    int primitiveIndex = incidentPrimitives[i];
                    if (!m_Primitives.IsAlive(primitiveIndex))
                        continue;

                    if (policy == VertexDeletePolicy.DeleteIncidentPrimitives)
                        RemovePrimitiveWhenAdjacencyDirty(primitiveIndex);
                    else
                        RemoveVertexFromPrimitiveAllOccurrencesWhenAdjacencyDirty(primitiveIndex, vertexIndex);
                }
            }

            m_VertexToPoint[vertexIndex] = -1;
            bool removed = m_Vertices.Release(vertexIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private bool RemovePrimitiveWhenAdjacencyDirty(int primitiveIndex)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return false;

            m_PrimitiveStorage.ClearRecord(primitiveIndex);
            bool removed = m_Primitives.Release(primitiveIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private void RemoveVertexFromPrimitiveAllOccurrencesWhenAdjacencyDirty(int primitiveIndex, int vertexIndex)
        {
            PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
            bool removedAny = false;
            for (int k = record.Length - 1; k >= 0; k--)
            {
                if (m_PrimitiveStorage.GetVertexUnchecked(record, k) != vertexIndex)
                    continue;

                m_PrimitiveStorage.RemoveVertexAt(primitiveIndex, k);
                removedAny = true;
            }

            if (!removedAny)
                return;

            if (m_PrimitiveStorage.GetLength(primitiveIndex) < 3)
                RemovePrimitiveWhenAdjacencyDirty(primitiveIndex);
        }

        private bool HasIncidentPrimitivesNoPrepare(int vertexIndex)
        {
            if (!m_VertexToPrimitives.TryGetFirstValue(vertexIndex, out int primitiveIndex, out NativeParallelMultiHashMapIterator<int> iterator))
                return false;

            do
            {
                if (m_Primitives.IsAlive(primitiveIndex))
                    return true;
            }
            while (m_VertexToPrimitives.TryGetNextValue(out primitiveIndex, ref iterator));

            return false;
        }

        private bool RemoveVertexInternal(int vertexIndex, VertexDeletePolicy policy, bool adjacencyPrepared)
        {
            if (!m_Vertices.IsAlive(vertexIndex))
                return false;

            if (!adjacencyPrepared && m_AdjacencyDirty)
                return RemoveVertexWhenAdjacencyDirty(vertexIndex, policy);

            if (!adjacencyPrepared)
                EnsureAdjacencyUpToDate();

            if (policy == VertexDeletePolicy.FailIfIncidentPrimitivesExist && HasIncidentPrimitivesNoPrepare(vertexIndex))
                return false;

            if (policy != VertexDeletePolicy.FailIfIncidentPrimitivesExist)
            {
                if (policy != VertexDeletePolicy.DeleteIncidentPrimitives &&
                    policy != VertexDeletePolicy.RemoveFromIncidentPrimitives)
                    return false;

                using var incidentPrimitives = new NativeList<int>(Allocator.Temp);
                CollectMultiMapValues(in m_VertexToPrimitives, vertexIndex, incidentPrimitives);

                using var uniquePrimitiveGuard = new NativeHashSet<int>(math_max(1, incidentPrimitives.Length), Allocator.Temp);
                for (int i = 0; i < incidentPrimitives.Length; i++)
                {
                    int primitiveIndex = incidentPrimitives[i];
                    if (!uniquePrimitiveGuard.Add(primitiveIndex))
                        continue;
                    if (!m_Primitives.IsAlive(primitiveIndex))
                        continue;

                    if (policy == VertexDeletePolicy.DeleteIncidentPrimitives)
                        RemovePrimitiveInternal(primitiveIndex, adjacencyPrepared: true);
                    else
                        RemoveVertexFromPrimitiveAllOccurrencesInternal(primitiveIndex, vertexIndex, adjacencyPrepared: true);
                }
            }

            int pointIndex = m_VertexToPoint[vertexIndex];
            if (pointIndex >= 0)
                RemoveValueFromMultiMap(ref m_PointToVertices, pointIndex, vertexIndex, removeAllMatches: true);

            m_VertexToPrimitives.Remove(vertexIndex);
            m_VertexToPoint[vertexIndex] = -1;
            bool removed = m_Vertices.Release(vertexIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private bool RemovePrimitiveInternal(int primitiveIndex, bool adjacencyPrepared)
        {
            if (!m_Primitives.IsAlive(primitiveIndex))
                return false;

            if (!adjacencyPrepared)
                EnsureAdjacencyUpToDate();

            var vertices = m_PrimitiveStorage.GetVertices(primitiveIndex);
            using var uniqueVertices = new NativeHashSet<int>(math_max(1, vertices.Length), Allocator.Temp);
            for (int i = 0; i < vertices.Length; i++)
            {
                int vertexIndex = vertices[i];
                if (!uniqueVertices.Add(vertexIndex))
                    continue;

                RemoveValueFromMultiMap(ref m_VertexToPrimitives, vertexIndex, primitiveIndex, removeAllMatches: true);
            }

            m_PrimitiveStorage.ClearRecord(primitiveIndex);
            bool removed = m_Primitives.Release(primitiveIndex);
            if (removed)
                m_TopologyVersion++;

            return removed;
        }

        private void EnsureAdjacencyUpToDate()
        {
            if (!m_AdjacencyDirty)
                return;

            m_PointToVertices.Clear();
            m_VertexToPrimitives.Clear();

            EnsureMultiMapCapacity(ref m_PointToVertices, m_Vertices.Count);
            EnsureMultiMapCapacity(ref m_VertexToPrimitives, m_PrimitiveStorage.TotalVertexCount);

            if (m_Points.IsDenseContiguous && m_Vertices.IsDenseContiguous && m_Primitives.IsDenseContiguous)
            {
                int pointCount = m_Points.Count;
                int vertexCount = m_Vertices.Count;
                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    int pointIndex = m_VertexToPoint[vertexIndex];
                    if ((uint)pointIndex < (uint)pointCount)
                        m_PointToVertices.Add(pointIndex, vertexIndex);
                }

                int primitiveCount = m_Primitives.Count;
                if (m_PrimitiveStorage.IsDenseTriangleLayout)
                {
                    int* primitiveData = m_PrimitiveStorage.GetDataPointerUnchecked();
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        int triStart = primitiveIndex * 3;
                        m_VertexToPrimitives.Add(primitiveData[triStart], primitiveIndex);
                        m_VertexToPrimitives.Add(primitiveData[triStart + 1], primitiveIndex);
                        m_VertexToPrimitives.Add(primitiveData[triStart + 2], primitiveIndex);
                    }
                }
                else
                {
                    for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                    {
                        PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
                        for (int k = 0; k < record.Length; k++)
                            m_VertexToPrimitives.Add(m_PrimitiveStorage.GetVertexUnchecked(record, k), primitiveIndex);
                    }
                }

                m_AdjacencyDirty = false;
                return;
            }

            using var aliveVertices = new NativeList<int>(Allocator.Temp);
            m_Vertices.GetAliveIndices(aliveVertices);
            for (int i = 0; i < aliveVertices.Length; i++)
            {
                int vertexIndex = aliveVertices[i];
                int pointIndex = m_VertexToPoint[vertexIndex];
                if (!m_Points.IsAlive(pointIndex))
                    continue;

                m_PointToVertices.Add(pointIndex, vertexIndex);
            }

            using var alivePrimitives = new NativeList<int>(Allocator.Temp);
            m_Primitives.GetAliveIndices(alivePrimitives);
            for (int i = 0; i < alivePrimitives.Length; i++)
            {
                int primitiveIndex = alivePrimitives[i];
                PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
                for (int k = 0; k < record.Length; k++)
                {
                    int vertexIndex = m_PrimitiveStorage.GetVertexUnchecked(record, k);
                    if (!m_Vertices.IsAlive(vertexIndex))
                        continue;

                    m_VertexToPrimitives.Add(vertexIndex, primitiveIndex);
                }
            }

            m_AdjacencyDirty = false;
        }

        private void RemoveVertexFromPrimitiveAllOccurrencesInternal(int primitiveIndex, int vertexIndex, bool adjacencyPrepared)
        {
            if (!adjacencyPrepared)
                EnsureAdjacencyUpToDate();

            PrimitiveRecord record = m_PrimitiveStorage.GetRecordUnchecked(primitiveIndex);
            bool removedAny = false;
            for (int k = record.Length - 1; k >= 0; k--)
            {
                if (m_PrimitiveStorage.GetVertexUnchecked(record, k) != vertexIndex)
                    continue;

                m_PrimitiveStorage.RemoveVertexAt(primitiveIndex, k);
                removedAny = true;
            }

            if (!removedAny)
                return;

            RemoveValueFromMultiMap(ref m_VertexToPrimitives, vertexIndex, primitiveIndex, removeAllMatches: true);

            if (m_PrimitiveStorage.GetLength(primitiveIndex) < 3)
                RemovePrimitiveInternal(primitiveIndex, adjacencyPrepared: true);
        }

        private static void CollectMultiMapValues(
            in NativeParallelMultiHashMap<int, int> map,
            int key,
            NativeList<int> output)
        {
            output.Clear();

            if (!map.TryGetFirstValue(key, out int value, out NativeParallelMultiHashMapIterator<int> iterator))
                return;

            do
            {
                output.Add(value);
            }
            while (map.TryGetNextValue(out value, ref iterator));
        }

        private static int RemoveValueFromMultiMap(
            ref NativeParallelMultiHashMap<int, int> map,
            int key,
            int valueToRemove,
            bool removeAllMatches)
        {
            int removed = 0;
            while (map.TryGetFirstValue(key, out int value, out NativeParallelMultiHashMapIterator<int> iterator))
            {
                bool removedOne = false;
                do
                {
                    if (value != valueToRemove)
                        continue;

                    map.Remove(iterator);
                    removed++;
                    removedOne = true;
                    break;
                }
                while (map.TryGetNextValue(out value, ref iterator));

                if (!removedOne || !removeAllMatches)
                    break;
            }

            return removed;
        }

        private static void EnsureMultiMapCapacity(ref NativeParallelMultiHashMap<int, int> map, int additionalEntries)
        {
            if (additionalEntries <= 0)
                return;

            int required = map.Count() + additionalEntries;
            if (required <= map.Capacity)
                return;

            int newCapacity = math_max(1, map.Capacity);
            while (newCapacity < required)
                newCapacity *= 2;

            map.Capacity = newCapacity;
        }

        private static unsafe CompiledAttributeSet CompileAttributes(
            AttributeStore source,
            NativeList<int> aliveSparseIndices,
            Allocator allocator)
        {
            int attributeCount = source.GetColumnCount();
            int elementCount = aliveSparseIndices.Length;

            var descriptors = new NativeArray<CompiledAttributeDescriptor>(attributeCount, allocator);
            var idToDescriptor = new NativeParallelHashMap<int, int>(math_max(1, attributeCount), allocator);

            int totalBytes = 0;
            for (int i = 0; i < attributeCount; i++)
            {
                var column = source.GetColumnAt(i);
                totalBytes += column.Stride * elementCount;
            }

            var data = new NativeArray<byte>(math_max(1, totalBytes), allocator, NativeArrayOptions.UninitializedMemory);
            byte* basePtr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(data);

            int runningOffset = 0;
            for (int i = 0; i < attributeCount; i++)
            {
                var column = source.GetColumnAt(i);

                descriptors[i] = new CompiledAttributeDescriptor
                {
                    AttributeId = column.AttributeId,
                    TypeHash = column.TypeHash,
                    Stride = column.Stride,
                    Count = elementCount,
                    OffsetBytes = runningOffset
                };

                idToDescriptor[column.AttributeId] = i;

                for (int k = 0; k < elementCount; k++)
                {
                    int sparseIndex = aliveSparseIndices[k];
                    byte* src = column.Buffer.Ptr + sparseIndex * column.Stride;
                    byte* dst = basePtr + runningOffset + k * column.Stride;
                    UnsafeUtility.MemCpy(dst, src, column.Stride);
                }

                runningOffset += column.Stride * elementCount;
            }

            return new CompiledAttributeSet(descriptors, idToDescriptor, data);
        }

        private static unsafe CompiledAttributeSet CompileAttributesDense(
            AttributeStore source,
            int elementCount,
            Allocator allocator)
        {
            int attributeCount = source.GetColumnCount();

            var descriptors = new NativeArray<CompiledAttributeDescriptor>(attributeCount, allocator);
            var idToDescriptor = new NativeParallelHashMap<int, int>(math_max(1, attributeCount), allocator);

            int totalBytes = 0;
            for (int i = 0; i < attributeCount; i++)
            {
                var column = source.GetColumnAt(i);
                totalBytes += column.Stride * elementCount;
            }

            var data = new NativeArray<byte>(math_max(1, totalBytes), allocator, NativeArrayOptions.UninitializedMemory);
            byte* basePtr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(data);

            int runningOffset = 0;
            for (int i = 0; i < attributeCount; i++)
            {
                var column = source.GetColumnAt(i);
                int copyBytes = column.Stride * elementCount;

                descriptors[i] = new CompiledAttributeDescriptor
                {
                    AttributeId = column.AttributeId,
                    TypeHash = column.TypeHash,
                    Stride = column.Stride,
                    Count = elementCount,
                    OffsetBytes = runningOffset
                };

                idToDescriptor[column.AttributeId] = i;

                if (copyBytes > 0)
                    UnsafeUtility.MemCpy(basePtr + runningOffset, column.Buffer.Ptr, copyBytes);

                runningOffset += copyBytes;
            }

            return new CompiledAttributeSet(descriptors, idToDescriptor, data);
        }

        private static unsafe void FillNativeArrayWithMinusOne(NativeArray<int> array)
        {
            if (array.Length <= 0)
                return;

            UnsafeUtility.MemSet(NativeArrayUnsafeUtility.GetUnsafePtr(array), 0xFF, array.Length * UnsafeUtility.SizeOf<int>());
        }

        private static unsafe void FillNativeListWithMinusOne(NativeList<int> list, int length)
        {
            if (length <= 0)
                return;

            var array = list.AsArray();
            UnsafeUtility.MemSet(NativeArrayUnsafeUtility.GetUnsafePtr(array), 0xFF, length * UnsafeUtility.SizeOf<int>());
        }

        private static int math_max(int a, int b) => a > b ? a : b;

        #endregion
    }
}
