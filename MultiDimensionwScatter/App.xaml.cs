using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;

namespace MultiDimensionwScatter
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Force software rendering to avoid using GPU
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            base.OnStartup(e);
        }
    }
}
