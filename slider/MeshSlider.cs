using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// このスクリプトは VRC_Pickup と同じ Knob オブジェクトに付ける。
/// trackStart / trackEnd は Knob の「兄弟」(同じ親の子) にすること。
///
/// 動作原理:
///   VRC_Pickup が Knob をハンド位置へ移動
///   → LateUpdate が localPosition をトラックへ投影・スナップ (LateUpdate はSDKより後に走る)
///   → ノブがレール上をスライドする
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MeshSlider : UdonSharpBehaviour
{
    [Header("Track  ※ Knob と同じ親(SliderParent)の子を指定")]
    [SerializeField] Transform trackStart;
    [SerializeField] Transform trackEnd;

    [Header("制御対象")]
    [SerializeField] Renderer targetRenderer;   // MaterialPropertyBlock 用（任意）
    [SerializeField] Material targetMaterial;   // 共有マテリアル用 (Skybox 等)
    [SerializeField] string propertyName = "_Hue";
    [SerializeField] float minValue = 0f;
    [SerializeField] float maxValue = 1f;

    [Header("初期値  ※ 有効にするとStart時にknobをこの位置へ移動")]
    [SerializeField] bool useInitialValue = false;
    [SerializeField, Range(0f, 1f)] float initialT = 0.5f;

    [UdonSynced] float syncedT;

    bool isHeld;
    Vector3 localStart;
    Vector3 localDir;
    float trackLen;
    MaterialPropertyBlock mpb;
    Quaternion initialLocalRotation;

    // Apply() の二重呼び出し防止: 前回適用した t と異なる場合のみ SetFloat を実行する
    float lastAppliedT = -1f;

    // 同期閾値: トラック全体の 0.5% 未満の変化は送信しない (知覚不可能な差)
    // Mathf.Approximately (~1e-5) より大きい閾値で RequestSerialization の無駄な呼び出しを削減
    const float SYNC_THRESHOLD = 0.005f;

    // Lerp の収束判定: sqrMagnitude がこの値未満なら目標位置へスナップして Lerp を終了する
    // 0.1mm^2 = 1e-8 (実際は ~0.316mm 以内でスナップ)
    const float SNAP_SQR_DIST = 1e-8f;

    void Start()
    {
        mpb = new MaterialPropertyBlock();

        if (trackStart == null || trackEnd == null)
        {
            Debug.LogError($"[MeshSlider:{name}] trackStart / trackEnd が未設定です");
            enabled = false;
            return;
        }

        // trackStart / trackEnd は Knob と同じ親 (SliderParent) の子
        // → .localPosition は全て SliderParent 空間で統一される
        localStart = trackStart.localPosition;
        Vector3 localEnd = trackEnd.localPosition;
        Vector3 vec = localEnd - localStart;
        trackLen = vec.magnitude;

        if (trackLen < 0.0001f)
        {
            Debug.LogError($"[MeshSlider:{name}] trackStart と trackEnd が同位置です");
            enabled = false;
            return;
        }

        localDir = vec / trackLen;
        initialLocalRotation = transform.localRotation;

        float startT = useInitialValue ? initialT : ComputeT(transform.localPosition);
        syncedT = startT;
        Apply(syncedT);
        SnapTo(syncedT);
    }

    void LateUpdate()
    {
        if (trackLen < 0.0001f) return;

        // ピックアップ中も含め、常にローテーションを固定
        transform.localRotation = initialLocalRotation;

        if (isHeld)
        {
            float t = ComputeT(transform.localPosition);
            SnapTo(t);
            Apply(t);

            // SYNC_THRESHOLD 未満の変化は送信しない
            if (Networking.IsOwner(gameObject) && Mathf.Abs(t - syncedT) > SYNC_THRESHOLD)
            {
                syncedT = t;
                RequestSerialization();
            }
        }
        else
        {
            // syncedT の位置へスムーズ追従
            // sqrMagnitude でスナップ閾値をチェックし、収束後は Lerp を停止する
            Vector3 targetPos = PosAt(syncedT);
            Vector3 diff = targetPos - transform.localPosition;

            if (diff.sqrMagnitude > SNAP_SQR_DIST)
                transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.deltaTime * 20f);
            else
                transform.localPosition = targetPos;

            // lastAppliedT ガードにより、syncedT が変化したフレームのみ SetFloat を実行
            Apply(syncedT);
        }
    }

    public override void OnPickup()
    {
        isHeld = true;
        lastAppliedT = -1f; // 掴んだ瞬間に強制再適用
        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    public override void OnDrop()
    {
        isHeld = false;
        float t = ComputeT(transform.localPosition);
        syncedT = t;
        SnapTo(t);
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        // LateUpdate の else ブランチが syncedT へ自動追従するため空でよい
        // (syncedT 更新により lastAppliedT と不一致 → Apply が自動的に実行される)
    }

    // ---- 内部ヘルパー ----

    float ComputeT(Vector3 localPos)
    {
        float proj = Vector3.Dot(localPos - localStart, localDir);
        return Mathf.Clamp01(proj / trackLen);
    }

    Vector3 PosAt(float t)
    {
        return localStart + localDir * (t * trackLen);
    }

    void SnapTo(float t)
    {
        transform.localPosition = PosAt(t);
    }

    void Apply(float t)
    {
        // 前回と同じ値なら SetFloat を呼ばない
        if (Mathf.Approximately(t, lastAppliedT)) return;
        lastAppliedT = t;

        float v = Mathf.Lerp(minValue, maxValue, t);
        if (targetRenderer != null)
        {
            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(propertyName, v);
            targetRenderer.SetPropertyBlock(mpb);
        }
        if (targetMaterial != null)
            targetMaterial.SetFloat(propertyName, v);
    }
}
