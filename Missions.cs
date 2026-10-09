using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Missions: the beginner/mastership/training/Item World sheets (MBeginnerMission) and the
// trophy tabs (MTrophy, MTrophyDaily, MTrophyWeekly, MTrophyRepetition).
//
// The client never computes progress: it shows the server's now_num/status. Progress comes from
// Progress counters and from what the save already records (stage clears, mission stars, levels).
// Rewards go to the gift box.
//
// Offline policy for conditions the server can't see or features that don't exist offline yet:
// on the beginner sheets they count as done (a sheet only moves on once all 12 are received, so
// one impossible mission would block the rest); on the trophy tabs they stay at 0, because
// thousands of trophies would otherwise pay out at once.
public sealed class Missions(MasterData master, Rewards rewards)
{
    private const int StatusProgress = 0, StatusClear = 1, StatusReceived = 2;
    private const int SheetTypeTraining = 3, SheetTypeItemWorld = 4;
    private const int SheetTraining = 31, SheetItemWorld = 41; // the client hardcodes these
    private const int MissionsPerSheet = 12;
    private const int AllComplete = 99;
    // Stages cost no AP offline; for "use AP" missions every battle counts as this much.
    public const int ActPerBattle = 10;

    private static string Now => Time.Format(DateTime.UtcNow);

    // ---- Beginner / training / Item World sheets ----------------------------------------------

    public object? BeginnerMissions(Player? p, JsonObject q)
    {
        if (p == null) return null;
        return Obj(("mission_datas", SheetWire(p, SheetFor(p, I(q, "sheet_type")))));
    }

    public object? ReceiveBeginner(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var id = (q["ids"] as JsonArray ?? []).Select(x => ulong.TryParse(x?.ToString(), out var v) ? v : 0).FirstOrDefault();
        var row = master.Get("MBeginnerMission", id);
        if (row == null) return null;
        var sheet = MasterData.F<int>(row, "sheet_no");
        var sheetType = MasterData.F<int>(row, "sheet_type");

        if (!p.BeginnerReceived.Contains(id) && BeginnerNow(p, row) >= MasterData.F<long>(row, "condition_num"))
        {
            p.BeginnerReceived.Add(id);
            GiveFrom(p, row, MasterData.F<string>(row, "title"));
        }

        object nextRows = Array.Empty<object>();
        if (SheetRows(sheet).All(r => p.BeginnerReceived.Contains(MasterData.F<ulong>(r, "id"))))
        {
            var done = sheetType is SheetTypeTraining ? p.TrainingMissionFinishedAt : sheetType is SheetTypeItemWorld ? p.ItemWorldMissionFinishedAt : null;
            if (done == "" || (done == null && sheet == p.MissionSheetNo))
            {
                foreach (var reward in master.All("MSheetReward").Where(r => MasterData.F<int>(r, "sheet_no") == sheet))
                    GiveFrom(p, reward, "Mission sheet complete!");
                if (sheetType == SheetTypeTraining) p.TrainingMissionFinishedAt = Now;
                else if (sheetType == SheetTypeItemWorld) p.ItemWorldMissionFinishedAt = Now;
                else
                {
                    p.MissionSheetNo = sheet + 1; // a sheet that doesn't exist hides the home icon
                    nextRows = SheetWire(p, p.MissionSheetNo);
                }
                Log.Info($"Mission sheet {sheet} complete.");
            }
        }

        return Obj(
            ("new_present", Rewards.PendingCount(p)),
            ("mission_sheet_no", p.MissionSheetNo), // a different number than the client's = sheet finished
            ("mission_datas", SheetWire(p, sheet)),
            ("next_mission_datas", nextRows),
            ("after_t_sub_tutorials", SchemaWriter.Nil));
    }

    private int SheetFor(Player p, int sheetType) => sheetType switch
    {
        SheetTypeTraining => SheetTraining,
        SheetTypeItemWorld => SheetItemWorld,
        _ => p.MissionSheetNo,
    };

    // The client only takes the first 12 rows of a sheet.
    private List<object> SheetRows(int sheet) => master.All("MBeginnerMission")
        .Where(r => MasterData.F<int>(r, "sheet_no") == sheet)
        .OrderBy(r => MasterData.F<int>(r, "sort")).Take(MissionsPerSheet).ToList();

    private List<object?> SheetWire(Player p, int sheet) => SheetRows(sheet).Select(r =>
    {
        var id = MasterData.F<ulong>(r, "id");
        return (object?)Obj(("id", id), ("status", p.BeginnerReceived.Contains(id)),
            ("now_num", Math.Min(BeginnerNow(p, r), MasterData.F<long>(r, "condition_num"))));
    }).ToList();

    // SyncDefineData.mission_condition_type_*
    private long BeginnerNow(Player p, object row)
    {
        var type = MasterData.F<int>(row, "condition_type");
        var id = MasterData.F<ulong>(row, "condition_id");
        var num = MasterData.F<long>(row, "condition_num");
        return type switch
        {
            1 => B(AllCleared(p, EpisodeStages(id, 1))),                     // episode_clear (Easy)
            2 => B(AllCleared(p, AreaStages(id, 1))),                        // area_clear
            3 or 42 or 44 => id == 0 ? p.StageClears.Count : B(p.StageClears.ContainsKey(id)), // stage_clear
            4 => AreaStages(id, 0).Count(p.StageClears.ContainsKey),         // area_stage_clear
            5 => Progress.Total(p, Progress.AreaBattle, id),                 // area_stage_battle
            6 => Progress.Total(p, Progress.Battle),                         // battle_num
            8 => B(AllMissions(p, EpisodeStages(id, 1))),                    // easy_mission_complete
            10 => Progress.Total(p, Progress.Gacha),
            11 => p.Characters.Select(c => (long)c.Lv).DefaultIfEmpty(0).Max(), // character_lv
            21 => Progress.Total(p, Progress.Rebirth),                         // rebirth
            13 => Progress.Total(p, Progress.EquipBuy),
            14 => p.Gear.Select(g => (long)g.Lv).DefaultIfEmpty(0).Max(),     // equip_lv
            30 => Progress.Total(p, Progress.Party),                         // update_deck
            31 => Progress.Total(p, Progress.Present),                       // present_receive
            41 => Progress.Total(p, Progress.ItemWorldClear),
            56 => B(AllMissions(p, [id])),                                   // specific_stage_mission_complete
            66 => Progress.Total(p, Progress.ItemWorldWeapon),
            67 => Progress.Total(p, Progress.ItemWorldEquipment),
            68 => Progress.Total(p, Progress.ItemWorldFloor),                // item_world_stage
            69 => Progress.Total(p, Progress.EquipBuy, id),                  // equip_buy_rare (1/2/3 band)
            _ => num, // can't be observed / not available offline: counts as done (see top)
        };
    }

    // ---- Trophy tabs ----------------------------------------------------------------------------

    private sealed record Family(string Table, string IdField, string AfterField, bool Repetition = false);

    private static readonly Family Trophy = new("MTrophy", "m_trophy_id", "after_t_trophies");
    private static readonly Family Daily = new("MTrophyDaily", "m_trophy_daily_id", "after_t_trophy_dailies");
    private static readonly Family Weekly = new("MTrophyWeekly", "m_trophy_weekly_id", "after_t_trophy_weeklies");
    private static readonly Family Repetition = new("MTrophyRepetition", "m_trophy_repetition_id", "after_t_trophy_repetitions", true);
    private static readonly Family[] Families = [Trophy, Daily, Weekly, Repetition];

    public object? Trophies(Player? p, JsonObject q) => List(p, Trophy);
    public object? Dailies(Player? p, JsonObject q) => List(p, Daily);
    public object? Weeklies(Player? p, JsonObject q) => List(p, Weekly);
    public object? Repetitions(Player? p, JsonObject q) => List(p, Repetition);
    public object? RewardTrophy(Player? p, JsonObject q) => Receive(p, q, Trophy);
    public object? RewardDaily(Player? p, JsonObject q) => Receive(p, q, Daily);
    public object? RewardWeekly(Player? p, JsonObject q) => Receive(p, q, Weekly);
    public object? RewardRepetition(Player? p, JsonObject q) => Receive(p, q, Repetition);

    // Every row must have a master record (the client looks its title up); null would throw.
    private object? List(Player? p, Family f) =>
        p == null ? null : Obj(("t_trophies", Visible(p, f).Select(r => (object?)Wire(p, f, r)).ToList()));

    private object? Receive(Player? p, JsonObject q, Family f)
    {
        if (p == null) return null;
        var id = U(q, "id");
        var all = I(q, "receive_all") == 1;
        var done = new List<object?>();
        foreach (var row in Visible(p, f).ToList())
        {
            var rowId = MasterData.F<ulong>(row, "id");
            if ((!all && rowId != id) || Status(p, f, row) != StatusClear) continue;
            var title = MasterData.F<string>(row, "title");
            if (f.Repetition)
            {
                var cond = Math.Max(1, MasterData.F<long>(row, "condition_num"));
                var stock = NowNum(p, f, row) / cond;
                p.RepetitionUsed[rowId] = p.RepetitionUsed.GetValueOrDefault(rowId) + stock * cond;
                GiveFrom(p, row, title, stock);
            }
            else
            {
                Received(p, f)!.Add(rowId);
                GiveFrom(p, row, title);
            }
            done.Add(Wire(p, f, row));
        }
        Log.Info($"Trophies ({f.Table}): received {done.Count}.");
        // Only the rows just received; the client shows one reward per row.
        return Obj(Families.Select(x => (x.AfterField, x == f ? (object?)done : SchemaWriter.Nil)).ToArray());
    }

    public object? BadgeHomes(Player? p, JsonObject q)
    {
        if (p == null) return null;
        int Clear(Family f) => Visible(p, f).Count(r => Status(p, f, r) == StatusClear);
        return Obj(
            ("new_friend", Obj(("new_friend_count", 0), ("is_update", false))),
            ("new_information_at", ""),
            ("new_present", Rewards.PendingCount(p)),
            ("new_trophy", Clear(Trophy)), ("new_trophy_daily", Clear(Daily)),
            ("new_trophy_weekly", Clear(Weekly)), ("new_trophy_repetition", Clear(Repetition)),
            ("new_trophy_passport", 0), ("new_trophy_daily_request", 0), ("new_passport", 0));
    }

    private Dictionary<string, object?> Wire(Player p, Family f, object row)
    {
        var now = NowNum(p, f, row);
        var d = Obj(
            (f.IdField, MasterData.F<ulong>(row, "id")),
            ("now_num", f.Repetition ? now : Math.Min(now, MasterData.F<long>(row, "condition_num"))),
            ("status", Status(p, f, row)));
        if (f == Daily) d["date"] = p.DailyKey;
        return d;
    }

    private HashSet<ulong>? Received(Player p, Family f) =>
        f == Trophy ? p.TrophiesReceived : f == Daily ? p.DailyReceived : f == Weekly ? p.WeeklyReceived : null;

    private int Status(Player p, Family f, object row)
    {
        var id = MasterData.F<ulong>(row, "id");
        if (Received(p, f)?.Contains(id) == true) return StatusReceived;
        return NowNum(p, f, row) >= Math.Max(1, MasterData.F<long>(row, "condition_num")) ? StatusClear : StatusProgress;
    }

    // Rows in term; trophies form chains (next_m_trophy_id) and a link shows once the previous
    // one has been received.
    private IEnumerable<object> Visible(Player p, Family f)
    {
        Progress.Roll(p);
        var now = DateTime.UtcNow;
        var rows = master.All(f.Table).Where(r => InTerm(r, now));
        if (f != Trophy) return rows;
        var previous = PreviousTrophy();
        return rows.Where(r => !previous.TryGetValue(MasterData.F<ulong>(r, "id"), out var prev) || p.TrophiesReceived.Contains(prev));
    }

    private Dictionary<ulong, ulong>? _previousTrophy;
    private Dictionary<ulong, ulong> PreviousTrophy()
    {
        if (_previousTrophy != null) return _previousTrophy;
        var map = new Dictionary<ulong, ulong>();
        foreach (var r in master.All("MTrophy"))
        {
            var next = MasterData.F<ulong>(r, "next_m_trophy_id");
            if (next != 0) map.TryAdd(next, MasterData.F<ulong>(r, "id"));
        }
        return _previousTrophy = map;
    }

    // Blank or unreadable dates mean "no limit" here (many rows have none).
    private static bool InTerm(object row, DateTime now)
    {
        var open = MasterData.F<string>(row, "open_at");
        var close = MasterData.F<string>(row, "close_at");
        return !(DateTime.TryParse(open, out var o) && now < o) && !(DateTime.TryParse(close, out var c) && now >= c);
    }

    private long NowNum(Player p, Family f, object row)
    {
        var type = MasterData.F<int>(row, "condition_type");
        var id = MasterData.F<ulong>(row, "condition_id");
        if (f == Daily) return DailyNow(p, type);
        if (f == Weekly) return TrophyNow(p, type, id, (ev, i) => Progress.Week(p, ev, i));
        var total = TrophyNow(p, type, id, (ev, i) => Progress.Total(p, ev, i));
        return f.Repetition ? total - p.RepetitionUsed.GetValueOrDefault(MasterData.F<ulong>(row, "id")) : total;
    }

    // SyncDefineData.trophy_daily_type_*; the other daily missions (Dark Assembly, gates, friends)
    // don't exist offline and stay at 0.
    private static readonly int[] DailyTracked = [1, 4, 5, 6];

    private long DailyNow(Player p, int type) => type switch
    {
        1 => Progress.Today(p, Progress.Gacha),
        4 => Progress.Today(p, Progress.Act),
        5 => Progress.Today(p, Progress.EquipBuy),
        6 => 0, // item shop: not implemented yet
        AllComplete => B(Visible(p, Daily).Where(r => DailyTracked.Contains(MasterData.F<int>(r, "condition_type")))
            .All(r => p.DailyReceived.Contains(MasterData.F<ulong>(r, "id")))),
        _ => 0,
    };

    // SyncDefineData.trophy_type_* (also used by the weekly and repetition tables).
    private long TrophyNow(Player p, int type, ulong id, Func<string, ulong, long> counter) => type switch
    {
        1 => counter(Progress.Login, 0),                                    // date (days logged in)
        3 => counter(Progress.Rebirth, 0),                                  // rebirth
        8 or 9 or 10 => B(AllMissions(p, EpisodeStages(id, type - 7))),     // complete_mission easy/normal/hard
        11 or 12 or 13 => B(AllCleared(p, EpisodeStages(id, type - 10))),   // complete_episode easy/normal/hard
        14 => p.Rank,                                                       // player_rank
        15 => counter(Progress.Enemy, 0),                                   // enemy_defeat
        18 or 63 => counter(Progress.ItemWorldClear, 0),                    // item_world / item_world_clear
        19 => p.Gear.Count(g => g.Lv >= g.LvMax),                           // equipment_lv_max
        20 => p.Characters.Select(c => c.MCharacterId).Distinct().Count(),  // character_collection
        21 => p.Gear.Select(g => (g.Kind, g.MId)).Distinct().Count(),       // equipment_collection
        22 => p.Items.GetValueOrDefault(101UL),                             // hl
        23 => counter(Progress.Gacha, 0),
        25 => counter(Progress.Act, 0),                                     // consumed_act
        28 or 29 or 30 => p.TrophiesReceived.Count(t => master.Get("MTrophy", t) is { } r && MasterData.F<int>(r, "rank") == type - 27),
        62 => counter(Progress.StoneSpent, 0),                              // use_stone
        64 => counter(Progress.HlSpent, 0),                                 // use_hl
        _ => 0, // not tracked offline (see top)
    };

    // ---- Stages ---------------------------------------------------------------------------------

    private Dictionary<ulong, List<(ulong Id, int Rank, bool Story)>>? _stagesByArea;
    private Dictionary<ulong, List<ulong>>? _areasByEpisode;

    // rank 0 = any difficulty. Story-only stages (proper_level "-") have no missions.
    private IEnumerable<ulong> AreaStages(ulong area, int rank, bool battlesOnly = false)
    {
        _stagesByArea ??= master.All("MStage").GroupBy(s => MasterData.F<ulong>(s, "m_area_id")).ToDictionary(g => g.Key,
            g => g.Select(s => (MasterData.F<ulong>(s, "id"), MasterData.F<int>(s, "rank"), MasterData.F<string>(s, "proper_level") == "-")).ToList());
        return _stagesByArea.TryGetValue(area, out var list)
            ? list.Where(s => (rank == 0 || s.Rank == rank) && !(battlesOnly && s.Story)).Select(s => s.Id)
            : [];
    }

    private IEnumerable<ulong> EpisodeStages(ulong episode, int rank, bool battlesOnly = false)
    {
        _areasByEpisode ??= master.All("MArea").GroupBy(a => MasterData.F<ulong>(a, "m_episode_id"))
            .ToDictionary(g => g.Key, g => g.Select(a => MasterData.F<ulong>(a, "id")).ToList());
        return _areasByEpisode.TryGetValue(episode, out var areas) ? areas.SelectMany(a => AreaStages(a, rank, battlesOnly)) : [];
    }

    private static bool AllCleared(Player p, IEnumerable<ulong> stages)
    {
        var list = stages.ToList();
        return list.Count > 0 && list.All(p.StageClears.ContainsKey);
    }

    private bool AllMissions(Player p, IEnumerable<ulong> stages)
    {
        var list = stages.Where(s => master.Get("MStage", s) is { } m && MasterData.F<string>(m, "proper_level") != "-").ToList();
        return list.Count > 0 && list.All(s => p.StageMissions.TryGetValue(s, out var m) && m.All(x => x));
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private void GiveFrom(Player p, object row, string message, long times = 1) => rewards.Give(p,
        MasterData.F<int>(row, "present_type"), MasterData.F<ulong>(row, "present_id"),
        MasterData.F<int>(row, "present_rarity"), MasterData.F<long>(row, "present_num") * times, message);

    private static long B(bool v) => v ? 1 : 0;
}
