using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace UltraBootscreenEditor;

public sealed class TextLayer
{
    public bool Enabled { get; set; }
    public string Text { get; set; } = "";
    public string FontName { get; set; } = "Segoe UI";
    public float Size { get; set; } = 28;
    public bool Bold { get; set; }
    public Color Color { get; set; } = Color.White;
    public PointF Position { get; set; } = new(32, 28);
    public TextLayer Clone() => (TextLayer)MemberwiseClone();
}

public sealed class Document : IDisposable
{
    public Bitmap? Image { get; private set; }
    public string Source { get; private set; } = "";
    public float Zoom { get; set; } = 1;
    public PointF Pan { get; set; }
    public TextLayer Caption { get; set; } = new() { Text = "PRESS VOL DOWN TO HEKATE" };
    public TextLayer Nick { get; set; } = new() { Text = "Jeronimo701", Size = 20, Color = Color.FromArgb(135, 135, 135), Position = new(1050, 660) };
    public float CoverScale => Image is null ? 1 : Math.Max(1280f / Image.Width, 720f / Image.Height);

    public void SetImage(Bitmap bitmap, string source)
    {
        Image?.Dispose(); Image = bitmap; Source = source; ResetCrop();
    }
    public void ResetCrop() { Zoom = 1; Pan = PointF.Empty; }
    public void ClampPan()
    {
        if (Image is null) return;
        float scale = CoverScale * Zoom;
        float x = Math.Max(0, (Image.Width * scale - 1280) / 2);
        float y = Math.Max(0, (Image.Height * scale - 720) / 2);
        Pan = new(Math.Clamp(Pan.X, -x, x), Math.Clamp(Pan.Y, -y, y));
    }
    public void Draw(Graphics graphics)
    {
        graphics.Clear(Color.Black);
        if (Image is not null)
        {
            ClampPan();
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = CoverScale * Zoom;
            var rect = new RectangleF(640 - Image.Width * scale / 2 + Pan.X,
                360 - Image.Height * scale / 2 + Pan.Y, Image.Width * scale, Image.Height * scale);
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            if (Math.Abs(scale - 1) < .00001f && rect.X == MathF.Round(rect.X) && rect.Y == MathF.Round(rect.Y))
                graphics.DrawImageUnscaled(Image, (int)rect.X, (int)rect.Y);
            else
                graphics.DrawImage(Image, Rectangle.Round(rect), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, attributes);
        }
        DrawText(graphics, Caption); DrawText(graphics, Nick);
    }
    public static RectangleF TextBounds(Graphics g, TextLayer layer)
    {
        using var font = new Font(layer.FontName, layer.Size, layer.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        return new(layer.Position, g.MeasureString(layer.Text, font, 1280, StringFormat.GenericTypographic));
    }
    private static void DrawText(Graphics g, TextLayer layer)
    {
        if (!layer.Enabled || string.IsNullOrEmpty(layer.Text)) return;
        using var font = new Font(layer.FontName, layer.Size, layer.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(layer.Color);
        using var shadow = new SolidBrush(Color.FromArgb(180, Color.Black));
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.DrawString(layer.Text, font, shadow, layer.Position.X + 1.5f, layer.Position.Y + 1.5f, StringFormat.GenericTypographic);
        g.DrawString(layer.Text, font, brush, layer.Position, StringFormat.GenericTypographic);
    }
    public Bitmap Render()
    {
        var bitmap = new Bitmap(1280, 720, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bitmap);
        Draw(g); return bitmap;
    }
    public void Dispose() => Image?.Dispose();
}

public static class ImageFiles
{
    public const int MaxBytes = 80 * 1024 * 1024;
    public static Bitmap Load(byte[] data, string source)
    {
        if (data.Length > MaxBytes) throw new InvalidDataException("Файл больше 80 МБ.");
        using var stream = new MemoryStream(data);
        using var image = System.Drawing.Image.FromStream(stream, false, true);
        if (image.Width > 16000 || image.Height > 16000 || (long)image.Width * image.Height > 80000000)
            throw new InvalidDataException("Слишком большая картинка: предел 16000 px и 80 мегапикселей.");
        if (image.PropertyIdList.Contains(0x112))
        {
            var orientation = image.GetPropertyItem(0x112)?.Value;
            if (orientation is { Length: >= 2 }) image.RotateFlip(BitConverter.ToUInt16(orientation, 0) switch
            {
                2 => RotateFlipType.RotateNoneFlipX, 3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX, 5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone, 7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone
            });
        }
        // Existing Ultra BMPs are stored sideways. Present them in screen orientation.
        if (data.Length >= 54 && data[0] == 'B' && data[1] == 'M' && image.Width == 720 && image.Height == 1280)
            image.RotateFlip(RotateFlipType.Rotate90FlipNone);
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Black); g.DrawImageUnscaled(image, 0, 0);
        return bitmap;
    }
    public static Bitmap LoadPath(string path)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Файл больше 80 МБ.");
        return Load(File.ReadAllBytes(path), path);
    }
    public static string ConvertBatchFile(string path, string folder, TextLayer caption, TextLayer nick)
    {
        using var doc = new Document { Caption = caption.Clone(), Nick = nick.Clone() };
        doc.SetImage(LoadPath(path), path);
        string stem = Path.GetFileNameWithoutExtension(path), output = Path.Combine(folder, stem + ".bmp");
        int suffix = 2;
        while (File.Exists(output) || Directory.Exists(output) || string.Equals(Path.GetFullPath(path), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            output = Path.Combine(folder, stem + "-" + suffix++ + ".bmp");
        using var bitmap = doc.Render(); UltraBmp.Save(bitmap, output, false);
        return output;
    }
    public static async Task<Bitmap> LoadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("Нужна прямая HTTP/HTTPS-ссылка на изображение.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UltraBootscreenEditor/1.0");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("Картинка больше 80 МБ.");
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        byte[] buffer = new byte[65536];
        int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (output.Length + read > MaxBytes) throw new InvalidDataException("Картинка больше 80 МБ.");
            output.Write(buffer, 0, read);
        }
        return Load(output.ToArray(), url);
    }
}

public static class UltraBmp
{
    public const int Size = 54 + 720 * 1280 * 4 + 2;
    // Deterministic header matches the supplied UltraNX bootscreen (including trailer).
    public static byte[] Encode(Bitmap landscape)
    {
        if (landscape.Width != 1280 || landscape.Height != 720) throw new ArgumentException("Expected 1280x720 landscape.");
        byte[] bytes = new byte[Size];
        using (var writer = new BinaryWriter(new MemoryStream(bytes)))
        {
            writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(Size);
            writer.Write(0); writer.Write(54); writer.Write(40);
            writer.Write(720); writer.Write(1280); writer.Write((short)1); writer.Write((short)32);
            writer.Write(0); writer.Write(Size - 54); writer.Write(3779); writer.Write(3779);
            writer.Write(0); writer.Write(0);
        }
        using var rgb = new Bitmap(1280, 720, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(rgb)) { g.Clear(Color.Black); g.DrawImageUnscaled(landscape, 0, 0); }
        var bits = rgb.LockBits(new(0, 0, 1280, 720), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            byte[] row = new byte[1280 * 4];
            for (int y = 0; y < 720; y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, row.Length);
                for (int x = 0; x < 1280; x++)
                {
                    int destination = 54 + (x * 720 + y) * 4;
                    bytes[destination] = row[x * 4]; bytes[destination + 1] = row[x * 4 + 1];
                    bytes[destination + 2] = row[x * 4 + 2]; bytes[destination + 3] = 0;
                }
            }
        }
        finally { rgb.UnlockBits(bits); }
        return bytes;
    }
    public static void Save(Bitmap landscape, string path, bool overwrite)
    {
        byte[] bytes = Encode(landscape);
        string fullPath = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(fullPath)!, ".ultra-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes); output.Flush(true); }
            using (var check = new Bitmap(temp))
                if (check.Width != 720 || check.Height != 1280 || check.PixelFormat != PixelFormat.Format32bppRgb)
                    throw new InvalidDataException("Проверка BMP не пройдена.");
            File.Move(temp, fullPath, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
