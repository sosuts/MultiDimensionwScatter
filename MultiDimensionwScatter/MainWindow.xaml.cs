using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using HelixToolkit.Wpf;
using MultiDimensionwScatter.Models;

namespace MultiDimensionwScatter
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ComponentParam> _components = new ObservableCollection<ComponentParam>();
        private readonly Random _rand = new Random(12345);

        public MainWindow()
        {
            InitializeComponent();
            GridComponents.ItemsSource = _components;
            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            // Two default components
            _components.Add(new ComponentParam
            {
                Weight = 0.5,
                MeanX = -1, MeanY = 0, MeanZ = 0,
                C11 = 0.4, C22 = 0.2, C33 = 0.6,
                C12 = 0.0, C13 = 0.0, C23 = 0.0,
                SampleCount = 500,
                Color = Colors.SteelBlue
            });

            _components.Add(new ComponentParam
            {
                Weight = 0.5,
                MeanX = 1.5, MeanY = 0.5, MeanZ = -0.5,
                C11 = 0.3, C22 = 0.5, C33 = 0.3,
                C12 = 0.1, C13 = 0.0, C23 = -0.05,
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

        private ModelVisual3D _pointsRoot;
    private ModelVisual3D _volumeRoot;
    private GeometryModel3D _sliceXModel, _sliceYModel, _sliceZModel;
    private TranslateTransform3D _txX, _txY, _txZ;
    private WriteableBitmap _bmpX, _bmpY, _bmpZ;
    private int _res = 64;
    private double[] _grid; // length = res^3, order [x + y*res + z*res*res]
    private double _minX, _maxX, _minY, _maxY, _minZ, _maxZ;

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true);
                if (!double.TryParse(TxtPointSize.Text, out var pointSize) || pointSize <= 0)
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

                // Decide sample counts: if all SampleCount <= 0 and TotalSamples > 0, distribute by weights.
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

                // Heavy sampling off the UI thread
                var results = await System.Threading.Tasks.Task.Run(() =>
                {
                    var list = new List<(ComponentParam comp, Point3D[] pts)>();
                    // Use thread-local Randoms per component
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
                        var localRng = new Random(rng.Next());
                        var pts = new Point3D[n];
                        // ローカルに展開してインデックス計算を最小化
                        double l00 = L[0,0], l01 = L[0,1], l02 = L[0,2];
                        double l10 = L[1,0], l11 = L[1,1], l12 = L[1,2];
                        double l20 = L[2,0], l21 = L[2,1], l22 = L[2,2];
                        for (int i = 0; i < n; i++)
                        {
                            // Marsaglia polar 法（三角関数なし）
                            double z0 = NextStandardNormal(localRng);
                            double z1 = NextStandardNormal(localRng);
                            double z2 = NextStandardNormal(localRng);
                            double x = comp.MeanX + l00*z0 + l01*z1 + l02*z2;
                            double y = comp.MeanY + l10*z0 + l11*z1 + l12*z2;
                            double zc = comp.MeanZ + l20*z0 + l21*z1 + l22*z2;
                            pts[i] = new Point3D(x, y, zc);
                        }
                        list.Add((comp, pts));
                    }
                    return list;
                });

                // Create visuals on UI thread, freeze collections; add under a single root for faster visual tree updates
                if (_pointsRoot != null)
                {
                    Viewport.Children.Remove(_pointsRoot);
                    _pointsRoot = null;
                }
                _pointsRoot = new ModelVisual3D();
                foreach (var (comp, ptsArray) in results)
                {
                    var pts = new Point3DCollection(ptsArray);
                    if (pts.CanFreeze) pts.Freeze();
                    var pv = new PointsVisual3D
                    {
                        Points = pts,
                        Color = comp.Color,
                        Size = pointSize
                    };
                    _pointsRoot.Children.Add(pv);
                }
                Viewport.Children.Add(_pointsRoot);

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
            // Remove previous root node if exists (faster than removing many children individually)
            if (_pointsRoot != null)
            {
                Viewport.Children.Remove(_pointsRoot);
                _pointsRoot = null;
            }
        }

        // ===== Volume (Orthogonal Slices) =====
        private async void BtnGenVol_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true);
                if (!int.TryParse(TxtVolRes.Text, out var res) || res < 16 || res > 256)
                {
                    MessageBox.Show("Resolution は 16〜256 の整数で指定してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!double.TryParse(TxtSigmaK.Text, out var sigmaK) || sigmaK <= 0)
                {
                    MessageBox.Show("Bounds ±σ は正の数で指定してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var comps = _components.Where(c => c.Weight > 0).ToList();
                if (comps.Count == 0)
                {
                    MessageBox.Show("Weight>0 の成分がありません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _res = res;
                // Compute bounds from means ± k*sqrt(diag(C))
                ComputeBounds(comps, sigmaK);

                // Compute density grid off UI thread
                var grid = await System.Threading.Tasks.Task.Run(() => EvaluateDensityGrid(comps));
                _grid = grid;

                // Initialize bitmaps and slice models
                InitVolumeVisuals();

                // Set sliders range
                SliderX.Maximum = _res - 1;
                SliderY.Maximum = _res - 1;
                SliderZ.Maximum = _res - 1;
                SliderX.Value = _res / 2; SliderY.Value = _res / 2; SliderZ.Value = _res / 2;

                // Show volume root
                if (_volumeRoot != null) Viewport.Children.Remove(_volumeRoot);
                _volumeRoot = new ModelVisual3D();
                var group = new Model3DGroup();
                group.Children.Add(_sliceXModel);
                group.Children.Add(_sliceYModel);
                group.Children.Add(_sliceZModel);
                _volumeRoot.Content = group;
                Viewport.Children.Add(_volumeRoot);
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

        private void BtnClearVol_Click(object sender, RoutedEventArgs e)
        {
            if (_volumeRoot != null)
            {
                Viewport.Children.Remove(_volumeRoot);
                _volumeRoot = null;
            }
            _bmpX = _bmpY = _bmpZ = null;
            _sliceXModel = _sliceYModel = _sliceZModel = null;
            _grid = null;
        }

        private void SliderX_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_grid == null || _sliceXModel == null) return;
            int xi = Math.Max(0, Math.Min(_res - 1, (int)Math.Round(SliderX.Value)));
            UpdateSliceX(xi);
        }
        private void SliderY_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_grid == null || _sliceYModel == null) return;
            int yi = Math.Max(0, Math.Min(_res - 1, (int)Math.Round(SliderY.Value)));
            UpdateSliceY(yi);
        }
        private void SliderZ_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_grid == null || _sliceZModel == null) return;
            int zi = Math.Max(0, Math.Min(_res - 1, (int)Math.Round(SliderZ.Value)));
            UpdateSliceZ(zi);
        }

        private void ComputeBounds(List<ComponentParam> comps, double k)
        {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (var c in comps)
            {
                double sx = Math.Sqrt(Math.Max(1e-9, c.C11));
                double sy = Math.Sqrt(Math.Max(1e-9, c.C22));
                double sz = Math.Sqrt(Math.Max(1e-9, c.C33));
                minX = Math.Min(minX, c.MeanX - k * sx);
                maxX = Math.Max(maxX, c.MeanX + k * sx);
                minY = Math.Min(minY, c.MeanY - k * sy);
                maxY = Math.Max(maxY, c.MeanY + k * sy);
                minZ = Math.Min(minZ, c.MeanZ - k * sz);
                maxZ = Math.Max(maxZ, c.MeanZ + k * sz);
            }
            if (!(maxX > minX)) { minX = -1; maxX = 1; }
            if (!(maxY > minY)) { minY = -1; maxY = 1; }
            if (!(maxZ > minZ)) { minZ = -1; maxZ = 1; }
            _minX = minX; _maxX = maxX;
            _minY = minY; _maxY = maxY;
            _minZ = minZ; _maxZ = maxZ;
        }

        private double[] EvaluateDensityGrid(List<ComponentParam> comps)
        {
            int n = _res; int n2 = n * n; int total = n * n * n;
            var grid = new double[total];

            // Precompute per-component inverse covariance and coefficient
            var items = new List<(double[] mu, double[,] invS, double coeff)>();
            double cst = 1.0 / Math.Pow(2.0 * Math.PI, 1.5);
            foreach (var c in comps)
            {
                double[,] S = { { c.C11, c.C12, c.C13 }, { c.C12, c.C22, c.C23 }, { c.C13, c.C23, c.C33 } };
                if (!TryCholesky3x3(S, out var L, out _)) continue;
                // det = (prod diag L)^2, sqrt(det) = prod diag L
                double sqrtDet = L[0,0] * L[1,1] * L[2,2];
                // invS via Cholesky inverse: solve per basis vector
                double[,] invS = InvertSymmetricFromCholesky(L);
                double coeff = c.Weight * cst / sqrtDet;
                items.Add((new[] { c.MeanX, c.MeanY, c.MeanZ }, invS, coeff));
            }

            double dx = (_maxX - _minX) / (n - 1);
            double dy = (_maxY - _minY) / (n - 1);
            double dz = (_maxZ - _minZ) / (n - 1);

            double maxv = 0.0;
            for (int z = 0; z < n; z++)
            {
                double zc = _minZ + z * dz;
                int zoff = z * n2;
                for (int y = 0; y < n; y++)
                {
                    double yc = _minY + y * dy;
                    int yoff = zoff + y * n;
                    for (int x = 0; x < n; x++)
                    {
                        double xc = _minX + x * dx;
                        double val = 0.0;
                        foreach (var (mu, invS, coeff) in items)
                        {
                            double dx0 = xc - mu[0];
                            double dx1 = yc - mu[1];
                            double dx2 = zc - mu[2];
                            // q = d^T invS d
                            double t0 = invS[0,0] * dx0 + invS[0,1] * dx1 + invS[0,2] * dx2;
                            double t1 = invS[1,0] * dx0 + invS[1,1] * dx1 + invS[1,2] * dx2;
                            double t2 = invS[2,0] * dx0 + invS[2,1] * dx1 + invS[2,2] * dx2;
                            double q = dx0 * t0 + dx1 * t1 + dx2 * t2;
                            val += coeff * Math.Exp(-0.5 * q);
                        }
                        grid[yoff + x] = val;
                        if (val > maxv) maxv = val;
                    }
                }
            }

            // normalize to [0,1]
            if (maxv > 0)
            {
                for (int i = 0; i < grid.Length; i++) grid[i] /= maxv;
            }
            return grid;
        }

        private static double[,] InvertSymmetricFromCholesky(double[,] L)
        {
            // Invert S = L L^T; return symmetric inverse S^{-1}
            // Solve for columns of inv via forward/back substitution
            double[,] inv = new double[3,3];
            for (int j = 0; j < 3; j++)
            {
                // solve L y = e_j
                double y0 = (j == 0) ? 1.0 / L[0,0] : 0.0;
                double y1 = (j == 1) ? 1.0 / L[1,1] : -L[1,0] * y0 / L[1,1];
                double y2 = (j == 2) ? 1.0 / L[2,2] : 0.0; // compute sequentially below
                if (j == 0)
                {
                    y1 = -L[1,0] * y0 / L[1,1];
                    y2 = (-L[2,0] * y0 - L[2,1] * y1) / L[2,2];
                }
                else if (j == 1)
                {
                    double t0 = 0.0; // e1 at row0 is 0
                    y0 = t0 / L[0,0];
                    y1 = (1.0 - L[1,0] * y0) / L[1,1];
                    y2 = (-L[2,0] * y0 - L[2,1] * y1) / L[2,2];
                }
                else // j==2
                {
                    double t0 = 0.0; // e2 at row0 is 0
                    y0 = t0 / L[0,0];
                    double t1 = 0.0; // e2 at row1 is 0
                    y1 = (t1 - L[1,0] * y0) / L[1,1];
                    y2 = (1.0 - L[2,0] * y0 - L[2,1] * y1) / L[2,2];
                }

                // solve L^T x = y
                double x2 = y2 / L[2,2];
                double x1 = (y1 - L[2,1] * x2) / L[1,1];
                double x0 = (y0 - L[1,0] * x1 - L[2,0] * x2) / L[0,0];

                inv[0,j] = x0; inv[1,j] = x1; inv[2,j] = x2;
            }
            // symmetrize
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                {
                    double v = 0.5 * (inv[i,j] + inv[j,i]);
                    inv[i,j] = inv[j,i] = v;
                }
            return inv;
        }

        private void InitVolumeVisuals()
        {
            int n = _res;
            // Create bitmaps
            _bmpX = new WriteableBitmap(n, n, 96, 96, PixelFormats.Bgra32, null);
            _bmpY = new WriteableBitmap(n, n, 96, 96, PixelFormats.Bgra32, null);
            _bmpZ = new WriteableBitmap(n, n, 96, 96, PixelFormats.Bgra32, null);

            // Create slice models (planes) with ImageBrush materials
            _sliceXModel = CreateSliceModel(axis: 0, _bmpX, out _txX);
            _sliceYModel = CreateSliceModel(axis: 1, _bmpY, out _txY);
            _sliceZModel = CreateSliceModel(axis: 2, _bmpZ, out _txZ);

            // Initialize mid slices
            UpdateSliceX(n / 2);
            UpdateSliceY(n / 2);
            UpdateSliceZ(n / 2);
        }

        private GeometryModel3D CreateSliceModel(int axis, WriteableBitmap bmp, out TranslateTransform3D t)
        {
            // axis: 0->X (YZ plane), 1->Y (XZ), 2->Z (XY)
            var mb = new MeshBuilder(false, true);
            Point3D p0, p1, p2, p3;
            if (axis == 0)
            {
                // Plane parallel to YZ; X varies by translation
                double y0 = _minY, y1 = _maxY;
                double z0 = _minZ, z1 = _maxZ;
                double x = _minX; // will translate
                p0 = new Point3D(x, y0, z0);
                p1 = new Point3D(x, y1, z0);
                p2 = new Point3D(x, y1, z1);
                p3 = new Point3D(x, y0, z1);
            }
            else if (axis == 1)
            {
                // Plane parallel to XZ; Y varies by translation
                double x0 = _minX, x1 = _maxX;
                double z0 = _minZ, z1 = _maxZ;
                double y = _minY;
                p0 = new Point3D(x0, y, z0);
                p1 = new Point3D(x1, y, z0);
                p2 = new Point3D(x1, y, z1);
                p3 = new Point3D(x0, y, z1);
            }
            else
            {
                // Plane parallel to XY; Z varies by translation
                double x0 = _minX, x1 = _maxX;
                double y0 = _minY, y1 = _maxY;
                double z = _minZ;
                p0 = new Point3D(x0, y0, z);
                p1 = new Point3D(x1, y0, z);
                p2 = new Point3D(x1, y1, z);
                p3 = new Point3D(x0, y1, z);
            }
            mb.AddQuad(p0, p1, p2, p3);
            var mesh = mb.ToMesh(true);
            mesh.Freeze();

            var brush = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
            var mat = new DiffuseMaterial(brush);
            var gm = new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat };
            t = new TranslateTransform3D();
            gm.Transform = t;
            return gm;
        }

        private void UpdateSliceX(int xi)
        {
            int n = _res; if (_grid == null || _bmpX == null) return;
            // write image from grid at X=xi (YZ plane: width=n along Y, height=n along Z)
            int stride = n * 4;
            var buf = new byte[stride * n];
            for (int z = 0; z < n; z++)
            {
                int zoff = z * n * n;
                int row = z * stride;
                for (int y = 0; y < n; y++)
                {
                    double v = _grid[zoff + y * n + xi];
                    byte c = (byte)(Math.Max(0, Math.Min(1.0, v)) * 255.0);
                    int col = row + y * 4;
                    buf[col + 0] = c; // B
                    buf[col + 1] = c; // G
                    buf[col + 2] = c; // R
                    buf[col + 3] = 255; // A
                }
            }
            _bmpX.WritePixels(new Int32Rect(0, 0, n, n), buf, stride, 0);
            // move plane to world x
            double x = _minX + (double)xi / (n - 1) * (_maxX - _minX);
            _txX.OffsetX = x - _minX; // since geometry at _minX; translate by delta
        }

        private void UpdateSliceY(int yi)
        {
            int n = _res; if (_grid == null || _bmpY == null) return;
            int stride = n * 4;
            var buf = new byte[stride * n];
            for (int z = 0; z < n; z++)
            {
                int zoff = z * n * n;
                int row = z * stride;
                for (int x = 0; x < n; x++)
                {
                    double v = _grid[zoff + yi * n + x];
                    byte c = (byte)(Math.Max(0, Math.Min(1.0, v)) * 255.0);
                    int col = row + x * 4;
                    buf[col + 0] = c; buf[col + 1] = c; buf[col + 2] = c; buf[col + 3] = 255;
                }
            }
            _bmpY.WritePixels(new Int32Rect(0, 0, n, n), buf, stride, 0);
            double y = _minY + (double)yi / (n - 1) * (_maxY - _minY);
            _txY.OffsetY = y - _minY;
        }

        private void UpdateSliceZ(int zi)
        {
            int n = _res; if (_grid == null || _bmpZ == null) return;
            int stride = n * 4;
            var buf = new byte[stride * n];
            int zoff = zi * n * n;
            for (int y = 0; y < n; y++)
            {
                int row = y * stride;
                for (int x = 0; x < n; x++)
                {
                    double v = _grid[zoff + y * n + x];
                    byte c = (byte)(Math.Max(0, Math.Min(1.0, v)) * 255.0);
                    int col = row + x * 4;
                    buf[col + 0] = c; buf[col + 1] = c; buf[col + 2] = c; buf[col + 3] = 255;
                }
            }
            _bmpZ.WritePixels(new Int32Rect(0, 0, n, n), buf, stride, 0);
            double z = _minZ + (double)zi / (n - 1) * (_maxZ - _minZ);
            _txZ.OffsetZ = z - _minZ;
        }

        private static bool TryCholesky3x3(double[,] S, out double[,] L, out string error)
        {
            // Compute lower-triangular L such that S = L*L^T; return false if not SPD
            L = new double[3,3];
            error = string.Empty;
            try
            {
                // S is symmetric by construction
                double l11 = Math.Sqrt(S[0,0]);
                if (double.IsNaN(l11) || l11 <= 0) { error = "C11<=0"; return false; }
                double l21 = S[1,0] / l11;
                double l31 = S[2,0] / l11;

                double s22p = S[1,1] - l21*l21;
                if (s22p <= 0) { error = "leading minor not positive (2x2)"; return false; }
                double l22 = Math.Sqrt(s22p);
                double l32 = (S[2,1] - l31*l21) / l22;

                double s33p = S[2,2] - l31*l31 - l32*l32;
                if (s33p <= 0) { error = "leading minor not positive (3x3)"; return false; }
                double l33 = Math.Sqrt(s33p);

                L[0,0]=l11; L[0,1]=0;   L[0,2]=0;
                L[1,0]=l21; L[1,1]=l22; L[1,2]=0;
                L[2,0]=l31; L[2,1]=l32; L[2,2]=l33;
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
            // Marsaglia polar method (no trig) with cached spare value per thread
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }

            double u, v, s;
            do
            {
                u = 2.0 * r.NextDouble() - 1.0; // (-1,1)
                v = 2.0 * r.NextDouble() - 1.0; // (-1,1)
                s = u * u + v * v;
            } while (s <= double.Epsilon || s >= 1.0);

            double m = Math.Sqrt(-2.0 * Math.Log(s) / s);
            _spare = v * m;
            _hasSpare = true;
            return u * m;
        }

        private void SetBusy(bool busy)
        {
            if (BtnGenerate != null) BtnGenerate.IsEnabled = !busy;
            if (BtnClear != null) BtnClear.IsEnabled = !busy;
            if (BtnAdd != null) BtnAdd.IsEnabled = !busy;
            if (BtnRemove != null) BtnRemove.IsEnabled = !busy;
            if (GridComponents != null) GridComponents.IsEnabled = !busy;
        }
    }
}
