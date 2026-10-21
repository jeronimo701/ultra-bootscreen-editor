using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace UltraBootscreenEditor;

internal static class SelfTest
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) throw new ArgumentException("--self-test OUTPUT_DIRECTORY [ORIGINAL_BMP] [SOURCE_IMAGE]");
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var results = new List<string>();
        void Test(string name, Action action)
        {
            try { action(); results.Add("PASS " + name); }
            catch (Exception ex) { results.Add("FAIL " + name + ": " + ex.Message); }
        }
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Fails(Action action)
        {
            bool failed = false; try { action(); } catch { failed = true; }
            Check(failed, "Expected rejection");
        }
        using var corners = new Bitmap(1280, 720, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(corners))
        {
            g.Clear(Color.FromArgb(15, 40, 80));
            g.FillRectangle(Brushes.Red, 0, 0, 640, 360); g.FillRectangle(Brushes.Blue, 640, 0, 640, 360);
            g.FillRectangle(Brushes.Green, 0, 360, 640, 360); g.FillRectangle(Brushes.Yellow, 640, 360, 640, 360);
        }
        byte[] encoded = UltraBmp.Encode(corners);
        string output = Path.Combine(root, "corners.bmp");
        Test("Ultra BMP dimensions, bit-depth, length and header", () =>
        {
            UltraBmp.Save(corners, output, true);
            Check(encoded.Length == 3686456, "Wrong byte length");
            using var bmp = new Bitmap(output);
            Check(bmp.Width == 720 && bmp.Height == 1280 && bmp.PixelFormat == PixelFormat.Format32bppRgb, "Wrong format");
            if (args.Length >= 2) Check(encoded.AsSpan(0, 54).SequenceEqual(File.ReadAllBytes(args[1]).AsSpan(0, 54)), "Header differs from original Ultra");
        });
        Test("Rotation and BGR order: full 1280x720 pixel round-trip", () =>
        {
            using var rotated = new Bitmap(output); rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);
            for (int y = 0; y < 720; y++) for (int x = 0; x < 1280; x++)
                if (rotated.GetPixel(x, y).ToArgb() != corners.GetPixel(x, y).ToArgb()) throw new Exception($"Pixel mismatch {x},{y}");
        });
        Test("Opening Ultra BMP restores landscape", () =>
        {
            using var loaded = ImageFiles.LoadPath(output);
            Check(loaded.Size == corners.Size && loaded.GetPixel(50, 50).ToArgb() == Color.Red.ToArgb(), "Wrong BMP orientation");
        });
        Test("Portrait PNG remains portrait", () =>
        {
            using var portrait = new Bitmap(720, 1280); using var png = new MemoryStream(); portrait.Save(png, ImageFormat.Png);
            using var loaded = ImageFiles.Load(png.ToArray(), "portrait.png"); Check(loaded.Width == 720 && loaded.Height == 1280, "PNG rotated unexpectedly");
        });
        Test("Portrait, ultrawide and square cover the screen at pan limits", () =>
        {
            foreach (var size in new[] { new Size(600, 1600), new Size(2400, 400), new Size(900, 900) })
            {
                using var doc = new Document();
                var solid = new Bitmap(size.Width, size.Height); using (var g = Graphics.FromImage(solid)) g.Clear(Color.Cyan);
                doc.SetImage(solid, "test"); doc.Zoom = 3; doc.Pan = new(99999, -99999); doc.ClampPan();
                using var rendered = doc.Render();
                foreach (var point in new[] { new Point(1, 1), new Point(1278, 1), new Point(1, 718), new Point(1278, 718), new Point(640, 360) })
                    Check(rendered.GetPixel(point.X, point.Y).G > 240, "Black gap at " + point);
            }
        });
        Test("Caption and nickname are rendered and can be disabled", () =>
        {
            using var doc = new Document();
            var solid = new Bitmap(1280, 720); using (var g = Graphics.FromImage(solid)) g.Clear(Color.Black);
            doc.SetImage(solid, "test");
            doc.Caption.Enabled = true; doc.Nick.Enabled = true;
            using var withText = doc.Render();
            int bright = 0, nick = 0;
            for (int y = 20; y < 85; y++) for (int x = 25; x < 700; x++) if (withText.GetPixel(x, y).R > 50) bright++;
            for (int y = 650; y < 710; y++) for (int x = 1040; x < 1280; x++) if (withText.GetPixel(x, y).R > 50) nick++;
            Check(bright > 100 && nick > 50, "Missing text layers");
            doc.Caption.Enabled = false; doc.Nick.Enabled = false;
            using var noText = doc.Render(); Check(noText.GetPixel(40, 35).R == 0, "Text disabled incorrectly");
            withText.Save(Path.Combine(root, "text-preview.png"));
        });
        Test("Batch conversion preserves existing and input files", () =>
        {
            string expected = Path.Combine(root, "corners-2.bmp");
            int suffix = 3;
            while (File.Exists(expected)) expected = Path.Combine(root, $"corners-{suffix++}.bmp");
            string first = ImageFiles.ConvertBatchFile(output, root, new(), new());
            Check(first != output && first == expected, "Collision not resolved");
            Check(File.ReadAllBytes(output).SequenceEqual(encoded), "Original overwritten");
            Check(File.ReadAllBytes(first).SequenceEqual(encoded), "Converted bytes changed");
        });
        Test("Invalid export and overwrite=false preserve destination", () =>
        {
            byte[] before = File.ReadAllBytes(output);
            using var invalid = new Bitmap(10, 10);
            Fails(() => UltraBmp.Save(invalid, output, true)); Fails(() => UltraBmp.Save(corners, output, false));
            Check(File.ReadAllBytes(output).SequenceEqual(before), "Destination changed");
            Check(!Directory.GetFiles(root, ".ultra-*.tmp").Any(), "Staging file leaked");
        });
        Test("Invalid image and unsafe URL schemes are rejected", () =>
        {
            Fails(() => ImageFiles.Load(Encoding.UTF8.GetBytes("<html>not an image</html>"), "page"));
            Fails(() => ImageFiles.LoadUrl("file:///C:/test.png").GetAwaiter().GetResult());
        });
        Test("Direct HTTP image download and oversized response rejection", () =>
        {
            using var png = new MemoryStream(); corners.Save(png, ImageFormat.Png);
            using var downloaded = Serve(png.ToArray(), null);
            Check(downloaded.Width == 1280 && downloaded.GetPixel(40, 40).R == 255, "HTTP image load failed");
            Fails(() => { using var ignored = Serve([], ImageFiles.MaxBytes + 1); });
        });
        if (args.Length >= 3) Test("Selected real image export", () =>
        {
            using var doc = new Document(); doc.SetImage(ImageFiles.LoadPath(args[2]), args[2]);
            using var preview = doc.Render(); preview.Save(Path.Combine(root, "real-preview.png"));
            UltraBmp.Save(preview, Path.Combine(root, "real-bootscreen.bmp"), true);
        });
        File.WriteAllLines(Path.Combine(root, "results.txt"), results);
        return results.Any(r => r.StartsWith("FAIL")) ? 1 : 0;
    }
    private static Bitmap Serve(byte[] body, long? claimedLength)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
            string header = $"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: {claimedLength ?? body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), timeout.Token);
            if (body.Length > 0) await stream.WriteAsync(body, timeout.Token);
        });
        try { return ImageFiles.LoadUrl($"http://127.0.0.1:{port}/image.png").GetAwaiter().GetResult(); }
        finally { server.GetAwaiter().GetResult(); }
    }
}
