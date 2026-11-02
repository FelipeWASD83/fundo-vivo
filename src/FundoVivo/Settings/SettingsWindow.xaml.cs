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

    readonly HashSet<string> _tagFilter = new();
    readonly HashSet<string> _ratingFilter = new();

    /// <summary>One chip per tag of the current type filter, most used first, then the ratings, then Limpar.</summary>
    void RenderTagFilters(List<LibraryItem> ofType)
    {
        TagFilters.Children.Clear();
        var groups = ofType.SelectMany(i => i.Tags.Select(t => (key: Tags.Key(t), name: t, item: i.Id)))
            .Where(x => x.key.Length > 0)
            .GroupBy(x => x.key)
            .Select(g => (key: g.Key, name: g.First().name, count: g.Select(x => x.item).Distinct().Count()))
            .OrderByDescending(g => g.count).ThenBy(g => g.name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        // A checked tag that the current type filter no longer has stays visible, so it can be unchecked.
        foreach (string key in _tagFilter.Where(k => groups.All(g => g.key != k)).ToList())
            groups.Add((key, _scan.Items.SelectMany(i => i.Tags).FirstOrDefault(t => Tags.Key(t) == key) ?? key, 0));
        foreach (var g in groups)
            TagFilters.Children.Add(Chip(g.name, g.count, _tagFilter.Contains(g.key), on => { if (on) _tagFilter.Add(g.key); else _tagFilter.Remove(g.key); }));

        var ratings = Tags.RatingOrder.Select(r => (name: r, count: ofType.Count(i => i.Rating == r))).Where(r => r.count > 0 || _ratingFilter.Contains(r.name)).ToList();
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
        string tip = item.Title + (item.Tags.Count > 0 ? "\n" + string.Join(" · ", item.Tags) : "") + (item.Rating != null ? "\nClassificação: " + item.Rating : "");
        text.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = tip });
        string meta = TypeName(item.Type) + (item.FromWallpaperEngine ? " · Wallpaper Engine" : item.IsExample ? " · exemplo" : " · seu arquivo");
        if (item.Type == WallpaperType.Package)
        {
            string stamp = Packages.Stamp(item.File);
            var done = _config.ConvertedScenes.Where(c => c.SceneId == item.Id && c.SourceStamp == stamp && File.Exists(c.Output)).Select(c => $"{c.MonitorWidth}×{c.MonitorHeight}").Distinct().ToList();
            meta += item.Realtime ? " · ao vivo, reage ao som" : done.Count > 0 ? " · vídeo pronto: " + string.Join(", ", done) : " · vira vídeo ao usar";
        }
        if (item.Type == WallpaperType.Scene)
        {
            var done = _config.ConvertedScenes.Where(c => c.SceneId == item.Id && File.Exists(c.Output)).Select(c => $"{c.MonitorWidth}×{c.MonitorHeight}").Distinct().ToList();
            meta += done.Count > 0 ? " · convertida: " + string.Join(", ", done) : " · converter para usar";
        }
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
        if (item.Type == WallpaperType.Scene)
        {
            // A scene plays as the video recorded for this monitor's size; without one, it is converted first.
            var done = _config.ConvertedScenes.FirstOrDefault(c => c.SceneId == item.Id && c.MonitorWidth == monitor.Bounds.Width
                && c.MonitorHeight == monitor.Bounds.Height && File.Exists(c.Output));
            if (done == null) { ConvertScene(item); return; }
            FlushSettings();
            Mutate(c => c.Monitors[monitor.Id] = new MonitorAssignment
            {
                ItemId = item.Id, Type = WallpaperType.Video, File = done.Output, Title = item.Title, MonitorName = monitor.Label,
                SceneId = item.Id, ParallaxMargin = done.Margin,
            });
            Notify("reload");
            RenderLibrary();
            DrawMonitors();
            UpdateSidePanel();
            return;
        }
        if (item.Type == WallpaperType.Package && !item.Realtime)
        {
            // Nothing in it depends on the moment: it plays as the video rendered for this monitor, made first if needed.
            string stamp = Packages.Stamp(item.File);
            var done = _config.ConvertedScenes.FirstOrDefault(c => c.SceneId == item.Id && c.MonitorWidth == monitor.Bounds.Width
                && c.MonitorHeight == monitor.Bounds.Height && c.SourceStamp == stamp && File.Exists(c.Output));
            if (done == null) { RenderPackage(item); return; }
            FlushSettings();
            Mutate(c => c.Monitors[monitor.Id] = new MonitorAssignment
            {
                ItemId = item.Id, Type = WallpaperType.Video, File = done.Output, Title = item.Title, MonitorName = monitor.Label,
                SceneId = item.Id, ParallaxMargin = done.Margin, Package = item.File,
            });
            Notify("reload");
            RenderLibrary();
            DrawMonitors();
            UpdateSidePanel();
            return;
        }
        FlushSettings();
        // A .vivo that reacts to sound plays live: the engine opens its runtime page like a web wallpaper.
        bool live = item.Type == WallpaperType.Package;
        Mutate(c => c.Monitors[monitor.Id] = new MonitorAssignment
        {
            ItemId = item.Id, Type = live ? WallpaperType.Web : item.Type, File = item.File, Title = item.Title, MonitorName = monitor.Label,
            Package = live ? item.File : null,
        });
        Notify("reload");
        RenderLibrary();
        DrawMonitors();
        UpdateSidePanel();
    }

    void RenderPackage(LibraryItem item)
    {
        var monitor = SelectedMonitor;
        if (monitor == null) return;
        if (VideoConverter.FindFfmpeg() == null) { MessageBox.Show(this, "Para transformar o wallpaper em vídeo, o ffmpeg precisa estar no PATH.", AppPaths.AppName); return; }
        var info = Packages.Inspect(item.File);
        if (info == null) { MessageBox.Show(this, "Não deu para ler esse arquivo .vivo (veja o registro).", AppPaths.AppName); return; }
        SceneJobs.Enqueue(new SceneJob
        {
            Kind = "package", ItemId = item.Id, PackageFile = item.File, SceneTitle = item.Title, MonitorId = monitor.Id, MonitorName = monitor.Label,
            Width = monitor.Bounds.Width, Height = monitor.Bounds.Height, Duration = info.Loop, Crossfade = 0,
            Fps = Math.Min(60, _config.EffectiveFps(monitor.RefreshRate)),
        });
        _sceneJobSeen = null;
        RefreshSceneJob();
    }

    void ConvertScene(LibraryItem item)
    {
        var monitor = SelectedMonitor;
        if (monitor == null) return;
        if (VideoConverter.FindFfmpeg() == null) { MessageBox.Show(this, "Para converter cenas, o ffmpeg precisa estar no PATH.", AppPaths.AppName); return; }
        string we = SceneConverter.FindWallpaperEngine();
        if (we == null) { MessageBox.Show(this, "O Wallpaper Engine não foi encontrado: é ele que desenha a cena para a gravação.", AppPaths.AppName); return; }
        var dialog = new SceneDialog(this, item, monitor, _config.SceneDuration, _config.SceneCrossfade, SceneConverter.WallpaperEngineFps(we));
        if (dialog.ShowDialog() != true) return;
        double duration = dialog.Duration, crossfade = dialog.Crossfade;
        Mutate(c => { c.SceneDuration = duration; c.SceneCrossfade = crossfade; });
        SceneJobs.Enqueue(new SceneJob
        {
            SceneFolder = item.Folder, SceneTitle = item.Title, MonitorId = monitor.Id, MonitorName = monitor.Label,
            Width = monitor.Bounds.Width, Height = monitor.Bounds.Height, Duration = duration, Crossfade = crossfade,
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
        SideDetail.Text = a.Package != null
            ? a.Type == WallpaperType.Web
                ? $"Vivo ao vivo (reage ao som) · {state}"
                : $"Vivo renderizado em vídeo · {state}" + (a.ParallaxMargin > 0 ? ". O parallax virou um pan com o mouse: o vídeo inteiro anda junto." : "")
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
