using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Nether Pass (passport 1), active for everyone offline. The client only checks the days left
// (sum_days - now_days > 0): the home icon shows "Nd left" and the pass can't be bought again.
// Its value is the daily reward from MPassport (quartz, Gate Keys, Dark Gate skip coupons),
// sent to the gift box on the first login/update of each day together with the passport row,
// which opens the "today's pass reward" popup.
public sealed class Passport(MasterData master, Rewards rewards)
{
    private const ulong NetherPassId = 1;     // Const.NetherPassportId / SyncDefineData.now_m_passport_id
    private const int DaysLeft = 90;          // kept constant; 90+ also blocks re-buying (limit_num 90)

    public object? Index(Player? p, JsonObject q) =>
        p == null ? null : new List<object?> { Row(p) }; // ids must be unique (the client adds them to a dictionary)

    // Called from login/update: today's rewards once per day, else nothing (the popup would repeat).
    public List<object?> Daily(Player p)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (p.IsTutorial || p.PassportLastDay == today) return new List<object?>();
        p.PassportLastDay = today;
        p.PassportDays++;
        if (p.PassportStartedAt == "") p.PassportStartedAt = Time.Format(DateTime.UtcNow);
        if (master.Get("MPassport", NetherPassId) is { } pass)
        {
            var ids = MasterData.A<ulong>(pass, "m_item_ids");
            var nums = MasterData.A<int>(pass, "m_item_nums");
            for (var i = 0; i < ids.Length && i < nums.Length; i++)
                rewards.Give(p, Rewards.TypeItem, ids[i], 0, nums[i], "Nether Pass daily reward");
        }
        Log.Info($"Nether Pass: day {p.PassportDays} rewards sent to the gift box.");
        return new List<object?> { Row(p) };
    }

    private static Dictionary<string, object?> Row(Player p)
    {
        var now = Time.Format(DateTime.UtcNow);
        return Obj(
            ("m_passport_id", NetherPassId),
            ("now_days", p.PassportDays),
            ("sum_days", p.PassportDays + DaysLeft),
            ("last_received_at", p.PassportLastDay == "" ? now : p.PassportLastDay + " 00:00:00"),
            ("last_purchased_at", p.PassportStartedAt == "" ? now : p.PassportStartedAt),
            ("updated_at", now));
    }
}
