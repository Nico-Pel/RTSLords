using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(MapMaskGenerator))]
public class MapMaskGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Map Tools", EditorStyles.boldLabel);

        MapMaskGenerator generator = (MapMaskGenerator)target;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Auto-Setup"))
            {
                RunEditorAction(generator, () =>
                {
                    generator.ConfigureDefaultsFromProjectAssets();
                    generator.AssignSceneTeams(FindObjectsOfType<TeamManager>(true));
                });
            }

            if (GUILayout.Button("Clear"))
            {
                RunEditorAction(generator, generator.ClearGeneratedMap);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate"))
            {
                RunEditorAction(generator, generator.Generate);
            }

            if (GUILayout.Button("Regenerate"))
            {
                RunEditorAction(generator, () =>
                {
                    generator.ClearGeneratedMap();
                    generator.Generate();
                });
            }
        }
    }

    private void RunEditorAction(MapMaskGenerator generator, System.Action action)
    {
        if (generator == null || action == null)
        {
            return;
        }

        GameObject[] rootGameObjects = generator.gameObject.scene.GetRootGameObjects();
        for (int i = 0; i < rootGameObjects.Length; i++)
        {
            if (rootGameObjects[i] != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(rootGameObjects[i], "Map Generator Action");
            }
        }

        action.Invoke();
        EditorUtility.SetDirty(generator);
        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }
}
