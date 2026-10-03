using System.Security.Cryptography;
using System.Text;

namespace Cake.Download.Module.Tests.Fakes;

internal static class TestHashes
{
    public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
