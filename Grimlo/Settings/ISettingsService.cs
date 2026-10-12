namespace Grimlo.Settings;

/// <summary>
/// Service managing persistent application settings and secure DPAPI credential storage.
/// </summary>
public interface ISettingsService
{
    UserSettings Settings { get; }

    /// <summary>Retrieves the decrypted API key for the specified provider.</summary>
    string? GetApiKey(string provider);

    /// <summary>Encrypts and saves the API key for the specified provider using Windows DPAPI.</summary>
    void SetApiKey(string provider, string apiKey);

    /// <summary>Deletes the stored API key for the specified provider.</summary>
    void RemoveApiKey(string provider);

    /// <summary>Checks whether a key is configured for the specified provider.</summary>
    bool HasApiKey(string provider);

    /// <summary>Saves current user settings to disk.</summary>
    void Save();
}
