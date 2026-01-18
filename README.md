# MultiDimensionwScatter 詳細設計書

本プロジェクトは、WPF(.NET Framework 4.6.2, C# 7.3)上で HelixToolkit.Wpf.SharpDX を用いて、3次元ガウス混合モデル(Gaussian Mixture Model; GMM)の散布図を描画し、非対話の2D投影(XY, XZ, YZ)を表示するツールです。各成分の平均ベクトル・共分散行列からサンプルを生成し、色分けして描画します。さらに、軸表示、シード指定、重みによるサンプル配分、共分散ランダム生成などの機能を提供しています。

## 技術スタック
- WPF(.NET Framework 4.6.2)
- C# 7.3
- HelixToolkit.Wpf.SharpDX(3D可視化)
- SharpDX(ベクトル・色型など HelixToolkit が内部で使用)

## アーキテクチャ概要
-`MainWindow.xaml`:
  - 3D表示用の`Viewport3DX`と散布図モデル`PointGeometryModel3D`、軸ライン用`LineGeometryModel3D`を定義
  - 右ペイン(パラメータパネル)に各種操作UI(成分一覧`DataGrid`、生成・クリアボタン、ボリューム未実装のスライダー、2D投影の`Image`など)
-`MainWindow.xaml.cs`:
  - ViewModel的な役割を兼ねるコードビハインド
  - データ生成、幾何更新、軸表示更新、2D投影生成、共分散ランダム生成のロジックを実装
-`Models/ComponentParam.cs`(想定):
  - 混合成分の重み、平均、共分散要素、サンプル数、色などのプロパティを保持

## 主要パッケージの使い方
- HelixToolkit.Wpf.SharpDX
  -`DefaultEffectsManager`: エフェクト管理(マテリアル/シェーダ)
  -`Viewport3DX`: 3Dカメラ・ライト・モデルを配置するコンテナ
  -`PointGeometryModel3D`: 点群描画のためのモデル。`Geometry`に`PointGeometry3D`を与える
  -`PointGeometry3D`:`Positions(Vector3Collection)`と`Colors(Color4Collection)`を持つ点群幾何
  -`LineGeometryModel3D`: 軸などの線分描画。`Geometry`に`LineGeometry3D`を与える
  -`LineBuilder`: 線分を追加して`LineGeometry3D`に変換する補助クラス
- WPF 2D描画
  -`DrawingVisual`+`RenderTargetBitmap`で非対話の 2D 投影画像を合成し、`Image.Source`に設定

## SharpDX のAPIと使用目的
本プロジェクトでは、HelixToolkit.Wpf.SharpDX が内部で SharpDX の型・リソースを活用しています。アプリコードから直接操作している主な SharpDX 型は次の通りです。

-`SharpDX.Vector3`
  - 目的: 3D点群の頂点座標表現。
  - 使用箇所:`PointGeometry3D.Positions`に格納する各サンプル点(`new Vector3(x, y, z)`)。
  - 背景: HelixToolkit の`Vector3Collection`は SharpDX の`Vector3`を要素型に取ります。頂点バッファに反映されます。

-`SharpDX.Color4`
  - 目的: 各点の RGBA 色表現。
  - 使用箇所:`PointGeometry3D.Colors`に各サンプルの色を設定(WPF`Color`から`Color4`に変換)。
  - 背景: HelixToolkit の`Color4Collection`は GPU へ配列としてアップロードされ、ポイント描画時のカラーに利用されます。

- HelixToolkit 経由の SharpDX リソース
  -`DefaultEffectsManager`
    - 目的: シェーダ/パイプラインの管理。マテリアル、頂点/ピクセルシェーダ、レンダリングステートの初期化。
    - 拡張性: PBR マテリアル、カスタムシェーダへの差し替え、ライティング拡張。
  -`Viewport3DX`
    - 目的: デバイス・スワップチェイン管理、カメラ・ライト・モデルのレンダリング統合。
    - 拡張性: マルチパスレンダ、ポストプロセス(FXAA/SSAO)、スナップショット出力などの拡張が可能。

補足として、`LineGeometryModel3D`/`PointGeometryModel3D`は SharpDX の頂点バッファ/インデックスバッファを HelixToolkit が生成して描画します。アプリ側は`Vector3Collection`/`Color4Collection`を更新するだけで GPU バッファへ反映されます。

## 拡張性(SharpDX/HelixToolkit観点)
- 点群の大量描画の最適化
  - インスタンシング: 同形状ポイントをGPUインスタンシングで描画し、CPU負担を低減。
  - カラーの圧縮/パレット化:`Color4`をパレット参照にし、メモリ帯域を削減。
- カスタムシェーダ
  - HelixToolkit の`EffectsManager`を拡張し、SharpDX の HLSL シェーダを差し替え可能。
  - サイズ/色を属性ベースで変化させるジオメトリシェーダ、ポストプロセスパスの追加。
- Compute Shader による生成・投影の高速化
  - サンプル生成を GPU 側で行う(正規乱数と線形変換)。
  - 2D投影のビンニング/ラスタ化を Compute Shader で並列化。
- 直接的な Direct2D/DirectWrite 連携
  - ラベル、目盛、注釈のベクタ描画を Direct2D で重ね合わせ。
- マルチスレッド/バックグラウンド生成
  - 現在は`Task.Run`によるCPU生成。SharpDX の`Device`と協調してレンダリングスレッドの負荷分散を行うことでスムーズなUIを維持。
- 大規模データ向けLOD/クラスタリング
  - Octree/グリッドLODにより遠景でポイントを集約表示。
- 軸・ガイドの強化
  - 軸ラベル、目盛線、グリッド面、原点マーカーを`LineGeometryModel3D`/カスタムモデルで追加。

## UIと描画の詳細

### 3Dビュー(`MainWindow.xaml`)
-`Viewport3DX`
  -`PerspectiveCamera`を使用
  -`DirectionalLight3D`を2つ配置して基本的な照明
  -`PointGeometryModel3D`(`ScatterModel`): 3D散布図を表示
    -`Geometry`はコードビハインドから`PointGeometry3D`を割り当て
    -`Size`は点のピクセルサイズ(WPFの`Size`型で横/縦)
  - 軸`LineGeometryModel3D`(`AxisXModel`,`AxisYModel`,`AxisZModel`): 原点中心に X/Y/Z 軸を表示
- 右ペイン
  - コンポーネント編集用`DataGrid`
  - シード、トータルサンプル、点サイズなどの入力欄
  - 操作ボタン: 生成、クリア、ランダムシード、共分散ランダム生成
  - 2D投影表示:`Image`(`ImgXY`,`ImgXZ`,`ImgYZ`)を`Border`で囲み固定サイズで表示

### コードビハインド(`MainWindow.xaml.cs`)

#### 初期化
-`EffectsManager`に`DefaultEffectsManager`を設定
-`ScatterGeometry`をデータコンテキストとしてバインドし、`ScatterModel.Geometry`に設定
-`InitializeDefaults()`で2つの初期成分を追加
- 初期の軸描画`UpdateAxes()`を呼び出し

#### サンプル生成(`BtnGenerate_Click`)
1. 入力検証: 点サイズ、シード、成分重み/サンプル数の妥当性
2. サンプル数配分: 各成分の`SampleCount`が0なら、`Total Samples`と`Weight`に基づき配分
3. 生成ロジック:
   - 各成分について、共分散行列`S`を構築
   -`TryCholesky3x3(S, out L)`: 共分散が正定値か検証し、下三角行列`L`を求める
   - 標準正規乱数`z`を生成して、`mean + L * z`で3次元サンプルを作成
   -`Vector3Collection`に座標、`Color4Collection`に色を追加
4.`PointGeometry3D`を構築し`ScatterModel.Geometry`に設定、`ScatterModel.Size`に点サイズを反映
5.`Viewport.ZoomExtents()`で範囲にフィット
6.`UpdateAxes()`と`UpdateProjections()`を呼び出し

#### ランダム共分散生成(`BtnRandomCov_Click`)
- 目的: ガウス分布を「偏らせる」ため、異方性の強い正定値対称行列を各成分に付与
- アルゴリズム:
  1. ランダム回転行列`Q`を Gram-Schmidt で生成
  2. 固有値を`minEigen`〜`maxEigen`の広い範囲から二峰性バイアスでサンプリング(小さい/大きい値に寄せる)
  3. 対角行列`D`を作り、`S = Q D Q^T`で共分散行列を生成
  4. 数値誤差対策で対称化
- 生成された`S`の要素を`C11..C33`に割り当て、`DataGrid`を`Items.Refresh()`で更新

#### 軸描画(`UpdateAxes`)
- 現在の点群の座標範囲からスケール`L`を計算
-`LineBuilder`を用いて`[-L, L]`の X/Y/Z 軸線を作成し、各`Axis*Model.Geometry`に設定

#### 2D投影生成(`UpdateProjections`/`RenderProjection`)
- 入力: 3D点群の`Positions`と`Colors`
- 軸選択:`'X','Y'`、`'X','Z'`、`'Y','Z'`
- スケーリング:
  - 各軸の最小/最大から等方スケールとパディングを計算
  - 固定サイズ(デフォルト 210×210)のキャンバスに等方フィット
- 描画:
  -`DrawingVisual`に対して点を高速矩形で描画
  -`RenderTargetBitmap`でラスタライズし`Image.Source`に設定

#### 乱数生成(`NextStandardNormal`)
- Box-Muller 法(極座標版)を使用
-`[ThreadStatic]`な予備値(`_spare`)を活用して1回の計算で2標準正規を生成(性能最適化)

#### 線形代数補助
-`TryCholesky3x3`: 3x3 対称正定値行列の下三角分解(失敗時はエラー文字列を返す)
- ランダムSPD生成用の 3Dベクトル演算(`Dot`,`Cross`,`Normalize`,`Multiply`,`Transpose`)

## データモデル(`Models/ComponentParam.cs`について)
- プロパティ例:
  -`double Weight`(混合重み)
  -`double MeanX, MeanY, MeanZ`(平均)
  -`double C11, C12, C13, C22, C23, C33`(対称共分散要素)
  -`int SampleCount`(生成サンプル数)
  -`System.Windows.Media.Color Color`(色)
-`DataGrid`は上記プロパティにバインドして編集可能

## 処理フロー
1. 初期起動: デフォルト成分をセット、空の点群を表示、軸を描画
2. ユーザ操作:
   - 成分追加/削除、共分散の編集
   - シード指定・ランダム化
   - ランダム共分散生成で異方性付与
   - サンプル生成・描画で3D表示更新、2D投影更新
   - クリアで点群・投影を消去、軸リサイズ

## エラーハンドリング
- 入力検証で不正値を`MessageBox`により通知
- 正定値でない共分散は`TryCholesky3x3`が検知し例外メッセージ表示

## 拡張ポイント
- 2D投影のサイズ/点サイズの係数を UI で調整可能にする
- 軸ラベル/目盛の追加(現状線のみ)
- 3Dボリューム(密度の体積可視化)は未実装スロットあり(`BtnGenVol_Click`など)
- GMM の確率密度計算と等高線/ヒートマップの 2D 表示

## ビルドと実行
- 依存: HelixToolkit.Wpf.SharpDX(NuGet)
- Visual Studio でソリューションを開き、`MultiDimensionwScatter`を起動

## 注意事項
- 本プロジェクトは .NET Framework 4.6.2 に依存します(C# 7.3)。
- HelixToolkit のバージョンにより API/挙動が異なる場合があります。`DefaultEffectsManager`と`Viewport3DX`の互換性に留意してください。
