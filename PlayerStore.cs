using System.Collections.Concurrent;
using System.Text.Json;

namespace DrpgServer;

public sealed class Player
{
    public ulong Id { get; set; }
    public string Uuid { get; set; } = "";
    public string Password { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsTutorial { get; set; } = true;
    public int TutorialStep { get; set; } // 0 = prologue (TutorialManager.TutorialControllerByStep)
    public string CreatedAt { get; set; } = "";
    public List<OwnedCharacter> Characters { get; set; } = new();
    public List<ulong> Deck { get; set; } = new(); // t_character ids of deck 1, leader first
    public int GachaRarity { get; set; }

    public OwnedCharacter? Character(ulong id) => Characters.FirstOrDefault(c => c.Id == id);
    public string PublicId => (100000000 + Id).ToString();
}

// One JSON file per account under the save folder.
public sealed class PlayerStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _dir;
    private readonly ConcurrentDictionary<string, Player> _bySession = new();
    private readonly object _gate = new();

    public PlayerStore(ServerConfig cfg)
    {
        _dir = Path.Combine(cfg.DataDir, "players");
        Directory.CreateDirectory(_dir);
    }

    public (Player Player, bool IsNew) Login(string uuid, string password)
    {
        lock (_gate)
        {
            var file = FileFor(uuid);
            if (File.Exists(file))
                return (JsonSerializer.Deserialize<Player>(File.ReadAllText(file))!, false);

            var p = new Player
            {
                Id = (ulong)Directory.GetFiles(_dir, "*.json").Length + 1,
                Uuid = uuid,
                Password = password,
                CreatedAt = Time.Format(DateTime.UtcNow),
            };
            Save(p);
            return (p, true);
        }
    }

    public string OpenSession(Player p)
    {
        var sid = Guid.NewGuid().ToString("N");
        _bySession[sid] = p;
        return sid;
    }

    public Player? BySession(string? sid) =>
        sid != null && _bySession.TryGetValue(sid, out var p) ? p : null;

    public void Save(Player p)
    {
        lock (_gate) File.WriteAllText(FileFor(p.Uuid), JsonSerializer.Serialize(p, Json));
    }

    private string FileFor(string uuid)
    {
        var safe = string.Concat(uuid.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_dir, (safe.Length == 0 ? "_" : safe) + ".json");
    }
}

public static class Time
{
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static string Format(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss");
}
