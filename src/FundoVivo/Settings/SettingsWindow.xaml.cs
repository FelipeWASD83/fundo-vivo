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
        // room under the monitors for their names, and the selection ring around them
        const double gap = 18, captionRoom = 30, ring = 5;
        double scale = Math.Min((MonitorCanvas.ActualWidth - gap * _monitors.Count - 2 * ring) / (maxX - minX),
            (MonitorCanvas.ActualHeight - captionRoom - 2 * ring) / (double)(maxY - minY));
        double used = (maxX - minX) * scale + gap * (_monitors.Count - 1);
        double offsetX = Math.Max(ring, (MonitorCanvas.ActualWidth - used) / 2);

        foreach (var m in _monitors)
        {
            int index = _monitors.Count(o => o.Bounds.Left < m.Bounds.Left);
            double x = offsetX + (m.Bounds.Left - minX) * scale + index * gap;
            double y = ring + (m.Bounds.Top - minY) * scale;
            double w = m.Bounds.Width * scale, h = m.Bounds.Height * scale;
            bool selected = m.Id == _selectedMonitor;

            var image = new Image { Stretch = Stretch.UniformToFill };
            var grid = new Grid { Width = w, Height = h, Clip = new RectangleGeometry(new Rect(0, 0, w, h), 6, 6) };
            grid.Children.Add(new Border { Background = (Brush)FindResource("StageLine") });
            grid.Children.Add(image);
            if (_config.Monitors.TryGetValue(m.Id, out var a) && a != null)
            {
                var item = _scan.Items.FirstOrDefault(i => i.Id == a.ItemId);
                Thumbnails.Request(item?.Preview, a.File, bmp => image.Source = bmp);
            }
            else
            {
                grid.Children.Add(new TextBlock { Text = "Papel de parede do Windows", FontSize = 11, Foreground = (Brush)FindResource("StageInk2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(6) });
            }

            // The chosen monitor is lit and ringed; the others sit a little dimmer until the mouse is over them.
            var (stateText, stateBrush) = StateLabel(m.Id);
            var frame = new Border
            {
                Child = grid, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(6 + ring), Padding = new Thickness(ring - 2),
                BorderThickness = new Thickness(2), BorderBrush = selected ? (Brush)FindResource("StageInk") : Brushes.Transparent,
                Background = Brushes.Transparent, Opacity = selected ? 1 : 0.62,
                ToolTip = $"{m.Label}\n{m.Bounds.Width} × {m.Bounds.Height}, {m.RefreshRate:0} Hz{(m.Primary ? ", principal" : "")}\n{stateText}",
            };
            if (!selected)
            {
                frame.MouseEnter += (_, _) => frame.Opacity = 1;
                frame.MouseLeave += (_, _) => frame.Opacity = 0.62;
            }
            frame.MouseLeftButtonUp += (_, _) => { _selectedMonitor = m.Id; DrawMonitors(); UpdateSidePanel(); RenderLibrary(); };
            Canvas.SetLeft(frame, x - ring);
            Canvas.SetTop(frame, y - ring);
            MonitorCanvas.Children.Add(frame);

            // under it: the state dot, the name in full ink when chosen, the size in the softer grey
            var caption = new TextBlock { Width = Math.Max(w, 120), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, IsHitTestVisible = false };
            caption.Inlines.Add(new System.Windows.Documents.InlineUIContainer(new Ellipse { Width = 7, Height = 7, Fill = stateBrush, Margin = new Thickness(0, 0, 7, 1) }));
            caption.Inlines.Add(new System.Windows.Documents.Run(m.Label) { Foreground = (Brush)FindResource(selected ? "StageInk" : "StageInk2"), FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal });
            caption.Inlines.Add(new System.Windows.Documents.Run($"  {m.Bounds.Width}×{m.Bounds.Height}") { Foreground = (Brush)FindResource("StageInk2") });
            Canvas.SetLeft(caption, x);
            Canvas.SetTop(caption, y + h + ring + 6);
            MonitorCanvas.Children.Add(caption);
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
            "paused-program" => ($"Pausado: {_engineState?["program"]} aberto", (Brush)FindResource("Paused")),
            "unloaded-program" => ($"Fora da memória: {_engineState?["program"]} aberto", (Brush)FindResource("Paused")),
            "missing" => ("O arquivo não existe mais", (Brush)FindResource("Problem")),
            "error" => ("Não deu para abrir (veja os registros)", (Brush)FindResource("Problem")),
            _ => ("Abrindo", (Brush)FindResource("Ink3")),
        };
    }

    // ------------------------------------------------------------------ engine state

    void RefreshState()
    {
        try { RefreshSceneJob(); } catch (Exception e) { Log.Write("scene job status: " + e.Message); }
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
        if (_scan.Scenes > 0) info += $" · {_scan.Scenes} são cenas do Wallpaper Engine, que viram vídeo quando você converte para um monitor";
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
            : FilterImage.IsChecked == true ? WallpaperType.Image : FilterScene.IsChecked == true ? WallpaperType.Scene
            : FilterVivo.IsChecked == true ? WallpaperType.Package : null;
        string q = SearchBox.Text.Trim();
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var ofType = _scan.Items.Where(i => type == null || i.Type == type).ToList();
        SetFilterCounts();
        RenderTagFilters(ofType);
        var items = ofType
            .Where(i => q.Length == 0 || compare.IndexOf(i.Title, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            // Every checked tag must be there (narrows down); any checked rating is enough (Livre or Questionável).
            .Where(i => _tagFilter.All(k => i.Tags.Any(t => Tags.Key(t) == k)))
            .Where(i => _ratingFilter.Count == 0 || (i.Rating != null && _ratingFilter.Contains(i.Rating)))
            .OrderByDescending(i => i.Date)
            .ToList();
        LibraryList.Items.Clear();
        foreach (var item in items) LibraryList.Items.Add(Card(item));
        if (items.Count == 0)
            LibraryList.Items.Add(new TextBlock { Text = _scan.Items.Count == 0 ? "Nada na biblioteca ainda. Use Adicionar arquivo para colocar um vídeo, imagem ou página." : "Nada com esse filtro.", Style = (Style)FindResource("Muted"), Margin = new Thickness(2, 8, 0, 0) });
    }

    /// <summary>Each type filter says how many it holds, the count in the softer grey.</summary>
    void SetFilterCounts()
    {
        void Set(RadioButton button, string name, WallpaperType? t)
        {
            int n = _scan.Items.Count(i => t == null || i.Type == t);
            var text = new TextBlock();
            text.Inlines.Add(new System.Windows.Documents.Run(name));
            if (n > 0) text.Inlines.Add(Faint("  " + n));
            button.Content = text;
            button.Visibility = n > 0 || t == null || button.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        Set(FilterAll, "Todos", null);
        Set(FilterVideo, "Vídeo", WallpaperType.Video);
        Set(FilterWeb, "Web", WallpaperType.Web);
        Set(FilterImage, "Imagem", WallpaperType.Image);
        Set(FilterVivo, "Vivo", WallpaperType.Package);
        Set(FilterScene, "Cena", WallpaperType.Scene);
    }

    /// <summary>The softer part of a label: the same colour as the rest, see-through, so it also reads on a lit (white) pill.</summary>
    static System.Windows.Documents.Inline Faint(string text) =>
        new System.Windows.Documents.InlineUIContainer(new TextBlock { Text = text, Opacity = 0.55 }) { BaselineAlignment = BaselineAlignment.Baseline };

    bool _tagsOpen;
    readonly HashSet<string> _tagFilter = new();
    readonly HashSet<string> _ratingFilter = new();

    /// <summary>
    /// "Tags +" opens one chip per tag of the current type filter (most used first), then the ratings, then Limpar.
    /// Folded, only the chips that are on stay in view, so the filter never hides the library behind a wall of tags.
    /// </summary>
    void RenderTagFilters(List<LibraryItem> ofType)
    {
        TagFilters.Children.Clear();
        int on = _tagFilter.Count + _ratingFilter.Count;
        var toggleText = new TextBlock();
        toggleText.Inlines.Add(new System.Windows.Documents.Run(_tagsOpen ? "Tags  −" : "Tags  +") { FontWeight = FontWeights.SemiBold });
        if (on > 0 && !_tagsOpen) toggleText.Inlines.Add(Faint($"  {on} {(on == 1 ? "ligada" : "ligadas")}"));
        var toggle = new System.Windows.Controls.Primitives.ToggleButton { Content = toggleText, IsChecked = _tagsOpen, Style = (Style)FindResource("Chip") };
        toggle.Click += (_, _) => { _tagsOpen = !_tagsOpen; RenderLibrary(); };
        TagFilters.Children.Add(toggle);
        var groups = ofType.SelectMany(i => i.Tags.Select(t => (key: Tags.Key(t), name: t, item: i.Id)))
            .Where(x => x.key.Length > 0)
            .GroupBy(x => x.key)
            .Select(g => (key: g.Key, name: g.First().name, count: g.Select(x => x.item).Distinct().Count()))
            .OrderByDescending(g => g.count).ThenBy(g => g.name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        // A checked tag that the current type filter no longer has stays visible, so it can be unchecked.
        foreach (string key in _tagFilter.Where(k => groups.All(g => g.key != k)).ToList())
            groups.Add((key, _scan.Items.SelectMany(i => i.Tags).FirstOrDefault(t => Tags.Key(t) == key) ?? key, 0));
        foreach (var g in groups.Where(g => _tagsOpen || _tagFilter.Contains(g.key)))
            TagFilters.Children.Add(Chip(g.name, g.count, _tagFilter.Contains(g.key), on => { if (on) _tagFilter.Add(g.key); else _tagFilter.Remove(g.key); }));

        var ratings = Tags.RatingOrder.Select(r => (name: r, count: ofType.Count(i => i.Rating == r)))
            .Where(r => _ratingFilter.Contains(r.name) || (_tagsOpen && r.count > 0)).ToList();
        if (ratings.Count > 0)
        {
            // One block, so the label never ends a line apart from its chips.
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
            group.Children.Add(new TextBlock { Text = "Classificação", Style = (Style)FindResource("Label"), Margin = new Thickness(0, 0, 8, 6), VerticalAlignment = VerticalAlignment.Center });
            foreach (var r in ratings)
                group.Children.Add(Chip(r.name, r.count, _ratingFilter.Contains(r.name), on => { if (on) _ratingFilter.Add(r.name); else _ratingFilter.Remove(r.name); }));
            TagFilters.Children.Add(group);
        }
        if (_tagFilter.Count + _ratingFilter.Count > 0)
        {
            var clear = new Button { Content = "Limpar", Style = (Style)FindResource("Quiet"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 6) };
            clear.Click += (_, _) => { _tagFilter.Clear(); _ratingFilter.Clear(); RenderLibrary(); };
            TagFilters.Children.Add(clear);
        }
    }

    FrameworkElement Chip(string name, int count, bool on, Action<bool> toggled)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = name });
        content.Children.Add(new TextBlock { Text = " " + count, Opacity = 0.6 });
        var chip = new System.Windows.Controls.Primitives.ToggleButton { Content = content, IsChecked = on, Style = (Style)FindResource("Chip") };
        chip.Click += (_, _) => { toggled(chip.IsChecked == true); Dispatcher.BeginInvoke(RenderLibrary); };
        return chip;
    }

    /// <summary>Every tag that exists anywhere in the library, for the editor's suggestions.</summary>
    IEnumerable<string> AllTags() => _scan.Items.SelectMany(i => i.Tags);

    double _cardWidth = 212;

    void LibraryScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Cards stretch so each row fills the width exactly, whatever the window size.
        const double gap = 14, ideal = 210;
        // Every card carries its right margin, the last one included, so a row needs columns * (width + gap).
        // The scroll bar's room is always reserved: it shows up only after the cards are in, and taking those
        // pixels then would push the last column to the next row.
        double available = LibraryScroll.ActualWidth - 14;
        if (available <= 0) return;
        int columns = Math.Max(1, (int)(available / (ideal + gap)));
        double width = Math.Floor(available / columns - gap);
        if (Math.Abs(width - _cardWidth) < 1) return;
        _cardWidth = width;
        RenderLibrary();
    }

    FrameworkElement Card(LibraryItem item)
    {
        // No box around it: the picture is the card, rounded like the album art of the Relógio Mond, and a white ring
        // a few pixels off it marks the one on this monitor.
        const double ringGap = 3, radius = 16;
        double width = _cardWidth, inner = width - 2 * (ringGap + 2), thumbHeight = Math.Round(inner * 9 / 16);
        var image = new Image { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        var thumbGrid = new Grid { Width = inner, Height = thumbHeight, Clip = new RectangleGeometry(new Rect(0, 0, inner, thumbHeight), radius, radius) };
        thumbGrid.Children.Add(new Border { Background = (Brush)FindResource("Surface") });
        thumbGrid.Children.Add(new TextBlock { Text = TypeName(item.Type), Foreground = (Brush)FindResource("Ink3"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        thumbGrid.Children.Add(image);
        Thumbnails.Request(item.Preview, item.File, bmp => image.Source = bmp);

        var onMonitors = _config.Monitors.Where(kv => kv.Value?.ItemId == item.Id).Select(kv => _monitors.FirstOrDefault(m => m.Id == kv.Key)?.Label ?? kv.Value.MonitorName).ToList();
        bool here = SelectedAssignment?.ItemId == item.Id;

        var text = new StackPanel { Margin = new Thickness(ringGap + 4, 8, ringGap + 4, 0) };
        string tip = item.Title + (item.Tags.Count > 0 ? "\n" + string.Join(" · ", item.Tags) : "") + (item.Rating != null ? "\nClassificação: " + item.Rating : "");
        text.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = tip });
        string meta = TypeName(item.Type) + (item.FromWallpaperEngine ? " · Wallpaper Engine" : item.IsExample ? " · exemplo" : " · seu arquivo");
        if (item.Type == WallpaperType.Package)
        {
            string stamp = Packages.Stamp(item.File);
            var done = _config.ConvertedScenes.Where(c => c.SceneId == item.Id && c.SourceStamp == stamp && File.Exists(c.Output)).Select(c => $"{c.MonitorWidth}×{c.MonitorHeight}").Distinct().ToList();
            meta += item.Realtime ? (item.ReactsToSound ? " · ao vivo, reage ao som" : " · ao vivo") : done.Count > 0 ? " · vídeo pronto: " + string.Join(", ", done) : " · vira vídeo ao usar";
        }
        if (item.Type == WallpaperType.Scene)
        {
            var done = _config.ConvertedScenes.Where(c => c.SceneId == item.Id && File.Exists(c.Output)).Select(c => $"{c.MonitorWidth}×{c.MonitorHeight}").Distinct().ToList();
            meta += done.Count > 0 ? " · convertida: " + string.Join(", ", done) : " · converter para usar";
        }
        text.Children.Add(new TextBlock { Text = meta, FontSize = 11.5, Foreground = (Brush)FindResource("Ink3"), Margin = new Thickness(0, 2, 0, 0) });
        if (onMonitors.Count > 0)
            text.Children.Add(new TextBlock { Text = "Em: " + string.Join(", ", onMonitors), FontSize = 11.5, Foreground = (Brush)FindResource("Ink2"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

        var ring = new Border
        {
            CornerRadius = new CornerRadius(radius + ringGap + 2), BorderThickness = new Thickness(2), Padding = new Thickness(ringGap), Child = thumbGrid,
            BorderBrush = here ? (Brush)FindResource("Ink") : Brushes.Transparent,
        };
        var stack = new StackPanel();
        stack.Children.Add(ring);
        stack.Children.Add(text);
        var card = new Border { Width = width, Margin = new Thickness(0, 0, 14, 20), Background = Brushes.Transparent, Child = stack, Cursor = Cursors.Hand };
        // a slow, small push into the picture on hover, the only motion in the library
        var zoom = (ScaleTransform)image.RenderTransform;
        void Zoom(double to)
        {
            var a = new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(260))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        card.MouseEnter += (_, _) => { Zoom(1.04); if (!here) ring.BorderBrush = (Brush)FindResource("Line"); };
        card.MouseLeave += (_, _) => { Zoom(1); if (!here) ring.BorderBrush = Brushes.Transparent; };
        card.MouseLeftButtonUp += (_, _) => Assign(item);

        var menu = new ContextMenu();
        var toPlaylist = new MenuItem { Header = "Pôr na playlist deste monitor" };
        toPlaylist.Click += (_, _) => AddToPlaylist(item);
        menu.Items.Add(toPlaylist);
        var open = new MenuItem { Header = "Abrir a pasta" };
        open.Click += (_, _) => Process.Start("explorer.exe", $"/select,\"{item.File}\"");
        menu.Items.Add(open);
        if (item.Type == WallpaperType.Scene)
        {
            var again = new MenuItem { Header = "Converter de novo para este monitor" };
            again.Click += (_, _) => ConvertScene(item);
            menu.Items.Add(again);
            if (_config.ConvertedScenes.Any(c => c.SceneId == item.Id))
            {
                var drop = new MenuItem { Header = "Apagar as conversões desta cena" };
                drop.Click += (_, _) => DropSceneConversions(item);
                menu.Items.Add(drop);
            }
        }
        if (item.Type == WallpaperType.Package && !item.Realtime)
        {
            var again = new MenuItem { Header = "Renderizar de novo para este monitor" };
            again.Click += (_, _) => RenderPackage(item);
            menu.Items.Add(again);
            if (_config.ConvertedScenes.Any(c => c.SceneId == item.Id))
            {
                var drop = new MenuItem { Header = "Apagar os vídeos feitos deste wallpaper" };
                drop.Click += (_, _) => DropSceneConversions(item);
                menu.Items.Add(drop);
            }
        }
        if (!item.FromWallpaperEngine && !item.IsExample)
        {
            var tags = new MenuItem { Header = "Editar tags" };
            tags.Click += (_, _) => EditTags(item);
            menu.Items.Add(tags);
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

    void EditTags(LibraryItem item)
    {
        var editor = new TagEditor(this, item.Title, item.Tags, AllTags());
        if (editor.ShowDialog() != true) return;
        var result = editor.Result.ToList();
        Mutate(c =>
        {
            var u = c.UserItems.FirstOrDefault(x => x.Id == item.Id);
            if (u != null) u.Tags = result;
        });
        ReloadLibrary();
    }

    static string TypeName(WallpaperType t) => t switch { WallpaperType.Video => "Vídeo", WallpaperType.Web => "Web", WallpaperType.Scene => "Cena", WallpaperType.Package => "Vivo", _ => "Imagem" };

    void Assign(LibraryItem item)
    {
        var monitor = SelectedMonitor;
        if (monitor == null) return;
        // Ready to play here now (or made before for this monitor's size): straight to the monitor.
        if (Placement.Ready(_config, item, monitor) is { } ready)
        {
            FlushSettings();
            Mutate(c =>
            {
                c.Monitors[monitor.Id] = ready;
                if (c.Playlists.TryGetValue(monitor.Id, out var pl)) pl.Changed = DateTime.Now;   // a choice by hand restarts the clock
            });
            Notify("reload");
            RenderLibrary();
            DrawMonitors();
            UpdateSidePanel();
            return;
        }
        // A recorded scene asks for its loop first; everything else is made with nothing to choose.
        if (item.Type == WallpaperType.Scene && !item.Traits.HasFlag(SceneTraits.Direct)) { ConvertScene(item); return; }
        Enqueue(Placement.JobFor(_config, item, monitor, out string problem), problem);
    }

    void Enqueue(SceneJob job, string problem)
    {
        if (job == null) { MessageBox.Show(this, "Não deu para preparar esse wallpaper: " + problem + ".", AppPaths.AppName); return; }
        SceneJobs.Enqueue(job);
        _sceneJobSeen = null;
        RefreshSceneJob();
    }

    void RenderPackage(LibraryItem item)
    {
        var monitor = SelectedMonitor;
        if (monitor == null) return;
        Enqueue(Placement.JobFor(_config, item, monitor, out string problem), problem);
    }

    void ConvertScene(LibraryItem item)
    {
        var monitor = SelectedMonitor;
        if (monitor == null) return;
        if (VideoConverter.FindFfmpeg() == null) { MessageBox.Show(this, "Para converter cenas, o ffmpeg precisa estar no PATH.", AppPaths.AppName); return; }
        // Cut straight from its sprite sheet: the loop is the scene's own, so there is nothing to choose.
        if (item.Traits.HasFlag(SceneTraits.Direct)) { Enqueue(Placement.JobFor(_config, item, monitor, out string problem), problem); return; }
        string we = SceneConverter.FindWallpaperEngine();
        if (we == null) { MessageBox.Show(this, "O Wallpaper Engine não foi encontrado: é ele que desenha a cena para a gravação.", AppPaths.AppName); return; }
        var dialog = new SceneDialog(this, item, monitor, _config.SceneDuration, _config.SceneCrossfade, SceneConverter.WallpaperEngineFps(we), _config.SettingsFor(item.Id));
        if (dialog.ShowDialog() != true) return;
        double duration = dialog.Duration, crossfade = dialog.Crossfade;
        var chosen = dialog.Properties;
        // the options are kept with the scene, so converting it again starts from them
        Mutate(c =>
        {
            c.SceneDuration = duration;
            c.SceneCrossfade = crossfade;
            foreach (var (k, v) in chosen) c.SettingsFor(item.Id).Properties[k] = v?.DeepClone();
        });
        SceneJobs.Enqueue(new SceneJob
        {
            SceneFolder = item.Folder, SceneTitle = item.Title, MonitorId = monitor.Id, MonitorName = monitor.Label,
            Width = monitor.Bounds.Width, Height = monitor.Bounds.Height, Duration = duration, Crossfade = crossfade,
            Properties = chosen.ToDictionary(kv => kv.Key, kv => kv.Value?.DeepClone()),
        });
        _sceneJobSeen = null;
        RefreshSceneJob();
    }

    void DropSceneConversions(LibraryItem item)
    {
        var files = _config.ConvertedScenes.Where(c => c.SceneId == item.Id).Select(c => c.Output).ToList();
        Mutate(c =>
        {
            c.ConvertedScenes.RemoveAll(x => x.SceneId == item.Id);
            foreach (var id in c.Monitors.Where(kv => kv.Value?.SceneId == item.Id).Select(kv => kv.Key).ToList()) c.Monitors.Remove(id);
        });
        Notify("reload");
        // Deleted after the engine let go of them.
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(1500);
            foreach (var f in files) { try { File.Delete(f); } catch (Exception e) { Log.Write("delete converted scene: " + e.Message); } }
        });
        ReloadLibrary();
    }

    string _sceneJobSeen;
    SceneJobStatus _sceneJob;

    /// <summary>Shows the conversion that is running (or how the last one ended), from the worker's status file.</summary>
    void RefreshSceneJob()
    {
        var s = SceneJobs.Status();
        int queued = SceneJobs.Pending().Count;
        bool running = s != null && s.State == "running" && SceneJobs.WorkerRunning;
        bool recent = s != null && s.State is "done" or "error" or "canceled" && (DateTime.Now - s.Updated).TotalMinutes < 10 && s.JobId != _sceneJobSeen;
        if (s?.State == "done" && _sceneJob?.State == "running" && _sceneJob.JobId == s.JobId) { _config = ConfigStore.Load(); ReloadLibrary(); }
        _sceneJob = s;
        if (!running && !recent && queued == 0) { SceneJobBar.Visibility = Visibility.Collapsed; return; }
        SceneJobBar.Visibility = Visibility.Visible;
        string more = queued > 0 ? $" · mais {queued} na fila" : "";
        if (running)
        {
            SceneJobText.Text = $"{(s.Kind == "package" ? "Renderizando" : "Convertendo")} «{s.SceneTitle}» para {s.MonitorName}: {s.Message} {s.Progress:P0}{more}";
            SceneJobProgress.Visibility = Visibility.Visible;
            SceneJobProgress.Value = s.Progress;
            SceneJobButton.Content = "Cancelar";
        }
        else if (recent)
        {
            SceneJobText.Text = s.State switch
            {
                "done" => s.Kind == "package" ? $"«{s.SceneTitle}» virou vídeo e foi para {s.MonitorName}.{more}" : $"«{s.SceneTitle}» convertida e posta em {s.MonitorName}.{more}",
                "canceled" => $"{(s.Kind == "package" ? "Renderização" : "Conversão")} de «{s.SceneTitle}» cancelada.{more}",
                _ => $"{(s.Kind == "package" ? "A renderização" : "A conversão")} de «{s.SceneTitle}» falhou: {s.Message}{more}",
            };
            SceneJobProgress.Visibility = Visibility.Collapsed;
            SceneJobButton.Content = "Fechar";
        }
        else
        {
            SceneJobText.Text = $"Esperando para converter{more}";
            SceneJobProgress.Visibility = Visibility.Collapsed;
            SceneJobButton.Content = "Fechar";
        }
    }

    void SceneJob_Click(object sender, RoutedEventArgs e)
    {
        if (_sceneJob != null && _sceneJob.State == "running") SceneJobs.Cancel(_sceneJob.JobId);
        else { _sceneJobSeen = _sceneJob?.JobId; SceneJobBar.Visibility = Visibility.Collapsed; }
    }

    void AddFile_Click(object sender, RoutedEventArgs e)
    {
        string Join(string[] exts) => string.Join(";", exts.Select(x => "*" + x));
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha vídeos, imagens, páginas HTML ou wallpapers .vivo",
            Multiselect = true,
            Filter = $"Wallpapers|{Join(Library.VideoExtensions)};{Join(Library.ImageExtensions)};{Join(Library.WebExtensions)};*{Packages.Extension}|Vídeo|{Join(Library.VideoExtensions)}|Imagem|{Join(Library.ImageExtensions)}|Página web|{Join(Library.WebExtensions)}|Fundo Vivo|*{Packages.Extension}",
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
                // A .vivo brings its own title and tags; from here on the tags are the user's to edit.
                var info = type == WallpaperType.Package ? Packages.Inspect(f) : null;
                if (type == WallpaperType.Package && info == null) continue;
                c.UserItems.Add(new UserItem
                {
                    Id = id, File = f, Type = type.Value, Title = info?.Title ?? Path.GetFileNameWithoutExtension(f), Added = DateTime.Now,
                    Tags = info?.Tags.ToList() ?? new List<string>(),
                });
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
            // the monitor's name in full ink, its size and rate in the softer grey after it
            SideMonitorName.Inlines.Clear();
            if (m != null)
            {
                SideMonitorName.Inlines.Add(new System.Windows.Documents.Run(m.Label));
                var details = new System.Windows.Documents.Run($"   {m.Bounds.Width}×{m.Bounds.Height} · {m.RefreshRate:0} Hz{(m.Primary ? " · principal" : "")}")
                { FontFamily = (FontFamily)FindResource("Text"), FontWeight = FontWeights.Normal, FontSize = 12.5 };
                details.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink2");
                SideMonitorName.Inlines.Add(details);
            }
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

            // a .vivo makes no sound of its own
            SoundSection.Visibility = a.Type == WallpaperType.Image || a.Package != null ? Visibility.Collapsed : Visibility.Visible;
            VolumeSlider.Value = s.Volume;
            VolumeText.Text = VolumeLabel(s.Volume, a.Type);

            PropertiesSection.Visibility = Visibility.Collapsed;
            if (a.Type == WallpaperType.Web && a.Package == null)
            {
                var project = WeProject.Load(Path.GetDirectoryName(a.File));
                if (project != null && PropertyEditor.Build(PropertiesPanel, project, s, (key, value) => QueueSettings(a.ItemId, x => x.Properties[key] = value?.DeepClone())) > 0)
                    PropertiesSection.Visibility = Visibility.Visible;
            }
            _ = UpdateOptimizeAsync(m, a);
            _ = UpdateFpsLineAsync(m, a);
            RenderPlaylist();
        }
        finally { _loading = false; }
    }

    // Border.CornerRadius does not clip what is inside; the preview is cut to the same rounded shape by hand.
    void SideThumbHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        SideThumbHost.Clip = new RectangleGeometry(new Rect(e.NewSize), 16, 16);

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
        SideDetail.Text = a.Package != null
            ? a.Type == WallpaperType.Web
                ? (_scan.Items.FirstOrDefault(i => i.Id == a.ItemId)?.ReactsToSound == true ? $"Vivo ao vivo (reage ao som) · {state}" : $"Vivo ao vivo · {state}")
                : $"Vivo renderizado em vídeo · {state}" + (a.ParallaxMargin > 0 ? ". O parallax virou um pan com o mouse: o vídeo inteiro anda junto." : "")
            : a.SceneId != null && Path.GetExtension(_scan.Items.FirstOrDefault(i => i.Id == a.ItemId)?.File ?? "").Equals(".gif", StringComparison.OrdinalIgnoreCase)
            ? $"GIF animado, tocando como vídeo · {state}"
            : a.SceneId != null
            ? $"Cena convertida · {state}" + (a.ParallaxMargin > 0 ? ". O parallax virou um pan com o mouse: o vídeo inteiro anda junto, sem a profundidade de cada camada." : "")
            : $"{TypeName(a.Type)} · {source} · {state}";
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

    /// <summary>Says what the wallpaper on this monitor really runs at, and why; video keeps the frame rate it was made with.</summary>
    async Task UpdateFpsLineAsync(MonitorInfo m, MonitorAssignment a)
    {
        SideFps.Text = "";
        if (m == null || a == null) return;
        int hz = (int)Math.Round(m.RefreshRate);
        switch (a.Type)
        {
            case WallpaperType.Web:
                int fps = _config.EffectiveFps(m.RefreshRate);
                SideFps.Text = fps < _config.FpsLimit
                    ? $"Roda a {fps} fps, o máximo deste monitor ({hz} Hz)."
                    : $"Roda a {fps} fps, o limite escolhido em Ajustes.";
                break;
            case WallpaperType.Image:
                SideFps.Text = "Imagem parada: não desenha quadro nenhum.";
                break;
            case WallpaperType.Video:
                string file = a.File;
                double rate = 0;
                try
                {
                    var f = await Windows.Storage.StorageFile.GetFileFromPathAsync(file);
                    var props = await f.Properties.RetrievePropertiesAsync(new[] { "System.Video.FrameRate" });
                    // Stored as frames per 1000 seconds.
                    if (props.TryGetValue("System.Video.FrameRate", out object v) && v is uint milli) rate = milli / 1000.0;
                }
                catch (Exception e) { Log.Write("frame rate: " + e.Message); }
                if (SelectedAssignment?.File != file) return;
                SideFps.Text = rate > 0
                    ? $"Vídeo toca no fps em que foi gravado ({rate:0.##} fps); o limite de fps não muda vídeo."
                    : "Vídeo toca no fps em que foi gravado; o limite de fps não muda vídeo.";
                break;
        }
    }

    async Task UpdateOptimizeAsync(MonitorInfo m, MonitorAssignment a)
    {
        // A converted scene is already made for this monitor (with the parallax margin on purpose).
        if (m == null || a == null || a.Type != WallpaperType.Video || a.SceneId != null)
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
            RenderProgramRules();
            FpsChoices.Children.Clear();
            foreach (int fps in AppConfig.FpsChoices)
            {
                var choice = new RadioButton
                {
                    Content = fps.ToString(CultureInfo.InvariantCulture), GroupName = "fps", Style = (Style)FindResource("Segment"),
                    IsChecked = _config.FpsLimit == fps, Tag = fps,
                };
                if (fps == AppConfig.FpsChoices[^1]) choice.Margin = new Thickness(0);
                choice.Checked += Fps_Checked;
                FpsChoices.Children.Add(choice);
            }
            VersionText.Text = $"Versão {AppPaths.Version}" + (AppPaths.IsDevelopmentCopy ? " · cópia de desenvolvimento (atualiza pelo git e pelo compilar.ps1)" : "");
            TokenText.Text = string.IsNullOrEmpty(Secrets.GitHubToken)
                ? "Token de leitura (fine-grained, só este repositório, Contents: leitura). Fica guardado protegido pelo Windows, fora do app."
                : "Já tem um token guardado. Para trocar, cole outro e guarde; para tirar, guarde vazio.";
        }
        finally { _loading = false; }
    }

    // ------------------------------------------------------------------ playlist of the selected monitor

    void RenderPlaylist()
    {
        var m = SelectedMonitor;
        PlaylistEntries.Children.Clear();
        if (m == null) return;
        var pl = _config.Playlists.GetValueOrDefault(m.Id);
        int count = pl?.Entries.Count ?? 0;
        string current = SelectedAssignment?.ItemId;
        PlaylistHint.Text = count switch
        {
            0 => "Botão direito num wallpaper da biblioteca: Pôr na playlist deste monitor. Com dois ou mais, ele troca sozinho.",
            1 => "Com mais um, ele troca sozinho.",
            _ => pl.Enabled ? $"Troca a cada {Interval(pl.Minutes)}, {(pl.Shuffle ? "aleatório" : "em ordem")}, só enquanto o monitor está tocando (nunca no meio de um jogo)." : "Parada: ligue Trocar sozinho.",
        };
        for (int i = 0; i < count; i++)
        {
            var e = pl.Entries[i];
            int index = i;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = e.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontWeight = e.ItemId == current ? FontWeights.SemiBold : FontWeights.Normal };
            if (e.ItemId != current) name.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
            row.Children.Add(name);
            var remove = new Button { Content = "Tirar", Style = (Style)FindResource("Quiet"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
            remove.Click += (_, _) => { Mutate(c => { if (c.Playlists.TryGetValue(m.Id, out var p) && index < p.Entries.Count) p.Entries.RemoveAt(index); }); Notify("settings"); RenderPlaylist(); };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            PlaylistEntries.Children.Add(row);
        }
        bool loading = _loading;
        _loading = true;
        PlaylistOptions.Visibility = count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistNext.Visibility = count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistAddCurrent.Visibility = SelectedAssignment != null && (pl?.Entries.All(x => x.ItemId != current) ?? true) ? Visibility.Visible : Visibility.Collapsed;
        PlaylistOn.IsChecked = pl?.Enabled ?? true;
        PlaylistShuffle.IsChecked = pl?.Shuffle == true;
        PlaylistInOrder.IsChecked = pl?.Shuffle != true;
        PlaylistMinutes.Children.Clear();
        foreach (int minutes in Playlist.MinuteChoices)
        {
            var choice = new RadioButton { Content = Interval(minutes), GroupName = "plmin", Style = (Style)FindResource("Segment"), IsChecked = (pl?.Minutes ?? 30) == minutes, Tag = minutes };
            if (minutes == Playlist.MinuteChoices[^1]) choice.Margin = new Thickness(0);
            choice.Checked += Playlist_Changed;
            PlaylistMinutes.Children.Add(choice);
        }
        _loading = loading;
    }

    static string Interval(int minutes) => minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";

    void Playlist_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || SelectedMonitor == null) return;
        string id = SelectedMonitor.Id;
        int minutes = PlaylistMinutes.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag is int t ? t : 30;
        Mutate(c =>
        {
            if (!c.Playlists.TryGetValue(id, out var pl)) return;
            pl.Enabled = PlaylistOn.IsChecked == true;
            pl.Shuffle = PlaylistShuffle.IsChecked == true;
            pl.Minutes = minutes;
        });
        Notify("settings");
        RenderPlaylist();
    }

    void AddToPlaylist(LibraryItem item)
    {
        var m = SelectedMonitor;
        if (m == null) return;
        if (Placement.Ready(_config, item, m) is not { } ready)
        {
            MessageBox.Show(this, $"«{item.Title}» precisa virar vídeo para este monitor antes de entrar na playlist: clique nele uma vez e, quando estiver pronto, ponha de novo.", AppPaths.AppName);
            return;
        }
        var current = SelectedAssignment;
        Mutate(c =>
        {
            if (!c.Playlists.TryGetValue(m.Id, out var pl)) c.Playlists[m.Id] = pl = new Playlist { Changed = DateTime.Now };
            // what is on the monitor now comes in first, so the list starts from it
            if (pl.Entries.Count == 0 && current != null && current.ItemId != item.Id) pl.Entries.Add(current);
            if (pl.Entries.All(e => e.ItemId != item.Id)) pl.Entries.Add(ready);
        });
        Notify("settings");
        UpdateSidePanel();
    }

    void PlaylistAddCurrent_Click(object sender, RoutedEventArgs e)
    {
        var m = SelectedMonitor;
        var a = SelectedAssignment;
        if (m == null || a == null) return;
        Mutate(c =>
        {
            if (!c.Playlists.TryGetValue(m.Id, out var pl)) c.Playlists[m.Id] = pl = new Playlist { Changed = DateTime.Now };
            if (pl.Entries.All(x => x.ItemId != a.ItemId)) pl.Entries.Add(a);
        });
        Notify("settings");
        RenderPlaylist();
    }

    void PlaylistNext_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMonitor == null) return;
        if (!EngineHost.Send(new JsonObject { ["cmd"] = "next", ["monitor"] = SelectedMonitor.Id }.ToJsonString())) return;
        Dispatcher.BeginInvoke(async () => { await Task.Delay(600); _config = ConfigStore.Load(); RefreshState(); DrawMonitors(); UpdateSidePanel(); RenderLibrary(); });
    }

    // ------------------------------------------------------------------ programs that pause everything

    void RenderProgramRules()
    {
        ProgramRulesList.Children.Clear();
        var rules = _config.Pause.Programs;
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            int index = i;
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = r.Exe, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var bar = new Border { Style = (Style)FindResource("SegmentBar"), Margin = new Thickness(8, 0, 0, 0) };
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (action, label) in new[] { (ProgramAction.Pause, "Pausar"), (ProgramAction.Unload, "Tirar da memória") })
            {
                var choice = new RadioButton { Content = label, GroupName = "rule" + i, Style = (Style)FindResource("Segment"), IsChecked = r.Action == action };
                if (action == ProgramAction.Unload) choice.Margin = new Thickness(0);
                choice.Checked += (_, _) =>
                {
                    if (_loading) return;
                    Mutate(c => { if (index < c.Pause.Programs.Count) c.Pause.Programs[index].Action = action; });
                    Notify("settings");
                };
                seg.Children.Add(choice);
            }
            bar.Child = seg;
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            var remove = new Button { Content = "Tirar", Style = (Style)FindResource("Quiet"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
            remove.Click += (_, _) => { Mutate(c => { if (index < c.Pause.Programs.Count) c.Pause.Programs.RemoveAt(index); }); Notify("settings"); RenderProgramRules(); };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            ProgramRulesList.Children.Add(row);
        }
    }

    void AddProgramRule(string exe)
    {
        exe = exe?.Trim();
        if (string.IsNullOrEmpty(exe)) return;
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
        Mutate(c =>
        {
            if (c.Pause.Programs.All(r => !r.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)))
                c.Pause.Programs.Add(new ProgramRule { Exe = exe, Action = ProgramAction.Pause });
        });
        Notify("settings");
        ProgramRuleBox.Text = "";
        RenderProgramRules();
    }

    void AddProgramRule_Click(object sender, RoutedEventArgs e) => AddProgramRule(ProgramRuleBox.Text);
    void ProgramRuleBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddProgramRule(ProgramRuleBox.Text); }
    void ProgramRuleBox_TextChanged(object sender, TextChangedEventArgs e) => ProgramRuleHint.Visibility = ProgramRuleBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The programs with a window open right now, by name, to pick from.</summary>
    void PickProgram_Click(object sender, RoutedEventArgs e)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
            using (p)
                try { if (p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId) names.Add(p.ProcessName + ".exe"); } catch { }
        var menu = new ContextMenu { PlacementTarget = PickProgramButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (string n in names)
        {
            var item = new MenuItem { Header = n };
            item.Click += (_, _) => AddProgramRule(n);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
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

    void Fps_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded || sender is not RadioButton { Tag: int fps }) return;
        Mutate(c => c.FpsLimit = fps);
        Notify("settings");
        _ = UpdateFpsLineAsync(SelectedMonitor, SelectedAssignment);
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
            case "tags":
            {
                // argument: a file to add as the user's own; gets the tags "anime" (must become the existing "Anime") and "Meu".
                Mutate(c =>
                {
                    string id = Library.IdFor(argument);
                    c.UserItems.RemoveAll(u => u.Id == id);
                    var existing = AllTags().ToList();
                    c.UserItems.Add(new UserItem
                    {
                        Id = id, File = argument, Type = Library.TypeForFile(argument) ?? WallpaperType.Video, Title = "Teste de tags",
                        Added = DateTime.Now, Tags = new List<string> { Tags.Canonical("anime", existing), Tags.Canonical("Meu", existing) },
                    });
                });
                ReloadLibrary();
                _tagFilter.Add(Tags.Key("Anime"));
                _ratingFilter.Add("Livre");
                RenderLibrary();
                int both = LibraryList.Items.OfType<FrameworkElement>().Count(x => x is Border);
                _ratingFilter.Clear();
                RenderLibrary();
                int anime = LibraryList.Items.OfType<FrameworkElement>().Count(x => x is Border);
                var mine = ConfigStore.Load().UserItems.First(u => u.Title == "Teste de tags");
                Log.Write($"selftest tags: Anime+Livre={both} Anime={anime} chips={TagFilters.Children.Count} saved=[{string.Join(", ", mine.Tags)}]");
                break;
            }
            case "tageditor":
            {
                // Opens the editor off-screen for one of the user's files, types like a person would, saves a picture of it.
                var own = _scan.Items.First(i => !i.FromWallpaperEngine);
                var editor = new TagEditor(this, own.Title, own.Tags, AllTags()) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false };
                editor.Show();
                string suggestions = string.Join(", ", editor.Suggest("na"));
                editor.Type("ANIME");
                editor.Type("Relaxante");
                Log.Write($"selftest tageditor: suggestions for 'na' = [{suggestions}] result=[{string.Join(", ", editor.Result)}]");
                editor.UpdateLayout();
                var root = (FrameworkElement)editor.Content;
                double scale = VisualTreeHelper.GetDpi(editor).DpiScaleX;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen()) dc.DrawRectangle((Brush)FindResource("Paper"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                bmp.Render(bg);
                bmp.Render(root);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var fs = File.Create(Path.Combine(AppPaths.Local, "tageditor.png"))) png.Save(fs);
                editor.Close();
                break;
            }
            case "scenes":
                // Shows the scene filter, with one classification tag checked when given (e.g. "Parallax vira pan").
                FilterScene.IsChecked = true;
                if (argument != "-") _tagFilter.Add(Tags.Key(argument));
                RenderLibrary();
                Log.Write($"selftest scenes: {LibraryList.Items.OfType<Border>().Count()} cards");
                break;
            case "scenedialog":
            {
                // argument: part of a scene's folder; the convert dialog off-screen, saved as a picture.
                var scene = _scan.Items.First(i => i.Type == WallpaperType.Scene && i.Folder.Contains(argument, StringComparison.OrdinalIgnoreCase));
                var monitor = SelectedMonitor ?? _monitors.First();
                var dialog = new SceneDialog(this, scene, monitor, _config.SceneDuration, _config.SceneCrossfade, 30, _config.SettingsFor(scene.Id))
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false };
                dialog.Show();
                dialog.UpdateLayout();
                var root = (FrameworkElement)dialog.Content;
                double scale = VisualTreeHelper.GetDpi(dialog).DpiScaleX;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)((root.ActualWidth + root.Margin.Left + root.Margin.Right) * scale), (int)((root.ActualHeight + root.Margin.Top + root.Margin.Bottom) * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen()) dc.DrawRectangle((Brush)FindResource("Paper"), null, new Rect(0, 0, root.ActualWidth + root.Margin.Left + root.Margin.Right, root.ActualHeight + root.Margin.Top + root.Margin.Bottom));
                bmp.Render(bg);
                bmp.Render(root);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var fs = File.Create(Path.Combine(AppPaths.Local, "scenedialog.png"))) png.Save(fs);
                Log.Write($"selftest scenedialog: {scene.Title} duration={dialog.Duration} options={dialog.Properties.Count}");
                dialog.Close();
                break;
            }
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
