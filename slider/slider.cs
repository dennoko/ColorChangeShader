using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class slider : UdonSharpBehaviour
{
    [Header("Target Settings")]
    [SerializeField] private Renderer targetRenderer;     // 特定のオブジェクトのRenderer（MaterialPropertyBlockを使用）
    [SerializeField] private Material targetMaterial;     // スカイボックスや共有マテリアル（直接SetFloatを使用）
    [SerializeField] private string propertyName = "_Hue";

    [Header("Value Settings")]
    [SerializeField] private float minValue = 0.0f;
    [SerializeField] private float maxValue = 1.0f;

    [Header("Sync Settings")]
    [SerializeField] private bool isSynced = true;

    private Slider uiSlider;
    private MaterialPropertyBlock propertyBlock;

    [UdonSynced]
    private float syncedValue;
    
    private float lastValue;
    private bool isUpdatingFromSync = false;

    void Start()
    {
        uiSlider = GetComponent<Slider>();
        propertyBlock = new MaterialPropertyBlock();

        if (uiSlider == null)
        {
            Debug.LogError("[ShaderSlider] Slider component not found on this GameObject!");
            return;
        }

        // 初期化処理: オーナーである場合は初期値を設定して同期
        if (isSynced && Networking.IsOwner(gameObject))
        {
            syncedValue = uiSlider.value;
            RequestSerialization();
        }
        
        UpdateShader(uiSlider.value);
    }

    // UI Slider の OnValueChanged イベントから呼び出されるメソッド
    public void OnSliderValueChanged()
    {
        if (uiSlider == null || isUpdatingFromSync) return;

        float value = uiSlider.value;
        if (Mathf.Approximately(value, lastValue)) return;
        
        lastValue = value;

        if (isSynced)
        {
            // 操作を行う前にローカルプレイヤーがオーナー権限を取得
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
            syncedValue = value;
            RequestSerialization();
        }

        UpdateShader(value);
    }

    // ネットワーク同期データが届いた時の処理
    public override void OnDeserialization()
    {
        if (isSynced && !Networking.IsOwner(gameObject))
        {
            isUpdatingFromSync = true;
            if (uiSlider != null)
            {
                uiSlider.value = syncedValue;
            }
            isUpdatingFromSync = false;
            UpdateShader(syncedValue);
        }
    }

    // マテリアルプロパティブロックを用いてパフォーマンス良くパラメータを反映
    private void UpdateShader(float normalizedValue)
    {
        // スライダーの 0~1 の値を、指定された設定範囲 (minValue ~ maxValue) にマッピング
        float mappedValue = Mathf.Lerp(minValue, maxValue, normalizedValue);

        // 1. Rendererが設定されている場合、MaterialPropertyBlockでパフォーマンス良く反映
        if (targetRenderer != null)
        {
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(propertyName, mappedValue);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        // 2. Materialが設定されている場合（Skyboxや共有マテリアル）、マテリアルを直接書き換え
        if (targetMaterial != null)
        {
            targetMaterial.SetFloat(propertyName, mappedValue);
        }
    }
}
