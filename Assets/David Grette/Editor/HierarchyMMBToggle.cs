#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HierarchyMMBToggle
{
    const string MenuPath = "Tools/Hierarchy MMB Toggle (Enable)";
    static bool enabled;

    static HierarchyMMBToggle()
    {
        enabled = SessionState.GetBool("HierarchyMMBToggle_enabled", true);
        Menu.SetChecked(MenuPath, enabled);
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
    }

    [MenuItem(MenuPath)]
    public static void TogglePlugin()
    {
        enabled = !enabled;
        SessionState.SetBool("HierarchyMMBToggle_enabled", enabled);
        Menu.SetChecked(MenuPath, enabled);
        EditorApplication.RepaintHierarchyWindow();
    }

    static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
    {
        if (!enabled) return;

        Event e = Event.current;
        if (e == null) return;
        if (e.type == EventType.MouseDown && e.button == 2 && selectionRect.Contains(e.mousePosition))
        {
            var obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
            if (obj != null)
            {
                ToggleActive(obj, e);
                e.Use();
            }
        }
    }

    static void ToggleActive(GameObject go, Event e)
    {
        bool withChildren = e.control || e.command;

        if (withChildren)
        {
            var transforms = go.GetComponentsInChildren<Transform>(true);
            bool newState = !go.activeSelf;

            foreach (var t in transforms)
            {
                var childGO = t.gameObject;
                Undo.RecordObject(childGO, "Toggle Active (Hierarchy MMB)");
                childGO.SetActive(newState);
                MarkDirty(childGO);
            }
        }
        else
        {
            Undo.RecordObject(go, "Toggle Active (Hierarchy MMB)");
            go.SetActive(!go.activeSelf);
            MarkDirty(go);
        }
    }

    static void MarkDirty(GameObject go)
    {
        if (go.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(go.scene);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go);
    }
}
#endif
