// Core: Result codes returned by core API operations.
namespace SangriaMesh
{
    public enum CoreResult : byte
    {
        Success = 0,
        AlreadyExists = 1,
        NotFound = 2,
        TypeMismatch = 3,
        IndexOutOfRange = 4,
        InvalidHandle = 5,
        InvalidOperation = 6
    }
}
