using System.Drawing.Drawing2D;

namespace UltraBootscreenEditor;

public sealed class PreviewCanvas : Control
{
    public Document Document { get; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int DragTarget { get; set; }
    public event Action? Changed;
    public event Action<float>? ZoomRequested;
    private Point last;
    private bool dragging;
    public PreviewCanvas(Document document)
    {
        Document = document; Dock = DockStyle.Fill; BackColor = Color.FromArgb(13, 15, 20);
        DoubleBuffered = true; TabStop = true; Cursor = Cursors.SizeAll;
    }
    public RectangleF ScreenRect
    {
        get
        {
            float scale = Math.Max(.01f, Math.Min((ClientSize.Width - 40) / 1280f, (ClientSize.Height - 80) / 720f));
            return new((ClientSize.Width - 1280 * scale) / 2, (ClientSize.Height - 720 * scale) / 2, 1280 * scale, 720 * scale);
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var r = ScreenRect;
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(r.X, r.Y); e.Graphics.ScaleTransform(r.Width / 1280, r.Height / 720);
        e.Graphics.SetClip(new Rectangle(0, 0, 1280, 720));
        Document.Draw(e.Graphics);
        if (Document.Image is null)
        {
            using var font = new Font("Segoe UI", 25, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb(145, 154, 173));
            e.Graphics.DrawString("Открой картинку или перетащи файл сюда", font, brush, 320, 340);
        }
        e.Graphics.Restore(state);
        using var border = new Pen(Color.FromArgb(70, 82, 103));
        e.Graphics.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
        using var small = new Font("Segoe UI", 9);
        using var muted = new SolidBrush(Color.FromArgb(160, 170, 190));
        e.Graphics.DrawString("ЭКРАН SWITCH · 1280 × 720 · перетаскивание мышью / колесо — масштаб", small, muted, r.X, r.Bottom + 12);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (e.Button != MouseButtons.Left || !ScreenRect.Contains(e.Location) || Document.Image is null) return;
        dragging = true; Capture = true; last = e.Location;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        float scale = 1280 / ScreenRect.Width;
        var delta = new PointF((e.X - last.X) * scale, (e.Y - last.Y) * scale);
        if (DragTarget == 0)
        {
            Document.Pan = new(Document.Pan.X + delta.X, Document.Pan.Y + delta.Y);
            Document.ClampPan();
        }
        else
        {
            var layer = DragTarget == 1 ? Document.Caption : Document.Nick;
            layer.Position = new(Math.Clamp(layer.Position.X + delta.X, 0, 1279), Math.Clamp(layer.Position.Y + delta.Y, 0, 719));
        }
        last = e.Location; Invalidate(); Changed?.Invoke();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Document.Image is null) return;
        var r = ScreenRect;
        float old = Document.Zoom, next = Math.Clamp(old * (e.Delta > 0 ? 1.1f : 1 / 1.1f), 1, 5);
        float x = (e.X - r.X) * 1280 / r.Width - 640, y = (e.Y - r.Y) * 720 / r.Height - 360;
        Document.Pan = new(x - (x - Document.Pan.X) * next / old, y - (y - Document.Pan.Y) * next / old);
        Document.Zoom = next; Document.ClampPan(); ZoomRequested?.Invoke(next); Invalidate(); Changed?.Invoke();
    }
}

public sealed class MainForm : Form
{
    private readonly Document document = new();
    private readonly PreviewCanvas canvas;
    private readonly FlowLayoutPanel toolbar = new() { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(12, 8, 8, 0) };
    private readonly FlowLayoutPanel settings = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0), Text = "Готово · PNG, JPG, BMP, GIF, TIFF · UltraNX 720 × 1280 / 32 bit" };
    private readonly TrackBar zoom = new() { Minimum = 100, Maximum = 500, Value = 100, TickFrequency = 50, Width = 285, Height = 40 };
    private readonly List<Action> sync = new();
    private bool busy;
    private static readonly Color Surface = Color.FromArgb(28, 32, 42);
    private static readonly Color Muted = Color.FromArgb(157, 168, 189);

    public MainForm()
    {
        Text = "Ultra Bootscreen Editor"; Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(19, 22, 29); ForeColor = Color.FromArgb(235, 239, 248);
        ClientSize = new Size(1280, 820); MinimumSize = new Size(1000, 700); StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi; AllowDrop = true; KeyPreview = true;
        canvas = new PreviewCanvas(document);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new(SizeType.Absolute, 60)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 40));
        Controls.Add(root); root.Controls.Add(toolbar, 0, 0);
        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        middle.ColumnStyles.Add(new(SizeType.Percent, 100)); middle.ColumnStyles.Add(new(SizeType.Absolute, 340));
        middle.Controls.Add(canvas, 0, 0); middle.Controls.Add(settings, 1, 0);
        settings.BackColor = Surface;
        root.Controls.Add(middle, 0, 1); root.Controls.Add(status, 0, 2);
        AddButton(toolbar, "Открыть файл", (_, _) => OpenFile());
        AddButton(toolbar, "По ссылке", async (_, _) => await OpenUrl());
        AddButton(toolbar, "Предпросмотр", (_, _) => FullPreview());
        AddButton(toolbar, "Сохранить BMP", (_, _) => SaveBmp(), true);
        AddButton(toolbar, "Пакетная обработка", async (_, _) => await Batch());
        Heading("КАДРИРОВАНИЕ");
        Hint("Кадр всегда 16:9. Двигай картинку мышью; чёрные полосы не появятся.");
        var target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290 };
        target.Items.AddRange(["Мышь: двигать картинку", "Мышь: двигать надпись", "Мышь: двигать ник"]); target.SelectedIndex = 0;
        target.SelectedIndexChanged += (_, _) => canvas.DragTarget = target.SelectedIndex;
        settings.Controls.Add(target); settings.Controls.Add(zoom);
        zoom.ValueChanged += (_, _) => { document.Zoom = zoom.Value / 100f; document.ClampPan(); canvas.Invalidate(); };
        canvas.ZoomRequested += value => zoom.Value = Math.Clamp((int)Math.Round(value * 100), 100, 500);
        AddButton(settings, "Сбросить кадр", (_, _) => { document.ResetCrop(); zoom.Value = 100; canvas.Invalidate(); });
        AddButton(settings, "Повернуть на 90°", (_, _) =>
        {
            if (document.Image is null) return;
            document.Image.RotateFlip(RotateFlipType.Rotate90FlipNone); document.ResetCrop(); zoom.Value = 100; canvas.Invalidate();
        });
        LayerEditor("НАДПИСЬ", document.Caption);
        LayerEditor("НИК", document.Nick);
        Hint("Текст по умолчанию выключен: включи нужный слой. Ник можно сделать ненавязчивым, выбрав тёмный цвет.");
        canvas.Changed += () => { foreach (var update in sync) update(); };
        DragEnter += (_, e) => { if (!busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (!busy && e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) LoadPath(files[0]); };
        KeyDown += (_, e) =>
        {
            if (busy) return;
            if (e.Control && e.KeyCode == Keys.O) { OpenFile(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.S) { SaveBmp(); e.SuppressKeyPress = true; }
        };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "Дождись завершения операции…"; } };
        FormClosed += (_, _) => document.Dispose();
    }
    private static Button AddButton(Control parent, string text, EventHandler handler, bool accent = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 34, Padding = new Padding(10, 3, 10, 3), FlatStyle = FlatStyle.Flat,
            BackColor = accent ? Color.FromArgb(44, 99, 202) : Color.FromArgb(42, 48, 63), ForeColor = Color.White, Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 9) };
        b.FlatAppearance.BorderColor = Color.FromArgb(65, 76, 96); b.Click += handler; parent.Controls.Add(b); return b;
    }
    private void Heading(string text) => settings.Controls.Add(new Label { Text = text, Width = 290, Height = 28, Margin = new Padding(0, 10, 0, 2), ForeColor = Color.FromArgb(115, 166, 255), Font = new Font("Segoe UI", 10, FontStyle.Bold) });
    private void Hint(string text) => settings.Controls.Add(new Label { Text = text, Width = 290, AutoSize = true, MaximumSize = new Size(290, 0), ForeColor = Muted, Margin = new Padding(0, 2, 0, 12) });
    private void LayerEditor(string title, TextLayer layer)
    {
        Heading(title);
        var enabled = new CheckBox { Text = "Добавить " + (title == "НИК" ? "ник" : "надпись"), Width = 290, Height = 27, Checked = layer.Enabled };
        var text = new TextBox { Width = 290, Text = layer.Text };
        var family = new ComboBox { Width = 290, DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 15 };
        family.Items.AddRange(FontFamily.Families.Where(f => f.IsStyleAvailable(FontStyle.Regular)).Select(f => (object)f.Name).OrderBy(n => n).ToArray());
        family.SelectedItem = layer.FontName;
        var row = new FlowLayoutPanel { Width = 295, Height = 37, WrapContents = false, Margin = Padding.Empty };
        var size = Number(8, 180, (decimal)layer.Size, 65);
        var bold = new CheckBox { Text = "Жирный", Width = 105, Height = 30, Checked = layer.Bold };
        var color = AddButton(row, "Цвет", (_, _) =>
        {
            using var dialog = new ColorDialog { Color = layer.Color, FullOpen = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) { layer.Color = dialog.Color; canvas.Invalidate(); }
        });
        color.Width = 70; color.AutoSize = false;
        row.Controls.Add(size); row.Controls.Add(bold);
        var positions = new FlowLayoutPanel { Width = 295, Height = 36, WrapContents = false, Margin = Padding.Empty };
        positions.Controls.Add(new Label { Text = "X", Width = 18, Height = 28, TextAlign = ContentAlignment.MiddleLeft });
        var x = Number(0, 1279, (decimal)layer.Position.X, 85); positions.Controls.Add(x);
        positions.Controls.Add(new Label { Text = "Y", Width = 18, Height = 28, TextAlign = ContentAlignment.MiddleLeft });
        var y = Number(0, 719, (decimal)layer.Position.Y, 85); positions.Controls.Add(y);
        settings.Controls.Add(enabled); settings.Controls.Add(text); settings.Controls.Add(family); settings.Controls.Add(row); settings.Controls.Add(positions);
        enabled.CheckedChanged += (_, _) => { layer.Enabled = enabled.Checked; canvas.Invalidate(); };
        text.TextChanged += (_, _) => { layer.Text = text.Text; canvas.Invalidate(); };
        family.SelectedIndexChanged += (_, _) => { layer.FontName = family.Text; canvas.Invalidate(); };
        size.ValueChanged += (_, _) => { layer.Size = (float)size.Value; canvas.Invalidate(); };
        bold.CheckedChanged += (_, _) => { layer.Bold = bold.Checked; canvas.Invalidate(); };
        bool updating = false;
        x.ValueChanged += (_, _) => { if (!updating) { layer.Position = new((float)x.Value, layer.Position.Y); canvas.Invalidate(); } };
        y.ValueChanged += (_, _) => { if (!updating) { layer.Position = new(layer.Position.X, (float)y.Value); canvas.Invalidate(); } };
        sync.Add(() => { updating = true; x.Value = Math.Clamp((decimal)layer.Position.X, 0, 1279); y.Value = Math.Clamp((decimal)layer.Position.Y, 0, 719); updating = false; });
    }
    private static NumericUpDown Number(decimal min, decimal max, decimal value, int width) => new() { Minimum = min, Maximum = max, Value = value, Width = width };
    private void Error(Exception ex) => MessageBox.Show(this, ex is OperationCanceledException ? "Загрузка заняла больше 45 секунд. Попробуй снова." : ex.Message, "Не удалось выполнить операцию", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private void SetBusy(bool value) { busy = value; toolbar.Enabled = !value; settings.Enabled = !value; canvas.Enabled = !value; UseWaitCursor = value; }
    private bool RequireImage()
    {
        if (document.Image is not null) return true;
        status.Text = "Сначала открой картинку."; return false;
    }
    private static OpenFileDialog ImageDialog(bool multiple = false) => new() { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Все файлы|*.*", Multiselect = multiple };
    private void OpenFile()
    {
        using var dialog = ImageDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadPath(dialog.FileName);
    }
    internal void LoadPath(string path)
    {
        try { document.SetImage(ImageFiles.LoadPath(path), path); zoom.Value = 100; canvas.Invalidate(); status.Text = $"{Path.GetFileName(path)} · {document.Image!.Width} × {document.Image.Height}"; }
        catch (Exception ex) { Error(ex); }
    }
    private async Task OpenUrl()
    {
        using var prompt = new Form { Text = "Прямая ссылка на картинку", ClientSize = new Size(620, 145), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var label = new Label { Text = "Вставь HTTP/HTTPS-ссылку на сам файл, а не на страницу сайта.", Left = 16, Top = 12, Width = 590, Height = 26 };
        var input = new TextBox { Left = 16, Top = 43, Width = 586 };
        var button = new Button { Text = "Загрузить", Left = 486, Top = 88, Width = 116, DialogResult = DialogResult.OK };
        prompt.Controls.AddRange([label, input, button]); prompt.AcceptButton = button;
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true); status.Text = "Загружаю картинку…";
        try { var image = await ImageFiles.LoadUrl(input.Text.Trim()); document.SetImage(image, input.Text.Trim()); zoom.Value = 100; canvas.Invalidate(); status.Text = "Картинка загружена по ссылке."; }
        catch (Exception ex) { status.Text = "Загрузить картинку не удалось."; Error(ex); }
        finally { SetBusy(false); }
    }
    private void FullPreview()
    {
        if (!RequireImage()) return;
        using var image = document.Render();
        using var form = new Form { Text = "Экран Switch — 1280 × 720 · Esc: закрыть", ClientSize = new Size(1280, 720), StartPosition = FormStartPosition.CenterParent, KeyPreview = true, BackColor = Color.Black };
        form.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image });
        form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) form.Close(); }; form.ShowDialog(this);
    }
    private void SaveBmp()
    {
        if (!RequireImage()) return;
        using var dialog = new SaveFileDialog { Filter = "UltraNX bootscreen|*.bmp", FileName = "bootscreen.bmp", DefaultExt = "bmp", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var bitmap = document.Render(); UltraBmp.Save(bitmap, dialog.FileName, true);
            status.Text = "Сохранено: " + dialog.FileName;
        }
        catch (Exception ex) { Error(ex); }
    }
    private async Task Batch()
    {
        using var files = ImageDialog(true);
        files.Title = "Выбери картинки для пакетной конвертации";
        if (files.ShowDialog(this) != DialogResult.OK) return;
        using var folder = new FolderBrowserDialog { Description = "Папка для готовых BMP", UseDescriptionForTitle = true };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "Каждая картинка будет обрезана по центру под 16:9.\nВключённые текстовые слои добавятся ко всем файлам.\nСуществующие файлы получат новое имя.\n\nНачать?", "Пакетная обработка", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        var caption = document.Caption.Clone(); var nick = document.Nick.Clone();
        SetBusy(true);
        var progress = new Progress<string>(s => status.Text = s);
        try
        {
            var errors = new List<string>(); int done = 0;
            await Task.Run(() =>
            {
                foreach (var path in files.FileNames)
                {
                    try
                    {
                        ImageFiles.ConvertBatchFile(path, folder.SelectedPath, caption, nick); done++;
                    }
                    catch (Exception ex) { errors.Add(Path.GetFileName(path) + ": " + ex.Message); }
                    ((IProgress<string>)progress).Report($"Обработано {done + errors.Count} из {files.FileNames.Length}…");
                }
            });
            status.Text = $"Пакет готов: {done} файлов, ошибок: {errors.Count}.";
            MessageBox.Show(this, status.Text + (errors.Count > 0 ? "\n\n" + string.Join("\n", errors.Take(12)) : "\n" + folder.SelectedPath), "Пакетная обработка", MessageBoxButtons.OK, errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex) { Error(ex); }
        finally { SetBusy(false); }
    }
    internal void CaptureForTest(string source, string destination)
    {
        document.SetImage(ImageFiles.LoadPath(source), source);
        ShowInTaskbar = false; Opacity = 0; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000);
        Show(); Application.DoEvents(); PerformLayout();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(destination);
        Close();
    }
}
