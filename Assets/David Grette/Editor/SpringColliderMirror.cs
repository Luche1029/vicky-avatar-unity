#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;

public class SpringColliderMirror : EditorWindow
{
    // ---- UI enums ----
    public enum SpaceMode { WorldOrigin, ReferenceTransform }               // Откуда считать систему координат
    public enum MirrorAxis { X, Y, Z }                                      // Нормаль плоскости зеркала
    public enum LocalAxis { PlusX, MinusX, PlusY, MinusY, PlusZ, MinusZ }   // Какой локальный вектор считать "взглядом"
    
    // ---- UI state ----
    [SerializeField] Transform source;        // Что зеркалим (левый коллайдер/пустышка)
    [SerializeField] Transform target;        // Куда применяем (правый коллайдер/пустышка)
    [SerializeField] SpaceMode spaceMode = SpaceMode.ReferenceTransform;
    [SerializeField] Transform reference;     // Часто это корень персонажа или общий родитель L/R кости
    [SerializeField] MirrorAxis mirrorAxis = MirrorAxis.X;

    [Header("Ориентация после зеркала")]
    [SerializeField] LocalAxis lookAxis = LocalAxis.PlusZ;   // какую локальную ось считать "вперёд"
    [SerializeField] LocalAxis upAxis = LocalAxis.PlusY;     // какую локальную ось считать "вверх"
    [SerializeField] bool preserveRoll = false;              // пытаться сохранить кручение вокруг look

    [Header("Применять")]
    [SerializeField] bool applyPosition = true;
    [SerializeField] bool applyRotation = true;

    // предпросмотр
    Vector3 previewPos;
    Quaternion previewRot;
    bool hasPreview;

    [MenuItem("Tools/Spring Colliders/Mirror Tool")]
    public static void Open()
    {
        var win = GetWindow<SpringColliderMirror>("Spring Collider Mirror");
        win.minSize = new Vector2(360, 300);
        win.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Источники", EditorStyles.boldLabel);
        source = (Transform)EditorGUILayout.ObjectField("Source (левый)", source, typeof(Transform), true);
        target = (Transform)EditorGUILayout.ObjectField("Target (правый)", target, typeof(Transform), true);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Система координат", EditorStyles.boldLabel);
        spaceMode = (SpaceMode)EditorGUILayout.EnumPopup("Режим", spaceMode);
        using (new EditorGUI.DisabledScope(spaceMode == SpaceMode.WorldOrigin))
        {
            reference = (Transform)EditorGUILayout.ObjectField("Reference", reference, typeof(Transform), true);
        }
        mirrorAxis = (MirrorAxis)EditorGUILayout.EnumPopup("Зеркальная плоскость ⟂", mirrorAxis);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Ориентация", EditorStyles.boldLabel);
        lookAxis = (LocalAxis)EditorGUILayout.EnumPopup("Локальный 'вперёд'", lookAxis);
        upAxis   = (LocalAxis)EditorGUILayout.EnumPopup("Локальный 'вверх'", upAxis);
        preserveRoll = EditorGUILayout.Toggle(new GUIContent("Сохранить roll", "Ставит roll близко к исходному (не всегда возможно идеально)"), preserveRoll);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Что применять", EditorStyles.boldLabel);
        applyPosition = EditorGUILayout.Toggle("Позиция", applyPosition);
        applyRotation = EditorGUILayout.Toggle("Ротация", applyRotation);

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(source == null || target == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Предпросмотр"))
            {
                ComputePreview();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Применить"))
            {
                ApplyNow();
                hasPreview = false;
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Сброс предпросмотра"))
            {
                hasPreview = false;
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(8);
        if (source != null && target != null && hasPreview)
        {
            EditorGUILayout.HelpBox("Предпросмотр рассчитан. В сцене рисуются призмы-позиции и оси.", MessageType.Info);
            DrawPreviewInfo(previewPos, previewRot);
        }

        // Подсказка по выбору reference
        if (reference == null && spaceMode == SpaceMode.ReferenceTransform)
        {
            EditorGUILayout.HelpBox("Укажи Reference (общий корень персонажа). Плоскость зеркала пройдёт через него.", MessageType.None);
        }
    }

    void OnFocus()  { SceneView.duringSceneGui -= OnSceneGUI; SceneView.duringSceneGui += OnSceneGUI; }
    void OnDestroy(){ SceneView.duringSceneGui -= OnSceneGUI; }

    void OnSceneGUI(SceneView view)
    {
        if (!hasPreview || target == null) return;

        // Рисуем превью-позицию/ориентацию
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = Color.cyan;
        Handles.SphereHandleCap(0, previewPos, Quaternion.identity, HandleUtility.GetHandleSize(previewPos)*0.05f, EventType.Repaint);

        // оси
        DrawAxes(previewPos, previewRot, HandleUtility.GetHandleSize(previewPos)*0.25f, 2f);
    }

    // ---------- core ----------
    void ComputePreview()
    {
        if (source == null || target == null) return;

        // Вычислим систему координат (origin & basis)
        Vector3 origin;
        Quaternion basis;
        GetSpace(out origin, out basis); // basis: world->mirrorSpace

        // 1) Берём локальные векторы look/up источника → в мир → в пространство зеркала
        Vector3 srcLook_local = AxisToLocalVec(lookAxis);
        Vector3 srcUp_local   = AxisToLocalVec(upAxis);

        Vector3 srcLook_world = source.TransformDirection(srcLook_local);
        Vector3 srcUp_world   = source.TransformDirection(srcUp_local);

        Vector3 srcPos_world  = source.position;

        Matrix4x4 toSpace = Matrix4x4.TRS(origin, basis, Vector3.one).inverse; // world -> space
        Vector3 p = toSpace.MultiplyPoint3x4(srcPos_world);
        Vector3 f = toSpace.MultiplyVector(srcLook_world).normalized;
        Vector3 u = toSpace.MultiplyVector(srcUp_world).normalized;

        // 2) Зеркалим относительно выбранной плоскости (нормаль по mirrorAxis)
        Vector3 n = AxisUnit(mirrorAxis); // нормаль плоскости в space
        p = ReflectPointAcrossPlane(p, n); 
        f = ReflectVectorAcrossPlane(f, n).normalized;
        u = ReflectVectorAcrossPlane(u, n).normalized;

        // 3) Синтезируем ортонормированный базис (Look/Up могут стать неортогональны после зеркала)
        Orthonormalize(ref f, ref u, out Vector3 r);
        Quaternion rot_space = Quaternion.LookRotation(f, u);

        // 4) Возвращаемся в мир
        Matrix4x4 fromSpace = Matrix4x4.TRS(origin, basis, Vector3.one);
        Vector3 pos_world = fromSpace.MultiplyPoint3x4(p);
        Quaternion rot_world = fromSpace.rotation * rot_space;

        // 5) Если хотим сохранить roll: переориентируем вокруг look, приблизив локальный up целевого к "зеркальному up"
        if (preserveRoll && applyRotation)
        {
            // Обновлённый up — текущий up из rot_world
            Vector3 desiredUp = (fromSpace.rotation * u).normalized;
            Vector3 currentForward = (rot_world * AxisToLocalVec(lookAxis)).normalized;
            Vector3 currentUp      = (rot_world * AxisToLocalVec(upAxis)).normalized;
            // вращаем вокруг forward так, чтобы currentUp приблизился к desiredUp
            Quaternion rollFix = Quaternion.FromToRotation(
                Vector3.ProjectOnPlane(currentUp, currentForward),
                Vector3.ProjectOnPlane(desiredUp, currentForward)
            );
            rot_world = rollFix * rot_world;
        }

        previewPos = pos_world;
        previewRot = rot_world;
        hasPreview = true;
    }

    void ApplyNow()
    {
        if (source == null || target == null) return;
        ComputePreview();

        Undo.RecordObject(target, "Mirror Collider Transform");

        if (applyPosition) target.position = previewPos;
        if (applyRotation) target.rotation = previewRot;

        // полезно: зафиксировать модификации префаба и пометить сцену грязной
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        if (target.gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
    }

    // ---------- math helpers ----------
    void GetSpace(out Vector3 origin, out Quaternion basis)
    {
        if (spaceMode == SpaceMode.WorldOrigin || reference == null)
        {
            origin = Vector3.zero;
            basis = Quaternion.identity;   // мировые оси
        }
        else
        {
            origin = reference.position;   // плоскость проходит через reference
            basis = reference.rotation;    // оси зеркала совпадают с осями reference
        }
    }

    static Vector3 AxisUnit(MirrorAxis a)
    {
        switch (a)
        {
            case MirrorAxis.X: return Vector3.right;
            case MirrorAxis.Y: return Vector3.up;
            default:           return Vector3.forward;
        }
    }

    static Vector3 AxisToLocalVec(LocalAxis a)
    {
        switch (a)
        {
            case LocalAxis.PlusX:  return Vector3.right;
            case LocalAxis.MinusX: return Vector3.left;
            case LocalAxis.PlusY:  return Vector3.up;
            case LocalAxis.MinusY: return Vector3.down;
            case LocalAxis.PlusZ:  return Vector3.forward;
            default:               return Vector3.back;
        }
    }

    // Отражение точки относительно плоскости через начало (в space). Плоскость: n·x = 0
    static Vector3 ReflectPointAcrossPlane(Vector3 p, Vector3 n)
    {
        n.Normalize();
        float d = Vector3.Dot(n, p);   // расстояние до плоскости по нормали
        return p - 2f * d * n;
    }

    // Отражение вектора относительно плоскости
    static Vector3 ReflectVectorAcrossPlane(Vector3 v, Vector3 n)
    {
        n.Normalize();
        return v - 2f * Vector3.Dot(v, n) * n;
    }

    static void Orthonormalize(ref Vector3 f, ref Vector3 u, out Vector3 r)
    {
        f.Normalize();
        u = (u - Vector3.Dot(u, f) * f).normalized; // Gram-Schmidt
        r = Vector3.Cross(u, f).normalized;          // третья ось
    }

    // ---------- gizmos/UI ----------
    void DrawPreviewInfo(Vector3 pos, Quaternion rot)
    {
        EditorGUILayout.LabelField("Preview World Pos:", $"{pos.x:F4}, {pos.y:F4}, {pos.z:F4}");
        var eul = rot.eulerAngles;
        EditorGUILayout.LabelField("Preview World Rot (Euler):", $"{eul.x:F2}, {eul.y:F2}, {eul.z:F2}");
    }

    static void DrawAxes(Vector3 origin, Quaternion rot, float len, float thickness)
    {
        Handles.DrawLine(origin, origin + rot * Vector3.right * len, thickness);
        Handles.DrawLine(origin, origin + rot * Vector3.up * len, thickness);
        Handles.DrawLine(origin, origin + rot * Vector3.forward * len, thickness);
    }
}
#endif
