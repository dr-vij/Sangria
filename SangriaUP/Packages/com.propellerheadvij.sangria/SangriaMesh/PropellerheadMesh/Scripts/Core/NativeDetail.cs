using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

namespace PropellerheadMesh
{
    [BurstCompile]
    public partial struct NativeDetail : IDisposable
    {
        private bool m_IsDisposed;
        private readonly Allocator m_Allocator;

        // Core validity tracking
        private NativeBitArray m_ValidPoints;
        private NativeBitArray m_ValidVertices;

        // Free indices management
        private NativeList<int> m_FreePointIndices;
        private NativeList<int> m_FreeVertexIndices;
        private NativeList<int> m_FreePrimIndices;

        // Attributes
        private AttributeMap m_PointAttributes;
        private AttributeMap m_VertexAttributes;
        private AttributeMap m_PrimitiveAttributes;

        // Relationships
        private NativeArray<int> m_VertexToPoint; // vertex index -> point index

        // Primitive vertices storage using NativeArray2D
        private NativeArray2D<int> m_Primitives;

        // Capacity tracking
        private int m_PointCapacity;
        private int m_VertexCapacity;
        private int m_PrimitiveCapacity;

        // Counts
        private int m_PointCount;
        private int m_VertexCount;
        private int m_PrimitiveCount;

        public int PointCount => m_PointCount;
        public int VertexCount => m_VertexCount;
        public int PrimitiveCount => m_PrimitiveCount;

        public NativeDetail(int initialCapacity, Allocator allocator)
        {
            m_Allocator = allocator;
            m_IsDisposed = false;

            // Initialize capacities
            m_PointCapacity = initialCapacity;
            m_VertexCapacity = initialCapacity;
            m_PrimitiveCapacity = initialCapacity;

            // Initialize validity tracking
            m_ValidPoints = new NativeBitArray(m_PointCapacity, allocator);
            m_ValidVertices = new NativeBitArray(m_VertexCapacity, allocator);

            // Initialize free indices
            m_FreePointIndices = new NativeList<int>(allocator);
            m_FreeVertexIndices = new NativeList<int>(allocator);
            m_FreePrimIndices = new NativeList<int>(allocator);

            // Initialize attributes
            m_PointAttributes = new AttributeMap(8, allocator);
            m_VertexAttributes = new AttributeMap(8, allocator);
            m_PrimitiveAttributes = new AttributeMap(8, allocator);

            // Initialize relationships
            m_VertexToPoint = new NativeArray<int>(m_VertexCapacity, allocator);

            // Initialize primitive vertices storage
            m_Primitives = new NativeArray2D<int>(m_PrimitiveCapacity, 8, allocator);

            // Initialize counts
            m_PointCount = 0;
            m_VertexCount = 0;
            m_PrimitiveCount = 0;

            // Register mandatory Position attribute
            m_PointAttributes.RegisterAttribute<float3>(AttributeID.Position, m_PointCapacity);
        }

        #region Capacity Management

        private void EnsurePointCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= m_PointCapacity)
                return;

            int newCapacity = m_PointCapacity;
            while (newCapacity < requiredCapacity)
                newCapacity *= 2;

            // Resize validity tracking
            var newValidPoints = new NativeBitArray(newCapacity, m_Allocator);
            for (int i = 0; i < m_PointCapacity; i++)
            {
                newValidPoints.Set(i, m_ValidPoints.IsSet(i));
            }

            m_ValidPoints.Dispose();
            m_ValidPoints = newValidPoints;

            // Resize attributes
            m_PointAttributes.ResizeAllAttributes(newCapacity);

            m_PointCapacity = newCapacity;
        }

        private void EnsureVertexCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= m_VertexCapacity)
                return;

            int newCapacity = m_VertexCapacity;
            while (newCapacity < requiredCapacity)
                newCapacity *= 2;

            // Resize validity tracking
            var newValidVertices = new NativeBitArray(newCapacity, m_Allocator);
            for (int i = 0; i < m_VertexCapacity; i++)
            {
                newValidVertices.Set(i, m_ValidVertices.IsSet(i));
            }

            m_ValidVertices.Dispose();
            m_ValidVertices = newValidVertices;

            // Resize vertex to point mapping
            var newVertexToPoint = new NativeArray<int>(newCapacity, m_Allocator);
            NativeArray<int>.Copy(m_VertexToPoint, newVertexToPoint, m_VertexCapacity);
            m_VertexToPoint.Dispose();
            m_VertexToPoint = newVertexToPoint;

            // Resize attributes
            m_VertexAttributes.ResizeAllAttributes(newCapacity);

            m_VertexCapacity = newCapacity;
        }

        private void EnsurePrimitiveCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= m_PrimitiveCapacity)
                return;

            int newCapacity = m_PrimitiveCapacity;
            while (newCapacity < requiredCapacity)
                newCapacity *= 2;

            // Resize attributes
            m_PrimitiveAttributes.ResizeAllAttributes(newCapacity);

            m_PrimitiveCapacity = newCapacity;
        }

        #endregion

        #region Attribute Management

        public AttributeMapResult AddPointAttribute<T>(int attributeId) where T : unmanaged
        {
            return m_PointAttributes.RegisterAttribute<T>(attributeId, m_PointCapacity);
        }

        public AttributeMapResult AddVertexAttribute<T>(int attributeId) where T : unmanaged
        {
            return m_VertexAttributes.RegisterAttribute<T>(attributeId, m_VertexCapacity);
        }

        public AttributeMapResult AddPrimitiveAttribute<T>(int attributeId) where T : unmanaged
        {
            return m_PrimitiveAttributes.RegisterAttribute<T>(attributeId, m_PrimitiveCapacity);
        }

        public AttributeMapResult RemovePointAttribute(int attributeId)
        {
            if (attributeId == AttributeID.Position)
                return AttributeMapResult.AttributeNotFound; // Cannot remove position

            return m_PointAttributes.RemoveAttribute(attributeId);
        }

        public AttributeMapResult RemoveVertexAttribute(int attributeId)
        {
            return m_VertexAttributes.RemoveAttribute(attributeId);
        }

        public AttributeMapResult RemovePrimitiveAttribute(int attributeId)
        {
            return m_PrimitiveAttributes.RemoveAttribute(attributeId);
        }

        public AttributeMapResult GetPointAttributeAccessor<T>(int attributeId, out NativeAttributeAccessor<T> accessor)
            where T : unmanaged
        {
            return m_PointAttributes.TryGetAccessor(attributeId, out accessor);
        }

        public AttributeMapResult GetVertexAttributeAccessor<T>(int attributeId,
            out NativeAttributeAccessor<T> accessor) where T : unmanaged
        {
            return m_VertexAttributes.TryGetAccessor(attributeId, out accessor);
        }

        public AttributeMapResult GetPrimitiveAttributeAccessor<T>(int attributeId,
            out NativeAttributeAccessor<T> accessor) where T : unmanaged
        {
            return m_PrimitiveAttributes.TryGetAccessor(attributeId, out accessor);
        }

        public bool HasPointAttribute(int attributeId) => m_PointAttributes.ContainsAttribute(attributeId);
        public bool HasVertexAttribute(int attributeId) => m_VertexAttributes.ContainsAttribute(attributeId);
        public bool HasPrimitiveAttribute(int attributeId) => m_PrimitiveAttributes.ContainsAttribute(attributeId);

        public T GetPointAttribute<T>(int pointIndex, int attributeId) where T : unmanaged
        {
            if (!IsPointValid(pointIndex))
                return default;

            if (m_PointAttributes.TryGetAccessor<T>(attributeId, out var accessor) == AttributeMapResult.Success)
                return accessor[pointIndex];

            return default;
        }

        public bool SetPointAttribute<T>(int pointIndex, int attributeId, T value) where T : unmanaged
        {
            if (!IsPointValid(pointIndex))
                return false;

            if (m_PointAttributes.TryGetAccessor<T>(attributeId, out var accessor) != AttributeMapResult.Success)
                return false;

            accessor[pointIndex] = value;
            return true;
        }

        public T GetVertexAttribute<T>(int vertexIndex, int attributeId) where T : unmanaged
        {
            if (!IsVertexValid(vertexIndex))
                return default;

            if (m_VertexAttributes.TryGetAccessor<T>(attributeId, out var accessor) == AttributeMapResult.Success)
            {
                return accessor[vertexIndex];
            }

            return default;
        }

        public bool SetVertexAttribute<T>(int vertexIndex, int attributeId, T value) where T : unmanaged
        {
            if (!IsVertexValid(vertexIndex))
                return false;

            if (m_VertexAttributes.TryGetAccessor<T>(attributeId, out var accessor) != AttributeMapResult.Success)
                return false;

            accessor[vertexIndex] = value;
            return true;
        }

        public T GetPrimitiveAttribute<T>(int primitiveIndex, int attributeId) where T : unmanaged
        {
            if (!IsPrimitiveValid(primitiveIndex))
                return default;

            if (m_PrimitiveAttributes.TryGetAccessor<T>(attributeId, out var accessor) == AttributeMapResult.Success)
                return accessor[primitiveIndex];

            return default;
        }

        public bool SetPrimitiveAttribute<T>(int primitiveIndex, int attributeId, T value) where T : unmanaged
        {
            if (!IsPrimitiveValid(primitiveIndex))
                return false;

            if (m_PrimitiveAttributes.TryGetAccessor<T>(attributeId, out var accessor) != AttributeMapResult.Success)
                return false;

            accessor[primitiveIndex] = value;
            return true;
        }

        #endregion

        #region Point Management

        public int AddPoint(float3 position)
        {
            int pointIndex;

            if (m_FreePointIndices.Length > 0)
            {
                pointIndex = m_FreePointIndices[^1];
                m_FreePointIndices.RemoveAtSwapBack(m_FreePointIndices.Length - 1);
            }
            else
            {
                pointIndex = m_PointCount;
                EnsurePointCapacity(pointIndex + 1);
            }

            m_ValidPoints.Set(pointIndex, true);
            m_PointCount++;

            // Set position
            SetPointAttribute(pointIndex, AttributeID.Position, position);

            return pointIndex;
        }

        public bool RemovePoint(int pointIndex)
        {
            if (!IsPointValid(pointIndex))
                return false;

            // Remove all vertices that reference this point
            for (int i = 0; i < m_VertexCapacity; i++)
            {
                if (m_ValidVertices.IsSet(i) && m_VertexToPoint[i] == pointIndex)
                {
                    RemoveVertex(i);
                }
            }

            m_ValidPoints.Set(pointIndex, false);
            m_FreePointIndices.Add(pointIndex);
            m_PointCount--;

            return true;
        }

        public float3 GetPointPosition(int pointIndex)
        {
            return GetPointAttribute<float3>(pointIndex, AttributeID.Position);
        }

        public void SetPointPosition(int pointIndex, float3 position)
        {
            SetPointAttribute(pointIndex, AttributeID.Position, position);
        }

        public bool IsPointValid(int pointIndex)
        {
            return pointIndex >= 0 && pointIndex < m_PointCapacity && m_ValidPoints.IsSet(pointIndex);
        }

        public void GetAllValidPoints(NativeList<int> validPoints)
        {
            validPoints.Clear();
            for (int i = 0; i < m_PointCapacity; i++)
            {
                if (m_ValidPoints.IsSet(i))
                    validPoints.Add(i);
            }
        }

        #endregion

        #region Vertex Management

        public int AddVertex(int pointIndex)
        {
            if (!IsPointValid(pointIndex))
                return -1;

            int vertexIndex;

            if (m_FreeVertexIndices.Length > 0)
            {
                vertexIndex = m_FreeVertexIndices[m_FreeVertexIndices.Length - 1];
                m_FreeVertexIndices.RemoveAtSwapBack(m_FreeVertexIndices.Length - 1);
            }
            else
            {
                vertexIndex = m_VertexCount;
                EnsureVertexCapacity(vertexIndex + 1);
            }

            m_ValidVertices.Set(vertexIndex, true);
            m_VertexToPoint[vertexIndex] = pointIndex;
            m_VertexCount++;

            return vertexIndex;
        }

        public bool RemoveVertex(int vertexIndex)
        {
            if (!IsVertexValid(vertexIndex))
                return false;

            // Remove from all primitives that contain this vertex
            var enumerator = m_Primitives.GetActivePageEnumerator();
            while (enumerator.MoveNext())
            {
                int pageIndex = enumerator.CurrentIndex;
                var vertexSlice = m_Primitives.GetRowSlice(pageIndex);
                for (int i = vertexSlice.Length - 1; i >= 0; i--)
                {
                    if (vertexSlice[i] == vertexIndex)
                    {
                        m_Primitives.RemoveAtArray(pageIndex, i);
                    }
                }

                // Remove primitive if it has less than 3 vertices
                if (m_Primitives.GetLength(pageIndex) < 3)
                {
                    RemovePrimitive(pageIndex);
                }
            }

            m_ValidVertices.Set(vertexIndex, false);
            m_FreeVertexIndices.Add(vertexIndex);
            m_VertexCount--;

            return true;
        }

        public int GetVertexPoint(int vertexIndex)
        {
            if (!IsVertexValid(vertexIndex))
                return -1;

            return m_VertexToPoint[vertexIndex];
        }

        public int GetVertexPointUnsafe(int vertexIndex)
        {
            return m_VertexToPoint[vertexIndex];
        }

        public bool IsVertexValid(int vertexIndex)
        {
            return vertexIndex >= 0 && vertexIndex < m_VertexCapacity && m_ValidVertices.IsSet(vertexIndex);
        }

        public void GetAllValidVertices(NativeList<int> validVertices)
        {
            validVertices.Clear();
            for (int i = 0; i < m_VertexCapacity; i++)
            {
                if (m_ValidVertices.IsSet(i))
                    validVertices.Add(i);
            }
        }

        #endregion

        #region Primitive Management

        public int AddPrimitive(NativeArray<int> vertexIndices)
        {
            // Validate all vertices
            for (int i = 0; i < vertexIndices.Length; i++)
            {
                if (!IsVertexValid(vertexIndices[i]))
                    return -1;
            }

            // Ensure capacity before creating new primitive
            EnsurePrimitiveCapacity(m_PrimitiveCount + 1);

            // Create a new array record in NativeArray2D
            int primitiveIndex = m_Primitives.CreateArrayRecord();

            // Add vertices to the array
            for (int i = 0; i < vertexIndices.Length; i++)
            {
                m_Primitives.Append(vertexIndices[i]);
            }

            m_PrimitiveCount++;

            return primitiveIndex;
        }

        public bool RemovePrimitive(int primitiveIndex)
        {
            if (!IsPrimitiveValid(primitiveIndex))
                return false;

            // Clear the primitive vertices by setting length to 0
            // Note: NativeArray2D will automatically mark the page as inactive when length becomes 0
            while (m_Primitives.GetLength(primitiveIndex) > 0)
                m_Primitives.RemoveAtArray(primitiveIndex, 0);

            m_FreePrimIndices.Add(primitiveIndex);
            m_PrimitiveCount--;

            return true;
        }

        public bool IsPrimitiveValid(int primitiveIndex)
        {
            return primitiveIndex >= 0 && primitiveIndex < m_PrimitiveCapacity;
        }

        public NativeSlice<int> GetPrimitiveVertices(int primitiveIndex)
        {
            if (!IsPrimitiveValid(primitiveIndex))
                return default;

            return m_Primitives.GetRowSlice(primitiveIndex);
        }

        public int GetPrimitiveVertexCount(int primitiveIndex)
        {
            if (!IsPrimitiveValid(primitiveIndex))
                return 0;

            return m_Primitives.GetLength(primitiveIndex);
        }

        public int GetPrimitiveVertex(int primitiveIndex, int vertexIndexInPrimitive)
        {
            if (!IsPrimitiveValid(primitiveIndex))
                return -1;

            if (vertexIndexInPrimitive < 0 || vertexIndexInPrimitive >= m_Primitives.GetLength(primitiveIndex))
                return -1;

            return m_Primitives[primitiveIndex, vertexIndexInPrimitive];
        }

        public bool AddVertexToPrimitive(int primitiveIndex, int vertexIndex)
        {
            if (!IsPrimitiveValid(primitiveIndex) || !IsVertexValid(vertexIndex))
                return false;

            m_Primitives.AppendAt(primitiveIndex, vertexIndex);
            return true;
        }

        public bool RemoveVertexFromPrimitive(int primitiveIndex, int vertexIndexInPrimitive)
        {
            if (!IsPrimitiveValid(primitiveIndex))
                return false;

            bool result = m_Primitives.RemoveAtArray(primitiveIndex, vertexIndexInPrimitive);

            // Remove primitive if it has less than 3 vertices
            if (result && m_Primitives.GetLength(primitiveIndex) < 3)
            {
                RemovePrimitive(primitiveIndex);
            }

            return result;
        }

        public void GetAllValidPrimitives(NativeList<int> validPrimitives)
        {
            validPrimitives.Clear();
            var enumerator = m_Primitives.GetActivePageEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.CurrentPageInfo.DataLength > 0)
                    validPrimitives.Add(enumerator.CurrentIndex);
            }
        }

        #endregion

        #region Utility Methods

        public void Clear()
        {
            // Clear validity arrays
            m_ValidPoints.SetBits(0, false, m_PointCapacity);
            m_ValidVertices.SetBits(0, false, m_VertexCapacity);

            // Clear free indices
            m_FreePointIndices.Clear();
            m_FreeVertexIndices.Clear();
            m_FreePrimIndices.Clear();

            // Clear primitive vertices
            m_Primitives.Dispose();
            m_Primitives = new NativeArray2D<int>(m_PrimitiveCapacity, 8, m_Allocator);

            // Reset counts
            m_PointCount = 0;
            m_VertexCount = 0;
            m_PrimitiveCount = 0;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            // Dispose validity tracking
            if (m_ValidPoints.IsCreated)
                m_ValidPoints.Dispose();
            if (m_ValidVertices.IsCreated)
                m_ValidVertices.Dispose();

            // Dispose free indices
            if (m_FreePointIndices.IsCreated)
                m_FreePointIndices.Dispose();
            if (m_FreeVertexIndices.IsCreated)
                m_FreeVertexIndices.Dispose();
            if (m_FreePrimIndices.IsCreated)
                m_FreePrimIndices.Dispose();

            // Dispose attributes
            m_PointAttributes.Dispose();
            m_VertexAttributes.Dispose();
            m_PrimitiveAttributes.Dispose();

            // Dispose relationships
            if (m_VertexToPoint.IsCreated)
                m_VertexToPoint.Dispose();

            // Dispose primitive vertices
            m_Primitives.Dispose();

            m_IsDisposed = true;
        }

        #endregion
    }
}