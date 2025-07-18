namespace PomoChallengeCounter.Services;

public interface ILocalizationService
{
    Task InitializeAsync();
    string GetString(string key, string language = "en", params object[] args);
} 