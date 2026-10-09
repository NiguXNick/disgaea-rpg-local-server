namespace DrpgServer;

// Counters behind mission and trophy progress. Handlers that see an action bump its counter;
// Missions turns counters (and stage clears, levels, etc.) into now_num values.
// Each counter is kept for the whole game, for today and for this week (daily/weekly missions).
public static class Progress
{
    public const string Login = "login";               // days logged in
    public const string Gacha = "gacha";               // characters summoned
    public const string Battle = "battle";             // battles won
    public const string AreaBattle = "area_battle";    // battles won, by m_area_id
    public const string Enemy = "enemy";               // enemies defeated
    public const string Act = "act";                   // AP "used" (stages cost no AP offline, see Missions.ActPerBattle)
    public const string Party = "party";               // party edits saved
    public const string Rebirth = "rebirth";           // reincarnations
    public const string Present = "present";           // gifts received
    public const string EquipBuy = "equip_buy";        // weapons/equipment bought (by rarity band 1/2/3 too)
    public const string HlSpent = "hl_spent";
    public const string StoneSpent = "stone_spent";
    public const string ItemWorldWeapon = "iw_weapon"; // Item World runs started on a weapon
    public const string ItemWorldEquipment = "iw_equipment";
    public const string ItemWorldClear = "iw_clear";   // Item World floors cleared
    public const string ItemWorldFloor = "iw_floor";   // highest Item World floor cleared (max, not a sum)

    public static void Add(Player p, string ev, long n = 1, ulong id = 0)
    {
        if (n <= 0) return;
        Roll(p);
        var key = Key(ev, id);
        foreach (var d in new[] { p.Counters, p.DailyCounters, p.WeeklyCounters })
        {
            d[key] = d.GetValueOrDefault(key) + n;
            if (id != 0) d[ev] = d.GetValueOrDefault(ev) + n;
        }
    }

    public static void Max(Player p, string ev, long value)
    {
        Roll(p);
        foreach (var d in new[] { p.Counters, p.DailyCounters, p.WeeklyCounters })
            d[ev] = Math.Max(d.GetValueOrDefault(ev), value);
    }

    public static long Total(Player p, string ev, ulong id = 0) => p.Counters.GetValueOrDefault(Key(ev, id));

    public static long Today(Player p, string ev, ulong id = 0)
    {
        Roll(p);
        return p.DailyCounters.GetValueOrDefault(Key(ev, id));
    }

    public static long Week(Player p, string ev, ulong id = 0)
    {
        Roll(p);
        return p.WeeklyCounters.GetValueOrDefault(Key(ev, id));
    }

    // One login per day counts.
    public static void LoggedIn(Player p)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (p.LastLoginDate == today) return;
        p.LastLoginDate = today;
        Add(p, Login);
    }

    // Daily missions reset every day, weekly ones every Monday (server time zone is UTC).
    public static void Roll(Player p)
    {
        var now = DateTime.UtcNow.Date;
        var today = now.ToString("yyyy-MM-dd");
        if (p.DailyKey != today)
        {
            p.DailyKey = today;
            p.DailyCounters.Clear();
            p.DailyReceived.Clear();
        }
        var week = now.AddDays(-(((int)now.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd");
        if (p.WeeklyKey != week)
        {
            p.WeeklyKey = week;
            p.WeeklyCounters.Clear();
            p.WeeklyReceived.Clear();
        }
    }

    private static string Key(string ev, ulong id) => id == 0 ? ev : $"{ev}:{id}";
}
