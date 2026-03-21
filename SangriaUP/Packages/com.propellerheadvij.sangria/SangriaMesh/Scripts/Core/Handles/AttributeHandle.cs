// Core: Typed handle that caches attribute id, column index, and type hash for fast attribute access.
namespace SangriaMesh
{
    public readonly struct AttributeHandle<T> where T : unmanaged
    {
        internal readonly int ColumnIndex;
        internal readonly int TypeHash;
        public readonly int AttributeId;

        internal AttributeHandle(int attributeId, int columnIndex, int typeHash)
        {
            AttributeId = attributeId;
            ColumnIndex = columnIndex;
            TypeHash = typeHash;
        }

        public bool IsValid => ColumnIndex >= 0;
    }
}
