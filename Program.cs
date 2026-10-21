namespace UltraBootscreenEditor;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args.Skip(1).ToArray());
            if (args.Length == 4 && args[0] == "--capture")
            {
                using var form = new MainForm();
                form.CaptureForTest(args[1], args[2]);
                File.WriteAllText(args[3], "UI render OK"); return 0;
            }
            using var editor = new MainForm();
            if (args.Length > 0 && File.Exists(args[0])) editor.LoadPath(args[0]);
            Application.Run(editor); return 0;
        }
        catch (Exception ex)
        {
            if (args.Length > 0) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "editor-error.txt"), ex.ToString()); return 1; }
            MessageBox.Show(ex.Message, "Ultra Bootscreen Editor"); return 1;
        }
    }
}
