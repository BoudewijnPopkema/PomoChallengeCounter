using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PomoChallengeCounter.Services;

public class LocalizationService(ILogger<LocalizationService> logger)
{
    private readonly Dictionary<string, Dictionary<string, object>> _translations = new();

    public async Task InitializeAsync()
    {
        try
        {
            logger.LogInformation("Loading localization files...");
            
            await LoadTranslationsAsync("en");
            await LoadTranslationsAsync("nl");
            
            logger.LogInformation("Localization initialized with {Count} languages", _translations.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize localization");
            throw;
        }
    }

    public string GetString(string key, string language = "en", params object[] args)
    {
        try
        {
            var translation = GetTranslation(key, language);
            return args.Length > 0 ? string.Format(translation, args) : translation;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get translation for key '{Key}' in language '{Language}'", key, language);
            return key; // Fallback to key if translation fails
        }
    }

    private string GetTranslation(string key, string language)
    {
        // Try requested language first
        if (_translations.TryGetValue(language, out var languageDict))
        {
            if (TryGetNestedValue(languageDict, key, out var value))
            {
                return value;
            }
        }

        // Fallback to English
        if (language != "en" && _translations.TryGetValue("en", out var englishDict))
        {
            if (TryGetNestedValue(englishDict, key, out var englishValue))
            {
                return englishValue;
            }
        }

        // Return key if no translation found
        return key;
    }

    private static bool TryGetNestedValue(Dictionary<string, object> dict, string key, out string value)
    {
        value = string.Empty;
        var keys = key.Split('.');
        object current = dict;

        foreach (var keyPart in keys)
        {
            if (current is Dictionary<string, object> currentDict && currentDict.TryGetValue(keyPart, out var next))
            {
                current = next;
            }
            else
            {
                return false;
            }
        }

        switch (current)
        {
            case string stringValue:
                value = stringValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } jsonElement:
                value = jsonElement.GetString() ?? string.Empty;
                return true;
            default:
                return false;
        }
    }

    private async Task LoadTranslationsAsync(string language)
    {
        var resourceName = $"PomoChallengeCounter.Localization.{language}.json";
        var assembly = typeof(LocalizationService).Assembly;
        
        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            logger.LogWarning("Translation file not found: {ResourceName}", resourceName);
            return;
        }

        var jsonContent = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(stream);
        if (jsonContent != null)
        {
            _translations[language] = jsonContent;
            logger.LogDebug("Loaded {Count} translation keys for language '{Language}'", jsonContent.Count, language);
        }
    }
} 