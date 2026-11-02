using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FundoVivo.Settings;

public sealed class SettingsApp : Application
{
    readonly string[] _screenshot;

    /// <param name="screenshot">null for the normal window; otherwise [png path, "library"|"settings", width, height].</param>
    public SettingsApp(string[] screenshot = null)
    {
        _screenshot = screenshot;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/FundoVivo;component/Settings/Theme.xaml") });
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Write("ui error: " + e.Exception);
            if (_screenshot == null) MessageBox.Show(e.Exception.Message, AppPaths.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new SettingsWindow();
        MainWindow = window;
        if (_screenshot == null)
        {
            window.Show();
            return;
        }

        // Rendered far off-screen and saved to a PNG, so the window can be checked without appearing on the desktop.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Left = -30000;
        window.Top = -30000;
        if (_screenshot.Length > 3) { window.Width = double.Parse(_screenshot[2]); window.Height = double.Parse(_screenshot[3]); }
        window.Show();
        if (_screenshot.Length > 1 && _screenshot[1] == "settings") window.TabSettings.IsChecked = true;
        if (_screenshot.Length > 5)
            window.Dispatcher.BeginInvoke(() => window.SelfTest(_screenshot[4], _screenshot[5]), DispatcherPriority.ApplicationIdle);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var root = (FrameworkElement)window.Content;
            double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            var bmp = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle((Brush)window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            bmp.Render(bg);
            bmp.Render(root);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(_screenshot[0])) png.Save(f);
            window.Close();
        };
        timer.Start();
    }
}
