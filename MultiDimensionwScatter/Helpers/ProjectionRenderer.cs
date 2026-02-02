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
        public static void RenderProjection(PointGeometry3D geom, char axisU, char axisV, System.Windows.Controls.Image target, int pixelW, int pixelH, Size pointSize, Color4? fallbackColor)
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

            // Build a pixel-to-color map using majority voting for overlapping particles
            var pixelMap = new Dictionary<(int, int), Dictionary<System.Windows.Media.Color, int>>();

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
                    byte a = (byte)Math.Round(Math.Max(0, Math.Min(1, c4.Alpha)) * 255.0);
                    byte r8 = (byte)Math.Round(Math.Max(0, Math.Min(1, c4.Red)) * 255.0);
                    byte g8 = (byte)Math.Round(Math.Max(0, Math.Min(1, c4.Green)) * 255.0);
                    byte b8 = (byte)Math.Round(Math.Max(0, Math.Min(1, c4.Blue)) * 255.0);
                    col = System.Windows.Media.Color.FromArgb(a, r8, g8, b8);
                }
                else if (fallbackColor.HasValue)
                {
                    var fc = fallbackColor.Value;
                    byte a = (byte)Math.Round(Math.Max(0, Math.Min(1, fc.Alpha)) * 255.0);
                    byte r8 = (byte)Math.Round(Math.Max(0, Math.Min(1, fc.Red)) * 255.0);
                    byte g8 = (byte)Math.Round(Math.Max(0, Math.Min(1, fc.Green)) * 255.0);
                    byte b8 = (byte)Math.Round(Math.Max(0, Math.Min(1, fc.Blue)) * 255.0);
                    col = System.Windows.Media.Color.FromArgb(a, r8, g8, b8);
                }
                else
                {
                    col = System.Windows.Media.Colors.DodgerBlue;
                }

                // Determine the pixel region this point covers
                int minPx = (int)Math.Floor(x - r * 0.5);
                int maxPx = (int)Math.Ceiling(x + r * 0.5);
                int minPy = (int)Math.Floor(y - r * 0.5);
                int maxPy = (int)Math.Ceiling(y + r * 0.5);

                for (int px = minPx; px <= maxPx; px++)
                {
                    for (int py = minPy; py <= maxPy; py++)
                    {
                        if (px < 0 || px >= pixelW || py < 0 || py >= pixelH) continue;

                        var key = (px, py);
                        if (!pixelMap.TryGetValue(key, out var colorVotes))
                        {
                            colorVotes = new Dictionary<System.Windows.Media.Color, int>();
                            pixelMap[key] = colorVotes;
                        }

                        if (!colorVotes.ContainsKey(col))
                        {
                            colorVotes[col] = 0;
                        }
                        colorVotes[col]++;
                    }
                }
            }

            // Now render using the majority-voted colors
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixelW, pixelH));
                var brushCache = new Dictionary<System.Windows.Media.Color, SolidColorBrush>();

                foreach (var kvp in pixelMap)
                {
                    var (px, py) = kvp.Key;
                    var colorVotes = kvp.Value;

                    // Find the color with the most votes
                    System.Windows.Media.Color majorityColor = System.Windows.Media.Colors.White;
                    int maxVotes = 0;
                    foreach (var vote in colorVotes)
                    {
                        if (vote.Value > maxVotes)
                        {
                            maxVotes = vote.Value;
                            majorityColor = vote.Key;
                        }
                    }

                    if (!brushCache.TryGetValue(majorityColor, out var brush))
                    {
                        brush = new SolidColorBrush(majorityColor);
                        brush.Freeze();
                        brushCache[majorityColor] = brush;
                    }

                    dc.DrawRectangle(brush, null, new Rect(px, py, 1, 1));
                }
            }
            var rtb = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            target.Source = rtb;
        }
    }
}
