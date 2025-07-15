using Microsoft.Extensions.Logging;
using Moq;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;
using Shouldly;

namespace PomoChallengeCounter.Tests;

public class EmojiServiceTests
{
    private readonly EmojiService _emojiService;
    private readonly Mock<ILogger<EmojiService>> _mockLogger;

    public EmojiServiceTests()
    {
        _mockLogger = new Mock<ILogger<EmojiService>>();
        _emojiService = new EmojiService(_mockLogger.Object);
    }

    [Theory]
    [InlineData("📚", 1, 0, 0)] // Single Unicode emoji
    [InlineData("📚📖", 2, 0, 0)] // Multiple Unicode emojis
    [InlineData("📚 📖", 2, 0, 0)] // Unicode emojis with spaces
    [InlineData("Check out 📚 this!", 1, 0, 0)] // Unicode emoji in text
    public void DetectEmojis_WithUnicodeEmojis_ShouldDetectCorrectly(string input, int expectedUnicode, int expectedShortcode, int expectedCustom)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.UnicodeEmojis.Count.ShouldBe(expectedUnicode);
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcode);
        result.CustomEmojis.Count.ShouldBe(expectedCustom);
        result.TotalCount.ShouldBe(expectedUnicode + expectedShortcode + expectedCustom);
    }

    [Theory]
    [InlineData(":book:", 0, 1, 0)] // Single shortcode
    [InlineData(":book::fire::muscle:", 0, 3, 0)] // Multiple shortcodes
    [InlineData(":tomato: :dart:", 0, 2, 0)] // Shortcodes with spaces
    [InlineData("Check out :book: this!", 0, 1, 0)] // Shortcode in text
    public void DetectEmojis_WithShortcodeEmojis_ShouldDetectCorrectly(string input, int expectedUnicode, int expectedShortcode, int expectedCustom)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.UnicodeEmojis.Count.ShouldBe(expectedUnicode);
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcode);
        result.CustomEmojis.Count.ShouldBe(expectedCustom);
        result.TotalCount.ShouldBe(expectedUnicode + expectedShortcode + expectedCustom);
    }

    [Theory]
    [InlineData("<:custom_book:123456789>", 0, 0, 1)] // Single custom emoji
    [InlineData("<a:spinning_star:987654321>", 0, 0, 1)] // Animated custom emoji
    [InlineData("Check <:custom:123> this!", 0, 0, 1)] // Custom emoji in text
    [InlineData("<:book:123><:fire:456>", 0, 0, 2)] // Multiple custom emojis
    public void DetectEmojis_WithCustomEmojis_ShouldDetectCorrectly(string input, int expectedUnicode, int expectedShortcode, int expectedCustom)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.UnicodeEmojis.Count.ShouldBe(expectedUnicode);
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcode);
        result.CustomEmojis.Count.ShouldBe(expectedCustom);
        result.TotalCount.ShouldBe(expectedUnicode + expectedShortcode + expectedCustom);
    }

    [Theory]
    [InlineData("I studied with 📚 and used :tomato: timer plus <:fire:123> emoji!", 1, 1, 1)]
    [InlineData("📚📚:book::fire:<:custom:123><a:spin:456>", 2, 2, 2)]
    [InlineData("📚 :book: <:custom_book:123>", 1, 1, 1)]
    [InlineData("🔥 :dart: <:muscle:789> for motivation", 1, 1, 1)]
    public void DetectEmojis_WithMixedEmojiTypes_ShouldDetectAll(string input, int expectedUnicode, int expectedShortcode, int expectedCustom)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.UnicodeEmojis.Count.ShouldBe(expectedUnicode);
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcode);
        result.CustomEmojis.Count.ShouldBe(expectedCustom);
        result.TotalCount.ShouldBe(expectedUnicode + expectedShortcode + expectedCustom);
    }

    [Theory]
    [InlineData("No emojis here just text")]
    [InlineData("")]
    [InlineData("   ")]
    public void DetectEmojis_WithNoEmojis_ShouldReturnEmpty(string input)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.TotalCount.ShouldBe(0);
        result.UnicodeEmojis.ShouldBeEmpty();
        result.ShortcodeEmojis.ShouldBeEmpty();
        result.CustomEmojis.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("📚", EmojiFormat.Unicode)]
    [InlineData(":book:", EmojiFormat.Shortcode)]
    [InlineData("<:custom:123>", EmojiFormat.Custom)]
    [InlineData("<a:animated:456>", EmojiFormat.Custom)]
    public void ValidateEmojiFormat_WithValidFormats_ShouldReturnTrueAndCorrectFormat(string emojiCode, EmojiFormat expectedFormat)
    {
        // Act
        var isValid = _emojiService.ValidateEmojiFormat(emojiCode, out var format);

        // Assert
        isValid.ShouldBeTrue();
        format.ShouldBe(expectedFormat);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not_an_emoji")]
    [InlineData(":")]
    [InlineData("::")]
    [InlineData(null)]
    public void ValidateEmojiFormat_WithInvalidFormats_ShouldReturnFalse(string emojiCode)
    {
        // Act
        var isValid = _emojiService.ValidateEmojiFormat(emojiCode, out var format);

        // Assert
        isValid.ShouldBeFalse();
        format.ShouldBe(EmojiFormat.Unknown);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(25, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(-10, false)]
    public void ValidatePointValue_WithVariousValues_ShouldValidateCorrectly(int pointValue, bool expectedValid)
    {
        // Act
        var isValid = _emojiService.ValidatePointValue(pointValue);

        // Assert
        isValid.ShouldBe(expectedValid);
    }

    [Theory]
    [InlineData(EmojiType.Pomodoro, true)]
    [InlineData(EmojiType.Bonus, true)]
    [InlineData(EmojiType.Reward, true)]
    [InlineData(EmojiType.Goal, true)]
    public void ValidateEmojiType_WithVariousTypes_ShouldValidateCorrectly(EmojiType emojiType, bool expectedValid)
    {
        // Act
        var isValid = _emojiService.ValidateEmojiType(emojiType);

        // Assert
        isValid.ShouldBe(expectedValid);
    }

    [Theory]
    [InlineData("<:custom:123456>", "123456")]
    [InlineData("<a:animated:789012>", "789012")]
    [InlineData("<:test:999>", "999")]
    public void ExtractCustomEmojiId_WithValidCustomEmoji_ShouldExtractId(string customEmojiCode, string expectedId)
    {
        // Act
        var extractedId = _emojiService.ExtractCustomEmojiId(customEmojiCode);

        // Assert
        extractedId.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData("<:custom_name:123456>", "custom_name")]
    [InlineData("<a:animated_emoji:789012>", "animated_emoji")]
    [InlineData("<:test:999>", "test")]
    public void ExtractCustomEmojiName_WithValidCustomEmoji_ShouldExtractName(string customEmojiCode, string expectedName)
    {
        // Act
        var extractedName = _emojiService.ExtractCustomEmojiName(customEmojiCode);

        // Assert
        extractedName.ShouldBe(expectedName);
    }

    [Theory]
    [InlineData("invalid", "")]
    [InlineData(":book:", "")]
    [InlineData("📚", "")]
    [InlineData("", "")]
    public void ExtractCustomEmoji_WithInvalidFormats_ShouldReturnEmpty(string emojiCode, string expected)
    {
        // Act
        var extractedId = _emojiService.ExtractCustomEmojiId(emojiCode);
        var extractedName = _emojiService.ExtractCustomEmojiName(emojiCode);

        // Assert
        extractedId.ShouldBe(expected);
        extractedName.ShouldBe(expected);
    }

    [Theory]
    [InlineData("<:custom:123>", false)]
    [InlineData("<a:animated:456>", true)]
    [InlineData("invalid", false)]
    [InlineData(":book:", false)]
    public void IsAnimatedCustomEmoji_WithVariousInputs_ShouldDetectCorrectly(string emojiCode, bool expectedAnimated)
    {
        // Act
        var isAnimated = _emojiService.IsAnimatedCustomEmoji(emojiCode);

        // Assert
        isAnimated.ShouldBe(expectedAnimated);
    }

    [Fact]
    public void DetectEmojis_WithComplexRealWorldMessage_ShouldDetectAllEmojis()
    {
        // Arrange
        const string complexMessage = "🔥 Amazing study session today! Used :tomato: timer for 4 pomodoros 📚📚📚 " +
                           "and earned some bonus points with <:fire:123456> emoji. Goals achieved: 🎯 " +
                           "Next session: :calendar: tomorrow with <a:party:789012> for motivation! " +
                           "Final score: 📊 :chart_with_upwards_trend: <:trophy:345678> 🏆";

        // Act
        var result = _emojiService.DetectEmojis(complexMessage);

        // Assert - Count by type
        result.UnicodeEmojis.Count.ShouldBe(7); // 🔥,📚,📚,📚,🎯,📊,🏆
        result.ShortcodeEmojis.Count.ShouldBe(3); // :tomato:,:calendar:,:chart_with_upwards_trend:
        result.CustomEmojis.Count.ShouldBe(3); // <:fire:123456>,<a:spinning_book:789012>,<:trophy:345678>
        result.TotalCount.ShouldBe(13);
        
        result.AllEmojis.ShouldContain("📚");
        result.AllEmojis.ShouldContain(":tomato:");
        result.AllEmojis.ShouldContain("<:fire:123456>");
        result.AllEmojis.ShouldContain("<a:party:789012>");
    }
} 