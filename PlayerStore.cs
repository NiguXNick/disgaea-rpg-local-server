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
    public List<ulong> Deck { get; set; } = new(); // t_character ids of the selected deck, leader first
    public Dictionary<int, List<ulong>> Decks { get; set; } = new(); // deck_no -> 5 slots (0 = empty)
    public Dictionary<int, string> DeckNames { get; set; } = new();
    public int SelectedDeckNo { get; set; } = 1;
    public int GachaRarity { get; set; }

    // Nether Quartz: a starting amount (enough for one 10x summon); the rest is earned in game.
    // Paid quartz was the real-money currency and starts at zero.
    public long FreeStone { get; set; } = 3_000;
    public long PaidStone { get; set; }
    public long FreeStoneUsed { get; set; }
    public long PaidStoneUsed { get; set; }

    public Dictionary<ulong, GachaSum> GachaSums { get; set; } = new();

    // Player rank: Rank, exp within the rank, cumulative exp.
    public int Rank { get; set; } = 1;
    public long RankExp { get; set; }
    public long RankExpTotal { get; set; }

    // Owned items by m_item_id (101 = HL).
    public Dictionary<ulong, long> Items { get; set; } = new();

    // Real story progress (the client is shown every stage as cleared so everything is open).
    public Dictionary<ulong, int> StageClears { get; set; } = new();
    public Dictionary<ulong, int> StageLosses { get; set; } = new();
    public Dictionary<ulong, bool[]> StageMissions { get; set; } = new();
    public ulong CurrentStage { get; set; }
    public HashSet<ulong> SubTutorialsRead { get; set; } = new();
    public ulong BattleSeq { get; set; }

    public ulong NextCharacterId() => Math.Max(1UL, Characters.Count == 0 ? 0 : Characters.Max(c => c.Id)) + 1;

    public OwnedCharacter? Character(ulong id) => Characters.FirstOrDefault(c => c.Id == id);
    public string PublicId => (100000000 + Id).ToString();
}

public sealed class GachaSum
{
    public long Sum { get; set; }
    public long CountBonusDrawCount { get; set; }
    public long SeriesDrawCount { get; set; }
    public long TotalDrawCount { get; set; }
    public string LastDrawAt { get; set; } = "";
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

    // Account names are case-insensitive ("Niguxnick" and "niguxnick" are the same save).
    private string FileFor(string uuid)
    {
        var safe = string.Concat(uuid.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_dir, (safe.Length == 0 ? "_" : safe) + ".json");
    }
}

public static class Time
{
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static string Format(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss");
}
