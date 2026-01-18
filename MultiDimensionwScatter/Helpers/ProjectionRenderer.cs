using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MultiDimensionwScatter.Helpers
{
    public static class ProjectionRenderer
    {
        public static void RenderProjection(PointGeometry3D geom, char axisU, char axisV, System.Windows.Controls.Image target, int pixelW, int pixelH, Size pointSize)
        {
            if (target == null)
            {
                return;
            }
            var positions = geom?.Positions;
            var colors = geom?.Colors;
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

            double r = Math.Max(1.0, pointSize.Width * 0.6);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixelW, pixelH));
                var brushCache = new Dictionary<System.Windows.Media.Color, SolidColorBrush>();
                for (int i = 0; i < positions.Count; i++)
                {
                    var p = positions[i];
                    double u = GetComp(p, axisU);
                    double v = GetComp(p, axisV);
                    double x = (u - minU) * scale + offsetX;
                    double y = pixelH - ((v - minV) * scale + offsetY);

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
                    dc.DrawRectangle(brush, null, new Rect(x - r * 0.5, y - r * 0.5, Math.Max(1.0, r), Math.Max(1.0, r)));
                }
            }
            var rtb = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            target.Source = rtb;
        }
    }
}
