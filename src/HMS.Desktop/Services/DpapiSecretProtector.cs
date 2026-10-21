using System.Security.Cryptography;
using HMS.Desktop.Services.LocalServer;

namespace HMS.Desktop.Services;

/// <summary>Encrypts the built-in server's signing key with Windows DPAPI for the signed-in Windows user.</summary>
internal sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "HMS.LocalServer.v1"u8.ToArray();

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
