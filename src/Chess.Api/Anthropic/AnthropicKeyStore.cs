using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Chess.Api.Anthropic;

/// <summary>
/// The Anthropic API key the owner typed into the app, kept in a file in the user's own folder, encrypted with Data Protection
/// (the key ring sits beside it). Never in the repository, the database or a log. The key is read only by the process that
/// calls Anthropic, and no endpoint returns it.
/// </summary>
public sealed class AnthropicKeyStore(IOptions<AnthropicOptions> options)
{
    private const string Purpose = "Chess.Api.Anthropic.ApiKey";
    private readonly object _gate = new();
    private IDataProtector? _protector;

    private string Folder => options.Value.KeyFolder is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "llm-chess-arena");

    private string KeyPath => Path.Combine(Folder, "anthropic.key");

    /// <returns>The saved key, or null when none is saved or the file cannot be read (a damaged file is the same as no key).</returns>
    public string? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(KeyPath))
            {
                return null;
            }

            try
            {
                return Protector().Unprotect(File.ReadAllText(KeyPath)) is { Length: > 0 } key ? key : null;
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public void Save(string key)
    {
        lock (_gate)
        {
            var protector = Protector();
            var temporary = KeyPath + ".tmp";
            File.WriteAllText(temporary, protector.Protect(key));
            OwnerOnly(temporary);
            File.Move(temporary, KeyPath, overwrite: true);
        }
    }

    public void Delete()
    {
        lock (_gate)
        {
            if (File.Exists(KeyPath))
            {
                File.Delete(KeyPath);
            }
        }
    }

    private IDataProtector Protector()
    {
        if (_protector is not null)
        {
            return _protector;
        }

        Directory.CreateDirectory(Folder);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var ring = Directory.CreateDirectory(Path.Combine(Folder, "keys"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(ring.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        _protector = DataProtectionProvider.Create(ring).CreateProtector(Purpose);
        return _protector;
    }

    private static void OwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
