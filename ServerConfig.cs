namespace DrpgServer;

public sealed class ServerConfig
{
    public const string ServerKey = "standalonewindows64_20221122131322";

    public required int Port { get; init; }
    public required string GameDir { get; init; }
    public required string DataDir { get; init; }

    // Turns on every XD.tool.Debug tag in the client. Many client exceptions (callbacks, hotfix
    // code) are only logged through tagged LogException calls, so without this they're silent.
    public bool ClientLogAll { get; init; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public string StreamingAssets => Path.Combine(GameDir, "DISGAEA RPG_Data", "StreamingAssets");

    public static ServerConfig Load(string[] args)
    {
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var gameDir = Arg("--game") ?? @"C:\Program Files (x86)\Steam\steamapps\common\DISGAEA RPG";
        var dataDir = Arg("--data") ?? Path.Combine(AppContext.BaseDirectory, "save");
        Directory.CreateDirectory(dataDir);
        return new ServerConfig
        {
            Port = int.Parse(Arg("--port") ?? "8765"),
            GameDir = gameDir,
            DataDir = dataDir,
            ClientLogAll = args.Contains("--client-log-all"),
        };
    }
}
