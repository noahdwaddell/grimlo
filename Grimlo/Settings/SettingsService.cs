using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Grimlo.Settings;

/// <summary>
/// Persists Grimlo settings in %APPDATA%\Grimlo\settings.json,
/// with API keys encrypted using Windows DPAPI (CurrentUser scope).
/// </summary>
public class SettingsService : ISettingsService
{
    private class SettingsStorageModel
    {
        public UserSettings Settings { get; set; } = new();
        public Dictionary<string, string> EncryptedKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly object _lock = new();
    private readonly string _folderPath;
    private readonly string _filePath;
    private SettingsStorageModel _storage = new();

    public UserSettings Settings => _storage.Settings;

    public SettingsService(string? customFolder = null)
    {
        _folderPath = customFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Grimlo"
        );
        _filePath = Path.Combine(_folderPath, "settings.json");

        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    _storage = JsonSerializer.Deserialize<SettingsStorageModel>(json) ?? new();
                }
            }
            catch (Exception)
            {
                // Fall back to default settings if file is corrupt or unreadable
                _storage = new();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(_folderPath))
                {
                    Directory.CreateDirectory(_folderPath);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_storage, options);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception)
            {
                // In desktop apps, saving settings should fail gracefully if disk is locked
            }
        }
    }

    public string? GetApiKey(string provider)
    {
        lock (_lock)
        {
            string key = provider.Trim().ToLowerInvariant();

            // 1. Check encrypted DPAPI store
            if (_storage.EncryptedKeys.TryGetValue(key, out string? base64) && !string.IsNullOrWhiteSpace(base64))
            {
                try
                {
                    byte[] encrypted = Convert.FromBase64String(base64);
                    byte[] decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(decrypted);
                }
                catch (Exception)
                {
                    // Decryption failed (e.g. data transferred to different machine or user)
                }
            }

            // 2. Fall back to environment variable if set
            string? envProvider = Environment.GetEnvironmentVariable("GRIMLO_PROVIDER")?.Trim().ToLowerInvariant();
            if (string.Equals(envProvider ?? "gemini", key, StringComparison.OrdinalIgnoreCase))
            {
                string? envKey = Environment.GetEnvironmentVariable("GRIMLO_API_KEY");
                if (!string.IsNullOrWhiteSpace(envKey))
                {
                    return envKey;
                }
            }

            return null;
        }
    }

    public void SetApiKey(string provider, string apiKey)
    {
        lock (_lock)
        {
            string key = provider.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _storage.EncryptedKeys.Remove(key);
            }
            else
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(apiKey.Trim());
                byte[] encrypted = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                _storage.EncryptedKeys[key] = Convert.ToBase64String(encrypted);
            }

            Save();
        }
    }

    public void RemoveApiKey(string provider)
    {
        SetApiKey(provider, string.Empty);
    }

    public bool HasApiKey(string provider)
    {
        return !string.IsNullOrWhiteSpace(GetApiKey(provider));
    }
}
