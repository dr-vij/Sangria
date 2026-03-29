using UnityEditor;
using UnityEngine;

namespace SangriaMesh
{
    [CustomEditor(typeof(BvhGizmoVisualizer))]
    public sealed class BvhGizmoVisualizerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild BVH"))
            {
                var visualizer = (BvhGizmoVisualizer)target;
                visualizer.Rebuild();
                EditorUtility.SetDirty(visualizer);
                SceneView.RepaintAll();
            }
        }
    }
}
