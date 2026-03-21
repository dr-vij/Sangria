// Core: Metadata entry describing one packed compiled resource blob.
namespace SangriaMesh
{
    public struct CompiledResourceDescriptor
    {
        public int ResourceId;
        public int TypeHash;
        public int SizeBytes;
        public int OffsetBytes;
    }
}
