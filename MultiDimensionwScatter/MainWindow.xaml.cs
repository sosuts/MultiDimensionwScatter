using HelixToolkit.Wpf.SharpDX;
using MultiDimensionwScatter.Models;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MultiDimensionwScatter
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ComponentParam> _components = new ObservableCollection<ComponentParam>();
        public IEffectsManager EffectsManager { get; } = new DefaultEffectsManager();
        private readonly PointGeometry3D _scatterGeometry = new PointGeometry3D
        {
            Positions = new Vector3Collection(),
            Colors = new Color4Collection()
        };
        public PointGeometry3D ScatterGeometry => _scatterGeometry;
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeDefaults();
            GridComponents.ItemsSource = _components;
            ScatterModel.Geometry = _scatterGeometry;

            // 初期軸描画
            UpdateAxes();
        }
        private void BtnDrawSample_Click(object sender, RoutedEventArgs e)
        {
            int pointCount = 1000;
            var random = new Random();

            _scatterGeometry.Positions.Clear();
            _scatterGeometry.Colors.Clear();

            for (int i = 0; i < pointCount; i++)
            {
                float x = (float)(random.NextDouble() * 10 - 5);
                float y = (float)(random.NextDouble() * 10 - 5);
                float z = (float)(random.NextDouble() * 10 - 5);

                _scatterGeometry.Positions.Add(new Vector3(x, y, z));
                _scatterGeometry.Colors.Add(
                    new Color4(
                        (float)random.NextDouble(),
                        (float)random.NextDouble(),
                        (float)random.NextDouble(),
                        1f));
            }

            _scatterGeometry.UpdateBounds();
            ScatterModel.Size = new Size(5, 5);

            Viewport.ZoomExtents();

            UpdateAxes();
            UpdateProjections();
        }

        private void InitializeDefaults()
        {
            _components.Add(new ComponentParam
            {
                Weight = 0.5,
                MeanX = -1,
                MeanY = 0,
                MeanZ = 0,
                C11 = 0.4,
                C22 = 0.2,
                C33 = 0.6,
                C12 = 0.0,
                C13 = 0.0,
                C23 = 0.0,
                SampleCount = 500,
                Color = Colors.SteelBlue
            });

            _components.Add(new ComponentParam
            {
                Weight = 0.5,
                MeanX = 1.5,
                MeanY = 0.5,
                MeanZ = -0.5,
                C11 = 0.3,
                C22 = 0.5,
                C33 = 0.3,
                C12 = 0.1,
                C13 = 0.0,
                C23 = -0.05,
                SampleCount = 500,
                Color = Colors.IndianRed
            });
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var colors = new[] { Colors.SteelBlue, Colors.IndianRed, Colors.SeaGreen, Colors.DarkOrange, Colors.MediumPurple, Colors.Teal };
            var color = colors[_components.Count % colors.Length];
            _components.Add(new ComponentParam { Color = color });
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (GridComponents.SelectedItem is ComponentParam sel)
            {
                _components.Remove(sel);
            }
            else if (_components.Count > 0)
            {
                _components.RemoveAt(_components.Count - 1);
            }
        }

        private void BtnRandomizeSeed_Click(object sender, RoutedEventArgs e)
        {
            int seed = Guid.NewGuid().GetHashCode();
            TxtSeed.Text = seed.ToString();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearPoints();
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true);
                if (!float.TryParse(TxtPointSize.Text, out var pointSize) || pointSize <= 0)
                {
                    MessageBox.Show("Point Size は正の数で入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!int.TryParse(TxtSeed.Text, out var seed))
                {
                    MessageBox.Show("Seed は整数で入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var rng = new Random(seed);
                ClearPoints();

                var comps = _components.Where(c => c.Weight > 0).ToList();
                if (comps.Count == 0)
                {
                    MessageBox.Show("Weight>0 の成分がありません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool allZeroCounts = comps.All(c => c.SampleCount <= 0);
                if (allZeroCounts)
                {
                    if (!int.TryParse(TxtTotalSamples.Text, out int total) || total <= 0)
                    {
                        MessageBox.Show("Total Samples が無効です（正の整数）。サンプル数を各成分に直接指定するか、Total Samples を設定してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    double wsum = comps.Sum(c => c.Weight);
                    if (wsum <= 0)
                    {
                        MessageBox.Show("重みの合計が0です。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    int assigned = 0;
                    for (int i = 0; i < comps.Count; i++)
                    {
                        int count = (i == comps.Count - 1) ? (total - assigned) : (int)Math.Round(total * (comps[i].Weight / wsum));
                        comps[i].SampleCount = Math.Max(0, count);
                        assigned += comps[i].SampleCount;
                    }
                }

                var positive = comps.Where(c => c.SampleCount > 0).ToList();
                if (positive.Count == 0)
                {
                    MessageBox.Show("サンプル数が0です。各成分の #Samples を設定するか、Total Samples と Weight を設定してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var results = await System.Threading.Tasks.Task.Run(() =>
                {
                    var positions = new Vector3Collection();
                    var colors = new Color4Collection();
                    var localRng = new Random(rng.Next());

                    foreach (var comp in positive)
                    {
                        int n = comp.SampleCount;
                        if (n <= 0) continue;
                        double[,] S =
                        {
                            { comp.C11, comp.C12, comp.C13 },
                            { comp.C12, comp.C22, comp.C23 },
                            { comp.C13, comp.C23, comp.C33 },
                        };
                        if (!TryCholesky3x3(S, out var L, out string err))
                        {
                            throw new InvalidOperationException($"共分散が正定値ではありません: {err}");
                        }

                        double l00 = L[0, 0], l01 = L[0, 1], l02 = L[0, 2];
                        double l10 = L[1, 0], l11 = L[1, 1], l12 = L[1, 2];
                        double l20 = L[2, 0], l21 = L[2, 1], l22 = L[2, 2];
                        var color = new Color4(comp.Color.ScR, comp.Color.ScG, comp.Color.ScB, comp.Color.ScA);

                        for (int i = 0; i < n; i++)
                        {
                            double z0 = NextStandardNormal(localRng);
                            double z1 = NextStandardNormal(localRng);
                            double z2 = NextStandardNormal(localRng);
                            float x = (float)(comp.MeanX + l00 * z0 + l01 * z1 + l02 * z2);
                            float y = (float)(comp.MeanY + l10 * z0 + l11 * z1 + l12 * z2);
                            float zc = (float)(comp.MeanZ + l20 * z0 + l21 * z1 + l22 * z2);
                            positions.Add(new Vector3(x, y, zc));
                            colors.Add(color);
                        }
                    }
                    return (positions, colors);
                });

                var geometry = new PointGeometry3D
                {
                    Positions = results.positions,
                    Colors = results.colors
                };

                ScatterModel.Geometry = geometry;
                ScatterModel.Size = new Size(pointSize, pointSize);

                Viewport.ZoomExtents();

                UpdateAxes();
                UpdateProjections();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラー: {ex.Message}", "例外", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ClearPoints()
        {
            _scatterGeometry.Positions.Clear();
            _scatterGeometry.Colors.Clear();
            _scatterGeometry.UpdateBounds();

            // 軸と2D投影を更新/クリア
            UpdateAxes();
            ClearProjections();
        }

        private void BtnGenVol_Click(object sender, RoutedEventArgs e) { /* Not implemented */ }
        private void BtnClearVol_Click(object sender, RoutedEventArgs e) { /* Not implemented */ }
        private void SliderX_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { /* Not implemented */ }
        private void SliderY_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { /* Not implemented */ }
        private void SliderZ_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { /* Not implemented */ }

        private static bool TryCholesky3x3(double[,] S, out double[,] L, out string error)
        {
            L = new double[3, 3];
            error = string.Empty;
            try
            {
                double l11 = Math.Sqrt(S[0, 0]);
                if (double.IsNaN(l11) || l11 <= 0) { error = "C11<=0"; return false; }
                double l21 = S[1, 0] / l11;
                double l31 = S[2, 0] / l11;
                double s22p = S[1, 1] - l21 * l21;
                if (s22p <= 0) { error = "leading minor not positive (2x2)"; return false; }
                double l22 = Math.Sqrt(s22p);
                double l32 = (S[2, 1] - l31 * l21) / l22;
                double s33p = S[2, 2] - l31 * l31 - l32 * l32;
                if (s33p <= 0) { error = "leading minor not positive (3x3)"; return false; }
                double l33 = Math.Sqrt(s33p);
                L[0, 0] = l11; L[0, 1] = 0; L[0, 2] = 0;
                L[1, 0] = l21; L[1, 1] = l22; L[1, 2] = 0;
                L[2, 0] = l31; L[2, 1] = l32; L[2, 2] = l33;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        [ThreadStatic] private static bool _hasSpare;
        [ThreadStatic] private static double _spare;

        private static double NextStandardNormal(Random r)
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }
            double u, v, s;
            do
            {
                u = 2.0 * r.NextDouble() - 1.0;
                v = 2.0 * r.NextDouble() - 1.0;
                s = u * u + v * v;
            } while (s <= double.Epsilon || s >= 1.0);

            double m = Math.Sqrt(-2.0 * Math.Log(s) / s);
            _spare = v * m;
            _hasSpare = true;
            return u * m;
        }

        private void SetBusy(bool busy)
        {
            BtnGenerate.IsEnabled = !busy;
            BtnClear.IsEnabled = !busy;
            BtnAdd.IsEnabled = !busy;
            BtnRemove.IsEnabled = !busy;
            GridComponents.IsEnabled = !busy;
        }

        // ==== 追加: 軸描画と2D投影 ====

        private void UpdateAxes()
        {
            // 現在の幾何からスケールを算出し、原点中心の対称軸を描く
            float L = 5f;
            var geom = ScatterModel.Geometry as PointGeometry3D ?? _scatterGeometry;
            if (geom?.Positions != null && geom.Positions.Count > 0)
            {
                float minX = geom.Positions.Min(p => p.X);
                float maxX = geom.Positions.Max(p => p.X);
                float minY = geom.Positions.Min(p => p.Y);
                float maxY = geom.Positions.Max(p => p.Y);
                float minZ = geom.Positions.Min(p => p.Z);
                float maxZ = geom.Positions.Max(p => p.Z);
                L = Math.Max(Math.Max(Math.Abs(minX), Math.Abs(maxX)),
                    Math.Max(Math.Max(Math.Abs(minY), Math.Abs(maxY)), Math.Max(Math.Abs(minZ), Math.Abs(maxZ))));
                if (L <= 0) L = 5f;
                L *= 1.1f; // 少し余白
            }

            AxisXModel.Geometry = BuildLine(new Vector3(-L, 0, 0), new Vector3(L, 0, 0));
            AxisYModel.Geometry = BuildLine(new Vector3(0, -L, 0), new Vector3(0, L, 0));
            AxisZModel.Geometry = BuildLine(new Vector3(0, 0, -L), new Vector3(0, 0, L));
        }

        private static LineGeometry3D BuildLine(Vector3 p1, Vector3 p2)
        {
            var lb = new LineBuilder();
            lb.AddLine(p1, p2);
            return lb.ToLineGeometry3D();
        }

        private void UpdateProjections()
        {
            var geom = ScatterModel.Geometry as PointGeometry3D ?? _scatterGeometry;
            if (geom == null || geom.Positions == null || geom.Positions.Count == 0)
            {
                ClearProjections();
                return;
            }

            var positions = geom.Positions;
            var colors = geom.Colors;

            // 投影サイズ（XAMLのBorderに合わせる）
            const int w = 210;
            const int h = 210;

            RenderProjection(positions, colors, 'X', 'Y', ImgXY, w, h);
            RenderProjection(positions, colors, 'X', 'Z', ImgXZ, w, h);
            RenderProjection(positions, colors, 'Y', 'Z', ImgYZ, w, h);
        }

        private void ClearProjections()
        {
            if (ImgXY != null) ImgXY.Source = null;
            if (ImgXZ != null) ImgXZ.Source = null;
            if (ImgYZ != null) ImgYZ.Source = null;
        }

        private void RenderProjection(Vector3Collection positions, Color4Collection colors, char axisU, char axisV, System.Windows.Controls.Image target, int pixelW, int pixelH)
        {
            if (target == null) return;
            if (positions == null || positions.Count == 0)
            {
                target.Source = null;
                return;
            }

            double GetComp(Vector3 v, char axis)
            {
                switch (axis)
                {
                    case 'X': return v.X;
                    case 'Y': return v.Y;
                    case 'Z': return v.Z;
                    default: return 0.0;
                }
            }

            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            for (int i = 0; i < positions.Count; i++)
            {
                var p = positions[i];
                double u = GetComp(p, axisU);
                double v = GetComp(p, axisV);
                if (u < minU) minU = u; if (u > maxU) maxU = u;
                if (v < minV) minV = v; if (v > maxV) maxV = v;
            }
            if (!(maxU > minU) || !(maxV > minV))
            {
                // 退避: 範囲がゼロに近い場合
                double centerU = (double)GetComp(positions[0], axisU);
                double centerV = (double)GetComp(positions[0], axisV);
                double delta = 1.0;
                minU = centerU - delta; maxU = centerU + delta;
                minV = centerV - delta; maxV = centerV + delta;
            }

            const double pad = 8.0;
            double scaleU = (pixelW - 2 * pad) / (maxU - minU);
            double scaleV = (pixelH - 2 * pad) / (maxV - minV);
            double scale = Math.Min(scaleU, scaleV);
            double offsetX = pad + (pixelW - 2 * pad - (maxU - minU) * scale) * 0.5;
            double offsetY = pad + (pixelH - 2 * pad - (maxV - minV) * scale) * 0.5;

            // 2D点サイズ（3DのSizeから適当にスケール）
            double r = Math.Max(1.0, ScatterModel.Size.Width * 0.6);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 背景
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixelW, pixelH));

                // ポイント描画
                var brushCache = new Dictionary<System.Windows.Media.Color, SolidColorBrush>();
                for (int i = 0; i < positions.Count; i++)
                {
                    var p = positions[i];
                    double u = GetComp(p, axisU);
                    double v = GetComp(p, axisV);
                    double x = (u - minU) * scale + offsetX;
                    double y = pixelH - ((v - minV) * scale + offsetY); // y上向きを上に

                    System.Windows.Media.Color col;
                    if (colors != null && colors.Count == positions.Count)
                    {
                        var c4 = colors[i];
                        col = System.Windows.Media.Color.FromScRgb(c4.Alpha, c4.Red, c4.Green, c4.Blue);
                    }
                    else
                    {
                        col = System.Windows.Media.Colors.DodgerBlue;
                    }

                    if (!brushCache.TryGetValue(col, out var brush))
                    {
                        brush = new SolidColorBrush(col);
                        brush.Freeze();
                        brushCache[col] = brush;
                    }

                    // 小さな矩形で高速描画
                    dc.DrawRectangle(brush, null, new Rect(x - r * 0.5, y - r * 0.5, Math.Max(1.0, r), Math.Max(1.0, r)));
                }
            }
            var rtb = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            target.Source = rtb;
        }

        private void BtnRandomCov_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 可能ならSeedを利用、なければ時間ベース
                int seed;
                var rng = int.TryParse(TxtSeed.Text, out seed) ? new Random(seed) : new Random(Environment.TickCount);

                // 偏りを強めるための固有値レンジ（大きく異なるスケールを持つ）
                // 例: [0.05, 0.3, 1.5] をベースにランダム比
                foreach (var c in _components)
                {
                    var S = GenerateRandomSPD3(rng,
                        minEigen: 0.02,   // 最小固有値（大きくしすぎると丸くなる）
                        maxEigen: 2.5,    // 最大固有値（大きいほど細長くなる）
                        anisotropyBias: 0.6); // 異方性を促進するバイアス

                    c.C11 = S[0, 0];
                    c.C12 = S[0, 1];
                    c.C13 = S[0, 2];
                    c.C22 = S[1, 1];
                    c.C23 = S[1, 2];
                    c.C33 = S[2, 2];
                }

                // UIを反映
                GridComponents.Items.Refresh();

                MessageBox.Show("各成分の共分散をランダムに更新しました（正定値・異方性あり）。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"共分散生成でエラー: {ex.Message}", "例外", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ランダムな正定値3x3行列を生成（強い偏りをもたせる）
        private static double[,] GenerateRandomSPD3(Random rng, double minEigen, double maxEigen, double anisotropyBias)
        {
            // 1) ランダム直交行列（回転）Qを生成：ランダムベクトルからGram-Schmidt
            var a = RandomVector(rng);
            var b = RandomVector(rng);
            var c = Cross(a, b);

            // 正規直交化
            Normalize(ref a);
            // bからa成分を除去して正規化
            var bProj = Sub(b, Scale(a, Dot(a, b)));
            Normalize(ref bProj);
            // cを再計算して直交基底に
            c = Cross(a, bProj);
            Normalize(ref c);
            var Q = new double[,]
            {
                { a.X, bProj.X, c.X },
                { a.Y, bProj.Y, c.Y },
                { a.Z, bProj.Z, c.Z }
            };

            // 2) 固有値をランダム生成（異方性強調）
            // 大小の差が出やすいように指数的レンジを使用
            double e1 = SampleEigen(rng, minEigen, maxEigen, anisotropyBias);
            double e2 = SampleEigen(rng, minEigen, maxEigen, anisotropyBias);
            double e3 = SampleEigen(rng, minEigen, maxEigen, anisotropyBias);
            // 小さい順に並べ替え（安定化)
            var evals = new[] { e1, e2, e3 }.OrderBy(x => x).ToArray();
            var D = new double[,] { { evals[0], 0, 0 }, { 0, evals[1], 0 }, { 0, 0, evals[2] } };

            // 3) S = Q D Q^T
            var S = Multiply(Multiply(Q, D), Transpose(Q));

            // 対称化（数値誤差対策）
            S[0, 1] = S[1, 0] = 0.5 * (S[0, 1] + S[1, 0]);
            S[0, 2] = S[2, 0] = 0.5 * (S[0, 2] + S[2, 0]);
            S[1, 2] = S[2, 1] = 0.5 * (S[1, 2] + S[2, 1]);

            return S;
        }

        private static double SampleEigen(Random rng, double minEigen, double maxEigen, double bias)
        {
            // [0,1) からバイアス付き乱数。bias>0で小さい値または大きい値に寄せる。
            // ここでは広がりを大きくするため、二峰性にする。
            double u = rng.NextDouble();
            double t;
            if (rng.NextDouble() < 0.5)
            {
                // 小さい固有値側に寄せる
                t = Math.Pow(u, 1.0 + bias * 2.0);
            }
            else
            {
                // 大きい固有値側に寄せる
                t = 1.0 - Math.Pow(1.0 - u, 1.0 + bias * 2.0);
            }
            return minEigen + (maxEigen - minEigen) * t;
        }

        // 3Dベクトル・行列ユーティリティ（double）
        private struct V3
        {
            public double X, Y, Z;
            public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        }
        private static V3 RandomVector(Random rng)
        {
            return new V3(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
        }
        private static void Normalize(ref V3 v)
        {
            double n = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (n <= 1e-12)
            {
                v = new V3(1, 0, 0);
                return;
            }
            v = new V3(v.X / n, v.Y / n, v.Z / n);
        }
        private static V3 Cross(V3 a, V3 b)
        {
            return new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }
        private static double Dot(V3 a, V3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }
        private static V3 Scale(V3 a, double s) => new V3(a.X * s, a.Y * s, a.Z * s);
        private static V3 Sub(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        private static double[,] Multiply(double[,] A, double[,] B)
        {
            var r = new double[3, 3];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                        sum += A[i, k] * B[k, j];
                    r[i, j] = sum;
                }
            }
            return r;
        }
        private static double[,] Transpose(double[,] A)
        {
            return new double[,]
            {
                { A[0,0], A[1,0], A[2,0] },
                { A[0,1], A[1,1], A[2,1] },
                { A[0,2], A[1,2], A[2,2] },
            };
        }
    }
}
