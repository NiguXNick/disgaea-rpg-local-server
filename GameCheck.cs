using System.Text;

namespace DrpgServer;

// Only the global Steam release, client 3.2.10, is supported (the server reads its DLLs and
// rewrites its master data). app.info names the publisher; globalgamemanagers holds the app
// version as a length-prefixed string. Same check as tools/XdCrypt.ps1 Assert-SupportedGame.
public static class GameCheck
{
    private const string Publisher = "Boltrend";
    private const string Version = "3.2.10";

    public static string? Problem(string gameDir)
    {
        var data = Path.Combine(gameDir, "DISGAEA RPG_Data");
        var info = Path.Combine(data, "app.info");
        var managers = Path.Combine(data, "globalgamemanagers");
        if (!File.Exists(info) || !File.Exists(managers))
            return $"No DISGAEA RPG install found in '{gameDir}'. Pass --game with the game folder.";

        var publisher = File.ReadLines(info).FirstOrDefault()?.Trim() ?? "";
        var marker = new byte[] { (byte)Version.Length, 0, 0, 0 }.Concat(Encoding.ASCII.GetBytes(Version)).ToArray();
        var versioned = File.ReadAllBytes(managers).AsSpan().IndexOf(marker) >= 0;
        return publisher == Publisher && versioned
            ? null
            : $"Unsupported game build (publisher '{publisher}'). Only the global Steam release, client {Version}, is supported.";
    }
}
