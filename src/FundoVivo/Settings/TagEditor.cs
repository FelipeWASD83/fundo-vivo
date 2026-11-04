using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FundoVivo.Settings;

/// <summary>
/// Small dialog to edit the tags of one of the user's own files. While typing it suggests the tags that already
/// exist anywhere in the library, and a typed tag that matches one of them (ignoring case and accents) takes the
/// existing spelling, so "anime" never sits next to "Anime".
/// </summary>
public sealed class TagEditor : Window
{
    readonly List<string> _tags;
    readonly List<string> _existing;
    readonly WrapPanel _chips = new() { Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBox _input = new();
    readonly Popup _popup = new() { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
    readonly StackPanel _suggestions = new();

    public IReadOnlyList<string> Result => _tags;

    public TagEditor(Window owner, string title, IEnumerable<string> current, IEnumerable<string> existing)
    {
        Owner = owner;
        Title = Loc.T("dlg_tags_title", title);
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Style = (Style)FindResource("WindowText");
        _tags = current.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _existing = existing.Where(t => !string.IsNullOrWhiteSpace(t)).GroupBy(Tags.Key).Select(g => g.First()).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
        SourceInitialized += (_, _) => ThemeManager.SetTitleBar(this);

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        root.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("H2"), TextTrimming = TextTrimming.CharacterEllipsis });
        root.Children.Add(new TextBlock { Text = Loc.T("dlg_tags_hint"), Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(_chips);

        var border = new Border
        {
            Background = (Brush)FindResource("Surface"), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Child = new ScrollViewer { MaxHeight = 220, Content = _suggestions },
        };
        _popup.Child = border;
        _popup.PlacementTarget = _input;
        root.Children.Add(_input);
        root.Children.Add(_popup);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = Loc.T("dlg_cancel"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = Loc.T("dlg_save"), Style = (Style)FindResource("Primary") };
        save.Click += (_, _) => { AddTyped(); DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);
        Content = root;

        _input.TextChanged += (_, _) => ShowSuggestions();
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { AddTyped(); e.Handled = true; }
            else if (e.Key == Key.Back && _input.Text.Length == 0 && _tags.Count > 0) { _tags.RemoveAt(_tags.Count - 1); RenderChips(); }
            else if (e.Key == Key.Down && _popup.IsOpen && _suggestions.Children.Count > 0) { ((UIElement)_suggestions.Children[0]).Focus(); e.Handled = true; }
        };
        Loaded += (_, _) => { RenderChips(); _input.Focus(); };
    }

    void AddTyped()
    {
        Add(_input.Text);
        _input.Clear();
    }

    void Add(string typed)
    {
        string tag = Tags.Canonical(typed, _existing.Concat(_tags));
        if (tag == null || _tags.Any(t => Tags.Key(t) == Tags.Key(tag))) return;
        _tags.Add(tag);
        RenderChips();
        _popup.IsOpen = false;
    }

    void RenderChips()
    {
        _chips.Children.Clear();
        if (_tags.Count == 0)
            _chips.Children.Add(new TextBlock { Text = Loc.T("dlg_tags_none"), Style = (Style)FindResource("Muted") });
        foreach (string tag in _tags.ToList())
        {
            var remove = new Button { Content = "×", Style = (Style)FindResource("Quiet"), Padding = new Thickness(6, 0, 2, 0), FontSize = 14, ToolTip = Loc.T("dlg_tags_remove") };
            remove.Click += (_, _) => { _tags.Remove(tag); RenderChips(); _input.Focus(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = tag, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(remove);
            var chip = new Border
            {
                Background = (Brush)FindResource("Well"), Padding = new Thickness(11, 3, 4, 3),
                Margin = new Thickness(0, 0, 6, 6), Child = row,
            };
            PillRadius.Apply(chip);
            _chips.Children.Add(chip);
        }
    }

    /// <summary>Existing tags containing what was typed (ignoring case and accents), the ones starting with it first.</summary>
    internal List<string> Suggest(string typed)
    {
        string key = Tags.Key(typed);
        if (key.Length == 0) return new List<string>();
        return _existing
            .Where(t => Tags.Key(t).Contains(key) && _tags.All(x => Tags.Key(x) != Tags.Key(t)))
            .OrderBy(t => Tags.Key(t).StartsWith(key) ? 0 : 1).ThenBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .Take(8).ToList();
    }

    /// <summary>Self-test hook: the same path as typing and pressing Enter.</summary>
    internal void Type(string text) { _input.Text = text; AddTyped(); }

    void ShowSuggestions()
    {
        _suggestions.Children.Clear();
        var matches = Suggest(_input.Text);
        if (matches.Count == 0) { _popup.IsOpen = false; return; }
        foreach (string tag in matches)
        {
            var item = new Button
            {
                Content = tag, Style = (Style)FindResource("Quiet"), HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = (Brush)FindResource("Ink"), Padding = new Thickness(10, 5, 10, 5),
            };
            item.Click += (_, _) => { Add(tag); _input.Clear(); _input.Focus(); };
            _suggestions.Children.Add(item);
        }
        _popup.MinWidth = _input.ActualWidth;
        _popup.IsOpen = matches.Count > 0;
    }
}
