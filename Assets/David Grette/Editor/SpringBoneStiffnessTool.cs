// Assets/Editor/SpringBoneStiffnessTool.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SpringBoneStiffnessTool : EditorWindow
{
    [SerializeField] private List<Transform> roots = new List<Transform>();

    // ---- Stiffness Force (как было) ----
    [Header("Параметры (Stiffness Force)")]
    [SerializeField] private bool  enableStiffness = true;
    [SerializeField] private float startValue = 2000f;
    [SerializeField] private float step = 50f;
    [SerializeField] private float minClamp = 0f;
    [SerializeField] private bool  includeRoot = true;

    // ---- Angular Stiffness ----
    [Header("Angular Stiffness")]
    [SerializeField] private bool  enableAngularStiffness = true;
    [SerializeField] private float angularStart = 350f;
    [SerializeField] private float angularStep  = -20f;
    [SerializeField] private float angularMinClamp = 0f;

    // ---- Angle Limits X ----
    [Header("Angle Limits (X)")]
    [SerializeField] private bool  enableAngleX = true;
    [SerializeField] private bool  setXActive = true;
    [SerializeField] private float xMinStart = -10f;
    [SerializeField] private float xMinStep  = -5f;   // отрицательный — уходит вниз по цепи
    [SerializeField] private float xMaxStart = 4f;
    [SerializeField] private float xMaxStep  = 2f;

    // ---- Angle Limits Y ----
    [Header("Angle Limits (Y)")]
    [SerializeField] private bool  enableAngleY = false;
    [SerializeField] private bool  setYActive = true;
    [SerializeField] private float yMinStart = -15f;
    [SerializeField] private float yMinStep  = -5f;
    [SerializeField] private float yMaxStart = 10f;
    [SerializeField] private float yMaxStep  = 2f;

    // ---- Angle Limits Z ----
    [Header("Angle Limits (Z)")]
    [SerializeField] private bool  enableAngleZ = false;
    [SerializeField] private bool  setZActive = true;
    [SerializeField] private float zMinStart = -8f;
    [SerializeField] private float zMinStep  = -2f;
    [SerializeField] private float zMaxStart = 8f;
    [SerializeField] private float zMaxStep  = 2f;

    // ---- Обход ----
    [Header("Обход")]
    [SerializeField] private bool firstChildChainOnly = true;
    [SerializeField] private bool skipDuplicates = true;

    // ---- Collision Radius ----
    [Serializable] public enum GradientDirection { RootToLeaf, LeafToRoot }
    [Header("Collision Radius")]
    [SerializeField] private bool enableCollisionRadius = true;
    [Range(0f, 0.5f)] [SerializeField] private float radiusStart = 0.1f;
    [Range(0f, 0.5f)] [SerializeField] private float radiusStep  = 0.02f;
    [Range(0f, 0.5f)] [SerializeField] private float radiusClamp = 0f;
    [SerializeField] private GradientDirection radiusDirection = GradientDirection.RootToLeaf;

    // --- SCROLL STATE ---
    private Vector2 _scroll;

    [MenuItem("Tools/SpringBones/Apply Gradients...")]
    public static void ShowWindow()
    {
        var w = GetWindow<SpringBoneStiffnessTool>("SpringBone Gradients");
        w.minSize = new Vector2(600, 420);
        w.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("SpringBone Gradient Utility", EditorStyles.boldLabel);

        var so = new SerializedObject(this);
        so.Update();

        // ----- СКРОЛЛИРУЕМАЯ ЧАСТЬ -----
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        // список корней
        var rootsProp = so.FindProperty("roots");
        EditorGUILayout.PropertyField(rootsProp, new GUIContent("Roots (цепочки)"), true);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Добавить выделенные кости"))
            {
                foreach (var t in Selection.transforms) AddUniqueToList(rootsProp, t);
                so.ApplyModifiedProperties(); Repaint();
            }
            if (GUILayout.Button("Добавить родителей выделенных"))
            {
                foreach (var t in Selection.transforms)
                    if (t != null && t.parent != null) AddUniqueToList(rootsProp, t.parent);
                so.ApplyModifiedProperties(); Repaint();
            }
            if (GUILayout.Button("Удалить пустые ссылки"))
            {
                RemoveNullsFromList(rootsProp);
                so.ApplyModifiedProperties(); Repaint();
            }
            if (GUILayout.Button("Очистить"))
            {
                rootsProp.ClearArray();
                so.ApplyModifiedProperties(); Repaint();
            }
        }

        EditorGUILayout.Space(8);

        // Stiffness Force
        EditorGUILayout.LabelField("Stiffness Force", EditorStyles.boldLabel);
        enableStiffness = EditorGUILayout.Toggle("Enable", enableStiffness);
        using (new EditorGUI.DisabledScope(!enableStiffness))
        {
            startValue  = EditorGUILayout.FloatField("Start", startValue);
            step        = EditorGUILayout.FloatField("Step", step);
            minClamp    = EditorGUILayout.FloatField("Clamp Min", minClamp);
            includeRoot = EditorGUILayout.Toggle("Включать корень?", includeRoot);
        }

        EditorGUILayout.Space(8);

        // Angular Stiffness
        EditorGUILayout.LabelField("Angular Stiffness", EditorStyles.boldLabel);
        enableAngularStiffness = EditorGUILayout.Toggle("Enable", enableAngularStiffness);
        using (new EditorGUI.DisabledScope(!enableAngularStiffness))
        {
            angularStart    = EditorGUILayout.FloatField("Start", angularStart);
            angularStep     = EditorGUILayout.FloatField("Step", angularStep);
            angularMinClamp = EditorGUILayout.FloatField("Clamp Min", angularMinClamp);
        }

        EditorGUILayout.Space(8);

        // Angle Limits X
        EditorGUILayout.LabelField("Angle Limits (X)", EditorStyles.boldLabel);
        enableAngleX = EditorGUILayout.Toggle("Enable", enableAngleX);
        using (new EditorGUI.DisabledScope(!enableAngleX))
        {
            setXActive = EditorGUILayout.Toggle("Set Active = true", setXActive);
            EditorGUILayout.LabelField("X Min", EditorStyles.miniBoldLabel);
            xMinStart = EditorGUILayout.Slider("Start (-180..0)", xMinStart, -180f, 0f);
            xMinStep  = EditorGUILayout.Slider("Step (deg/bone)", xMinStep, -45f, 45f);
            EditorGUILayout.LabelField("X Max", EditorStyles.miniBoldLabel);
            xMaxStart = EditorGUILayout.Slider("Start (0..180)", xMaxStart, 0f, 180f);
            xMaxStep  = EditorGUILayout.Slider("Step (deg/bone)", xMaxStep, -45f, 45f);
        }

        EditorGUILayout.Space(8);

        // Angle Limits Y
        EditorGUILayout.LabelField("Angle Limits (Y)", EditorStyles.boldLabel);
        enableAngleY = EditorGUILayout.Toggle("Enable", enableAngleY);
        using (new EditorGUI.DisabledScope(!enableAngleY))
        {
            setYActive = EditorGUILayout.Toggle("Set Active = true", setYActive);
            EditorGUILayout.LabelField("Y Min", EditorStyles.miniBoldLabel);
            yMinStart = EditorGUILayout.Slider("Start (-180..0)", yMinStart, -180f, 0f);
            yMinStep  = EditorGUILayout.Slider("Step (deg/bone)", yMinStep, -45f, 45f);
            EditorGUILayout.LabelField("Y Max", EditorStyles.miniBoldLabel);
            yMaxStart = EditorGUILayout.Slider("Start (0..180)", yMaxStart, 0f, 180f);
            yMaxStep  = EditorGUILayout.Slider("Step (deg/bone)", yMaxStep, -45f, 45f);
        }

        EditorGUILayout.Space(8);

        // Angle Limits Z
        EditorGUILayout.LabelField("Angle Limits (Z)", EditorStyles.boldLabel);
        enableAngleZ = EditorGUILayout.Toggle("Enable", enableAngleZ);
        using (new EditorGUI.DisabledScope(!enableAngleZ))
        {
            setZActive = EditorGUILayout.Toggle("Set Active = true", setZActive);
            EditorGUILayout.LabelField("Z Min", EditorStyles.miniBoldLabel);
            zMinStart = EditorGUILayout.Slider("Start (-180..0)", zMinStart, -180f, 0f);
            zMinStep  = EditorGUILayout.Slider("Step (deg/bone)", zMinStep, -45f, 45f);
            EditorGUILayout.LabelField("Z Max", EditorStyles.miniBoldLabel);
            zMaxStart = EditorGUILayout.Slider("Start (0..180)", zMaxStart, 0f, 180f);
            zMaxStep  = EditorGUILayout.Slider("Step (deg/bone)", zMaxStep, -45f, 45f);
        }

        EditorGUILayout.Space(8);

        // Обход
        firstChildChainOnly = EditorGUILayout.ToggleLeft("Линейная цепочка (первый ребёнок)", firstChildChainOnly);
        skipDuplicates      = EditorGUILayout.ToggleLeft("Пропускать дубликаты между цепочками", skipDuplicates);

        EditorGUILayout.Space(10);

        // Collision Radius
        EditorGUILayout.LabelField("Collision Radius (SpringBone.radius)", EditorStyles.boldLabel);
        enableCollisionRadius = EditorGUILayout.Toggle("Enable", enableCollisionRadius);
        using (new EditorGUI.DisabledScope(!enableCollisionRadius))
        {
            radiusStart   = EditorGUILayout.Slider("Start (0..0.5 m)", radiusStart, 0f, 0.5f);
            radiusStep    = EditorGUILayout.Slider("Step (0..0.5 m)",  radiusStep,  0f, 0.5f);
            radiusClamp   = EditorGUILayout.Slider("Clamp Min",        radiusClamp, 0f, 0.5f);
            radiusDirection = (GradientDirection)EditorGUILayout.EnumPopup("Direction", radiusDirection);
        }

        EditorGUILayout.EndScrollView(); // ----- конец скролла -----

        so.ApplyModifiedProperties();

        // ----- НИЖНЯЯ ФИКСИРОВАННАЯ ПАНЕЛЬ -----
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(rootsProp.arraySize == 0))
            {
                if (GUILayout.Button("Применить", EditorStyles.toolbarButton, GUILayout.Width(120)))
                {
                    ApplyToAllRoots();
                }
            }
        }
    }

    // --- helpers for SerializedProperty list ---
    static void AddUniqueToList(SerializedProperty listProp, Transform t)
    {
        if (t == null) return;
        for (int i = 0; i < listProp.arraySize; i++)
        {
            var elem = listProp.GetArrayElementAtIndex(i);
            if (elem.objectReferenceValue == t) return;
        }
        int idx = listProp.arraySize;
        listProp.InsertArrayElementAtIndex(idx);
        listProp.GetArrayElementAtIndex(idx).objectReferenceValue = t;
    }

    static void RemoveNullsFromList(SerializedProperty listProp)
    {
        for (int i = listProp.arraySize - 1; i >= 0; i--)
        {
            var e = listProp.GetArrayElementAtIndex(i);
            if (e.objectReferenceValue == null) listProp.DeleteArrayElementAtIndex(i);
        }
    }

    // --- main apply ---
    void ApplyToAllRoots()
    {
        var validRoots = roots?.Where(r => r != null).Distinct().ToList() ?? new List<Transform>();
        if (validRoots.Count == 0) { Debug.LogWarning("Список Roots пуст."); return; }

        var visited = new HashSet<Transform>();
        var allSpringComponents = new HashSet<Component>();
        foreach (var root in validRoots)
        {
            foreach (var t in EnumerateChain(root))
            {
                if (skipDuplicates && !visited.Add(t)) continue;
                foreach (var c in GetSpringBoneComponents(t)) allSpringComponents.Add(c);
            }
        }
        if (allSpringComponents.Count == 0)
        {
            Debug.LogWarning("Не найдено ни одного компонента типа SpringBone");
            return;
        }

        Undo.RecordObjects(allSpringComponents.ToArray(), "Apply SpringBone Gradients");

        int totalNodes = 0;
        int totalStiffnessChanged = 0;
        int totalRadiusChanged = 0;
        int totalAngularChanged = 0;
        int totalAnglesXChanged = 0;
        int totalAnglesYChanged = 0;
        int totalAnglesZChanged = 0;

        foreach (var root in validRoots)
        {
            var chain = EnumerateChain(root).ToList();
            if (chain.Count == 0) continue;
            totalNodes += chain.Count;

            // Stiffness Force
            if (enableStiffness)
            {
                int depth = 0;
                foreach (var t in chain)
                {
                    float value = Mathf.Max(minClamp, startValue - step * depth);
                    foreach (var comp in GetSpringBoneComponents(t))
                    {
                        if (SetFloatOnProp(comp, new[] { "stiffnessForce", "StiffnessForce", "m_stiffnessForce", "stiffness" }, value))
                        { EditorUtility.SetDirty(comp); totalStiffnessChanged++; }
                    }
                    depth++;
                }
            }

            // Angular Stiffness
            if (enableAngularStiffness)
            {
                int depth = 0;
                foreach (var t in chain)
                {
                    float value = Mathf.Max(angularMinClamp, angularStart + angularStep * depth);
                    foreach (var comp in GetSpringBoneComponents(t))
                    {
                        if (SetFloatOnProp(comp, new[] { "angularStiffness", "m_angularStiffness", "AngularStiffness" }, value))
                        { EditorUtility.SetDirty(comp); totalAngularChanged++; }
                    }
                    depth++;
                }
            }

            // Angle Limits X
            if (enableAngleX)
            {
                int depth = 0;
                foreach (var t in chain)
                {
                    float minVal = Mathf.Clamp(xMinStart + xMinStep * depth, -180f, 0f);
                    float maxVal = Mathf.Clamp(xMaxStart + xMaxStep * depth, 0f, 180f);
                    foreach (var comp in GetSpringBoneComponents(t))
                    {
                        if (SetAngleLimits(comp, "xAngleLimits", setXActive, minVal, maxVal))
                        { EditorUtility.SetDirty(comp); totalAnglesXChanged++; }
                    }
                    depth++;
                }
            }

            // Angle Limits Y
            if (enableAngleY)
            {
                int depth = 0;
                foreach (var t in chain)
                {
                    float minVal = Mathf.Clamp(yMinStart + yMinStep * depth, -180f, 0f);
                    float maxVal = Mathf.Clamp(yMaxStart + yMaxStep * depth, 0f, 180f);
                    foreach (var comp in GetSpringBoneComponents(t))
                    {
                        if (SetAngleLimits(comp, "yAngleLimits", setYActive, minVal, maxVal))
                        { EditorUtility.SetDirty(comp); totalAnglesYChanged++; }
                    }
                    depth++;
                }
            }

            // Angle Limits Z
            if (enableAngleZ)
            {
                int depth = 0;
                foreach (var t in chain)
                {
                    float minVal = Mathf.Clamp(zMinStart + zMinStep * depth, -180f, 0f);
                    float maxVal = Mathf.Clamp(zMaxStart + zMaxStep * depth, 0f, 180f);
                    foreach (var comp in GetSpringBoneComponents(t))
                    {
                        if (SetAngleLimits(comp, "zAngleLimits", setZActive, minVal, maxVal))
                        { EditorUtility.SetDirty(comp); totalAnglesZChanged++; }
                    }
                    depth++;
                }
            }

            // Collision Radius
            if (enableCollisionRadius)
            {
                var iter = radiusDirection == GradientDirection.RootToLeaf ? chain : Enumerable.Reverse(chain);
                int rDepth = 0;
                foreach (var t in iter)
                {
                    float r = Mathf.Clamp(radiusStart - radiusStep * rDepth, radiusClamp, 0.5f);
                    foreach (var comp in GetSpringBoneComponents(t))
                    {
                        if (SetFloatOnProp(comp, new[] { "radius", "m_radius", "m_Radius", "Radius" }, r))
                        { EditorUtility.SetDirty(comp); totalRadiusChanged++; }
                    }
                    rDepth++;
                }
            }
        }

        Debug.Log($"SpringBone: узлов {totalNodes}, stiffness изменено {totalStiffnessChanged}, angular stiffness изменено {totalAngularChanged}, X-лимитов изменено {totalAnglesXChanged}, Y-лимитов изменено {totalAnglesYChanged}, Z-лимитов изменено {totalAnglesZChanged}, collision radius изменено {totalRadiusChanged}.");
    }

    IEnumerable<Transform> EnumerateChain(Transform start)
    {
        if (firstChildChainOnly)
        {
            var cur = includeRoot ? start : (start.childCount > 0 ? start.GetChild(0) : null);
            while (cur != null)
            {
                yield return cur;
                cur = cur.childCount > 0 ? cur.GetChild(0) : null;
            }
        }
        else
        {
            foreach (var t in EnumerateDepthFirst(start, includeRoot))
                yield return t;
        }
    }

    static IEnumerable<Transform> EnumerateDepthFirst(Transform start, bool includeStart)
    {
        if (includeStart) yield return start;
        foreach (Transform child in start)
            foreach (var t in EnumerateDepthFirst(child, true))
                yield return t;
    }

    static IEnumerable<Component> GetSpringBoneComponents(Transform t)
    {
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            var n = c.GetType().Name;
            if (n == "SpringBone" || n.EndsWith("SpringBone"))
                yield return c;
        }
    }

    static bool SetFloatOnProp(Component comp, string[] candidateNames, float value)
    {
        var so = new SerializedObject(comp); so.Update();
        SerializedProperty prop = null;
        foreach (var n in candidateNames)
        {
            prop = so.FindProperty(n);
            if (prop != null && prop.propertyType == SerializedPropertyType.Float) break;
            prop = null;
        }
        if (prop == null) return false;
        prop.floatValue = value;
        so.ApplyModifiedProperties();
        return true;
    }

    static bool SetAngleLimits(Component comp, string anglePropName, bool setActive, float minVal, float maxVal)
    {
        var so = new SerializedObject(comp); so.Update();
        var ax = so.FindProperty(anglePropName);
        if (ax == null || ax.propertyType != SerializedPropertyType.Generic)
        {
            so.Dispose();
            return false;
        }
        var activeProp = ax.FindPropertyRelative("active");
        var minProp    = ax.FindPropertyRelative("min");
        var maxProp    = ax.FindPropertyRelative("max");
        if (activeProp != null && setActive) activeProp.boolValue = true;
        if (minProp != null) minProp.floatValue = Mathf.Clamp(minVal, -180f, 0f);
        if (maxProp != null) maxProp.floatValue = Mathf.Clamp(maxVal, 0f, 180f);
        so.ApplyModifiedProperties();
        return true;
    }
}
