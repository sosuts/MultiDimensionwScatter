using System.Windows.Media;

namespace MultiDimensionwScatter.Models
{
    public class ComponentParam
    {
        public double Weight { get; set; } = 1.0;
        // Mean vector
        public double MeanX { get; set; } = 0.0;
        public double MeanY { get; set; } = 0.0;
        public double MeanZ { get; set; } = 0.0;
        // Upper-triangle (symmetric) covariance entries
        public double C11 { get; set; } = 1.0;
        public double C12 { get; set; } = 0.0;
        public double C13 { get; set; } = 0.0;
        public double C22 { get; set; } = 1.0;
        public double C23 { get; set; } = 0.0;
        public double C33 { get; set; } = 1.0;
        // Sample count for this component
        public int SampleCount { get; set; } = 200;
        // Display
        public Color Color { get; set; } = Colors.SteelBlue;
    }
}
