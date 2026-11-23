using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MidiMesh.Appearance;

/// <summary>
/// The icon in the chosen accent colour. The green one ships with the app; any other colour is
/// made once from it and kept in the data folder's icons folder.
/// </summary>
public static class IconFiles
{
    public static string ShippedIco => Path.Combine(AppContext.BaseDirectory, "Assets", "MidiMesh.ico");

    public static string ShippedPng => Path.Combine(AppContext.BaseDirectory, "Assets", "MidiMesh.png");

    /// <summary>Paths of the .ico (window, taskbar, tray) and .png (title bar) for this colour.</summary>
    public static async Task<(string Ico, string Png)> GetAsync(Rgb colour)
    {
        if (colour == AccentPalette.Green.Base) return (ShippedIco, ShippedPng);
        var name = $"MidiMesh-{colour.ToString().TrimStart('#')}";
        var ico = Path.Combine(AppPaths.IconCacheDir, name + ".ico");
        var png = Path.Combine(AppPaths.IconCacheDir, name + ".png");
        try
        {
            if (!File.Exists(ico) || !File.Exists(png))
            {
                Directory.CreateDirectory(AppPaths.IconCacheDir);
                await WriteTintedAsync(ShippedIco, ico, colour, asIco: true);
                await WriteTintedAsync(ShippedPng, png, colour, asIco: false);
            }
            return (ico, png);
        }
        catch (Exception ex)
        {
            // Never worth failing over: keep the green icon.
            Log.Info($"Could not make the {colour} icon: {ex.Message}");
            return (ShippedIco, ShippedPng);
        }
    }

    /// <summary>Decodes every size in the source, recolours it and writes it back out as PNG data.</summary>
    private static async Task WriteTintedAsync(string source, string target, Rgb colour, bool asIco)
    {
        var images = new List<(uint Width, uint Height, byte[] Png)>();
        using (var input = await ReadAsync(source))
        {
            var decoder = await BitmapDecoder.CreateAsync(input);
            for (uint i = 0; i < decoder.FrameCount; i++)
            {
                var frame = await decoder.GetFrameAsync(i);
                var data = await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                    new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                var pixels = data.DetachPixelData();
                IconTint.TintBgra(pixels, AccentPalette.Green.Base, colour);
                images.Add((frame.PixelWidth, frame.PixelHeight, await EncodePngAsync(pixels, frame.PixelWidth, frame.PixelHeight)));
            }
        }

        var temp = target + ".tmp";
        await File.WriteAllBytesAsync(temp, asIco ? BuildIco(images) : images[0].Png);
        File.Move(temp, target, overwrite: true);
    }

    private static async Task<IRandomAccessStream> ReadAsync(string path)
    {
        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync((await File.ReadAllBytesAsync(path)).AsBuffer());
        stream.Seek(0);
        return stream;
    }

    private static async Task<byte[]> EncodePngAsync(byte[] pixels, uint width, uint height)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        var bytes = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }

    /// <summary>An .ico holding PNG images, which Windows reads at every size.</summary>
    private static byte[] BuildIco(List<(uint Width, uint Height, byte[] Png)> images)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1); // Icon.
        w.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (width, height, png) in images)
        {
            w.Write((byte)(width >= 256 ? 0 : width)); // 0 means 256.
            w.Write((byte)(height >= 256 ? 0 : height));
            w.Write((byte)0); // No palette.
            w.Write((byte)0);
            w.Write((ushort)1); // Colour planes.
            w.Write((ushort)32); // Bits per pixel.
            w.Write((uint)png.Length);
            w.Write((uint)offset);
            offset += png.Length;
        }
        foreach (var (_, _, png) in images) w.Write(png);
        w.Flush();
        return ms.ToArray();
    }
}
