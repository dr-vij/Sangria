using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SangriaMesh.NativeTess
{
    internal struct PriorityHeapInternal : IDisposable
    {
        private UnsafeList<int> m_Nodes;
        private UnsafeList<int> m_HandleKeys;
        private UnsafeList<int> m_HandleNodes;
        private int m_Size;
        private int m_Max;
        private int m_FreeList;
        private bool m_Initialized;

        public bool Empty => m_Size == 0;

        public static PriorityHeapInternal Create(int initialSize, Allocator allocator)
        {
            var heap = new PriorityHeapInternal
            {
                m_Nodes = new UnsafeList<int>(initialSize + 2, allocator),
                m_HandleKeys = new UnsafeList<int>(initialSize + 2, allocator),
                m_HandleNodes = new UnsafeList<int>(initialSize + 2, allocator),
                m_Size = 0,
                m_Max = initialSize,
                m_FreeList = 0,
                m_Initialized = false,
            };

            heap.m_Nodes.Resize(initialSize + 2);
            heap.m_HandleKeys.Resize(initialSize + 2);
            heap.m_HandleNodes.Resize(initialSize + 2);
            for (int i = 0; i < heap.m_Nodes.Length; i++)
            {
                heap.m_Nodes[i] = 0;
                heap.m_HandleKeys[i] = -1;
                heap.m_HandleNodes[i] = 0;
            }
            heap.m_Nodes[1] = 1;

            return heap;
        }

        public void Dispose()
        {
            if (m_Nodes.IsCreated) m_Nodes.Dispose();
            if (m_HandleKeys.IsCreated) m_HandleKeys.Dispose();
            if (m_HandleNodes.IsCreated) m_HandleNodes.Dispose();
        }

        private void FloatDown(int curr, ref UnsafeList<SweepVertex> verts)
        {
            int hCurr = m_Nodes[curr];
            while (true)
            {
                int child = curr << 1;
                if (child < m_Size &&
                    VertLeq(m_HandleKeys[m_Nodes[child + 1]], m_HandleKeys[m_Nodes[child]], ref verts))
                {
                    ++child;
                }

                int hChild = m_Nodes[child];
                if (child > m_Size || VertLeq(m_HandleKeys[hCurr], m_HandleKeys[hChild], ref verts))
                {
                    m_Nodes[curr] = hCurr;
                    m_HandleNodes[hCurr] = curr;
                    break;
                }

                m_Nodes[curr] = hChild;
                m_HandleNodes[hChild] = curr;
                curr = child;
            }
        }

        private void FloatUp(int curr, ref UnsafeList<SweepVertex> verts)
        {
            int hCurr = m_Nodes[curr];
            while (true)
            {
                int parent = curr >> 1;
                int hParent = m_Nodes[parent];
                if (parent == 0 || VertLeq(m_HandleKeys[hParent], m_HandleKeys[hCurr], ref verts))
                {
                    m_Nodes[curr] = hCurr;
                    m_HandleNodes[hCurr] = curr;
                    break;
                }
                m_Nodes[curr] = hParent;
                m_HandleNodes[hParent] = curr;
                curr = parent;
            }
        }

        public void Init(ref UnsafeList<SweepVertex> verts)
        {
            for (int i = m_Size; i >= 1; --i)
                FloatDown(i, ref verts);
            m_Initialized = true;
        }

        public PQHandle Insert(int vertexIndex, ref UnsafeList<SweepVertex> verts)
        {
            int curr = ++m_Size;
            if ((curr * 2) > m_Max)
            {
                m_Max <<= 1;
                m_Nodes.Resize(m_Max + 2);
                m_HandleKeys.Resize(m_Max + 2);
                m_HandleNodes.Resize(m_Max + 2);
            }

            int free;
            if (m_FreeList == 0)
            {
                free = curr;
            }
            else
            {
                free = m_FreeList;
                m_FreeList = m_HandleNodes[free];
            }

            m_Nodes[curr] = free;
            m_HandleKeys[free] = vertexIndex;
            m_HandleNodes[free] = curr;

            if (m_Initialized)
                FloatUp(curr, ref verts);

            return new PQHandle { handle = free };
        }

        public int ExtractMin(ref UnsafeList<SweepVertex> verts)
        {
            int hMin = m_Nodes[1];
            int min = m_HandleKeys[hMin];

            if (m_Size > 0)
            {
                m_Nodes[1] = m_Nodes[m_Size];
                m_HandleNodes[m_Nodes[1]] = 1;

                m_HandleKeys[hMin] = -1;
                m_HandleNodes[hMin] = m_FreeList;
                m_FreeList = hMin;

                if (--m_Size > 0)
                    FloatDown(1, ref verts);
            }
            return min;
        }

        public int Minimum()
        {
            return m_HandleKeys[m_Nodes[1]];
        }

        public void Remove(PQHandle handle, ref UnsafeList<SweepVertex> verts)
        {
            int hCurr = handle.handle;
            int curr = m_HandleNodes[hCurr];
            m_Nodes[curr] = m_Nodes[m_Size];
            m_HandleNodes[m_Nodes[curr]] = curr;

            if (curr <= --m_Size)
            {
                if (curr <= 1 || VertLeq(m_HandleKeys[m_Nodes[curr >> 1]], m_HandleKeys[m_Nodes[curr]], ref verts))
                    FloatDown(curr, ref verts);
                else
                    FloatUp(curr, ref verts);
            }

            m_HandleKeys[hCurr] = -1;
            m_HandleNodes[hCurr] = m_FreeList;
            m_FreeList = hCurr;
        }

        private static bool VertLeq(int lhs, int rhs, ref UnsafeList<SweepVertex> verts)
        {
            ref var a = ref verts.ElementAt(lhs);
            ref var b = ref verts.ElementAt(rhs);
            return Geom.VertLeq(a, b);
        }
    }

    internal struct TessPQ : IDisposable
    {
        private PriorityHeapInternal m_Heap;
        private UnsafeList<int> m_Keys;
        private UnsafeList<int> m_Order;
        private int m_Size;
        private int m_Max;
        private bool m_Initialized;

        public bool Empty => m_Size == 0 && m_Heap.Empty;

        public static TessPQ Create(int initialSize, Allocator allocator)
        {
            return new TessPQ
            {
                m_Heap = PriorityHeapInternal.Create(initialSize, allocator),
                m_Keys = new UnsafeList<int>(initialSize + 1, allocator),
                m_Order = default,
                m_Size = 0,
                m_Max = initialSize,
                m_Initialized = false,
            };
        }

        public void Dispose()
        {
            m_Heap.Dispose();
            if (m_Keys.IsCreated) m_Keys.Dispose();
            if (m_Order.IsCreated) m_Order.Dispose();
        }

        public void Init(ref UnsafeList<SweepVertex> verts)
        {
            if (m_Order.IsCreated) m_Order.Dispose();
            m_Order = new UnsafeList<int>(m_Size + 1, Allocator.Persistent);
            m_Order.Resize(m_Size + 1);

            int p = 0;
            int r = m_Size - 1;
            for (int piv = 0, i = p; i <= r; ++piv, ++i)
                m_Order[i] = piv;

            if (m_Size > 0)
                SortRange(p, r, ref verts);

            m_Max = m_Size;
            m_Initialized = true;
            m_Heap.Init(ref verts);
        }

        private void SortRange(int p, int r, ref UnsafeList<SweepVertex> verts)
        {
            uint seed = 2016473283;
            var stack = new UnsafeList<int2>(32, Allocator.Temp);
            stack.Add(new int2(p, r));

            while (stack.Length > 0)
            {
                var top = stack[stack.Length - 1];
                stack.Length--;
                p = top.x;
                r = top.y;

                while (r > p + 10)
                {
                    seed = seed * 1539415821 + 1;
                    int i = p + (int)(seed % (uint)(r - p + 1));
                    int piv = m_Order[i];
                    m_Order[i] = m_Order[p];
                    m_Order[p] = piv;
                    i = p - 1;
                    int j = r + 1;
                    do
                    {
                        do { ++i; } while (!VertLeq(m_Keys[m_Order[i]], m_Keys[piv], ref verts));
                        do { --j; } while (!VertLeq(m_Keys[piv], m_Keys[m_Order[j]], ref verts));
                        int tmp = m_Order[i]; m_Order[i] = m_Order[j]; m_Order[j] = tmp;
                    } while (i < j);
                    { int tmp = m_Order[i]; m_Order[i] = m_Order[j]; m_Order[j] = tmp; }
                    if (i - p < r - j)
                    {
                        stack.Add(new int2(j + 1, r));
                        r = i - 1;
                    }
                    else
                    {
                        stack.Add(new int2(p, i - 1));
                        p = j + 1;
                    }
                }
                for (int i = p + 1; i <= r; ++i)
                {
                    int piv = m_Order[i];
                    int j;
                    for (j = i; j > p && !VertLeq(m_Keys[piv], m_Keys[m_Order[j - 1]], ref verts); --j)
                        m_Order[j] = m_Order[j - 1];
                    m_Order[j] = piv;
                }
            }
            stack.Dispose();
        }

        public PQHandle Insert(int vertexIndex, ref UnsafeList<SweepVertex> verts)
        {
            if (m_Initialized)
                return m_Heap.Insert(vertexIndex, ref verts);

            int curr = m_Size;
            if (++m_Size >= m_Max)
            {
                m_Max <<= 1;
                m_Keys.Resize(m_Max + 1);
            }
            else if (m_Keys.Length <= curr)
            {
                m_Keys.Resize(curr + 1);
            }

            m_Keys[curr] = vertexIndex;
            return new PQHandle { handle = -(curr + 1) };
        }

        public int ExtractMin(ref UnsafeList<SweepVertex> verts)
        {
            if (m_Size == 0)
                return m_Heap.ExtractMin(ref verts);

            int sortMin = m_Keys[m_Order[m_Size - 1]];
            if (!m_Heap.Empty)
            {
                int heapMin = m_Heap.Minimum();
                if (VertLeq(heapMin, sortMin, ref verts))
                    return m_Heap.ExtractMin(ref verts);
            }
            do { --m_Size; } while (m_Size > 0 && m_Keys[m_Order[m_Size - 1]] == -1);
            return sortMin;
        }

        public int Minimum(ref UnsafeList<SweepVertex> verts)
        {
            if (m_Size == 0)
                return m_Heap.Minimum();

            int sortMin = m_Keys[m_Order[m_Size - 1]];
            if (!m_Heap.Empty)
            {
                int heapMin = m_Heap.Minimum();
                if (VertLeq(heapMin, sortMin, ref verts))
                    return heapMin;
            }
            return sortMin;
        }

        public bool IsEmpty(ref UnsafeList<SweepVertex> verts)
        {
            if (m_Size > 0) return false;
            return m_Heap.Empty;
        }

        public void Remove(PQHandle handle, ref UnsafeList<SweepVertex> verts)
        {
            int curr = handle.handle;
            if (curr >= 0)
            {
                m_Heap.Remove(handle, ref verts);
                return;
            }
            curr = -(curr + 1);

            m_Keys[curr] = -1;
            while (m_Size > 0 && m_Keys[m_Order[m_Size - 1]] == -1)
                --m_Size;
        }

        private static bool VertLeq(int lhs, int rhs, ref UnsafeList<SweepVertex> verts)
        {
            return Geom.VertLeq(verts[lhs], verts[rhs]);
        }
    }
}
