using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Skein;

/// <summary>A short tour for new users: the keyboard, the sidebar, reading, and making Skein your own.</summary>
public partial class WelcomeWindow : Window
{
    readonly Dictionary<string, string> _keys;
    readonly List<Func<UIElement>> _pages;
    int _index;

    /// <summary>"Open Preferences" was clicked on the last page.</summary>
    public bool OpenPreferences { get; private set; }

    readonly bool _offerPreferences;

    /// <param name="offerPreferences">Show an Open Preferences button at the end (not when opened from Preferences).</param>
    public WelcomeWindow(AppSettings settings, bool offerPreferences = true)
    {
        InitializeComponent();
        _offerPreferences = offerPreferences;
        _keys = KeyMap.Effective(settings.KeyBindings);
        _pages = [Hello, Keyboard, Sidebar, Reading, MakeItYours];
        Show(0);
    }

    // ---- Pages ------------------------------------------------------------------------------------

    UIElement Hello() => PageOf("👋", "Welcome to Skein",
        "A fast, keyboard-friendly way to read Reddit on Windows. This one-minute tour shows the handful of things that make it quicker than the website.",
        Note("You can see this again any time from Preferences → Help."));

    UIElement Keyboard() => PageOf("⌨", "Your hands can stay on the keyboard",
        "Everything has a key. These are the ones you'll use most; press ? to see them all, and change any of them there.",
        KeyRow(Key("next") + " / " + Key("previous"), "Next and previous post"),
        KeyRow(Key("upvote") + " / " + Key("downvote"), "Upvote and downvote"),
        KeyRow(Key("save"), "Save a post"),
        KeyRow("Ctrl+K", "Go anywhere: any community, folder, tab or command"),
        KeyRow(Key("reading"), "Reading mode: just the post"),
        KeyRow(Key("nextNew"), "Jump to the next comment that's new since your last visit"));

    UIElement Sidebar() => PageOf("🗂", "Your Reddit, organized",
        "The sidebar keeps your communities in order.",
        Bullet("☆", "Hover over a community and click the star to make it a favorite."),
        Bullet("📁", "Folders group communities your way, and open as one combined feed."),
        Bullet("🖱", "Middle-click anything in the sidebar to open it in a new tab."),
        Bullet("🕘", "History finds the post you were reading yesterday; ⏰ Reminders brings posts back later."));

    UIElement Reading() => PageOf("💬", "Better threads",
        "When you open a post:",
        Bullet("🟧", "Comments posted since your last visit are marked, so you can catch up quickly."),
        Bullet("●", "Busy game and live threads update by themselves (or press ● Live on any thread)."),
        Bullet("⧉", "Open a post side by side, or pop a video out to keep watching while you read."),
        Bullet("✨", "Add an Anthropic API key in Preferences and Summarize sums up long threads."));

    UIElement MakeItYours()
    {
        var open = new Button { Content = "Open Preferences", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        open.Click += (_, _) =>
        {
            OpenPreferences = true;
            DialogResult = true;
        };
        return PageOf("🎨", "Make it yours",
            "Themes (including true black and high contrast), fonts, filters for what you'd rather not see, notifications, and sync between your PCs are all in Preferences.",
            Bullet("🔔", "Skein can sit in the tray and tell you about replies, alerts and reminders."),
            Bullet("🔄", "Sync keeps your folders, alerts and settings the same on every PC."),
            _offerPreferences ? open : new TextBlock());
    }

    // ---- Building blocks ------------------------------------------------------------------------

    string Key(string action) => KeyMap.Display(_keys[action]);

    UIElement PageOf(string icon, string title, string intro, params UIElement[] body)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = icon, FontSize = 34, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 48, Margin = new Thickness(0, 6, 0, 6) });
        panel.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("DialogTitle") });
        panel.Children.Add(new TextBlock { Text = intro, Style = (Style)FindResource("PageIntro"), FontSize = 14, Margin = new Thickness(0, 4, 0, 14) });
        foreach (var element in body) panel.Children.Add(element);
        return panel;
    }

    UIElement KeyRow(string keys, string label)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var cap = new Border
        {
            Style = (Style)FindResource("KeyCap"),
            MinWidth = 110,
            Margin = new Thickness(0, 0, 14, 0),
            Child = new TextBlock { Text = keys, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Center },
        };
        row.Children.Add(cap);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return row;
    }

    static UIElement Bullet(string icon, string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
        row.Children.Add(new TextBlock { Text = icon, Width = 30, FontSize = 15 });
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        return row;
    }

    static UIElement Note(string text) =>
        new TextBlock { Text = text, Opacity = 0.6, FontSize = 12, Margin = new Thickness(0, 18, 0, 0), TextWrapping = TextWrapping.Wrap };

    // ---- Moving through the tour ----------------------------------------------------------------

    void Show(int index)
    {
        _index = index;
        Page.Content = _pages[index]();
        BackButton.Visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;
        var last = index == _pages.Count - 1;
        NextButton.Content = last ? "Start reading" : "Next";
        SkipButton.Visibility = last ? Visibility.Hidden : Visibility.Visible;
        Dots.Items.Clear();
        for (var i = 0; i < _pages.Count; i++)
            Dots.Items.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(4, 0, 4, 0),
                Fill = i == index ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
            });
        AutomationProperties.SetName(Dots, $"Page {index + 1} of {_pages.Count}");
        NextButton.Focus();
    }

    void OnNext(object sender, RoutedEventArgs e)
    {
        if (_index < _pages.Count - 1) Show(_index + 1);
        else DialogResult = true;
    }

    void OnBack(object sender, RoutedEventArgs e)
    {
        if (_index > 0) Show(_index - 1);
    }

    void OnSkip(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>For the screenshot tests: how many pages, and showing one.</summary>
    internal int PageCount => _pages.Count;
    internal void ShowPage(int index) => Show(index);
}
