namespace SangriaMesh
{
    public struct BvhNode
    {
        public BvhAabb Bounds;
        public int Left;
        public int Right;
        public int FirstElement;
        public int ElementCount;
        public byte IsLeaf;

        public bool Leaf => IsLeaf != 0;
    }
}
