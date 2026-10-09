using System.Buffers;
using System.Text.Json.Nodes;
using MessagePack;
using MessagePack.Resolvers;

namespace DrpgServer;

// Handles the encrypted API: REST (/version_check, /signin) and JSON-RPC over /rpc.
public sealed class ApiRouter
{
    private static readonly MessagePackSerializerOptions Lenient =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private readonly GameTypes _types;
    public GameTypes Types => _types;
    private readonly PlayerStore _players;
    private readonly Handlers _handlers;

    public ApiRouter(ServerConfig cfg, PlayerStore players)
    {
        _types = new GameTypes(cfg);
        Crypto.Init(_types);
        _players = players;
        _handlers = new Handlers(_types, players);
    }

    public async Task Handle(HttpContext ctx)
    {
        var req = ctx.Request;
        var ivHeader = req.Headers["X-Crypt-Iv"].ToString();
        if (string.IsNullOrEmpty(ivHeader))
        {
            Log.Warn($"Request without X-Crypt-Iv: {req.Method} {req.Path}");
            ctx.Response.StatusCode = 404;
            return;
        }
        var iv = Convert.FromBase64String(ivHeader);

        var raw = await ReadBody(req);
        var body = Decode(raw, iv);

        ctx.Response.Headers["X-Crypt-Iv"] = ivHeader;
        ctx.Response.Headers["X-SERVER-UNIXTIME"] = Time.Now.ToString();

        byte[] payload;
        switch (req.Path.Value?.TrimStart('/'))
        {
            case "version_check":
                payload = Plain(new Dictionary<string, object?>
                {
                    ["status"] = 0,
                    ["need_update"] = false,
                    ["app_review"] = false,
                    ["store_url"] = "",
                });
                break;

            case "signin":
            case "steam/login":
            case "sdk/login":
                payload = Plain(SignIn(body));
                break;

            case "rpc":
                ctx.Response.Headers["X-APP-STATUS"] = "10000";
                payload = Rpc(body, req.Headers["X-SESSION"].ToString());
                break;

            default:
                Log.Warn($"Unknown REST route: {req.Path}");
                payload = Plain(new Dictionary<string, object?>());
                break;
        }

        ctx.Response.ContentType = "application/x-haut-hoiski";
        await ctx.Response.Body.WriteAsync(Crypto.Encrypt(payload, iv));
    }

    private Dictionary<string, object?> SignIn(Dictionary<object, object?> body)
    {
        // Local server: the account name only picks a save file; the password is ignored.
        var uuid = Str(body, "uuid") ?? Str(body, "openId");
        if (string.IsNullOrWhiteSpace(uuid)) uuid = "player";
        var password = Str(body, "password") ?? "";
        var (player, isNew) = _players.Login(uuid, password);
        Log.Info($"Login: '{uuid}' ({(isNew ? "new account" : "existing account")})");
        return new Dictionary<string, object?>
        {
            ["session_id"] = _players.OpenSession(player),
            ["fuji_key"] = Crypto.CommonKey,
            ["is_new"] = isNew,
        };
    }

    private byte[] Rpc(Dictionary<object, object?> body, string session)
    {
        var rpc = body.GetValueOrDefault("rpc") as Dictionary<object, object?> ?? body;
        var method = Str(rpc, "method") ?? "";
        var id = Str(rpc, "id") ?? "";
        var prmsJson = Str(rpc, "prms");
        var prms = string.IsNullOrEmpty(prmsJson) ? new JsonObject() : JsonNode.Parse(prmsJson) as JsonObject ?? new JsonObject();
        var player = _players.BySession(session);

        object? result;
        var known = _handlers.TryHandle(method, player, prms, out result);
        if (!known)
            Log.Warn($"RPC not implemented (default response): {method} {prmsJson}");
        else
            Log.Info($"RPC {method} {prmsJson}");

        _types.ResultTypes.TryGetValue(method, out var resultType);
        if (resultType == null)
            Log.Error($"Unknown response type for {method}; sending result=nil.");

        var buffer = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(buffer);
        w.WriteMapHeader(3);
        w.Write("jsonrpc"); w.Write("2.0");
        w.Write("id"); w.Write(id);
        w.Write("result");
        if (resultType == null || result is SchemaWriter.NilValue)
            w.WriteNil();
        else
            SchemaWriter.Write(ref w, resultType, result ?? Handlers.Empty(resultType), method);
        w.Flush();

        if (player != null) _players.Save(player);
        return buffer.WrittenSpan.ToArray();
    }

    // account: run as that save (it gets modified, so use a throwaway account).
    public string DumpDefault(string method, string prms = "{}", string? account = null)
    {
        var session = "";
        if (account != null) session = _players.OpenSession(_players.Login(account, "").Player);
        var bytes = Rpc(new Dictionary<object, object?>
        {
            ["rpc"] = new Dictionary<object, object?> { ["method"] = method, ["id"] = "dump", ["prms"] = prms },
        }, session);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        return $"{method}: {bytes.Length} bytes\n{(json.Length > 3000 ? json[..3000] + "..." : json)}";
    }

    private static byte[] Plain(Dictionary<string, object?> map) =>
        MessagePackSerializer.Serialize(map, Lenient);

    private static Dictionary<object, object?> Decode(byte[] raw, byte[] iv)
    {
        if (raw.Length == 0) return new();
        byte[] plain;
        try { plain = Crypto.Decrypt(raw, iv); }
        catch { plain = raw; }
        try
        {
            return MessagePackSerializer.Deserialize<object>(plain, Lenient) as Dictionary<object, object?> ?? new();
        }
        catch (Exception e)
        {
            Log.Error($"Unreadable request body: {e.Message}");
            return new();
        }
    }

    private static async Task<byte[]> ReadBody(HttpRequest req)
    {
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static string? Str(Dictionary<object, object?> d, string key) =>
        d.TryGetValue(key, out var v) ? v?.ToString() : null;
}
