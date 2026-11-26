namespace UltraBootscreenEditor;

internal static class AppInfo
{
    public const string Name = "Ultra Bootscreen Editor";
    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version!.ToString(3);
    public static string WindowTitle => $"{Name} {Version}";
    public static string UserAgent => $"UltraBootscreenEditor/{Version}";
}
