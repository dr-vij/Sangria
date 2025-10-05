#if UNITY_EDITOR
using UnityEditor;

namespace UTools.Editor
{
    /// <summary>
    /// Editor placeholder for UTools package.
    /// Add your editor utilities and menu items here.
    /// </summary>
    public static class EditorPlaceholder
    {
        [MenuItem("UTools/Hello From Editor Placeholder")]
        private static void Hello()
        {
            UnityEngine.Debug.Log("UTools Editor Placeholder");
        }
    }
}
#endif
