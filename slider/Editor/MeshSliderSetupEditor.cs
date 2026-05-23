#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;

/// <summary>
/// MeshSlider のセットアップを自動化するエディタ拡張。
/// Knob オブジェクト (MeshSlider が付いているオブジェクト) を選択した状態で
/// Inspector のボタンを押すとセットアップが完了する。
/// </summary>
[CustomEditor(typeof(MeshSlider))]
public class MeshSliderSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MeshSlider slider = (MeshSlider)target;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("── セットアップユーティリティ ──", EditorStyles.boldLabel);

        // ステータス表示
        bool hasRb      = slider.GetComponent<Rigidbody>() != null;
        bool hasCol     = slider.GetComponent<Collider>() != null;
        bool hasPickup  = slider.GetComponent<VRCPickup>() != null;
        bool hasParent  = slider.transform.parent != null;

        var startProp = serializedObject.FindProperty("trackStart");
        var endProp   = serializedObject.FindProperty("trackEnd");
        bool hasTrack = startProp.objectReferenceValue != null && endProp.objectReferenceValue != null;

        MessageType msgType = (hasRb && hasCol && hasPickup && hasTrack)
            ? MessageType.Info : MessageType.Warning;

        EditorGUILayout.HelpBox(
            $"Rigidbody : {Mark(hasRb)}   Collider : {Mark(hasCol)}   " +
            $"VRC_Pickup : {Mark(hasPickup)}   Track : {Mark(hasTrack)}   " +
            $"Parent : {Mark(hasParent)}",
            msgType
        );

        EditorGUILayout.Space(4);

        if (GUILayout.Button("① トラック (track_start / track_end) を自動生成", GUILayout.Height(28)))
            AutoCreateTrack(slider);

        if (GUILayout.Button("② 必要コンポーネントを自動追加 (Rigidbody / Collider / VRC_Pickup)", GUILayout.Height(28)))
            AutoAddComponents(slider);

        EditorGUILayout.Space(4);
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("⭐ 全てまとめてセットアップ", GUILayout.Height(36)))
        {
            AutoCreateTrack(slider);
            AutoAddComponents(slider);
        }
        GUI.backgroundColor = prevColor;

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "階層の確認:\n" +
            "SliderParent (空)\n" +
            "  ├─ Knob ← このオブジェクト (MeshSlider + VRC_Pickup etc.)\n" +
            "  ├─ track_start\n" +
            "  └─ track_end\n\n" +
            "track_start / track_end は Knob の「兄弟」(同じ親の子) である必要があります。",
            MessageType.None
        );
    }

    // ----------------------------------------------------------------

    void AutoCreateTrack(MeshSlider slider)
    {
        serializedObject.Update();

        Transform parent = slider.transform.parent;
        if (parent == null)
        {
            EditorUtility.DisplayDialog(
                "MeshSlider セットアップ",
                "Knob の親オブジェクト (SliderParent) が存在しません。\n" +
                "空の GameObject を作成して Knob の親にしてください。",
                "OK"
            );
            return;
        }

        var startProp = serializedObject.FindProperty("trackStart");
        var endProp   = serializedObject.FindProperty("trackEnd");

        Vector3 knobLocal = slider.transform.localPosition;

        if (startProp.objectReferenceValue == null)
        {
            GameObject go = new GameObject("track_start");
            Undo.RegisterCreatedObjectUndo(go, "Create track_start");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = knobLocal + Vector3.left * 0.05f;
            startProp.objectReferenceValue = go.transform;
            Debug.Log($"[MeshSlider] track_start を '{parent.name}' 下に作成");
        }

        if (endProp.objectReferenceValue == null)
        {
            GameObject go = new GameObject("track_end");
            Undo.RegisterCreatedObjectUndo(go, "Create track_end");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = knobLocal + Vector3.right * 0.05f;
            endProp.objectReferenceValue = go.transform;
            Debug.Log($"[MeshSlider] track_end を '{parent.name}' 下に作成");
        }

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(slider);
    }

    void AutoAddComponents(MeshSlider slider)
    {
        Undo.RecordObject(slider.gameObject, "MeshSlider Auto Add Components");

        // Rigidbody (Kinematic 必須)
        Rigidbody rb = slider.GetComponent<Rigidbody>();
        if (rb == null)
            rb = Undo.AddComponent<Rigidbody>(slider.gameObject);
        rb.isKinematic = true;
        rb.useGravity  = false;

        // Collider (なければ SphereCollider を追加)
        if (slider.GetComponent<Collider>() == null)
        {
            SphereCollider col = Undo.AddComponent<SphereCollider>(slider.gameObject);
            col.radius = 0.03f;
        }

        // VRC_Pickup
        if (slider.GetComponent<VRCPickup>() == null)
            Undo.AddComponent<VRCPickup>(slider.gameObject);

        EditorUtility.SetDirty(slider.gameObject);
        Debug.Log($"[MeshSlider] {slider.name} のコンポーネントを設定しました");
    }

    static string Mark(bool ok) => ok ? "✓" : "✗";
}
#endif
