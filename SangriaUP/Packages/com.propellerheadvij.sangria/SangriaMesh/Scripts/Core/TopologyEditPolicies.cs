// Core: Policies that define point/vertex deletion behavior during topology edits.
namespace SangriaMesh
{
    public enum VertexDeletePolicy : byte
    {
        RemoveFromIncidentPrimitives = 0,
        DeleteIncidentPrimitives = 1,
        FailIfIncidentPrimitivesExist = 2
    }

    public enum PointDeletePolicy : byte
    {
        DeleteIncidentVertices = 0,
        FailIfIncidentVerticesExist = 1
    }
}
