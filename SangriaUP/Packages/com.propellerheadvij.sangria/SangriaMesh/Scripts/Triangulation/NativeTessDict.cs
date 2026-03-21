using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh.NativeTess
{
    internal struct DictNode
    {
        public int key;
        public int prev;
        public int next;
    }

    internal struct TessDict : IDisposable
    {
        public UnsafeList<DictNode> nodes;
        private UnsafeList<int> m_Free;
        private FreeList m_FreeList;
        private int m_Head;

        public const int Null = -1;

        public static TessDict Create(int capacity, Allocator allocator)
        {
            var dict = new TessDict
            {
                nodes = new UnsafeList<DictNode>(capacity, allocator),
                m_Free = new UnsafeList<int>(capacity, allocator),
                m_FreeList = new FreeList(0),
            };
            dict.Init();
            return dict;
        }

        private void Init()
        {
            m_Head = AllocNode();
            nodes.ElementAt(m_Head).key = Null;
            nodes.ElementAt(m_Head).prev = m_Head;
            nodes.ElementAt(m_Head).next = m_Head;
        }

        public void Dispose()
        {
            if (nodes.IsCreated) nodes.Dispose();
            if (m_Free.IsCreated) m_Free.Dispose();
        }

        public void Reset()
        {
            nodes.Clear();
            m_Free.Clear();
            m_FreeList = new FreeList(0);
            Init();
        }

        public bool IsEmpty => nodes[m_Head].next == m_Head;

        public int Head => m_Head;

        public int Min() => nodes[m_Head].next;

        public int NodeKey(int node) => nodes[node].key;

        public int NodePrev(int node) => nodes[node].prev;

        public int NodeNext(int node) => nodes[node].next;

        private int AllocNode()
        {
            int idx;
            if (m_FreeList.HasFree)
            {
                idx = m_FreeList.Pop(ref m_Free);
            }
            else
            {
                idx = nodes.Length;
                nodes.Add(default);
                m_Free.Add(0);
            }
            return idx;
        }

        private void FreeNode(int idx)
        {
            m_FreeList.Push(ref m_Free, idx);
        }

        public int Insert(int key, NativeTessState state)
        {
            return InsertBefore(m_Head, key, state);
        }

        public int InsertBefore(int node, int key, NativeTessState state)
        {
            do
            {
                node = nodes[node].prev;
            } while (nodes[node].key != Null && !state.EdgeLeq(nodes[node].key, key));

            int newNode = AllocNode();
            nodes.ElementAt(newNode).key = key;

            int nodeNext = nodes[node].next;
            nodes.ElementAt(newNode).next = nodeNext;
            nodes.ElementAt(nodeNext).prev = newNode;
            nodes.ElementAt(newNode).prev = node;
            nodes.ElementAt(node).next = newNode;

            return newNode;
        }

        public int Find(int key, NativeTessState state)
        {
            int node = m_Head;
            do
            {
                node = nodes[node].next;
            } while (nodes[node].key != Null && !state.EdgeLeq(key, nodes[node].key));
            return node;
        }

        public void Remove(int node)
        {
            int nextN = nodes[node].next;
            int prevN = nodes[node].prev;
            nodes.ElementAt(nextN).prev = prevN;
            nodes.ElementAt(prevN).next = nextN;
            FreeNode(node);
        }
    }
}
