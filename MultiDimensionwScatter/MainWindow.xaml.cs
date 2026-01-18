using HelixToolkit.Wpf.SharpDX;
using MultiDimensionwScatter.Models;
using SharpDX;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

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
    }
}
