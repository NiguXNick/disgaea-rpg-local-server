using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace DrpgServer;

// The game's settings files (StreamingAssets/settings/*.ini): gzip, then RC4 (XD.tool.XDCryptor).
// The RC4 key is read from the installed XDDLL.dll (FastCryptUtil.key_string), not stored here.
public static class XdSettings
{
    public static string Read(GameTypes types, string file) => Decode(types, File.ReadAllBytes(file));

    // Same scheme for every XDCryptor file (settings, hook_j).
    public static string Decode(GameTypes types, byte[] data)
    {
        var plain = Rc4(data, Key(types));
        using var gz = new GZipStream(new MemoryStream(plain), CompressionMode.Decompress);
        return new StreamReader(gz, Encoding.UTF8).ReadToEnd();
    }

    public static byte[] Encode(GameTypes types, string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(new UTF8Encoding(false).GetBytes(text));
        return Rc4(ms.ToArray(), Key(types));
    }

    private static byte[] Key(GameTypes types) => Encoding.UTF8.GetBytes(
        types.Xd.GetType("XD.tool.FastCryptUtil")
            ?.GetField("key_string", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() as string
        ?? throw new InvalidOperationException("Settings key not found in XDDLL.dll (unsupported game build?)."));

    // "key=value" lines; parsing stops at the first blank line like XD.tool.Config.ReadText.
    public static string? Value(string text, string name) => text.Split('\n')
        .Select(l => l.Trim()).TakeWhile(l => l.Length > 0)
        .FirstOrDefault(l => l.StartsWith(name + "="))?[(name.Length + 1)..];

    private static byte[] Rc4(byte[] data, byte[] key)
    {
        var s = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 255;
            (s[i], s[j]) = (s[j], s[i]);
        }
        var output = new byte[data.Length];
        for (int n = 0, i = 0, j = 0; n < data.Length; n++)
        {
            i = (i + 1) & 255;
            j = (j + s[i]) & 255;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 255]);
        }
        return output;
    }
}
