//This mocks are used to test the source generator
//They are needed just to make the example compilable without referencing real Unity assemblies
namespace UnityEngine
{
    public class Shader
    {
        public static int PropertyToID(string name) => default;
    }

    public static class Resources
    {
        public static T Load<T>(string path) => default!;
    }

    public class Material
    {
        public Material(Shader shader)
        {
        }
    }

    public class LayerMask
    {
        public static int NameToLayer(string name) => 0;

        public static int GetMask(string name) => 0;
    }
}