using System;
using System.Linq;

namespace MultiDimensionwScatter.Helpers
{
    public static class CovarianceGenerator
    {
        public static double[,] GenerateRandomSPD3(Random rng, double minEigen, double maxEigen, double anisotropyBias)
        {
            var a = RandomVector(rng);
            var b = RandomVector(rng);
            var c = Cross(a, b);

            Normalize(ref a);
            var bProj = Sub(b, Scale(a, Dot(a, b)));
            Normalize(ref bProj);
            c = Cross(a, bProj);
            Normalize(ref c);

            var Q = new double[,]
            {
                { a.X, bProj.X, c.X },
                { a.Y, bProj.Y, c.Y },
                { a.Z, bProj.Z, c.Z }
            };

            double e1 = SampleEigen(rng, minEigen, maxEigen, anisotropyBias);
            double e2 = SampleEigen(rng, minEigen, maxEigen, anisotropyBias);
            double e3 = SampleEigen(rng, minEigen, maxEigen, anisotropyBias);
            var evals = new[] { e1, e2, e3 }.OrderBy(x => x).ToArray();
            var D = new double[,] { { evals[0], 0, 0 }, { 0, evals[1], 0 }, { 0, 0, evals[2] } };

            var S = Multiply(Multiply(Q, D), Transpose(Q));

            S[0, 1] = S[1, 0] = 0.5 * (S[0, 1] + S[1, 0]);
            S[0, 2] = S[2, 0] = 0.5 * (S[0, 2] + S[2, 0]);
            S[1, 2] = S[2, 1] = 0.5 * (S[1, 2] + S[2, 1]);

            return S;
        }

        private struct V3 { public double X, Y, Z; public V3(double x, double y, double z) { X = x; Y = y; Z = z; } }
        private static V3 RandomVector(Random rng)
        {
            return new V3(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
        }
        private static void Normalize(ref V3 v)
        {
            double n = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (n <= 1e-12) { v = new V3(1, 0, 0); return; }
            v = new V3(v.X / n, v.Y / n, v.Z / n);
        }
        private static V3 Cross(V3 a, V3 b)
        {
            return new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }
        private static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        private static V3 Scale(V3 a, double s) => new V3(a.X * s, a.Y * s, a.Z * s);
        private static V3 Sub(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        private static double[,] Multiply(double[,] A, double[,] B)
        {
            var r = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++) sum += A[i, k] * B[k, j];
                    r[i, j] = sum;
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

        private static double SampleEigen(Random rng, double minEigen, double maxEigen, double bias)
        {
            double u = rng.NextDouble();
            double t = rng.NextDouble() < 0.5
                ? Math.Pow(u, 1.0 + bias * 2.0)
                : 1.0 - Math.Pow(1.0 - u, 1.0 + bias * 2.0);
            return minEigen + (maxEigen - minEigen) * t;
        }
    }
}
