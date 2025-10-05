using Unity.Collections;

namespace PropellerheadMesh
{
    public struct ActivePageEnumerator
    {
        private NativeList<PageInfo> m_ActivePages;
        private int m_CurrentIndex;

        public ActivePageEnumerator(NativeList<PageInfo> activePages)
        {
            m_ActivePages = activePages;
            m_CurrentIndex = -1;
        }

        public bool MoveNext()
        {
            m_CurrentIndex++;
            return m_CurrentIndex < m_ActivePages.Length;
        }

        public PageInfo CurrentPageInfo => m_ActivePages[m_CurrentIndex];
        
        public int CurrentIndex => m_CurrentIndex;

        public ActivePageEnumerator GetEnumerator() => this;
    }
}