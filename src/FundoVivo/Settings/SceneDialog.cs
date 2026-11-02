using System.Globalization;
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

    public SceneDialog(Window owner, LibraryItem scene, MonitorInfo monitor, double duration, double crossfade, int fps)
    {
        Owner = owner;
        Title = "Converter cena";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Style = (Style)FindResource("WindowText");
        SourceInitialized += (_, _) => ThemeManager.SetTitleBar(this);
        _duration.Value = Math.Clamp(duration, 10, 60);
        _crossfade.Value = Math.Clamp(crossfade, 0.5, 3);

        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        root.Children.Add(new TextBlock { Text = scene.Title, Style = (Style)FindResource("H2"), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(Muted($"Vira um vídeo em loop para {monitor.Label} ({monitor.Bounds.Width}×{monitor.Bounds.Height}), a {fps} fps (o limite de fps do seu Wallpaper Engine; para 60, suba o limite lá antes)."));

        var traits = scene.Traits;
        var notes = new List<string>();
        if (traits.HasFlag(SceneTraits.Unreadable))
            notes.Add("Não deu para ler o que esta cena usa (formato de cena de GIF). Ela converte, mas sem a classificação.");
        else if ((traits & (SceneTraits.Parallax | SceneTraits.Sound | SceneTraits.MouseOrClock)) == 0)
            notes.Add("Converte sem perder nada: a cena não usa mouse, relógio, som nem parallax.");
        if (traits.HasFlag(SceneTraits.Parallax))
            notes.Add("Parallax vira pan: o vídeo sai um pouco maior que a tela e anda com o mouse. É uma aproximação: o vídeo inteiro anda junto, sem a profundidade de cada camada.");
        if (traits.HasFlag(SceneTraits.Sound))
            notes.Add("Perde a reação ao som. E o que estiver tocando no PC durante a gravação fica gravado na animação: converta com o som parado.");
        if (traits.HasFlag(SceneTraits.MouseOrClock))
            notes.Add("Perde mouse ou relógio: o que reage ao mouse fica parado e a hora que aparecer fica a da gravação.");
        root.Children.Add(new TextBlock { Text = "O QUE MUDA", Style = (Style)FindResource("Label") });
        foreach (string n in notes) root.Children.Add(Muted("• " + n, new Thickness(0, 0, 0, 4)));

        root.Children.Add(new TextBlock { Text = "DURAÇÃO DO LOOP", Style = (Style)FindResource("Label") });
        root.Children.Add(Row(_duration, v => $"{v:0} s"));
        root.Children.Add(new TextBlock { Text = "EMENDA (MISTURA DO FIM COM O COMEÇO)", Style = (Style)FindResource("Label") });
        root.Children.Add(Row(_crossfade, v => v.ToString("0.0", CultureInfo.CurrentCulture) + " s"));

        root.Children.Add(Muted(
            "Durante a gravação, a janela do Wallpaper Engine aparece por um instante no canto da tela e sai para fora dela; " +
            "dá para continuar usando o PC. Não abra jogo em tela cheia até terminar: o Wallpaper Engine pausa e a gravação sai parada.",
            new Thickness(0, 16, 0, 0)));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancelar", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var go = new Button { Content = "Converter", Style = (Style)FindResource("Primary"), IsDefault = true };
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
