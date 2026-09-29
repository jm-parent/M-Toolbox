using System.Runtime.Versioning;
using Meziantou.Framework.Win32;
using MToolbox.Core.Enrichment;

namespace MToolbox.App.Services;

/// <summary>Stocke les PAT dans le Gestionnaire d'identifiants Windows, jamais en clair sur disque.</summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class CredentialTokenStore : ITokenStore
{
    private static string Name(string key) => $"MToolbox:{key}";

    public string? Get(string key) => CredentialManager.ReadCredential(Name(key))?.Password;

    public void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            CredentialManager.DeleteCredential(Name(key));
        else
            CredentialManager.WriteCredential(Name(key), "token", value.Trim(), CredentialPersistence.LocalMachine);
    }
}
