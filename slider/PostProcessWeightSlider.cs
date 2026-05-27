using UdonSharp;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Post-process Volume の Weight をゲーム内で調整するための MeshSlider 派生ギミック。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PostProcessWeightSlider : UdonSharpBehaviour
{
    [Header("Track  ※ Knob と同じ親(SliderParent)の子を指定")]
    [SerializeField] Transform trackStart;
    [SerializeField] Transform trackEnd;

    [Header("制御対象 (シングルモード)")]
    [SerializeField] PostProcessVolume targetVolume;

    [Header("Weight 設定 (シングルモード)")]
    [SerializeField] float minWeight = 0f;
    [SerializeField] float maxWeight = 1f;

    [Header("初期値  ※ 有効にするとStart時にknobをこの位置へ移動")]
    [SerializeField] bool useInitialValue = false;
    [SerializeField, Range(0f, 1f)] float initialT = 0.5f;

    [Header("デュアルボリュームモード  ※ 有効にするとシングルモードの設定を無視")]
    [SerializeField] bool dualVolumeMode = false;
    [SerializeField] PostProcessVolume volumeMin;   // t=0(min端) でWeight=1、中間でWeight=0
    [SerializeField] PostProcessVolume volumeMax;   // t=1(max端) でWeight=1、中間でWeight=0

    [UdonSynced] float syncedT;

    bool isHeld;
    Vector3 localStart;
    Vector3 localDir;
    float trackLen;
    Quaternion initialLocalRotation;

    // Apply() の二重呼び出し防止
    float lastAppliedT = -1f;

    // 同期閾値: トラック全体の 0.5% 未満の変化は送信しない
    const float SYNC_THRESHOLD = 0.005f;

    // Lerp 収束判定: sqrMagnitude がこの値未満ならスナップ
    const float SNAP_SQR_DIST = 1e-8f;

    void Start()
    {
        if (trackStart == null || trackEnd == null)
        {
            Debug.LogError($"[PostProcessWeightSlider:{name}] trackStart / trackEnd が未設定です");
            enabled = false;
            return;
        }

        if (!dualVolumeMode && targetVolume == null)
        {
            Debug.LogWarning($"[PostProcessWeightSlider:{name}] targetVolume が未設定です。");
        }
        if (dualVolumeMode && volumeMin == null && volumeMax == null)
        {
            Debug.LogWarning($"[PostProcessWeightSlider:{name}] デュアルモード: volumeMin と volumeMax がどちらも未設定です。");
        }

        // trackStart / trackEnd は Knob と同じ親 (SliderParent) の子
        localStart = trackStart.localPosition;
        Vector3 localEnd = trackEnd.localPosition;
        Vector3 vec = localEnd - localStart;
        trackLen = vec.magnitude;

        if (trackLen < 0.0001f)
        {
            Debug.LogError($"[PostProcessWeightSlider:{name}] trackStart と trackEnd が同位置です");
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
            Vector3 targetPos = PosAt(syncedT);
            Vector3 diff = targetPos - transform.localPosition;

            if (diff.sqrMagnitude > SNAP_SQR_DIST)
                transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.deltaTime * 20f);
            else
                transform.localPosition = targetPos;

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
        if (Mathf.Approximately(t, lastAppliedT)) return;
        lastAppliedT = t;

        if (dualVolumeMode)
        {
            // 中間(t=0.5)で両方Weight=0、min端でvolumeMin=1、max端でvolumeMax=1
            if (t <= 0.5f)
            {
                float wMin = (0.5f - t) * 2f;
                if (volumeMin != null) volumeMin.weight = wMin;
                if (volumeMax != null) volumeMax.weight = 0f;
            }
            else
            {
                if (volumeMin != null) volumeMin.weight = 0f;
                float wMax = (t - 0.5f) * 2f;
                if (volumeMax != null) volumeMax.weight = wMax;
            }
        }
        else
        {
            if (targetVolume != null)
                targetVolume.weight = Mathf.Lerp(minWeight, maxWeight, t);
        }
    }
}
