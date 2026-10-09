using System.Security.Cryptography;
using System.Text;

namespace DrpgServer;

// AES-256-CBC/PKCS7 as in Orange.Scripts.Cryptor.WebRequestAesCryptor. The client switches
// to the per-session "fuji_key" after login; we hand out the common key as fuji_key so a
// single key covers every request. The key is not stored in this repository: Init reads the
// constant WebRequestAesCryptor.kCommonKey from the player's own Assembly-CSharp.dll.
public static class Crypto
{
    public static string CommonKey { get; private set; } = "";
    private static byte[] Key = [];

    public static void Init(GameTypes types)
    {
        var field = types.Get("Orange.Scripts.Cryptor.WebRequestAesCryptor")
            .GetField("kCommonKey", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        CommonKey = field?.GetRawConstantValue() as string
            ?? throw new InvalidOperationException("AES key not found in Assembly-CSharp.dll (unsupported game build?).");
        Key = Encoding.UTF8.GetBytes(CommonKey);
    }

    public static byte[] Decrypt(byte[] data, byte[] iv)
    {
        using var aes = Create();
        return aes.DecryptCbc(data, iv, PaddingMode.PKCS7);
    }

    public static byte[] Encrypt(byte[] data, byte[] iv)
    {
        using var aes = Create();
        return aes.EncryptCbc(data, iv, PaddingMode.PKCS7);
    }

    private static Aes Create()
    {
        var aes = Aes.Create();
        aes.Key = Key;
        return aes;
    }
}
