using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Sangria.SceneViewEditor
{
    [InitializeOnLoad]
    public static class EditorMeshIntersections
    {
        private static MethodInfo IntersectMeshMethod;

        static EditorMeshIntersections()
        {
            var typeHandleUtility = typeof(Editor).Assembly.GetTypes().First(t => t.Name == "HandleUtility");
            IntersectMeshMethod = typeHandleUtility.GetMethod("IntersectRayMesh", BindingFlags.Static | BindingFlags.NonPublic);
        }

        public static bool RaycastMeshFilter(Ray ray, MeshFilter meshFilter, out RaycastHit hit)
        {
            var trans = meshFilter.transform;
            var newRayOrigin = trans.InverseTransformPoint(ray.origin);
            var newRayDirection = trans.InverseTransformDirection(ray.direction);
            var newRay = new Ray(newRayOrigin, newRayDirection);
            var matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            var result = RaycastMesh(newRay, meshFilter.sharedMesh, matrix, out hit);
            hit.point = trans.TransformPoint(hit.point);
            return result;
        }

        public static bool RaycastMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
        {
            var parameters = new object[] { ray, mesh, matrix, null };
            var result = (bool)IntersectMeshMethod.Invoke(null, parameters);
            hit = (RaycastHit)parameters[3];
            return result;
        }

        public static GameObject GetObjectUnder2dPosition(Vector2 position, bool selectPrefabRoot = false) => HandleUtility.PickGameObject(position, selectPrefabRoot);
    }
}