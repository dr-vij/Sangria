namespace SangriaMesh
{
    public struct BvhElement<T> where T : unmanaged
    {
        public BvhAabb Bounds;
        public T Value;
    }
}
