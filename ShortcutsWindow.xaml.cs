using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Skein;

/// <summary>Every keyboard shortcut, and changing the single-key ones.</summary>
public partial class ShortcutsWindow : Window
{
    readonly AppSettings _settings;
    readonly Dictionary<string, Button> _keyButtons = [];
    string? _capturing;

    public ShortcutsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Build();
        PreviewTextInput += OnTextInput;
        PreviewKeyDown += OnKeyDown;
    }

    void Build()
    {
        List.Children.Clear();
        _keyButtons.Clear();
        foreach (var group in KeyMap.Actions.GroupBy(a => a.Group))
        {
            List.Children.Add(new TextBlock { Text = group.Key, Style = (Style)FindResource("Group") });
            var card = new StackPanel();
            foreach (var action in group) card.Children.Add(Row(action));
            List.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(16, 6, 16, 6), Child = card });
        }
        List.Children.Add(new TextBlock { Text = "Always", Style = (Style)FindResource("Group") });
        var fixedCard = new StackPanel();
        foreach (var (keys, label) in KeyMap.Fixed)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var key = new Border { Style = (Style)FindResource("KeyCap"), Child = new TextBlock { Text = keys, FontFamily = new System.Windows.Media.FontFamily("Consolas") } };
            DockPanel.SetDock(key, Dock.Right);
            row.Children.Add(key);
            row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            fixedCard.Children.Add(row);
        }
        List.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(16, 6, 16, 6), Child = fixedCard });
        ResetAll.IsEnabled = _settings.KeyBindings.Count > 0;
    }

    UIElement Row(KeyAction action)
    {
        var keys = KeyMap.Effective(_settings.KeyBindings);
        var changed = keys[action.Id] != action.Default;
        var button = new Button
        {
            MinWidth = 96,
            Content = KeyMap.Display(keys[action.Id]),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontWeight = changed ? FontWeights.Bold : FontWeights.Normal,
            ToolTip = changed ? $"Changed (standard: {KeyMap.Display(action.Default)}). Click to change." : "Click, then press the new key",
        };
        AutomationProperties.SetName(button, $"{action.Label}: {KeyMap.Display(keys[action.Id])}. Change");
        button.Click += (_, _) => StartCapture(action.Id);
        _keyButtons[action.Id] = button;
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        row.Children.Add(new TextBlock { Text = action.Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        return row;
    }

    void StartCapture(string actionId)
    {
        if (_capturing != null && _keyButtons.TryGetValue(_capturing, out var previous)) previous.Content = KeyMap.Display(KeyMap.Effective(_settings.KeyBindings)[_capturing]);
        _capturing = actionId;
        _keyButtons[actionId].Content = "Press a key…";
        ShowMessage(null);
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing == null) return;
        if (e.Key == Key.Escape)
        {
            _capturing = null;
            Build();
            e.Handled = true; // cancel the capture, not the window
        }
        else if (e.Key is Key.Space or Key.Enter or Key.Tab)
        {
            ShowMessage("Space, Enter and Tab can't be used. Pick a letter, number or symbol.");
            e.Handled = true;
        }
    }

    void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_capturing == null || !KeyMap.IsBindable(e.Text)) return;
        e.Handled = true;
        var action = KeyMap.Actions.First(a => a.Id == _capturing);
        var before = KeyMap.Effective(_settings.KeyBindings);
        var other = KeyMap.Actions.FirstOrDefault(a => a.Id != action.Id && before[a.Id] == e.Text);
        KeyMap.Bind(_settings.KeyBindings, action.Id, e.Text);
        _settings.Save();
        _capturing = null;
        Build();
        ShowMessage(other == null
            ? $"“{action.Label}” is now {KeyMap.Display(e.Text)}."
            : $"“{action.Label}” is now {KeyMap.Display(e.Text)}, and “{other.Label}” moved to {KeyMap.Display(before[action.Id])}.");
    }

    void OnResetAll(object sender, RoutedEventArgs e)
    {
        _settings.KeyBindings.Clear();
        _settings.Save();
        _capturing = null;
        Build();
        ShowMessage("Every shortcut is back to its standard key.");
    }

    void ShowMessage(string? text)
    {
        Message.Text = text ?? "";
        Message.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
