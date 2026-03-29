using Unity.Mathematics;

namespace SangriaMesh
{
    public struct KdElement<T> where T : unmanaged
    {
        public float3 Position;
        public T Value;
    }
}
