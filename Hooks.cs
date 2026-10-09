using System.Text.Json.Nodes;

namespace DrpgServer;

// UI hooks (XD.Hook): the client loads a hook_j file of small UI changes at boot, from the URL in
// the server config when there is one. The server serves the installed StreamingAssets/Data/hook_j
// with extra entries appended; the served file replaces the whole set, so the originals (about
// 1,800 layout and localisation fixes) are kept. No game file is changed.
public static class Hooks
{
    private const string AlwaysOn = "XD_Const_True"; // any key containing "Const_True" is always true

    // Offline AP is never spent: hide the AP block of the top bar (value, gauge, recovery timer
    // and the "+" button). XDHookActive sets the object inactive, also whenever it gets re-enabled.
    // Paths start at a GameObject carrying XDPluginHookRoot; a path that isn't found does nothing.
    private static readonly string[] Hidden =
    [
        "header/act_win",
        "arena_battle_header/act_win",
    ];

    public static byte[]? Build(ServerConfig cfg, GameTypes types)
    {
        var file = Path.Combine(cfg.StreamingAssets, "Data", "hook_j");
        if (!File.Exists(file))
        {
            Log.Warn($"{file} not found; UI hooks not served.");
            return null;
        }
        try
        {
            var root = JsonNode.Parse(XdSettings.Decode(types, File.ReadAllBytes(file)))!.AsObject();
            var attach = new JsonArray();
            foreach (var path in Hidden)
                attach.Add(new JsonObject
                {
                    ["path"] = path, ["setting_key"] = AlwaysOn, ["delay"] = "0", ["priority"] = "0", ["data"] = new JsonObject(),
                });
            root["hook"]!.AsArray().Add(new JsonObject { ["type"] = "XDDLL:XD.Hook.XDHookActive", ["attach"] = attach });
            Log.Info($"UI hooks: original set + {Hidden.Length} (AP bar hidden).");
            // Keep text as in the original (no \u escapes) for the game's own JSON reader.
            var options = new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            return XdSettings.Encode(types, root.ToJsonString(options));
        }
        catch (Exception e)
        {
            Log.Warn($"Could not build hook_j ({e.Message}); the client keeps its own.");
            return null;
        }
    }
}
