using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FundoVivo.Engine;
using Path = System.IO.Path;

namespace FundoVivo.Settings;

public partial class SettingsWindow : Window
{
    AppConfig _config;
    List<MonitorInfo> _monitors = new();
    LibraryScan _scan = new();
    string _selectedMonitor;
    JsonObject _engineState;
    readonly DispatcherTimer _stateTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly List<Action<AppConfig>> _pending = new();
    bool _loading;
    CancellationTokenSource _convert;
    UpdateInfo _update;

    public SettingsWindow()
    {
        InitializeComponent();
        _config = ConfigStore.Load();
        _stateTimer.Tick += (_, _) => RefreshState();
        _saveTimer.Tick += (_, _) => FlushSettings();
        Loaded += (_, _) =>
        {
            ReloadMonitors();
            ReloadLibrary();
            LoadOptions();
            RefreshState();
            _stateTimer.Start();
        };
        Closing += (_, _) => FlushSettings();
        SearchBox.GotFocus += (_, _) => SearchHint.Visibility = Visibility.Collapsed;
        SearchBox.LostFocus += (_, _) => SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(ReloadMonitors);
        SourceInitialized += (_, _) => ThemeManager.SetTitleBar(this);
        // Elements built in code take their brushes when created, so they are rebuilt with the new palette.
        ThemeManager.Changed += () => { DrawMonitors(); RenderLibrary(); UpdateSidePanel(); RefreshState(); };
        ThemeManager.WatchSystem(() => _config.Theme);
    }

    // ------------------------------------------------------------------ config and engine

    /// <summary>Reads the file again before changing it, so a pause made from the tray is never overwritten.</summary>
    void Mutate(Action<AppConfig> change)
    {
        var fresh = ConfigStore.Load();
        change(fresh);
        ConfigStore.Save(fresh);
        _config = fresh;
    }

    static void Notify(string cmd)
    {
        if (!EngineHost.Send($"{{\"cmd\":\"{cmd}\"}}")) Program.StartEngineProcess();
    }

    /// <summary>
    /// Queues one change to one item's settings. Only the change is replayed on a freshly read config, never a whole
    /// settings object, so volume, fit and options changed in any order all survive.
    /// </summary>
    void QueueSettings(string itemId, Action<ItemSettings> change)
    {
        change(_config.SettingsFor(itemId));
        _pending.Add(c => change(c.SettingsFor(itemId)));
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void FlushSettings()
    {
        _saveTimer.Stop();
        if (_pending.Count == 0) return;
        var changes = _pending.ToList();
        _pending.Clear();
        Mutate(c => { foreach (var change in changes) change(c); });
        Notify("settings");
    }

    MonitorAssignment SelectedAssignment =>
        _selectedMonitor != null && _config.Monitors.TryGetValue(_selectedMonitor, out var a) ? a : null;

    MonitorInfo SelectedMonitor => _monitors.FirstOrDefault(m => m.Id == _selectedMonitor);

    // ------------------------------------------------------------------ monitors

    void ReloadMonitors()
    {
        _monitors = Monitors.Enumerate();
        if (_selectedMonitor == null || _monitors.All(m => m.Id != _selectedMonitor))
            _selectedMonitor = (_monitors.FirstOrDefault(m => m.Primary) ?? _monitors.FirstOrDefault())?.Id;
        DrawMonitors();
        UpdateSidePanel();
    }

    void MonitorCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMonitors();

    void DrawMonitors()
    {
        MonitorCanvas.Children.Clear();
        if (_monitors.Count == 0 || MonitorCanvas.ActualWidth <= 0) return;
        int minX = _monitors.Min(m => m.Bounds.Left), minY = _monitors.Min(m => m.Bounds.Top);
        int maxX = _monitors.Max(m => m.Bounds.Right), maxY = _monitors.Max(m => m.Bounds.Bottom);
        double gap = 8;
        double scale = Math.Min((MonitorCanvas.ActualWidth - gap * _monitors.Count) / (maxX - minX), MonitorCanvas.ActualHeight / (double)(maxY - minY));
        double used = (maxX - minX) * scale + gap * (_monitors.Count - 1);
        double offsetX = Math.Max(0, (MonitorCanvas.ActualWidth - used) / 2);

        foreach (var m in _monitors)
        {
            int index = _monitors.Count(o => o.Bounds.Left < m.Bounds.Left);
            double x = offsetX + (m.Bounds.Left - minX) * scale + index * gap;
            double y = (m.Bounds.Top - minY) * scale;
            double w = m.Bounds.Width * scale, h = m.Bounds.Height * scale;
            bool selected = m.Id == _selectedMonitor;

            var image = new Image { Stretch = Stretch.UniformToFill };
            var grid = new Grid { ClipToBounds = true };
            grid.Children.Add(new Border { Background = (Brush)FindResource("Well") });
            grid.Children.Add(image);
            if (_config.Monitors.TryGetValue(m.Id, out var a) && a != null)
            {
                var item = _scan.Items.FirstOrDefault(i => i.Id == a.ItemId);
                Thumbnails.Request(item?.Preview, a.File, bmp => image.Source = bmp);
            }
            else
            {
                grid.Children.Add(new TextBlock { Text = "Papel de parede do Windows", FontSize = 11, Foreground = (Brush)FindResource("Ink3"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(6) });
            }

            var caption = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1D, 0x1B, 0x18)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(8, 4, 8, 5),
            };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var (stateText, stateBrush) = StateLabel(m.Id);
            line.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = stateBrush, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(new TextBlock { Text = $"{m.Label}  {m.Bounds.Width}×{m.Bounds.Height}", Foreground = Brushes.White, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            caption.Child = line;
            caption.ToolTip = stateText;
            grid.Children.Add(caption);

            var frame = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(6), Child = grid, Cursor = Cursors.Hand,
                BorderBrush = (Brush)FindResource(selected ? "Ink" : "Line"), BorderThickness = new Thickness(selected ? 3 : 1),
                ToolTip = $"{m.Label}\n{m.Bounds.Width} × {m.Bounds.Height}, {m.RefreshRate:0} Hz{(m.Primary ? ", principal" : "")}\n{stateText}",
            };
            grid.Clip = null;
            frame.MouseLeftButtonUp += (_, _) => { _selectedMonitor = m.Id; DrawMonitors(); UpdateSidePanel(); RenderLibrary(); };
            Canvas.SetLeft(frame, x);
            Canvas.SetTop(frame, y);
            MonitorCanvas.Children.Add(frame);
        }
    }

    (string, Brush) StateLabel(string monitorId)
    {
        if (!_config.Monitors.ContainsKey(monitorId)) return ("Sem wallpaper animado", (Brush)FindResource("Ink3"));
        string state = _engineState?["monitors"]?[monitorId]?["state"]?.ToString();
        if (_engineState == null) return ("O Fundo Vivo está fechado", (Brush)FindResource("Ink3"));
        return state switch
        {
            "playing" => ("Tocando", (Brush)FindResource("Playing")),
            "paused-fullscreen" => ("Pausado: tela cheia", (Brush)FindResource("Paused")),
            "paused-covered" => ("Pausado: janela na frente", (Brush)FindResource("Paused")),
            "unloaded-fullscreen" => ("Fora da memória: tela cheia", (Brush)FindResource("Paused")),
            "paused-user" => ("Pausado por você", (Brush)FindResource("Paused")),
            "paused-locked" => ("Pausado: tela bloqueada", (Brush)FindResource("Paused")),
            "missing" => ("O arquivo não existe mais", (Brush)FindResource("Problem")),
            "error" => ("Não deu para abrir (veja os registros)", (Brush)FindResource("Problem")),
            _ => ("Abrindo", (Brush)FindResource("Ink3")),
        };
    }

    // ------------------------------------------------------------------ engine state

    void RefreshState()
    {
        JsonObject state = null;
        try
        {
            if (File.Exists(AppPaths.StateFile) && EngineHost.IsRunning)
                state = JsonNode.Parse(File.ReadAllText(AppPaths.StateFile)) as JsonObject;
        }
        catch { }
        bool changed = state?.ToJsonString() != _engineState?.ToJsonString();
        _engineState = state;

        if (state == null)
        {
            StatusDot.Fill = (Brush)FindResource("Ink3");
            StatusText.Text = "Fechado: escolha um wallpaper para ligar";
            PauseAllButton.Content = "Ligar";
        }
        else
        {
            bool paused = state["pausedByUser"]?.GetValue<bool>() == true;
            var mons = state["monitors"] as JsonObject;
            int total = mons?.Count ?? 0, playing = mons?.Count(kv => kv.Value?["state"]?.ToString() == "playing") ?? 0;
            int pid = state["pid"]?.GetValue<int>() ?? 0;
            var (bytes, _) = pid > 0 ? ProcessTree.Measure(pid) : (0, 0);
            string mem = bytes > 0 ? $" · {bytes / 1048576.0:0} MB de RAM" : "";
            StatusDot.Fill = (Brush)FindResource(paused ? "Paused" : playing > 0 ? "Playing" : "Ink3");
            StatusText.Text = paused ? "Pausado por você" + mem
                : total == 0 ? "Ligado, sem wallpaper" + mem
                : $"{playing} de {total} tocando{mem}";
            PauseAllButton.Content = paused ? "Continuar" : "Pausar tudo";
        }
        if (changed)
        {
            FlushSettings();
            _config = ConfigStore.Load();
            DrawMonitors();
            UpdateSideState();
        }
    }

    void PauseAll_Click(object sender, RoutedEventArgs e)
    {
        if (!EngineHost.IsRunning)
        {
            Mutate(c => c.PausedByUser = false);
            Program.StartEngineProcess();
            return;
        }
        Mutate(c => c.PausedByUser = !c.PausedByUser);
        Notify("settings");
        RefreshState();
    }

    // ------------------------------------------------------------------ library

    void ReloadLibrary()
    {
        _scan = Library.Scan(_config);
        string info = $"{_scan.Items.Count} wallpapers";
        if (_scan.SkippedScenes > 0) info += $" · {_scan.SkippedScenes} do tipo cena ficam só no Wallpaper Engine (formato próprio dele)";
        if (_config.ScanWallpaperEngine && !_scan.FoundWallpaperEngine) info += " · Wallpaper Engine não encontrado neste PC";
        LibraryInfo.Text = info;
        WeInfo.Text = _scan.FoundWallpaperEngine
            ? $"Lido direto da pasta do Steam, sem copiar nada. {_scan.Items.Count(i => i.FromWallpaperEngine)} de vídeo e web aparecem aqui."
            : "Não achei o Wallpaper Engine nas bibliotecas do Steam.";
        RenderLibrary();
        DrawMonitors();
        UpdateSidePanel();
    }

    void Filter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RenderLibrary(); }
    void Search_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) RenderLibrary(); }

    void RenderLibrary()
    {
        WallpaperType? type = FilterVideo.IsChecked == true ? WallpaperType.Video : FilterWeb.IsChecked == true ? WallpaperType.Web
            : FilterImage.IsChecked == true ? WallpaperType.Image : null;
        string q = SearchBox.Text.Trim();
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var items = _scan.Items
            .Where(i => type == null || i.Type == type)
            .Where(i => q.Length == 0 || compare.IndexOf(i.Title, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .OrderByDescending(i => i.Date)
            .ToList();
        LibraryList.Items.Clear();
        foreach (var item in items) LibraryList.Items.Add(Card(item));
        if (items.Count == 0)
            LibraryList.Items.Add(new TextBlock { Text = _scan.Items.Count == 0 ? "Nada na biblioteca ainda. Use Adicionar arquivo para colocar um vídeo, imagem ou página." : "Nada com esse filtro.", Style = (Style)FindResource("Muted"), Margin = new Thickness(2, 8, 0, 0) });
    }

    double _cardWidth = 212;

    void LibraryScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Cards stretch so each row fills the width exactly, whatever the window size.
        const double gap = 14, ideal = 210;
        // Every card carries its right margin, the last one included, so a row needs columns * (width + gap).
        double available = (LibraryScroll.ViewportWidth > 0 ? LibraryScroll.ViewportWidth : LibraryScroll.ActualWidth - 18) - 1;
        if (available <= 0) return;
        int columns = Math.Max(1, (int)(available / (ideal + gap)));
        double width = Math.Floor(available / columns - gap);
        if (Math.Abs(width - _cardWidth) < 1) return;
        _cardWidth = width;
        RenderLibrary();
    }

    FrameworkElement Card(LibraryItem item)
    {
        double width = _cardWidth, thumbHeight = Math.Round(width * 9 / 16);
        var image = new Image { Stretch = Stretch.UniformToFill };
        var thumb = new Border { Height = thumbHeight, Background = (Brush)FindResource("Well"), CornerRadius = new CornerRadius(10, 10, 0, 0), ClipToBounds = true };
        var thumbGrid = new Grid();
        thumbGrid.Children.Add(new TextBlock { Text = TypeName(item.Type), Foreground = (Brush)FindResource("Ink3"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        thumbGrid.Children.Add(image);
        thumb.Child = thumbGrid;
        // Rounded top corners on the picture itself (Border.ClipToBounds clips square).
        thumbGrid.Clip = new RectangleGeometry(new Rect(0, 0, width, thumbHeight + 12), 10, 10);
        Thumbnails.Request(item.Preview, item.File, bmp => image.Source = bmp);

        var onMonitors = _config.Monitors.Where(kv => kv.Value?.ItemId == item.Id).Select(kv => _monitors.FirstOrDefault(m => m.Id == kv.Key)?.Label ?? kv.Value.MonitorName).ToList();
        bool here = SelectedAssignment?.ItemId == item.Id;

        var text = new StackPanel { Margin = new Thickness(12, 9, 12, 11) };
        text.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Title });
        string meta = TypeName(item.Type) + (item.FromWallpaperEngine ? " · Wallpaper Engine" : " · seu arquivo");
        text.Children.Add(new TextBlock { Text = meta, FontSize = 11.5, Foreground = (Brush)FindResource("Ink3"), Margin = new Thickness(0, 2, 0, 0) });
        if (onMonitors.Count > 0)
            text.Children.Add(new TextBlock { Text = "Em: " + string.Join(", ", onMonitors), FontSize = 11.5, Foreground = (Brush)FindResource("Ink2"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

        var stack = new StackPanel();
        stack.Children.Add(thumb);
        stack.Children.Add(text);
        var card = new Border
        {
            Width = width, Margin = new Thickness(0, 0, 14, 14), CornerRadius = new CornerRadius(11), Background = (Brush)FindResource("Surface"),
            BorderBrush = (Brush)FindResource(here ? "Ink" : "Line"), BorderThickness = new Thickness(here ? 2 : 1), Child = stack, Cursor = Cursors.Hand,
        };
        card.MouseEnter += (_, _) => { if (!here) card.BorderBrush = (Brush)FindResource("Ink3"); };
        card.MouseLeave += (_, _) => { if (!here) card.BorderBrush = (Brush)FindResource("Line"); };
        card.MouseLeftButtonUp += (_, _) => Assign(item);

        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Abrir a pasta" };
        open.Click += (_, _) => Process.Start("explorer.exe", $"/select,\"{item.File}\"");
        menu.Items.Add(open);
        if (!item.FromWallpaperEngine)
        {
            var remove = new MenuItem { Header = "Tirar da biblioteca" };
            remove.Click += (_, _) =>
            {
                Mutate(c => c.UserItems.RemoveAll(u => u.Id == item.Id));
                ReloadLibrary();
            };
            menu.Items.Add(remove);
        }
        card.ContextMenu = menu;
        return card;
    }

    static string TypeName(WallpaperType t) => t switch { WallpaperType.Video => "Vídeo", WallpaperType.Web => "Web", _ => "Imagem" };

    void Assign(LibraryItem item)
    {
        var monitor = SelectedMonitor;
        if (monitor == null) return;
        FlushSettings();
        Mutate(c => c.Monitors[monitor.Id] = new MonitorAssignment
        {
            ItemId = item.Id, Type = item.Type, File = item.File, Title = item.Title, MonitorName = monitor.Label,
        });
        Notify("reload");
        RenderLibrary();
        DrawMonitors();
        UpdateSidePanel();
    }

    void AddFile_Click(object sender, RoutedEventArgs e)
    {
        string Join(string[] exts) => string.Join(";", exts.Select(x => "*" + x));
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha vídeos, imagens ou páginas HTML",
            Multiselect = true,
            Filter = $"Wallpapers|{Join(Library.VideoExtensions)};{Join(Library.ImageExtensions)};{Join(Library.WebExtensions)}|Vídeo|{Join(Library.VideoExtensions)}|Imagem|{Join(Library.ImageExtensions)}|Página web|{Join(Library.WebExtensions)}",
        };
        if (dialog.ShowDialog(this) != true) return;
        Mutate(c =>
        {
            foreach (string f in dialog.FileNames)
            {
                var type = Library.TypeForFile(f);
                if (type == null) continue;
                string id = Library.IdFor(f);
                if (c.UserItems.Any(u => u.Id == id)) continue;
                c.UserItems.Add(new UserItem { Id = id, File = f, Type = type.Value, Title = Path.GetFileNameWithoutExtension(f), Added = DateTime.Now });
            }
        });
        ReloadLibrary();
    }

    // ------------------------------------------------------------------ side panel

    void UpdateSidePanel()
    {
        _loading = true;
        try
        {
            var m = SelectedMonitor;
            var a = SelectedAssignment;
            SideMonitorName.Text = m == null ? "" : $"{m.Label} · {m.Bounds.Width}×{m.Bounds.Height} · {m.RefreshRate:0} Hz{(m.Primary ? " · principal" : "")}";
            SideThumb.Source = null;
            if (a == null)
            {
                SideEmpty.Visibility = Visibility.Visible;
                SideTitle.Text = "Nenhum wallpaper animado";
                SideDetail.Text = "Escolha um na biblioteca ao lado.";
                SideControls.Visibility = Visibility.Collapsed;
                return;
            }
            SideEmpty.Visibility = Visibility.Collapsed;
            var item = _scan.Items.FirstOrDefault(i => i.Id == a.ItemId);
            Thumbnails.Request(item?.Preview, a.File, bmp => SideThumb.Source = bmp);
            SideTitle.Text = a.Title;
            UpdateSideState();
            SideControls.Visibility = Visibility.Visible;

            var s = _config.SettingsFor(a.ItemId);
            bool visual = a.Type != WallpaperType.Web;
            FitFill.IsChecked = s.Fit == FitMode.Fill;
            FitFit.IsChecked = s.Fit == FitMode.Fit;
            FitStretch.IsChecked = s.Fit == FitMode.Stretch;
            SetLabelVisibility(FitFill, visual);

            SoundSection.Visibility = a.Type == WallpaperType.Image ? Visibility.Collapsed : Visibility.Visible;
            VolumeSlider.Value = s.Volume;
            VolumeText.Text = VolumeLabel(s.Volume, a.Type);

            PropertiesSection.Visibility = Visibility.Collapsed;
            if (a.Type == WallpaperType.Web)
            {
                var project = WeProject.Load(Path.GetDirectoryName(a.File));
                if (project != null && PropertyEditor.Build(PropertiesPanel, project, s, (key, value) => QueueSettings(a.ItemId, x => x.Properties[key] = value?.DeepClone())) > 0)
                    PropertiesSection.Visibility = Visibility.Visible;
            }
            _ = UpdateOptimizeAsync(m, a);
        }
        finally { _loading = false; }
    }

    static void SetLabelVisibility(FrameworkElement segment, bool visible)
    {
        // The "ENCAIXE" label sits right before the segment bar in the same panel.
        var bar = (FrameworkElement)((FrameworkElement)segment.Parent).Parent;
        if (bar.Parent is Panel panel)
        {
            int i = panel.Children.IndexOf(bar);
            if (i > 0) panel.Children[i - 1].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            bar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    void UpdateSideState()
    {
        var a = SelectedAssignment;
        if (a == null || _selectedMonitor == null) return;
        var (state, _) = StateLabel(_selectedMonitor);
        string source = _scan.Items.FirstOrDefault(i => i.Id == a.ItemId)?.FromWallpaperEngine == true ? "Wallpaper Engine" : "seu arquivo";
        SideDetail.Text = $"{TypeName(a.Type)} · {source} · {state}";
    }

    static string VolumeLabel(int v, WallpaperType t) => v <= 0 ? "Mudo" : t == WallpaperType.Web ? "Ligado" : $"{v}%";

    void Fit_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || SelectedAssignment == null) return;
        var a = SelectedAssignment;
        var fit = FitFit.IsChecked == true ? FitMode.Fit : FitStretch.IsChecked == true ? FitMode.Stretch : FitMode.Fill;
        QueueSettings(a.ItemId, x => x.Fit = fit);
        // A converted copy is made for one fit; changing the fit may switch back to the original file.
        Dispatcher.BeginInvoke(() => { FlushSettings(); Notify("reload"); _ = UpdateOptimizeAsync(SelectedMonitor, a); }, DispatcherPriority.Background);
    }

    void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || SelectedAssignment == null) return;
        var a = SelectedAssignment;
        int volume = (int)Math.Round(e.NewValue);
        VolumeText.Text = VolumeLabel(volume, a.Type);
        QueueSettings(a.ItemId, x => x.Volume = volume);
    }

    void ResetProperties_Click(object sender, RoutedEventArgs e)
    {
        var a = SelectedAssignment;
        if (a == null) return;
        QueueSettings(a.ItemId, x => x.Properties.Clear());
        FlushSettings();
        UpdateSidePanel();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        string id = _selectedMonitor;
        if (id == null) return;
        FlushSettings();
        Mutate(c => c.Monitors.Remove(id));
        Notify("reload");
        DrawMonitors();
        UpdateSidePanel();
        RenderLibrary();
    }

    // ------------------------------------------------------------------ optimize (convert to monitor size)

    async Task UpdateOptimizeAsync(MonitorInfo m, MonitorAssignment a)
    {
        if (m == null || a == null || a.Type != WallpaperType.Video)
        {
            if (_convert == null) OptimizeSection.Visibility = Visibility.Collapsed;
            return;
        }
        OptimizeSection.Visibility = Visibility.Visible;
        if (_convert != null) return;
        OptimizeButton.Visibility = Visibility.Collapsed;
        OptimizeText.Text = "Lendo o vídeo...";
        var fit = _config.SettingsFor(a.ItemId).Fit;
        var existing = _config.Converted.FirstOrDefault(c => string.Equals(c.Source, a.File, StringComparison.OrdinalIgnoreCase)
            && c.Width == m.Bounds.Width && c.Height == m.Bounds.Height && c.Fit == fit && File.Exists(c.Output) && c.SourceLength == AppConfig.SafeLength(a.File));
        if (existing != null)
        {
            long saved = AppConfig.SafeLength(existing.Output);
            OptimizeText.Text = $"Usando a cópia feita para este monitor ({m.Bounds.Width}×{m.Bounds.Height}, {saved / 1048576.0:0} MB). O arquivo original continua intacto.";
            OptimizeButton.Content = "Voltar ao original";
            OptimizeButton.Tag = "undo";
            OptimizeButton.Visibility = Visibility.Visible;
            return;
        }
        uint vw = 0, vh = 0;
        double seconds = 0;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(a.File);
            var props = await file.Properties.GetVideoPropertiesAsync();
            vw = props.Width; vh = props.Height; seconds = props.Duration.TotalSeconds;
        }
        catch (Exception e) { Log.Write("video properties: " + e.Message); }
        if (SelectedAssignment?.ItemId != a.ItemId) return;
        OptimizeButton.Tag = seconds;
        if (vw == 0)
        {
            OptimizeText.Text = "Não deu para ler o tamanho do vídeo.";
            return;
        }
        // Work out what the screen actually needs: with Fill, the part of the video that ends up visible.
        bool bigger = (long)vw * vh > (long)m.Bounds.Width * m.Bounds.Height * 1.15;
        if (!bigger)
        {
            OptimizeText.Text = $"O vídeo tem {vw}×{vh}, que já é do tamanho do monitor ou menor. Não tem o que ganhar convertendo.";
            return;
        }
        if (VideoConverter.FindFfmpeg() == null)
        {
            OptimizeText.Text = $"O vídeo tem {vw}×{vh} e o monitor {m.Bounds.Width}×{m.Bounds.Height}. Para converter, o ffmpeg precisa estar no PATH.";
            return;
        }
        OptimizeText.Text = $"O vídeo tem {vw}×{vh} e o monitor {m.Bounds.Width}×{m.Bounds.Height}. Uma cópia do tamanho exato do monitor fica igual na tela e gasta bem menos para tocar. O original não é mexido.";
        OptimizeButton.Content = "Converter para este monitor";
        OptimizeButton.Visibility = Visibility.Visible;
    }

    async void Optimize_Click(object sender, RoutedEventArgs e)
    {
        var m = SelectedMonitor;
        var a = SelectedAssignment;
        if (m == null || a == null) return;
        var fit = _config.SettingsFor(a.ItemId).Fit;
        if (OptimizeButton.Tag as string == "undo")
        {
            Mutate(c =>
            {
                foreach (var cv in c.Converted.Where(x => string.Equals(x.Source, a.File, StringComparison.OrdinalIgnoreCase) && x.Width == m.Bounds.Width && x.Height == m.Bounds.Height && x.Fit == fit).ToList())
                {
                    c.Converted.Remove(cv);
                    if (!c.Converted.Any(o => o.Output == cv.Output)) { try { File.Delete(cv.Output); } catch { } }
                }
            });
            Notify("reload");
            await UpdateOptimizeAsync(m, a);
            return;
        }
        double seconds = OptimizeButton.Tag is double d ? d : 0;
        _convert = new CancellationTokenSource();
        OptimizeButton.Visibility = Visibility.Collapsed;
        OptimizeCancel.Visibility = Visibility.Visible;
        OptimizeProgress.Visibility = Visibility.Visible;
        OptimizeProgress.Value = 0;
        var progress = new Progress<double>(p =>
        {
            OptimizeProgress.Value = p;
            OptimizeText.Text = $"Convertendo para {m.Bounds.Width}×{m.Bounds.Height}... {p:P0}";
        });
        try
        {
            string output = await VideoConverter.ConvertAsync(a.File, m.Bounds.Width, m.Bounds.Height, fit, seconds, progress, _convert.Token);
            Mutate(c =>
            {
                c.Converted.RemoveAll(x => x.Output == output);
                c.Converted.Add(new ConvertedVideo { Source = a.File, Output = output, Width = m.Bounds.Width, Height = m.Bounds.Height, Fit = fit, SourceLength = AppConfig.SafeLength(a.File) });
            });
            Notify("reload");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppPaths.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _convert = null;
            OptimizeCancel.Visibility = Visibility.Collapsed;
            OptimizeProgress.Visibility = Visibility.Collapsed;
        }
        await UpdateOptimizeAsync(SelectedMonitor, SelectedAssignment);
    }

    void OptimizeCancel_Click(object sender, RoutedEventArgs e) => _convert?.Cancel();

    // ------------------------------------------------------------------ settings page

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (LibraryPage == null) return;
        bool lib = TabLibrary.IsChecked == true;
        LibraryPage.Visibility = lib ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = lib ? Visibility.Collapsed : Visibility.Visible;
    }

    void LoadOptions()
    {
        _loading = true;
        try
        {
            var p = _config.Pause;
            CoveredPause.IsChecked = p.WhenCovered == CoveredAction.Pause;
            CoveredKeep.IsChecked = p.WhenCovered == CoveredAction.Keep;
            FullPause.IsChecked = p.WhenFullscreen == FullscreenAction.Pause;
            FullUnload.IsChecked = p.WhenFullscreen == FullscreenAction.Unload;
            FullKeep.IsChecked = p.WhenFullscreen == FullscreenAction.Keep;
            FullscreenAll.IsChecked = p.FullscreenPausesAll;
            WebMouse.IsChecked = _config.WebMouse;
            WebAudio.IsChecked = _config.WebAudio;
            ScanWe.IsChecked = _config.ScanWallpaperEngine;
            try { StartWithWindows.IsChecked = StartupEntry.Enabled; } catch { }
            ThemeSystem.IsChecked = _config.Theme == AppTheme.System;
            ThemeLight.IsChecked = _config.Theme == AppTheme.Light;
            ThemeDark.IsChecked = _config.Theme == AppTheme.Dark;
            VersionText.Text = $"Versão {AppPaths.Version}" + (AppPaths.IsDevelopmentCopy ? " · cópia de desenvolvimento (atualiza pelo git e pelo compilar.ps1)" : "");
            TokenText.Text = string.IsNullOrEmpty(Secrets.GitHubToken)
                ? "Token de leitura (fine-grained, só este repositório, Contents: leitura). Fica guardado protegido pelo Windows, fora do app."
                : "Já tem um token guardado. Para trocar, cole outro e guarde; para tirar, guarde vazio.";
        }
        finally { _loading = false; }
    }

    void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        bool scanBefore = _config.ScanWallpaperEngine;
        Mutate(c =>
        {
            c.Pause.WhenCovered = CoveredKeep.IsChecked == true ? CoveredAction.Keep : CoveredAction.Pause;
            c.Pause.WhenFullscreen = FullUnload.IsChecked == true ? FullscreenAction.Unload : FullKeep.IsChecked == true ? FullscreenAction.Keep : FullscreenAction.Pause;
            c.Pause.FullscreenPausesAll = FullscreenAll.IsChecked == true;
            c.WebMouse = WebMouse.IsChecked == true;
            c.WebAudio = WebAudio.IsChecked == true;
            c.ScanWallpaperEngine = ScanWe.IsChecked == true;
        });
        Notify("settings");
        if (scanBefore != _config.ScanWallpaperEngine) ReloadLibrary();
    }

    void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var theme = ThemeDark.IsChecked == true ? AppTheme.Dark : ThemeLight.IsChecked == true ? AppTheme.Light : AppTheme.System;
        Mutate(c => c.Theme = theme);
        ThemeManager.Apply(theme);
    }

    void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        try { StartupEntry.Enabled = StartWithWindows.IsChecked == true; }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não deu para mudar: " + ex.Message, AppPaths.AppName);
            StartWithWindows.IsChecked = StartupEntry.Enabled;
        }
    }

    async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateText.Text = "Perguntando ao GitHub...";
        _update = await Updater.CheckAsync();
        CheckUpdateButton.IsEnabled = true;
        if (_update.Error != null) { UpdateText.Text = _update.Error; return; }
        if (!_update.IsNewer) { UpdateText.Text = $"Você já está na versão mais nova ({_update.Latest})."; return; }
        string notes = _update.Notes.Length > 600 ? _update.Notes[..600] + "..." : _update.Notes;
        UpdateText.Text = $"Versão {_update.Latest} disponível.\n\n{notes}".Trim();
        InstallUpdateButton.Visibility = Visibility.Visible;
    }

    async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null) return;
        InstallUpdateButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;
        try
        {
            await Updater.InstallAsync(_update, new Progress<string>(t => UpdateText.Text = t));
            Close();
        }
        catch (Exception ex)
        {
            UpdateText.Text = ex.Message;
            InstallUpdateButton.IsEnabled = true;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    void SaveToken_Click(object sender, RoutedEventArgs e)
    {
        Secrets.GitHubToken = TokenBox.Password;
        TokenBox.Clear();
        LoadOptions();
    }

    /// <summary>Screenshot mode only: does what a click would, so the result can be checked without a visible window.</summary>
    internal void SelfTest(string script, string argument)
    {
        switch (script)
        {
            case "theme":
                // argument: light | dark, chosen with the same radio button a click would use.
                (argument == "dark" ? ThemeDark : ThemeLight).IsChecked = true;
                Log.Write($"selftest theme {argument}: dark={ThemeManager.IsDark} saved={ConfigStore.Load().Theme}");
                break;
            case "assign":
                // argument: part of the item's folder; goes to the first non-primary monitor.
                _selectedMonitor = (_monitors.FirstOrDefault(m => !m.Primary) ?? _monitors.First()).Id;
                var item = _scan.Items.First(i => i.File.Contains(argument, StringComparison.OrdinalIgnoreCase));
                Assign(item);
                Log.Write($"selftest assigned {item.Title} to {SelectedMonitor.Label}: config has {_config.Monitors.Count} monitors");
                // Volume, then an option, with a state refresh in between: both must end up saved.
                VolumeSlider.Value = 40;
                _engineState = null;
                RefreshState();
                QueueSettings(item.Id, x => x.Properties["selftest"] = JsonValue.Create(7));
                FlushSettings();
                var saved = ConfigStore.Load().SettingsFor(item.Id);
                Log.Write($"selftest saved volume={saved.Volume} selftest={saved.Properties.GetValueOrDefault("selftest")}");
                break;
        }
    }

    void OpenLogs_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", AppPaths.LogFolder);

    void QuitEngine_Click(object sender, RoutedEventArgs e)
    {
        EngineHost.Send("{\"cmd\":\"quit\"}");
        RefreshState();
    }
}
