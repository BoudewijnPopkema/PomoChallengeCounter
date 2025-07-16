using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PomoChallengeCounter.Models;
using EmojiConverter = EmojiToolkit.Emoji;

namespace PomoChallengeCounter.Services;

public interface IEmojiService
{
    EmojiDetectionResult DetectEmojis(string messageContent);
    bool ValidateEmojiFormat(string emojiCode, out EmojiFormat format);
    bool ValidatePointValue(int pointValue);
    string NormalizeEmoji(string emojiCode);
    bool AreEmojisEquivalent(string emoji1, string emoji2);
}

public partial class EmojiService(ILogger<EmojiService> logger) : IEmojiService
{
    // Regex patterns for emoji detection
    [GeneratedRegex(@"<a?:[^:]+:\d+>")]
    private static partial Regex CustomEmojiRegex();
    
    [GeneratedRegex(@":[a-z0-9]*[a-z][a-z0-9_+-]*:")]
    private static partial Regex ShortcodeRegex();
    
    // Unicode emoji pattern (basic implementation - can be enhanced with full emoji list)
    [GeneratedRegex(@"[\u2600-\u27BF]|[\uD83C-\uD83D][\uDC00-\uDFFF]|[\u2B00-\u2BFF]")]
    private static partial Regex UnicodeEmojiRegex();

    public EmojiDetectionResult DetectEmojis(string messageContent)
    {
        try
        {
            var result = new EmojiDetectionResult();
            
            // Start with the original content
            var remainingContent = messageContent;
            
            // Detect custom Discord emojis first
            var customMatches = CustomEmojiRegex().Matches(messageContent);
            foreach (Match match in customMatches)
            {
                result.CustomEmojis.Add(match.Value);
            }
            
            // Replace custom emojis with placeholders to avoid double detection
            remainingContent = CustomEmojiRegex().Replace(remainingContent, " ");
            
            // Detect shortcode emojis (on content without custom emojis)
            var shortcodeMatches = ShortcodeRegex().Matches(remainingContent);
            foreach (Match match in shortcodeMatches)
            {
                result.ShortcodeEmojis.Add(match.Value);
            }
            
            // Detect Unicode emojis (on original content since they don't conflict)
            var unicodeMatches = UnicodeEmojiRegex().Matches(messageContent);
            foreach (Match match in unicodeMatches)
            {
                result.UnicodeEmojis.Add(match.Value);
            }
            
            logger.LogDebug("Detected {Custom} custom, {Shortcode} shortcode, {Unicode} unicode emojis in message",
                result.CustomEmojis.Count, result.ShortcodeEmojis.Count, result.UnicodeEmojis.Count);
            
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to detect emojis in message content");
            return new EmojiDetectionResult();
        }
    }

    public bool ValidateEmojiFormat(string emojiCode, out EmojiFormat format)
    {
        format = EmojiFormat.Unknown;
        
        if (string.IsNullOrWhiteSpace(emojiCode))
            return false;

        // Check custom Discord emoji format
        if (CustomEmojiRegex().IsMatch(emojiCode))
        {
            format = EmojiFormat.Custom;
            return true;
        }
        
        // Check shortcode format
        if (ShortcodeRegex().IsMatch(emojiCode))
        {
            format = EmojiFormat.Shortcode;
            return true;
        }
        
        // Check Unicode emoji format
        if (UnicodeEmojiRegex().IsMatch(emojiCode))
        {
            format = EmojiFormat.Unicode;
            return true;
        }
        
        return false;
    }

    public bool ValidatePointValue(int pointValue)
    {
        return pointValue is >= 1 and <= 999;
    }

    public bool ValidateEmojiType(EmojiType emojiType)
    {
        return Enum.IsDefined(typeof(EmojiType), emojiType);
    }

    public string ExtractCustomEmojiId(string customEmojiCode)
    {
        if (!CustomEmojiRegex().IsMatch(customEmojiCode))
            return string.Empty;
            
        // Extract ID from <:name:id> or <a:name:id> format
        var parts = customEmojiCode.Trim('<', '>').Split(':');
        return parts.Length >= 3 ? parts[^1] : string.Empty;
    }

    public string ExtractCustomEmojiName(string customEmojiCode)
    {
        if (!CustomEmojiRegex().IsMatch(customEmojiCode))
            return string.Empty;
            
        // Extract name from <:name:id> or <a:name:id> format
        var parts = customEmojiCode.Trim('<', '>').Split(':');
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    public bool IsAnimatedCustomEmoji(string customEmojiCode)
    {
        return customEmojiCode.StartsWith("<a:");
    }

    public string NormalizeEmoji(string emojiCode)
    {
        if (string.IsNullOrWhiteSpace(emojiCode))
            return string.Empty;

        try
        {
            // If it's already a shortcode, return as-is
            if (ShortcodeRegex().IsMatch(emojiCode))
                return emojiCode;

            // If it's a custom Discord emoji, return as-is (can't normalize these)
            if (CustomEmojiRegex().IsMatch(emojiCode))
                return emojiCode;

            // If it's a Unicode emoji, convert to shortcode for normalization
            if (UnicodeEmojiRegex().IsMatch(emojiCode))
            {
                var shortcode = EmojiConverter.Shortcode(emojiCode);
                // If conversion was successful and returned a valid shortcode, use it
                if (!string.IsNullOrEmpty(shortcode) && shortcode != emojiCode)
                    return shortcode;
            }

            // Return original if no conversion was possible
            return emojiCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to normalize emoji {EmojiCode}", emojiCode);
            return emojiCode;
        }
    }

    public bool AreEmojisEquivalent(string emoji1, string emoji2)
    {
        try
        {
            // Quick equality check first
            if (emoji1 == emoji2)
                return true;

            // Normalize both emojis to their canonical form (shortcode)
            var normalized1 = NormalizeEmoji(emoji1);
            var normalized2 = NormalizeEmoji(emoji2);

            // Compare normalized forms
            if (normalized1 == normalized2)
                return true;

            // Also try the reverse - convert both to Unicode for comparison
            // This handles cases where one is shortcode and other is Unicode
            var unicode1 = TryConvertToUnicode(emoji1);
            var unicode2 = TryConvertToUnicode(emoji2);

            return unicode1 == unicode2;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to compare emoji equivalence between {Emoji1} and {Emoji2}", emoji1, emoji2);
            return false;
        }
    }

    private string TryConvertToUnicode(string emojiCode)
    {
        try
        {
            // If already Unicode, return as-is
            if (UnicodeEmojiRegex().IsMatch(emojiCode))
                return emojiCode;

            // If it's a shortcode, convert to Unicode
            if (ShortcodeRegex().IsMatch(emojiCode))
            {
                var unicode = EmojiConverter.Raw(emojiCode);
                return !string.IsNullOrEmpty(unicode) ? unicode : emojiCode;
            }

            // Return original if no conversion possible
            return emojiCode;
        }
        catch
        {
            return emojiCode;
        }
    }
}

public class EmojiDetectionResult
{
    public List<string> CustomEmojis { get; } = new();
    public List<string> ShortcodeEmojis { get; } = new();
    public List<string> UnicodeEmojis { get; } = new();
    
    public IEnumerable<string> AllEmojis => CustomEmojis.Concat(ShortcodeEmojis).Concat(UnicodeEmojis);
    public int TotalCount => CustomEmojis.Count + ShortcodeEmojis.Count + UnicodeEmojis.Count;
}

public enum EmojiFormat
{
    Unknown,
    Custom,     // <:name:id> or <a:name:id>
    Shortcode,  // :shortcode:
    Unicode     // 📚 etc.
} 