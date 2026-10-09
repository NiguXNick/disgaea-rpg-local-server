namespace DrpgServer;

public sealed class ServerConfig
{
    public const string ServerKey = "standalonewindows64_20221122131322";

    public required int Port { get; init; }
    public required string GameDir { get; init; }
    public required string DataDir { get; init; }

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
        };
    }
}
