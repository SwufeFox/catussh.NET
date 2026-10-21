using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Catussh.App;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Honor the OS light/dark preference the way the Swift app follows
        // NSApp.appearance; the design system has both palettes.
        var dark = ReadSystemUsesDarkTheme();

        // catussh [--page <name>] [--dark|--light] [--screenshot <path>]
        var page = "home";
        string? screenshot = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--page" when i + 1 < args.Length:
                    page = args[++i];
                    break;
                case "--dark":
                    dark = true;
                    break;
                case "--light":
                    dark = false;
                    break;
                case "--screenshot" when i + 1 < args.Length:
                    screenshot = args[++i];
                    break;
            }
        }

        Application.Run(new MainWindow(startDark: dark, startPage: page, screenshotPath: screenshot));
    }

    /// <summary>
    /// Reads <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>.
    /// Falls back to light when the key is missing.
    /// </summary>
    private static bool ReadSystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
