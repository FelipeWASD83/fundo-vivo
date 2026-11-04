using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FundoVivo.Settings;

/// <summary>
/// Before converting a scene: what the video will lose (from the classification), how long the loop is and how long
/// the blend at the seam lasts, and what happens on screen while it records.
/// </summary>
public sealed class SceneDialog : Window
{
    readonly Slider _duration = new() { Minimum = 10, Maximum = 60, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly Slider _crossfade = new() { Minimum = 0.5, Maximum = 3, TickFrequency = 0.5, IsSnapToTickEnabled = true };

    public double Duration => _duration.Value;
    public double Crossfade => _crossfade.Value;
    /// <summary>The author's options as chosen here (all of them, so the copy gets exactly what was on screen).</summary>
    public Dictionary<string, JsonNode> Properties { get; } = new();

    public SceneDialog(Window owner, LibraryItem scene, MonitorInfo monitor, double duration, double crossfade, int fps, ItemSettings settings)
    {
        Owner = owner;
        Title = Loc.T("dlg_scene_title");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Style = (Style)FindResource("WindowText");
        SourceInitialized += (_, _) => ThemeManager.SetTitleBar(this);
        // the scene's own timeline decides the default when it declares one
        _duration.Value = Math.Clamp(scene.SuggestedLoop > 0 ? scene.SuggestedLoop : duration, 10, 60);
        _crossfade.Value = Math.Clamp(crossfade, 0.5, 3);

        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        root.Children.Add(new TextBlock { Text = scene.Title, Style = (Style)FindResource("H2"), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(Muted(Loc.T("dlg_scene_intro", monitor.Label, monitor.Bounds.Width, monitor.Bounds.Height, fps)));

        var traits = scene.Traits;
        var notes = new List<string>();
        if (traits.HasFlag(SceneTraits.Unreadable))
            notes.Add(Loc.T("dlg_scene_note_unreadable"));
        else if ((traits & (SceneTraits.Parallax | SceneTraits.Sound | SceneTraits.Mouse | SceneTraits.Clock)) == 0)
            notes.Add(Loc.T("dlg_scene_note_lossless"));
        if (traits.HasFlag(SceneTraits.Parallax))
            notes.Add(Loc.T("dlg_scene_note_parallax"));
        if (traits.HasFlag(SceneTraits.Sound))
            notes.Add(Loc.T("dlg_scene_note_sound"));
        if (traits.HasFlag(SceneTraits.Mouse))
            notes.Add(Loc.T("dlg_scene_note_mouse"));
        if (traits.HasFlag(SceneTraits.Clock))
            notes.Add(Loc.T("dlg_scene_note_clock"));
        if (traits.HasFlag(SceneTraits.BackgroundSound))
            notes.Add(Loc.T("dlg_scene_note_bgsound"));
        if (scene.SuggestedLoop > 0)
            notes.Add(Loc.T("dlg_scene_note_loop", scene.SuggestedLoop.ToString("0.##", CultureInfo.CurrentCulture)));
        root.Children.Add(new TextBlock { Text = Loc.T("dlg_scene_what_changes"), Style = (Style)FindResource("Label") });
        foreach (string n in notes) root.Children.Add(Muted("• " + n, new Thickness(0, 0, 0, 4)));

        root.Children.Add(new TextBlock { Text = Loc.T("dlg_scene_loop_length"), Style = (Style)FindResource("Label") });
        root.Children.Add(Row(_duration, v => $"{v:0} s"));
        root.Children.Add(new TextBlock { Text = Loc.T("dlg_scene_seam"), Style = (Style)FindResource("Label") });
        root.Children.Add(Row(_crossfade, v => v.ToString("0.0", CultureInfo.CurrentCulture) + " s"));

        // The author's options, the same editor as the web wallpapers; the choices go into the copy that is recorded.
        var project = WeProject.Load(scene.Folder);
        if (project != null)
        {
            var panel = new StackPanel();
            var current = new ItemSettings { Properties = new Dictionary<string, JsonNode>(settings?.Properties ?? new()) };
            foreach (var (k, v) in project.UserProperties(current)) Properties[k] = v?["value"]?.DeepClone();
            if (PropertyEditor.Build(panel, project, current, (key, value) => Properties[key] = value?.DeepClone()) > 0)
            {
                root.Children.Add(new TextBlock { Text = Loc.T("dlg_scene_options"), Style = (Style)FindResource("Label") });
                root.Children.Add(new ScrollViewer { Content = panel, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            }
        }

        root.Children.Add(Muted(
            Loc.T("dlg_scene_recording"),
            new Thickness(0, 16, 0, 0)));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = Loc.T("dlg_cancel"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var go = new Button { Content = Loc.T("dlg_scene_convert"), Style = (Style)FindResource("Primary"), IsDefault = true };
        go.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(go);
        root.Children.Add(buttons);
        Content = root;
    }

    TextBlock Muted(string text, Thickness? margin = null) =>
        new() { Text = text, Style = (Style)FindResource("Muted"), Margin = margin ?? new Thickness(0, 4, 0, 0) };

    FrameworkElement Row(Slider slider, Func<double, string> format)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        var value = new TextBlock { Text = format(slider.Value), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Ink2") };
        slider.ValueChanged += (_, e) => value.Text = format(e.NewValue);
        slider.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 1);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        return grid;
    }
}
