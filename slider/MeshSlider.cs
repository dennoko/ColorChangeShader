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

    [UdonSynced] float syncedT;

    bool isHeld;
    Vector3 localStart;
    Vector3 localDir;
    float trackLen;
    MaterialPropertyBlock mpb;

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

        // Knob の初期配置から t を逆算
        syncedT = ComputeT(transform.localPosition);
        Apply(syncedT);
        SnapTo(syncedT);
    }

    void LateUpdate()
    {
        if (trackLen < 0.0001f) return;

        if (isHeld)
        {
            // VRC_Pickup がこのフレームで Knob をハンド位置へ移動済み
            // → localPosition を読んでトラックへ投影
            float t = ComputeT(transform.localPosition);
            SnapTo(t);
            Apply(t);

            if (Networking.IsOwner(gameObject) && !Mathf.Approximately(t, syncedT))
            {
                syncedT = t;
                RequestSerialization();
            }
        }
        else
        {
            // 非保持時: syncedT の位置へスムーズ追従
            transform.localPosition = Vector3.Lerp(
                transform.localPosition,
                PosAt(syncedT),
                Time.deltaTime * 20f
            );
            Apply(syncedT);
        }
    }

    public override void OnPickup()
    {
        isHeld = true;
        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    public override void OnDrop()
    {
        isHeld = false;
        // トラック上の最近傍点へ即スナップ
        float t = ComputeT(transform.localPosition);
        syncedT = t;
        SnapTo(t);
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        // LateUpdate の else ブランチが自動的に syncedT へ追従するため空でよい
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
