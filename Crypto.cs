using System.Security.Cryptography;
using System.Text;

namespace DrpgServer;

// AES-256-CBC/PKCS7 as in Orange.Scripts.Cryptor.WebRequestAesCryptor. The client switches
// to the per-session "fuji_key" after login; we hand out the common key as fuji_key so a
// single key covers every request.
public static class Crypto
{
    public const string CommonKey = "<read from the installed game>";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(CommonKey);

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
