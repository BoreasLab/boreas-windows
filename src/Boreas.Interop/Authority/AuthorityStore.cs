using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Boreas.Interop.Tunnel;

namespace Boreas.Interop.Authority;

/// <summary>
/// The one thing that is persisted, and the only thing.
/// </summary>
/// <remarks>
/// A user's one-time approval of a root through the system dialog cannot be
/// reconstituted by the process. Other core state is cacheable, and stale cache
/// data can silently suppress filtering, so it is cheaper to relearn it.
/// Store and offer the authority on every launch: restoring and storing is a
/// no-op, and offering an already trusted root shows no dialog.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class AuthorityStore
{
    private readonly string _path;

    public AuthorityStore(string path) => _path = path;

    /// <summary>
    /// The default location: one file under the local application data of
    /// whichever account the host runs as.
    /// </summary>
    /// <remarks>
    /// Local rather than roaming, because DPAPI's current-user scope is bound
    /// to this machine's account and a roamed copy would be a file no other
    /// machine can open.
    /// </remarks>
    public static AuthorityStore Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Boreas",
        "authority.dpapi"));

    /// <summary>
    /// What to hand <c>boreas_tunnel_start</c>: the stored authority, or a
    /// request for a fresh one.
    /// </summary>
    /// <remarks>
    /// Every failure to read collapses to <see cref="Trust.Generate"/>, because
    /// they all mean the same thing to the caller and because the alternative -
    /// refusing to start - strands a user with no way forward. What they do not
    /// collapse to is silence: generating means the old root in the certificate
    /// store no longer matches, and the caller is expected to offer the new one.
    /// </remarks>
    public Trust Load()
    {
        byte[] protectedBlob;

        try
        {
            protectedBlob = File.ReadAllBytes(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Trust.Generate.Instance;
        }

        byte[] blob;

        try
        {
            blob = ProtectedData.Unprotect(protectedBlob, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // Written by a different account, or on a different machine, or
            // corrupted. Indistinguishable from here and identical in effect.
            return Trust.Generate.Instance;
        }

        return AuthorityBlob.TryDecode(blob) is { } material
            ? new Trust.Restore(material.RootCertificate, material.Keys)
            : Trust.Generate.Instance;
    }

    /// <summary>
    /// Writes both halves, or neither.
    /// </summary>
    /// <remarks>
    /// Write a temporary file and rename it within the same directory, the
    /// filesystem's closest atomic publish. An in-place write can expose a
    /// prefix that either fails to decode or, if lengths happen to align,
    /// decodes as the wrong material.
    /// </remarks>
    public void Save(AuthorityMaterial material)
    {
        var directory = Path.GetDirectoryName(_path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var protectedBlob = ProtectedData.Protect(
            AuthorityBlob.Encode(material), optionalEntropy: null, DataProtectionScope.CurrentUser);

        var partial = _path + ".partial";

        File.WriteAllBytes(partial, protectedBlob);
        File.Move(partial, _path, overwrite: true);
    }
}

/// <summary>
/// Puts the root where Windows will trust it.
/// </summary>
/// <remarks>
/// The certificate is public, DER, and goes to the current user's ROOT store.
/// The keys never come here: they are secret, and the two halves travel
/// separately from this point precisely because only one of them is meant to be
/// readable.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class RootCertificate
{
    /// <summary>
    /// Offers the root to the current user's trust store.
    /// </summary>
    /// <returns>
    /// True when the store holds it afterwards, which includes the case where
    /// it already did.
    /// </returns>
    /// <remarks>
    /// Adding a certificate the store already holds is a no-op and shows no
    /// dialog, which is what makes offering unconditionally cheaper than
    /// checking first - a check would be a second way to be wrong about the
    /// same question.
    /// </remarks>
    public static bool Offer(ReadOnlySpan<byte> der)
    {
        // X509CertificateLoader rather than the X509Certificate2 constructor,
        // which is obsolete from .NET 9: the constructor guessed at the content
        // type, and guessing about a trust anchor is not a thing to do.
        using var certificate = X509CertificateLoader.LoadCertificate(der);
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);

        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);

        return store.Certificates.Contains(certificate);
    }
}
