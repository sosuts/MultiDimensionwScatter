using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Linq;
using System.Windows.Media.Media3D;

namespace MultiDimensionwScatter.Helpers
{
    public static class CameraHelper
    {
        public static void GetSceneCenterAndRadius(PointGeometry3D geom, out Vector3 center, out float radius)
        {
            center = new Vector3(0, 0, 0);
            radius = 5f;
            if (geom?.Positions != null && geom.Positions.Count > 0)
            {
                float minX = geom.Positions.Min(p => p.X);
                float maxX = geom.Positions.Max(p => p.X);
                float minY = geom.Positions.Min(p => p.Y);
                float maxY = geom.Positions.Max(p => p.Y);
                float minZ = geom.Positions.Min(p => p.Z);
                float maxZ = geom.Positions.Max(p => p.Z);
                center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
                var dx = maxX - minX;
                var dy = maxY - minY;
                var dz = maxZ - minZ;
                radius = (float)Math.Max(Math.Max(dx, dy), dz) * 0.5f;
            }
        }

        public static void SetCameraToAxisViewOrtho(Viewport3DX viewport, PointGeometry3D geom,
            char axisU, char axisV, Vector3 lookDirUnit, Vector3 upDirUnit)
        {
            GetSceneCenterAndRadius(geom, out var center, out float radius);
            float distance = Math.Max(5f, radius * 2.5f);

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
            if (geom?.Positions != null && geom.Positions.Count > 0)
            {
                for (int i = 0; i < geom.Positions.Count; i++)
                {
                    var p = geom.Positions[i];
                    double u = GetComp(p, axisU);
                    double v = GetComp(p, axisV);
                    if (u < minU) minU = u; if (u > maxU) maxU = u;
                    if (v < minV) minV = v; if (v > maxV) maxV = v;
                }
            }
            else
            {
                minU = -5; maxU = 5;
                minV = -5; maxV = 5;
            }

            if (!(maxU > minU)) { var c = (minU + maxU) * 0.5; minU = c - 1; maxU = c + 1; }
            if (!(maxV > minV)) { var c = (minV + maxV) * 0.5; minV = c - 1; maxV = c + 1; }
            double rangeU = maxU - minU;
            double rangeV = maxV - minV;
            double width = Math.Max(rangeU, rangeV) * 1.05;

            var look = new Vector3D(lookDirUnit.X, lookDirUnit.Y, lookDirUnit.Z);
            var up = new Vector3D(upDirUnit.X, upDirUnit.Y, upDirUnit.Z);
            var c3 = new Point3D(center.X, center.Y, center.Z);
            var pos = new Point3D(c3.X - look.X * distance, c3.Y - look.Y * distance, c3.Z - look.Z * distance);

            var cam = new HelixToolkit.Wpf.SharpDX.OrthographicCamera
            {
                Position = pos,
                LookDirection = new Vector3D(look.X * distance, look.Y * distance, look.Z * distance),
                UpDirection = up,
                Width = width
            };
            viewport.Camera = cam;

            // Ensure immediate update
            viewport.ZoomExtents();
        }
    }
}
