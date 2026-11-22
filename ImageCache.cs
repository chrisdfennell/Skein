using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Skein;

/// <summary>
/// Pictures in lists (thumbnails, previews, community icons), kept on disk so going back through a
/// feed is instant and doesn't download them again, and decoded away from the window's thread so
/// scrolling stays smooth. Recently shown ones are also kept decoded in memory.
/// </summary>
public static class ImageCache
{
    /// <summary>Most the cache folder holds before the oldest pictures are cleared out.</summary>
    public const long MaxBytes = 300L * 1024 * 1024;

    const int MemoryEntries = 400;

    static readonly HttpClient Http = CreateHttp();
    static readonly SemaphoreSlim Downloads = new(6);
    static readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> Memory = [];
    static readonly LinkedList<(string Key, ImageSource Image)> Recent = new();
    static readonly object Lock = new();

    public static string Folder { get; set; } = Path.Combine(Paths.Local, "cache", "images");

    static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "windows:Skein (desktop Reddit client)");
        return http;
    }

    // ---- The attached properties used in XAML: local:ImageCache.Url="{Binding Thumbnail}" ------------

    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached(
        "Url", typeof(string), typeof(ImageCache), new PropertyMetadata(null, OnUrlChanged));

    public static string? GetUrl(DependencyObject d) => (string?)d.GetValue(UrlProperty);
    public static void SetUrl(DependencyObject d, string? value) => d.SetValue(UrlProperty, value);

    /// <summary>The width to decode at (smaller is faster and lighter); 0 = full size.</summary>
    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.RegisterAttached(
        "DecodeWidth", typeof(int), typeof(ImageCache), new PropertyMetadata(0));

    public static int GetDecodeWidth(DependencyObject d) => (int)d.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(DependencyObject d, int value) => d.SetValue(DecodeWidthProperty, value);

    static void Show(DependencyObject target, ImageSource? image)
    {
        switch (target)
        {
            case Image img: img.Source = image; break;
            case ImageBrush brush: brush.ImageSource = image; break;
        }
    }

    static async void OnUrlChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var url = e.NewValue as string;
        Show(target, null); // never leave a recycled row showing the previous post's picture
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return;
        var width = GetDecodeWidth(target);
        var image = await LoadAsync(url, width);
        // A recycled row may have moved on to another post meanwhile.
        if (image != null && GetUrl(target) == url) Show(target, image);
    }

    // ---- Loading ----------------------------------------------------------------------------------

    /// <summary>A picture, decoded at the given width: from memory, the disk cache, or the web.</summary>
    public static async Task<ImageSource?> LoadAsync(string url, int width)
    {
        var key = width + "|" + url;
        lock (Lock)
        {
            if (Memory.TryGetValue(key, out var node))
            {
                Recent.Remove(node);
                Recent.AddFirst(node);
                return node.Value.Image;
            }
        }
        var image = await Task.Run(async () =>
        {
            var bytes = await BytesAsync(url);
            return bytes == null ? null : Decode(bytes, width);
        });
        if (image == null) return null;
        lock (Lock)
        {
            if (!Memory.ContainsKey(key))
            {
                Memory[key] = Recent.AddFirst((key, image));
                while (Recent.Count > MemoryEntries)
                {
                    Memory.Remove(Recent.Last!.Value.Key);
                    Recent.RemoveLast();
                }
            }
        }
        return image;
    }

    public static string FileFor(string url) =>
        Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..40]);

    static async Task<byte[]?> BytesAsync(string url)
    {
        var file = FileFor(url);
        try
        {
            if (File.Exists(file)) return await File.ReadAllBytesAsync(file);
        }
        catch
        {
            // Being written or cleared right now: fetch it instead.
        }
        await Downloads.WaitAsync();
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            try
            {
                Directory.CreateDirectory(Folder);
                var temp = file + ".part";
                await File.WriteAllBytesAsync(temp, bytes);
                File.Move(temp, file, overwrite: true);
            }
            catch
            {
                // Not kept this time; it still shows.
            }
            return bytes;
        }
        catch
        {
            return null; // gone, or offline
        }
        finally
        {
            Downloads.Release();
        }
    }

    static ImageSource? Decode(byte[] bytes, int width)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            if (width > 0) image.DecodePixelWidth = width;
            image.EndInit();
            image.Freeze(); // so the window's thread can show it
            return image;
        }
        catch
        {
            return null; // not a picture WPF can read (some WebP, say)
        }
    }

    // ---- Housekeeping -----------------------------------------------------------------------------

    /// <summary>How much the cache holds on disk.</summary>
    public static long SizeOnDisk()
    {
        try
        {
            return Directory.Exists(Folder) ? new DirectoryInfo(Folder).EnumerateFiles().Sum(f => f.Length) : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Clears out the oldest pictures until the cache is under <paramref name="maxBytes"/>.</summary>
    public static void Trim(long maxBytes = MaxBytes)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            var files = new DirectoryInfo(Folder).EnumerateFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            long total = 0;
            foreach (var file in files)
            {
                total += file.Length;
                if (total > maxBytes || file.Name.EndsWith(".part")) file.Delete();
            }
        }
        catch
        {
            // Tried again next start.
        }
    }

    /// <summary>Empties the cache (Preferences → Library and downloads).</summary>
    public static void Clear()
    {
        lock (Lock)
        {
            Memory.Clear();
            Recent.Clear();
        }
        Trim(0);
    }
}
