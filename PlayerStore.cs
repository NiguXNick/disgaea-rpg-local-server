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

    public List<Gear> Gear { get; set; } = new();
    public ulong NextGearId { get; set; } = 1;
    public string ShopLineupDate { get; set; } = "";
    public int ShopLineupNo { get; set; }
    public int ShopUpdateNum { get; set; }
    public List<ShopItem> ShopItems { get; set; } = new();
    public ulong BattleSeq { get; set; }

    // Gear presets: deck no -> 5 party positions x [weapon, equipment 1..3] (0 = empty).
    public Dictionary<int, ulong[][]> EquipmentDecks { get; set; } = new();
    public Dictionary<int, string> EquipmentDeckNames { get; set; } = new();

    // Fishing Fleet (Survey) areas by m_survey_id, and the fleet rank.
    public Dictionary<ulong, SurveyState> Surveys { get; set; } = new();
    public uint SurveyRank { get; set; } = 1;
    public ulong SurveyExp { get; set; }
    public ulong SurveyExpTotal { get; set; }

    // Item World floor in progress (item_world/start -> battle/end).
    public ulong ItemWorldGearId { get; set; }
    public int ItemWorldFloor { get; set; }

    // Gift box: mission/trophy rewards arrive here and are applied when received.
    public List<Gift> Gifts { get; set; } = new();
    public ulong NextGiftId { get; set; } = 1;

    // Mission progress counters ("event" or "event:id" -> count), lifetime / today / this week.
    public Dictionary<string, long> Counters { get; set; } = new();
    public Dictionary<string, long> DailyCounters { get; set; } = new();
    public Dictionary<string, long> WeeklyCounters { get; set; } = new();
    public string DailyKey { get; set; } = "";
    public string WeeklyKey { get; set; } = "";
    public string LastLoginDate { get; set; } = "";

    // Received missions/trophies by master id (daily/weekly sets are cleared on reset).
    public HashSet<ulong> BeginnerReceived { get; set; } = new();
    public HashSet<ulong> TrophiesReceived { get; set; } = new();
    public HashSet<ulong> DailyReceived { get; set; } = new();
    public HashSet<ulong> WeeklyReceived { get; set; } = new();
    public Dictionary<ulong, long> RepetitionUsed { get; set; } = new(); // progress already turned into rewards
    public int MissionSheetNo { get; set; } = 1;  // beginner/mastership sheet (0 hides the home icon)
    public string TrainingMissionFinishedAt { get; set; } = "";
    public string ItemWorldMissionFinishedAt { get; set; } = "";

    public ulong NextCharacterId() => Math.Max(1UL, Characters.Count == 0 ? 0 : Characters.Max(c => c.Id)) + 1;

    public OwnedCharacter? Character(ulong id) => Characters.FirstOrDefault(c => c.Id == id);
    public string PublicId => (100000000 + Id).ToString();
}

// An owned weapon (Kind 3) or piece of equipment (Kind 4).
public sealed class Gear
{
    public ulong Id { get; set; }
    public int Kind { get; set; }
    public ulong MId { get; set; }
    public int RarityValue { get; set; }
    public int Pop { get; set; }
    public int Lv { get; set; } = 1;
    public int LvMax { get; set; } = 10;
    public int Stage { get; set; } // Item World floors cleared
    public ulong SetCharaId { get; set; } // character wearing it (0 = none)
    public int SetNo { get; set; }        // slot: weapon 0, equipment 0..2
    public int Hp { get; set; }
    public int Atk { get; set; }
    public int Def { get; set; }
    public int Inte { get; set; }
    public int Res { get; set; }
    public int Spd { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class ShopItem
{
    public ulong Id { get; set; }
    public int ItemType { get; set; }
    public ulong ItemId { get; set; }
    public int Rarity { get; set; }
    public int Pop { get; set; }
    public bool Sold { get; set; }
}

public sealed class SurveyState
{
    public List<ulong> CharacterIds { get; set; } = new();
    public int AreaCondition { get; set; } = 3; // 1..5, the "fish condition"
    public int Hour { get; set; }
    public string EndAt { get; set; } = "";     // "" = idle; future = sailing; past = back
}

public sealed class Gift
{
    public ulong Id { get; set; }
    public int Type { get; set; }       // present_type: 1 item, 2 character, 3 weapon, 4 equipment
    public ulong PresentId { get; set; }
    public int Rarity { get; set; }
    public int Num { get; set; }
    public string Message { get; set; } = "";
    public bool Received { get; set; }
    public string CreatedAt { get; set; } = "";
    public string ReceivedAt { get; set; } = "";
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

    // Session ids carry the account ("<hex uuid>.<random>") so a game left open across a server
    // restart is still recognised instead of getting empty answers.
    public string OpenSession(Player p)
    {
        var sid = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(p.Uuid)) + "." + Guid.NewGuid().ToString("N");
        _bySession[sid] = p;
        return sid;
    }

    public Player? BySession(string? sid)
    {
        if (string.IsNullOrEmpty(sid)) return null;
        if (_bySession.TryGetValue(sid, out var p)) return p;
        var dot = sid.IndexOf('.');
        if (dot <= 0) return null;
        try
        {
            var uuid = System.Text.Encoding.UTF8.GetString(Convert.FromHexString(sid[..dot]));
            lock (_gate)
            {
                var file = FileFor(uuid);
                if (!File.Exists(file)) return null;
                p = JsonSerializer.Deserialize<Player>(File.ReadAllText(file))!;
            }
            _bySession[sid] = p;
            Log.Info($"Session restored for '{uuid}'.");
            return p;
        }
        catch (FormatException) { return null; }
    }

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
