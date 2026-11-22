using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Skein.Tests;

/// <summary>
/// Builds Skein's dialogs off screen with the app's real styles, saves a picture of every page
/// (CI keeps them, so a change in looks can be seen without running the app), and checks that
/// nothing sticks out past the window's edges.
/// </summary>
public static class Ui
{
    static readonly Dispatcher Dispatcher = Start();

    /// <summary>Where the pictures go: SKEIN_SCREENSHOTS, or "screenshots" next to the tests.</summary>
    public static readonly string Folder =
        Environment.GetEnvironmentVariable("SKEIN_SCREENSHOTS") is { Length: > 0 } folder ? folder : Path.Combine(AppContext.BaseDirectory, "screenshots");

    static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Dark };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Skein;component/Styles.xaml") });
            app.Resources["Accent"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x00));
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "UI tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    public static T Run<T>(Func<T> work) => Dispatcher.Invoke(work);
    public static void Run(Action work) => Dispatcher.Invoke(work);

    /// <summary>Shows a window off screen (never taking focus), lays it out, and returns it.</summary>
    public static T Open<T>(Func<T> create) where T : Window => Run(() =>
    {
        var window = create();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Owner = null;
        window.Show();
        Settle(window);
        return window;
    });

    /// <summary>Lets layout and rendering catch up.</summary>
    public static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    /// <summary>Saves a picture of the window's content.</summary>
    public static void Shot(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        var size = new Size(Math.Max(1, content.ActualWidth), Math.Max(1, content.ActualHeight));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Directory.CreateDirectory(Folder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Folder, name + ".png"));
        encoder.Save(file);
    }

    /// <summary>
    /// Text, buttons and inputs that are showing but reach past the right edge of the window (or,
    /// with <paramref name="checkBottom"/>, below it): they'd be cut off. Scrolling areas only
    /// scroll up and down, so only those are allowed to run off the bottom.
    /// </summary>
    public static List<string> Overflowing(Window window, bool checkBottom = false)
    {
        var content = (FrameworkElement)window.Content;
        var problems = new List<string>();
        foreach (var element in Descendants(content).OfType<FrameworkElement>())
        {
            if (element is not (TextBlock or Button or TextBox or ComboBox or CheckBox or Slider or PasswordBox) ||
                !element.IsVisible || element.ActualWidth <= 0) continue;
            var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
            var label = element switch
            {
                TextBlock t => $"text “{Clip(t.Text)}”",
                ContentControl { Content: string s } => $"{element.GetType().Name} “{Clip(s)}”",
                _ => element.Name.Length > 0 ? element.Name : element.GetType().Name,
            };
            if (bounds.Right > content.ActualWidth + 1) problems.Add($"{label} runs {bounds.Right - content.ActualWidth:0} px past the right edge");
            if (bounds.Left < -1) problems.Add($"{label} starts {-bounds.Left:0} px left of the window");
            if (checkBottom && bounds.Bottom > content.ActualHeight + 1) problems.Add($"{label} runs {bounds.Bottom - content.ActualHeight:0} px below the window");
        }
        return problems;
    }

    static string Clip(string s) => s.Length > 40 ? s[..40] + "…" : s;

    static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        // The logical tree: what the window's XAML and code put there, not controls' insides.
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}

public class WindowLayoutTests
{
    static AppSettings Settings() => new() { Theme = "Dark" };

    [Fact]
    public void Every_preferences_page_fits()
    {
        var problems = Ui.Run(() =>
        {
            var settings = Settings();
            var window = Ui.Open(() => new PreferencesWindow(settings, new UpdateService(settings)));
            var nav = (ListBox)window.FindName("Nav");
            var found = new List<string>();
            Assert.Equal(11, nav.Items.Count);
            for (var i = 0; i < nav.Items.Count; i++)
            {
                nav.SelectedIndex = i;
                Ui.Settle(window);
                var name = ((ListBoxItem)nav.Items[i]).Tag is var tag ? tag!.GetType().GetProperty("Key")!.GetValue(tag) : i;
                Ui.Shot(window, $"preferences-{name}");
                found.AddRange(Ui.Overflowing(window).Select(p => $"{name}: {p}"));
            }
            window.Close();
            return found;
        });
        Assert.Empty(problems);
    }

    [Fact]
    public void Preferences_search_finds_the_page()
    {
        var pages = Ui.Run(() =>
        {
            var settings = Settings();
            var window = Ui.Open(() => new PreferencesWindow(settings, new UpdateService(settings)));
            ((TextBox)window.FindName("Search")).Text = "onedrive";
            Ui.Settle(window);
            var nav = (ListBox)window.FindName("Nav");
            var names = nav.Items.Cast<ListBoxItem>().Select(i => System.Windows.Automation.AutomationProperties.GetName(i)).ToList();
            window.Close();
            return names;
        });
        Assert.Equal(["Sync and backup"], pages);
    }

    [Fact]
    public void Every_welcome_page_fits_without_scrolling()
    {
        var problems = Ui.Run(() =>
        {
            var window = Ui.Open(() => new WelcomeWindow(Settings()));
            var found = new List<string>();
            for (var i = 0; i < window.PageCount; i++)
            {
                window.ShowPage(i);
                Ui.Settle(window);
                Ui.Shot(window, $"welcome-{i + 1}");
                found.AddRange(Ui.Overflowing(window, checkBottom: true).Select(p => $"page {i + 1}: {p}"));
            }
            window.Close();
            return found;
        });
        Assert.Empty(problems);
    }

    [Fact]
    public void Shortcuts_list_fits_and_shows_changed_keys()
    {
        var (problems, label) = Ui.Run(() =>
        {
            var settings = Settings();
            KeyMap.Bind(settings.KeyBindings, "upvote", "q");
            var window = Ui.Open(() => new ShortcutsWindow(settings));
            Ui.Shot(window, "shortcuts");
            var found = Ui.Overflowing(window);
            var buttons = FindAll<Button>(window).Select(b => b.Content as string).ToList();
            window.Close();
            return (found, buttons.Contains("Q") ? "Q" : "");
        });
        Assert.Empty(problems);
        Assert.Equal("Q", label);
    }

    [Theory]
    [InlineData("when")]
    [InlineData("text-input")]
    public void Small_dialogs_fit(string which)
    {
        var problems = Ui.Run(() =>
        {
            Window window = which == "when"
                ? Ui.Open(() => new WhenWindow("Remind me", "Remind me about this post:", "A fairly long post title that might wrap onto a second line", LaterTimes.ReminderChoices))
                : Ui.Open(() => new TextInputWindow("Save search", "Name for this search in the sidebar:", "cats"));
            Ui.Shot(window, which);
            var found = Ui.Overflowing(window, checkBottom: true);
            window.Close();
            return found;
        });
        Assert.Empty(problems);
    }

    static IEnumerable<T> FindAll<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) yield return match;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }
}
