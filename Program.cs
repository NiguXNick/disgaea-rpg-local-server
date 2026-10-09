using DrpgServer;

// Local replacement for the shut-down Disgaea RPG (global) servers.
var config = ServerConfig.Load(args);
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(config.BaseUrl);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<PlayerStore>();
builder.Services.AddSingleton<ApiRouter>();

var app = builder.Build();
var api = app.Services.GetRequiredService<ApiRouter>(); // loads the game's types up front
var masterFix = new MasterFix(api.Types);

// --master-test <bin file> <element type>: deserialise a master table with the game's own reader.
var mt = Array.IndexOf(args, "--master-test");
if (mt >= 0 && mt + 2 < args.Length)
{
    Console.WriteLine(api.Types.TestMasterBin(args[mt + 1], args[mt + 2]));
    return;
}

if (args.Contains("--master-fix-all"))
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    masterFix.Apply();
    Log.Info($"Done in {sw.Elapsed.TotalSeconds:f0}s");
    return;
}

var mfix = Array.IndexOf(args, "--master-fix");
if (mfix >= 0 && mfix + 1 < args.Length)
{
    masterFix.ApplyTable(args[mfix + 1]);
    return;
}

var mf = Array.IndexOf(args, "--master-fields");
if (mf >= 0 && mf + 1 < args.Length)
{
    Console.WriteLine(api.Types.DescribeMasterFields(args[mf + 1]));
    return;
}

// --master-test-all <dir>: same check for every table (MFoo_1.bin -> MasterFooData, BFoo -> BoltrendFooData).
var mta = Array.IndexOf(args, "--master-test-all");
if (mta >= 0 && mta + 1 < args.Length)
{
    foreach (var f in Directory.GetFiles(args[mta + 1], "*.bin").OrderBy(x => x))
    {
        var name = Path.GetFileNameWithoutExtension(f);
        var table = name[..name.LastIndexOf('_')];
        var type = (table[0] == 'B' ? "Boltrend" : "Master") + table[1..] + "Data";
        string res;
        try { res = api.Types.TestMasterBin(f, type).Split('\n')[0].Trim(); }
        catch (Exception e) { res = "type not found (" + e.GetType().Name + ")"; }
        if (!res.Contains(": OK")) Console.WriteLine($"{Path.GetFileName(f)} [{type}] -> {res}");
    }
    Console.WriteLine("done");
    return;
}

// --master-rows <table> <field> <value>: print master rows whose field equals value.
var mr = Array.IndexOf(args, "--master-rows");
if (mr >= 0 && mr + 3 < args.Length)
{
    var md = new MasterData(api.Types);
    foreach (var row in md.All(args[mr + 1]).Where(r => MasterData.F<string>(r, args[mr + 2]) == args[mr + 3]).Take(15))
        Console.WriteLine(string.Join(" ", row.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(f => !f.FieldType.IsClass || f.FieldType == typeof(string))
            .Select(f => $"{f.Name}={f.GetValue(row)}")));
    return;
}

// --dump <method>: print the default response for an RPC method and exit (schema check).
var dump = Array.IndexOf(args, "--dump");
if (dump >= 0 && dump + 1 < args.Length)
{
    // --dump <method> [--prms <json>] [--as <account>]
    string? Opt(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    Console.WriteLine(api.DumpDefault(args[dump + 1], Opt("--prms") ?? "{}", Opt("--as")));
    return;
}

masterFix.Apply();

app.Use(async (ctx, next) =>
{
    await next();
    Log.Http(ctx.Request.Method, ctx.Request.Path + ctx.Request.QueryString, ctx.Response.StatusCode);
});

// Static assets must run before routing, otherwise the API fallback endpoint swallows them.
BootFiles.UseAssets(app, config);
app.UseRouting();
BootFiles.Map(app, config, masterFix);
app.MapFallback(async (HttpContext ctx) => await api.Handle(ctx));

Log.Info($"Netherworld Reborn listening on {config.BaseUrl}");
Log.Info($"Game folder: {config.GameDir}");
Log.Info($"Save data: {config.DataDir}");
app.Run();
