using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace FundoVivo.Settings;

/// <summary>Builds controls for the options a Wallpaper Engine web project declares in project.json.</summary>
public static class PropertyEditor
{
    public static int Build(Panel panel, WeProject project, ItemSettings settings, Action<string, JsonNode> changed)
    {
        panel.Children.Clear();
        int count = 0;
        var ordered = project.Properties
            .Where(kv => kv.Value is JsonObject)
            .OrderBy(kv => (kv.Value["order"]?.GetValueKind() == JsonValueKind.Number) ? kv.Value["order"].GetValue<double>() : 9999)
            .ToList();
        foreach (var (key, node) in ordered)
        {
            var p = (JsonObject)node;
            string type = p["type"]?.ToString()?.ToLowerInvariant() ?? "";
            string label = project.Text(p["text"]?.ToString());
            if (string.IsNullOrEmpty(label)) label = key;
            JsonNode current = settings.Properties.TryGetValue(key, out var v) ? v : p["value"];

            void Set(JsonNode value) => changed(key, value);

            FrameworkElement control = type switch
            {
                "slider" => Slider(p, current, Set),
                "bool" => Toggle(label, current, Set),
                "color" => ColorRow(current, Set),
                "combo" => Combo(p, project, current, Set),
                "textinput" => Text(current, Set),
                "text" => null,
                _ => null,
            };
            if (type == "text")
            {
                panel.Children.Add(new TextBlock { Text = label, Style = (Style)Application.Current.Resources["Muted"], Margin = new Thickness(0, 8, 0, 2) });
                continue;
            }
            if (control == null) continue;
            if (type != "bool")
                panel.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Margin = new Thickness(0, 10, 0, 4), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(control);
            count++;
        }
        return count;
    }

    static double Number(JsonNode n, double fallback)
    {
        if (n == null) return fallback;
        try
        {
            return n.GetValueKind() switch
            {
                JsonValueKind.Number => n.GetValue<double>(),
                JsonValueKind.String => double.TryParse(n.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback,
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => fallback,
            };
        }
        catch { return fallback; }
    }

    static FrameworkElement Slider(JsonObject p, JsonNode current, Action<JsonNode> set)
    {
        double min = Number(p["min"], 0), max = Number(p["max"], 100), step = Number(p["step"], 1);
        int precision = (int)Number(p["precision"], step < 1 ? 2 : 0);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        var slider = new Slider { Minimum = min, Maximum = Math.Max(max, min + 0.0001), Value = Number(current, min), VerticalAlignment = VerticalAlignment.Center };
        if (step > 0) { slider.TickFrequency = step; slider.IsSnapToTickEnabled = true; }
        var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["Ink2"] };
        string fmt = precision > 0 ? "0." + new string('0', precision) : "0";
        text.Text = slider.Value.ToString(fmt, CultureInfo.CurrentCulture);
        slider.ValueChanged += (_, e) =>
        {
            double value = Math.Round(e.NewValue, Math.Clamp(precision, 0, 6));
            text.Text = value.ToString(fmt, CultureInfo.CurrentCulture);
            set(JsonValue.Create(value));
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(slider);
        grid.Children.Add(text);
        return grid;
    }

    static FrameworkElement Toggle(string label, JsonNode current, Action<JsonNode> set)
    {
        var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = Number(current, 0) != 0, Margin = new Thickness(0, 8, 0, 0) };
        box.Click += (_, _) => set(JsonValue.Create(box.IsChecked == true));
        return box;
    }

    static FrameworkElement Text(JsonNode current, Action<JsonNode> set)
    {
        var box = new TextBox { Text = current?.ToString() ?? "" };
        box.LostFocus += (_, _) => set(JsonValue.Create(box.Text));
        box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) set(JsonValue.Create(box.Text)); };
        return box;
    }

    static FrameworkElement Combo(JsonObject p, WeProject project, JsonNode current, Action<JsonNode> set)
    {
        var combo = new ComboBox();
        var options = p["options"] as JsonArray ?? new JsonArray();
        string cur = current?.ToString();
        foreach (var o in options)
        {
            var item = new ComboBoxItem { Content = project.Text(o?["label"]?.ToString()), Tag = o?["value"]?.DeepClone() };
            combo.Items.Add(item);
            if (o?["value"]?.ToString() == cur) combo.SelectedItem = item;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem it) set(((JsonNode)it.Tag)?.DeepClone());
        };
        return combo;
    }

    // Wallpaper Engine colors are "r g b" with each channel from 0 to 1.
    static Color ParseColor(string s)
    {
        var parts = (s ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0).ToArray();
        if (parts.Length < 3) return Colors.White;
        byte C(double d) => (byte)Math.Clamp(Math.Round(d * 255), 0, 255);
        return Color.FromRgb(C(parts[0]), C(parts[1]), C(parts[2]));
    }

    static string FormatColor(Color c) =>
        string.Join(' ', new[] { c.R, c.G, c.B }.Select(b => (b / 255.0).ToString("0.#####", CultureInfo.InvariantCulture)));

    static FrameworkElement ColorRow(JsonNode current, Action<JsonNode> set)
    {
        var color = ParseColor(current?.ToString());
        var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(color), BorderBrush = (Brush)Application.Current.Resources["Line"], BorderThickness = new Thickness(1) };
        var button = new Button { Content = "Escolher cor", Margin = new Thickness(10, 0, 0, 0) };
        button.Click += (_, _) =>
        {
            var owner = Window.GetWindow(button);
            if (ColorDialog.Pick(owner, color, out var picked))
            {
                color = picked;
                swatch.Background = new SolidColorBrush(color);
                set(JsonValue.Create(FormatColor(color)));
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(swatch);
        row.Children.Add(button);
        return row;
    }
}

/// <summary>The Windows color dialog (comdlg32), so no color picker has to be drawn by hand.</summary>
public static class ColorDialog
{
    [StructLayout(LayoutKind.Sequential)]
    struct CHOOSECOLOR
    {
        public int lStructSize; public IntPtr hwndOwner, hInstance; public int rgbResult; public IntPtr lpCustColors;
        public int Flags; public IntPtr lCustData, lpfnHook, lpTemplateName;
    }

    [DllImport("comdlg32.dll")] static extern bool ChooseColorW(ref CHOOSECOLOR cc);

    static readonly IntPtr Custom = Marshal.AllocHGlobal(16 * 4);

    public static bool Pick(Window owner, Color initial, out Color picked)
    {
        var cc = new CHOOSECOLOR
        {
            lStructSize = Marshal.SizeOf<CHOOSECOLOR>(),
            hwndOwner = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle,
            rgbResult = initial.R | (initial.G << 8) | (initial.B << 16),
            lpCustColors = Custom,
            Flags = 0x1 /* RGBINIT */ | 0x2 /* FULLOPEN */,
        };
        if (ChooseColorW(ref cc))
        {
            picked = Color.FromRgb((byte)(cc.rgbResult & 0xFF), (byte)((cc.rgbResult >> 8) & 0xFF), (byte)((cc.rgbResult >> 16) & 0xFF));
            return true;
        }
        picked = initial;
        return false;
    }
}
