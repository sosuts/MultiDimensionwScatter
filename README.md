# MultiDimensionwScatter README / 内部設計書

本プロジェクトは、WPF (.NET Framework 4.6.2, C# 7.3) 上で HelixToolkit.Wpf.SharpDX を用い、3次元ガウス混合モデル (Gaussian Mixture Model; GMM) の散布図を高速に描画し、XY/XZ/YZ の2D投影画像を生成・表示するツールです。HelixToolkit の `Viewport3DX` / `PointGeometryModel3D` により大量点を安定・高速に描画します。

このドキュメントは、外部利用者のガイドと、開発者の内部設計書（構成・責務・処理フロー・拡張方針）を兼ねます。

## 目次
- ゴール / 機能
- 動作環境 / 依存関係
- システム全体構成
- モジュール設計 / クラス責務
- データモデル設計
- UI設計 / バインディング
- 3D描画設計（HelixToolkit/SharpDX）
- サンプル生成アルゴリズム（GMM）
- 2D投影レンダリング設計
- カメラ / 軸 / 補助オブジェクト
- エラーハンドリング / 入力検証
- パフォーマンス設計 / チューニング
- セットアップ / ビルド / 実行手順
- トラブルシューティング
- 拡張設計（ロードマップ）
- ライブラリ API メモ
- ライセンス / 注意
- 参考リンク

---

## ゴール / 機能
- HelixToolkit.Wpf.SharpDX による 3D 散布図の高速描画
- GMM（複数成分）の平均・共分散に基づくサンプル生成
- 成分ごとの色分け / 点サイズ指定（ピクセル単位）
- 軸表示（X/Y/Z）と半透明壁面（XY/XZ/YZ）
- XY/XZ/YZ の 2D 投影画像生成（非対話）
- 乱数シードの入力 / ランダム化、重みに基づくサンプル数自動配分
- 共分散ランダム生成（SPD, 異方性バイアス）
- カメラの直交ビュー切替

## 動作環境 / 依存関係
- OS: Windows 10/11
- .NET: .NET Framework 4.6.2（C# 7.3）
- IDE: Visual Studio 2019/2022
- NuGet 依存:
  - HelixToolkit.Wpf.SharpDX
  - SharpDX（HelixToolkit の内部依存）

## システム全体構成
- プレゼンテーション層: WPF（XAML + Code-behind）
- 3D描画基盤: HelixToolkit.Wpf.SharpDX（Viewport3DX / EffectsManager / Model3D）
- ロジック層: `MainWindow.xaml.cs`（生成・描画・UI操作ハンドラ）
- ヘルパ層: 共分散生成 / 投影レンダリング / カメラ補助
- データモデル層: `ComponentParam`（GMM成分設定）

ディレクトリ（概念）
- `MainWindow.xaml` / `MainWindow.xaml.cs`: 画面・ロジック
- `Models/ComponentParam.cs`: データモデル
- `Helpers/*`: アルゴリズム・ユーティリティ

## モジュール設計 / クラス責務
- `MainWindow`
  - 役割: 画面の初期化、イベントハンドリング、生成と描画の中核、軸/壁面/投影の更新
  - 主要フィールド:
    - `ObservableCollection<ComponentParam> _components`: 成分一覧（DataGrid バインド）
    - `IEffectsManager EffectsManager = new DefaultEffectsManager()`: HelixToolkit のエフェクト管理
    - `PointGeometry3D _scatterGeometry`: 散布図の既定ジオメトリ（Positions/Colors 保持）
    - `ScatterModel`: XAML 側の `PointGeometryModel3D`（`Geometry`に点群を設定）
    - 軸モデル: `AxisXModel`, `AxisYModel`, `AxisZModel`
    - 壁面モデル: `WallXModel`, `WallYModel`, `WallZModel`
  - 主要メソッド:
    - `InitializeDefaults()`: 初期成分の追加
    - `BtnGenerate_Click`: 入力検証→配分→生成→ジオメトリ更新→軸/投影更新
    - `ClearPoints()`: ジオメトリをクリアし、軸/投影を更新
    - `UpdateAxes()`: 現在の点群からスケール計算し、軸と壁面を構築
    - `BuildWalls(...)`: XY/XZ/YZ の半透明クアッド生成
    - `UpdateProjections()`: 点群から 2D 投影画像生成
    - `TryCholesky3x3(...)`: SPD 検証と下三角分解
- `ComponentParam`
  - 役割: 1成分の GMM パラメータ（重み、平均、共分散、色、サンプル数）
- `CovarianceGenerator`
  - 役割: ランダムな SPD 共分散行列の生成（ランダム回転 `Q` と固有値 `D` により `QDQ^T`）
- `ProjectionRenderer`（存在する場合）
  - 役割: `PointGeometry3D` から選択軸の 2D 投影画像（ヒートマップ風）を生成
- `CameraHelper`（存在する場合）
  - 役割: シーン中心・半径の取得、直交視のカメラ設定

## データモデル設計
- `ComponentParam`
  - `Weight: double`
  - `MeanX/MeanY/MeanZ: double`
  - `C11/C12/C13/C22/C23/C33: double`（対称行列の上三角）
  - `SampleCount: int`
  - `Color: System.Windows.Media.Color`

## UI設計 / バインディング
- `MainWindow.xaml`
  - 左: 3D ビュー（`Viewport3DX`）
    - `DefaultLights`
    - `PointGeometryModel3D x:Name="ScatterModel"`
    - 軸 `LineGeometryModel3D`（X/Y/Z）
    - 壁面 `MeshGeometryModel3D`（XY/XZ/YZ）と表示チェック / 透明度スライダ
  - 右: パラメータパネル
    - `DataGrid`（`ItemsSource = _components`）
    - 入力欄（Point Size, Seed, Total Samples）
    - 操作ボタン（Generate/Clear/Randomize Seed/Random Covariance/View XY/XZ/YZ）
    - 投影画像 `Image` ×3（XY/XZ/YZ）
- バインディングの要点
  - `DataContext = this` とし、`ScatterModel.Geometry` を直接更新
  - `Size` はピクセル指定（`new Size(w,h)`）

## 3D描画設計（HelixToolkit/SharpDX）
- ビューポート: `Viewport3DX` + `DefaultEffectsManager`
- 散布図: `PointGeometryModel3D`
  - `Geometry: PointGeometry3D{Positions: Vector3Collection, Colors: Color4Collection}`
  - 点サイズ: `ScatterModel.Size`
- 軸: `LineGeometryModel3D`（`LineBuilder.AddLine` → `ToLineGeometry3D()`）
- 壁面: `MeshGeometryModel3D`（`MeshBuilder.AddQuad` → `ToMeshGeometry3D()` + `PhongMaterial(DiffuseColor=Color4)`）
- 更新指針
  - `Positions/Colors` は一括で新規 `PointGeometry3D` を構築して差し替え（変更通知と GPU 転送が明確）
  - クリア時は `Positions.Clear(); Colors.Clear(); UpdateBounds()` の順

## サンプル生成アルゴリズム（GMM）
- 正規乱数: 極座標版 Box-Muller（Marsaglia Polar）。`[ThreadStatic]` なスペア値で 2個/回を活用
- 共分散: 入力値から対称行列 `S` を構築し、`TryCholesky3x3(S,out L)` により下三角行列 `L` を求める（SPD 検証）
- 生成: `mean + L * z`（`z ~ N(0,I)`）を各成分の `SampleCount` 回生成
- 色: 成分ごとの WPF `Color` を `Color4(R/255,G/255,B/255,A/255)` に変換し、各点に積む
- 自動配分: `TotalSamples` が指定され、各成分 `SampleCount` が 0 の場合、`Weight` 比で整数配分

## 2D投影レンダリング設計
- 入力: `PointGeometry3D.Positions/Colors`
- 軸選択: `('X','Y'), ('X','Z'), ('Y','Z')`
- スケーリング: 全点の min/max を計算、等方フィットとパディング
- 描画: `DrawingVisual` に高速矩形描画 → `RenderTargetBitmap` へラスタライズ → `Image.Source` に設定
- 出力: 固定サイズ（既定 210×210）だが UI で可変に拡張可能

## カメラ / 軸 / 補助オブジェクト
- カメラ: 直交ビュー設定 (`CameraHelper.SetCameraToAxisViewOrtho`)
- 軸: 原点中心に `[-L,L]` の X/Y/Z を作図
- 壁面: XY/XZ/YZ 半透明クアッド（表示/非表示、透明度スライダ）

## エラーハンドリング / 入力検証
- 生成前
  - `Point Size > 0`、`Seed` 整数
  - `Weight > 0` の成分が存在
  - 自動配分時の `TotalSamples > 0`、重み合計 > 0
  - 共分散 SPD 検証に失敗したら例外表示
- 例外
  - `try/catch` で `MessageBox.Show`、UI を `SetBusy(false)` で復旧

## パフォーマンス設計 / チューニング
- 描画
  - `PointGeometryModel3D` はビルボード点で最速（大量点でも安定）
  - `Geometry` の更新は一括置換で GPU 転送を最小化
- 生成
  - `Task.Run` で UI スレッドを塞がない
  - 正規乱数のキャッシュ `_spare` を使用
- 補助
  - 軸長は点群範囲から算出し、余分な再生成を抑制
- 将来の最適化
  - LOD/クラスタリング、投影のバケット化/並列化、Compute Shader 移行

## セットアップ / ビルド / 実行手順
1. リポジトリをクローン
2. Visual Studio でソリューションを開く
3. NuGet パッケージを復元（HelixToolkit.Wpf.SharpDX / SharpDX）
4. ビルド（Ctrl+Shift+B）
5. 実行（F5）
6. 右ペインで `Point Size` と `Seed` を指定 → `Generate`

## トラブルシューティング
- 何も表示されない
  - `ScatterModel.Geometry` に `Positions` が入っているか、`DefaultLights` があるか、`EffectsManager` 設定済みか
- 点サイズが変わらない
  - `ScatterModel.Size` を更新しているか（ピクセル指定）
- 色が反映されない
  - `Colors` の `Color4` を 0-1 範囲に正規化
- 共分散エラー
  - SPD 条件（主座小行列 > 0）を満たす値か再確認
- クラス図が関係を表示しない
  - Class Designer の「Show Associations/Inheritances」有効化、図へクラス追加、再レイアウト

## 拡張設計（ロードマップ）
- 2D投影の対話化（パン/ズーム）
- 軸ラベル/目盛（ビルボードテキスト/Direct2D）
- ボリューム可視化（IsoSurface/Volume Rendering）
- シーンスナップショット/動画出力
- .NET 6/8 版への移行と HelixToolkit 対応確認

## ライブラリ API メモ
- `PointGeometryModel3D`
  - `Geometry: PointGeometry3D`
  - `Size: System.Windows.Size`（ピクセル）
  - `Color: System.Windows.Media.Color`（単色時）
- `PointGeometry3D`
  - `Positions: Vector3Collection`
  - `Colors: Color4Collection`
- `LineBuilder`
  - `AddLine(Vector3 a, Vector3 b)` → `ToLineGeometry3D()`
- `MeshBuilder`
  - `AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)` → `ToMeshGeometry3D()`
- `DefaultEffectsManager`
  - HelixToolkit のパイプライン/マテリアル管理の既定実装

## ライセンス / 注意
- HelixToolkit / SharpDX のライセンスに従います
- .NET Framework 前提。WPF .NET 6/8 へ移行時は互換 API とパッケージを確認

## 参考リンク
- HelixToolkit.Wpf.SharpDX: https://github.com/helix-toolkit/helix-toolkit
- ドキュメント/サンプル: https://helix-toolkit.github.io/

以上により、HelixToolkit.Wpf.SharpDX 初学者でもソリューションの全体像・内部設計・動作原理を把握し、再現・拡張できるように構成しています。
