# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

VRChat 向けのリアルタイム色制御シェーダーシステム。VRCLV PhotoStudio プロジェクト用。インタラクティブな 3D スライダーで HSV（Hue・Saturation・Value）とエミッション強度をマルチプレイヤー同期しながらリアルタイム変更できる。

## Architecture

### Core Components

**Shader** — [Shader/ColorChangeShader.shader](Shader/ColorChangeShader.shader)
- HSV → RGB 変換（ブランチレスアルゴリズム）
- プロパティ: `_Hue`(0–1)、`_Saturation`(0–1)、`_Value`(0–1)、`_Emission`(Float)
- `_Value` は fragment shader 内で `pow(v, 2.2)` のガンマ補正を適用してから HSV 変換に渡す — スライダー下半分が暗い領域の調整に充てられ、知覚的に均一な明るさ分布になる
- GPU Instancing 対応（`UNITY_INSTANCING_BUFFER`）— `MaterialPropertyBlock` でインスタンスごとの値変更が可能
- Render Queue: Background、`ZWrite Off, Cull Off`（スカイボックス用途）

**MeshSlider** — [slider/MeshSlider.cs](slider/MeshSlider.cs)
- VRC_Pickup と **同じ Knob オブジェクト**に付ける（別オブジェクトにすると `OnPickup` / `OnDrop` が届かない）
- 動作原理: VRC_Pickup が Knob をハンド位置へ移動 → LateUpdate が `localPosition` をトラックへ投影・スナップ（LateUpdate は VRC SDK より後に実行されるため「勝てる」）
- `initialLocalRotation` を Start でキャッシュし、LateUpdate で毎フレーム上書き → ピックアップ中もローテーション固定
- `syncedT`（`[UdonSynced]` float、0–1）を `RequestSerialization()` で手動送信
- 非保持時は `syncedT` へ向けて Lerp（速度 20f）— `OnDeserialization` は空でよい

**slider** — [slider/slider.cs](slider/slider.cs)
- Unity UI `Slider` コンポーネント用の代替スクリプト（3D 非対応環境向け）
- MeshSlider と同じ dual-target パターン（Renderer vs Material）・ネットワーク同期ロジック

**Editor 拡張** — [slider/Editor/MeshSliderSetupEditor.cs](slider/Editor/MeshSliderSetupEditor.cs)
- MeshSlider の `[CustomEditor]`
- ボタン操作で `track_start` / `track_end` の自動生成、Rigidbody（Kinematic）・Collider・VRC_Pickup の自動追加が可能

### 必須の GameObject 階層

```
SliderParent（空 GameObject）
  ├── Knob  ← MeshSlider + VRC_Pickup + Rigidbody(Kinematic) + Collider + Mesh
  ├── track_start（空）
  └── track_end（空）
```

`track_start` / `track_end` は Knob の**兄弟**（同じ親の子）でなければならない。`localPosition` が SliderParent 空間で統一されるため、Knob の移動に連動してトラック原点がずれない。

### Data Flow

```
Player grabs Knob (VRC_Pickup)
  → OnPickup: isHeld=true, 自分がオーナーになる
  → LateUpdate (isHeld): localPosition を投影 → SnapTo(t) → Apply(t)
  → RequestSerialization: syncedT をブロードキャスト
  → 他プレイヤー OnDeserialization → LateUpdate (else): Lerp to syncedT → Apply(syncedT)
```

## Development Environment

- **Unity** + VRChat Creator Companion SDK
- **UdonSharp** — `.cs` 編集後は必ず UdonSharp コンパイルを実行（`.asset` ファイルが再生成される）
- **Target**: VRChatCreatorCompanion 経由でワールドアップロード

## Key Constraints

- MeshSlider と VRC_Pickup は**必ず同じ GameObject** に付ける。別オブジェクトに分離すると `OnPickup` / `OnDrop` イベントが MeshSlider に届かない（Udon の仕様）。
- Knob の Rigidbody は `isKinematic = true` 必須。非 Kinematic だと物理エンジンが LateUpdate の位置設定を毎フレーム上書きする。
- トラック座標は Start 時に SliderParent ローカル空間でキャッシュ。ボードが移動しても `localPosition` は変わらないため再計算不要。
- ネットワーク同期は Manual モード（`BehaviourSyncMode.Manual`）— `syncedT` を変更したら必ず `RequestSerialization()` を呼ぶ。
- `_Value` のガンマ補正はシェーダー側で行う（`pow(v, 2.2)`）。スクリプト側で補正を二重適用しないこと。
