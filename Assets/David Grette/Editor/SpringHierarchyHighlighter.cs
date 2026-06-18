#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SpringHierarchyHighlighter
{
    const string PrefKeyEnabled = "SpringHierarchyHighlighter.Enabled";
    const string PrefKeyFillMode = "SpringHierarchyHighlighter.FillMode"; // 0 = bar, 1 = full fill

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKeyEnabled, true);
        set { EditorPrefs.SetBool(PrefKeyEnabled, value); EditorApplication.RepaintHierarchyWindow(); }
    }
    static int FillMode
    {
        get => EditorPrefs.GetInt(PrefKeyFillMode, 1); // по умолчанию — заливка
        set { EditorPrefs.SetInt(PrefKeyFillMode, value); EditorApplication.RepaintHierarchyWindow(); }
    }

    // Цвета
    static readonly Color boneColor     = new Color(1f, 0.85f, 0.20f, 0.55f);
    static readonly Color managerColor  = new Color(0.20f, 0.80f, 1f,   0.55f);
    static readonly Color colliderColor = new Color(1f, 0.40f, 0.40f, 0.55f);

    // Типовые имена компонентов
    static readonly string[] BoneHints = { "SpringBone", "VRMSpringBone", "VRM10SpringBone" };
    static readonly string[] ManagerHints = { "SpringManager", "VRMSpringBoneManager", "VRM10SpringManager" };
    static readonly string[] ColliderHints = {
        "SpringCollider","SpringSphereCollider","SpringCapsuleCollider","SpringPlaneCollider",
        "VRMSpringBoneColliderGroup","VRM10SpringBoneColliderGroup"
    };

    // Кэш
    static readonly Dictionary<int, Kind> cache = new Dictionary<int, Kind>();
    static double nextScanTime;

    enum Kind { None = 0, Bone = 1, Manager = 2, Collider = 4 }

    // Отступы, чтобы фон аккуратно ложился
    const float LEFT_MARGIN  = 2f;   // не залезать на стрелку раскрытия
    const float RIGHT_TAG_W  = 46f;  // место справа под тег
    const float BAR_W        = 6f;   // ширина узкой полоски в режиме "bar"

    static SpringHierarchyHighlighter()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        EditorApplication.hierarchyChanged += () => { cache.Clear(); EditorApplication.RepaintHierarchyWindow(); };
    }

    [MenuItem("Tools/Spring/Toggle Hierarchy Highlighting")]
    static void Toggle() => Enabled = !Enabled;

    [MenuItem("Tools/Spring/Highlight Mode/Full Fill")]
    static void SetFill() => FillMode = 1;

    [MenuItem("Tools/Spring/Highlight Mode/Narrow Bar")]
    static void SetBar() => FillMode = 0;

    static void OnHierarchyGUI(int instanceId, Rect rect)
    {
        if (!Enabled) return;

        if (EditorApplication.timeSinceStartup > nextScanTime)
        {
            cache.Clear();
            nextScanTime = EditorApplication.timeSinceStartup + 0.25;
        }

        if (!cache.TryGetValue(instanceId, out var kind))
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            kind = go ? Classify(go) : Kind.None;
            cache[instanceId] = kind;
        }
        if (kind == Kind.None) return;

        var color = ChooseColor(kind);

        // Адаптируем прозрачность под тёмную/светлую тему и выделение
        bool selected = Selection.instanceIDs.Contains(instanceId);
        float themeMul = EditorGUIUtility.isProSkin ? 1.0f : 0.85f;

        // Если объект выделен — делаем подсветку мягче, чтобы не «перекрыть» стандартный синий фон
        color.a *= selected ? 0.35f * themeMul : 0.60f * themeMul;

        if (FillMode == 1)
        {
            // Почти полная заливка
            var bg = rect;
            bg.xMin += LEFT_MARGIN;
            bg.xMax -= RIGHT_TAG_W; // оставим место под теги справа
            EditorGUI.DrawRect(bg, color);
        }
        else
        {
            // Узкая полоска слева (старое поведение)
            var bar = new Rect(rect.x, rect.y, BAR_W, rect.height);
            EditorGUI.DrawRect(bar, color);
        }

        // Тег справа
        var labelRect = new Rect(rect.xMax - RIGHT_TAG_W + 6f, rect.y, RIGHT_TAG_W - 8f, rect.height);
        var old = GUI.color;
        GUI.color = new Color(1, 1, 1, selected ? 1f : 0.9f);
        EditorGUI.LabelField(labelRect, Tag(kind), EditorStyles.miniBoldLabel);
        GUI.color = old;
    }

    static Kind Classify(GameObject go)
    {
        Kind k = Kind.None;
        var comps = go.GetComponents<Component>();
        foreach (var c in comps)
        {
            if (!c) continue;
            var n = c.GetType().Name;

            if (ContainsAny(n, BoneHints))                    k |= Kind.Bone;
            else if (ContainsAny(n, ManagerHints))            k |= Kind.Manager;
            else if (ContainsAny(n, ColliderHints) ||
                     (n.IndexOf("Spring", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                      n.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0))
                k |= Kind.Collider;
        }
        return k;
    }

    static bool ContainsAny(string typeName, string[] hints)
    {
        for (int i = 0; i < hints.Length; i++)
            if (typeName.IndexOf(hints[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    static Color ChooseColor(Kind k)
    {
        if ((k & Kind.Bone) != 0 && (k & Kind.Manager) != 0) return Blend(boneColor, managerColor);
        if ((k & Kind.Bone) != 0 && (k & Kind.Collider) != 0) return Blend(boneColor, colliderColor);
        if ((k & Kind.Manager) != 0 && (k & Kind.Collider) != 0) return Blend(managerColor, colliderColor);
        if ((k & Kind.Bone) != 0)     return boneColor;
        if ((k & Kind.Manager) != 0)  return managerColor;
        if ((k & Kind.Collider) != 0) return colliderColor;
        return Color.clear;
    }

    static Color Blend(Color a, Color b)
    {
        var alpha = Mathf.Clamp01(a.a + b.a - a.a * b.a);
        var col = a * a.a + b * b.a * (1f - a.a);
        return new Color(col.r, col.g, col.b, alpha);
    }

    static string Tag(Kind k)
    {
        bool bone = (k & Kind.Bone) != 0;
        bool mgr  = (k & Kind.Manager) != 0;
        bool col  = (k & Kind.Collider) != 0;
        if (bone && mgr && col) return "SMB";
        if (bone && mgr)        return "SM";
        if (bone && col)        return "SB";
        if (mgr  && col)        return "SC";
        if (bone)               return "SB";
        if (mgr)                return "SM";
        if (col)                return "SC";
        return "";
    }
}
#endif
