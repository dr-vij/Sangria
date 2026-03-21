// Core: Metadata entry describing one packed compiled attribute stream.
namespace SangriaMesh
{
    public struct CompiledAttributeDescriptor
    {
        public int AttributeId;
        public int TypeHash;
        public int Stride;
        public int Count;
        public int OffsetBytes;
    }
}
