using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace DrpgServer;

// Files the client fetches before talking to the API: the server list, the
// server .ini (which points every other URL back at us), the SDK config and
// the asset CDN (served straight from the local StreamingAssets copy).
public static class BootFiles
{
    public static void Map(WebApplication app, ServerConfig cfg, MasterFix masterFix, GameTypes types)
    {
        var b = cfg.BaseUrl;

        // The client looks its server entry up by the server_key of its own config_server.ini
        // (setup-game.ps1 keeps it when redirecting): a '|'-separated list, the first one is used.
        var settings = Path.Combine(cfg.StreamingAssets, "settings", "config_server.ini");
        var serverKey = XdSettings.Value(XdSettings.Read(types, settings), "server_key")?.Split('|')[0];
        if (string.IsNullOrEmpty(serverKey))
            Log.Error($"No server_key in {settings}; run tools/setup-game.ps1 (or restore-game.ps1 first).");

        app.MapGet("/Server/List.ini", () => Text(serverKey + "\n"));

        // Config.ReadText stops at the first blank line and throws on lines without '='.
        app.MapGet($"/Server/{serverKey}.ini", () => Text(string.Join("\n",
            $"api={b}/",
            $"asset={b}/asset",
            $"master_bin={b}/master/master",
            "time_zone=0",
            $"url_file={b}/file",
            $"url_zip={b}/zip",
            $"config_sdk={b}/sdk/config_sdk.json",
            cfg.ClientLogAll ? "log=Log:ALL" : "log=Log:default,ErrorCheck,LogError",
            "AutoSignin=false") + "\n"));

        // "Disable" makes the hotfix skip the Boltrend web login and use /signin with uuid+password.
        app.MapGet("/sdk/config_sdk.json", () => Text("""
            {
                "status": "Disable",
                "default_login": 0,
                "default_pay": 0,
                "default_more_game": 0,
                "sdk_configs": [],
                "features": {
                    "back_to_exit": false,
                    "account_type": "Account",
                    "user_name": false,
                    "more_game": false,
                    "back_to_logout": false,
                    "exit_enable": false,
                    "user_center": false
                }
            }
            """));

        // No remote master: a 404 on master_bin_ver makes the client use StreamingAssets/Data/master.
        // The client asks for master_bin_ver right before loading the master tables: repair them now.
        app.MapGet("/master/{**rest}", () =>
        {
            masterFix.Apply();
            return Results.NotFound();
        });
        app.MapGet("/file/{**rest}", () => Results.NotFound());

        // Remote pictures (news/event banners) lived only on Boltrend's CDN. A 404 makes the client
        // skip them; an image (even a transparent one) gets cached and shown as an empty popup.
        app.MapGet("/asset/rpr/{**rest}", () => Results.NotFound());
        app.MapGet("/zip/{**rest}", () => Results.NotFound());
    }

    public static void UseAssets(WebApplication app, ServerConfig cfg)
    {
        var windows = Path.Combine(cfg.StreamingAssets, "windows");
        if (Directory.Exists(windows))
        {
            var types = new FileExtensionContentTypeProvider();
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(windows),
                RequestPath = "/asset/windows",
                ServeUnknownFileTypes = true,
                DefaultContentType = "application/octet-stream",
                ContentTypeProvider = types,
            });
        }
        else
        {
            Log.Warn($"StreamingAssets/windows not found at {windows}; asset downloads will fail.");
        }
    }

    private static IResult Text(string s) => Results.Text(s, "text/plain");
}
