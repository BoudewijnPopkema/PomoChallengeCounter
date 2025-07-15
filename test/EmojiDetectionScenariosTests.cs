using Microsoft.Extensions.Logging;
using Moq;
using PomoChallengeCounter.Services;
using Shouldly;

namespace PomoChallengeCounter.Tests;

public class EmojiDetectionScenariosTests
{
    private readonly EmojiService _emojiService;

    public EmojiDetectionScenariosTests()
    {
        var mockLogger = new Mock<ILogger<EmojiService>>();
        _emojiService = new EmojiService(mockLogger.Object);
    }

    [Theory]
    [InlineData("I'm happy :)", 0, 0, 0)] // ASCII emoticon - not detected as emoji
    [InlineData("I'm happy 😊", 1, 0, 0)] // Unicode emoji
    [InlineData("I'm happy :smile:", 0, 1, 0)] // Shortcode emoji
    [InlineData("I'm happy <:smile:123>", 0, 0, 1)] // Custom Discord emoji
    public void DetectEmojis_AsciiVsUnicode_ShouldDistinguishCorrectly(
        string input, int expectedUnicode, int expectedShortcode, int expectedCustom)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.UnicodeEmojis.Count.ShouldBe(expectedUnicode);
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcode);
        result.CustomEmojis.Count.ShouldBe(expectedCustom);
    }

    [Theory]
    [InlineData("📚📖", 2, 0, 0)] // Consecutive Unicode emojis
    [InlineData("📚 📖", 2, 0, 0)] // Spaced Unicode emojis
    [InlineData(":book::pencil:", 0, 2, 0)] // Consecutive shortcode emojis
    [InlineData(":book: :pencil:", 0, 2, 0)] // Spaced shortcode emojis
    [InlineData("<:book:123><:pencil:456>", 0, 0, 2)] // Consecutive custom emojis
    [InlineData("<:book:123> <:pencil:456>", 0, 0, 2)] // Spaced custom emojis
    public void DetectEmojis_MultipleEmojisSameType_ShouldDetectAll(
        string input, int expectedUnicode, int expectedShortcode, int expectedCustom)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.UnicodeEmojis.Count.ShouldBe(expectedUnicode);
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcode);
        result.CustomEmojis.Count.ShouldBe(expectedCustom);
    }

    [Theory]
    [InlineData("Study session complete! 📚:tomato:<:fire:123>", 1, 1, 1)]
    [InlineData("📚 great session with :timer: and some <:motivation:789>!", 1, 1, 1)]
    [InlineData("Mixed formats: 🔥 :book: <:star:456> in random order", 1, 1, 1)]
    [InlineData("📚📖:book::pencil:<:fire:123><:star:456>", 2, 2, 2)]
    public void DetectEmojis_MixedFormats_ShouldDetectAllTypes(
        string input, int expectedUnicode, int expectedShortcode, int expectedCustom)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.UnicodeEmojis.Count.ShouldBe(expectedUnicode);
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcode);
        result.CustomEmojis.Count.ShouldBe(expectedCustom);
    }

    [Theory]
    [InlineData("📚Study session", 1, 0, 0)] // Emoji at start
    [InlineData("Study 📚 session", 1, 0, 0)] // Emoji in middle
    [InlineData("Study session📚", 1, 0, 0)] // Emoji at end
    [InlineData(":book:Study session", 0, 1, 0)] // Shortcode at start
    [InlineData("Study:book:session", 0, 1, 0)] // Shortcode in middle
    [InlineData("Study session:book:", 0, 1, 0)] // Shortcode at end
    [InlineData("<:fire:123>Study session", 0, 0, 1)] // Custom at start
    [InlineData("Study<:fire:123>session", 0, 0, 1)] // Custom in middle
    [InlineData("Study session<:fire:123>", 0, 0, 1)] // Custom at end
    public void DetectEmojis_EmojiPositions_ShouldDetectRegardlessOfPosition(
        string input, int expectedUnicode, int expectedShortcode, int expectedCustom)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.UnicodeEmojis.Count.ShouldBe(expectedUnicode);
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcode);
        result.CustomEmojis.Count.ShouldBe(expectedCustom);
    }

    [Theory]
    [InlineData(":BOOK:", 0)] // All caps - should not match
    [InlineData(":Book:", 0)] // Mixed case - should not match
    [InlineData(":book:", 1)] // Lowercase - should match
    [InlineData(":book_club:", 1)] // With underscore - should match
    [InlineData(":book-mark:", 1)] // With hyphen - should match
    [InlineData(":book123:", 1)] // With numbers - should match
    [InlineData(":123book:", 1)] // Starting with numbers - should match
    public void DetectEmojis_ShortcodeCaseSensitivity_ShouldOnlyMatchValidFormat(
        string input, int expectedCount)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.ShortcodeEmojis.Count.ShouldBe(expectedCount);
    }

    [Theory]
    [InlineData("::", 0)] // Empty shortcode
    [InlineData(":", 0)] // Single colon
    [InlineData(":::", 0)] // Triple colon
    [InlineData(":book", 0)] // Missing closing colon
    [InlineData("book:", 0)] // Missing opening colon
    [InlineData(":book::", 1)] // Valid + extra colon (should find :book:)
    [InlineData("::book:", 1)] // Extra colon + valid (should find :book:)
    public void DetectEmojis_MalformedShortcodes_ShouldHandleGracefully(
        string input, int expectedCount)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.ShortcodeEmojis.Count.ShouldBe(expectedCount);
    }

    [Theory]
    [InlineData("<:>", 0)] // Empty custom emoji
    [InlineData("<:", 0)] // Incomplete custom emoji
    [InlineData(":123>", 0)] // Missing opening bracket
    [InlineData("<:name:", 0)] // Missing ID and closing bracket
    [InlineData("<:name:>", 0)] // Missing ID
    [InlineData("<::123>", 0)] // Missing name
    [InlineData("<:name:abc>", 0)] // Non-numeric ID
    [InlineData("<:valid:123>", 1)] // Valid custom emoji
    [InlineData("<a:animated:456>", 1)] // Valid animated emoji
    public void DetectEmojis_MalformedCustomEmojis_ShouldOnlyMatchValid(
        string input, int expectedCount)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.CustomEmojis.Count.ShouldBe(expectedCount);
    }

    [Fact]
    public void DetectEmojis_RealWorldComplexMessage_ShouldDetectAllEmojis()
    {
        // Arrange
        var complexMessage = "🔥 Amazing study session today! Used :tomato: timer for 4 pomodoros 📚📖 " +
                           "and earned some bonus points with <:fire:123456> emoji. Goals achieved: 🎯 " +
                           "Next session: :calendar: tomorrow with <a:spinning_book:789012> for motivation! " +
                           "Final score: 📊 :chart_with_upwards_trend: <:trophy:345678> 🏆";

        // Act
        var result = _emojiService.DetectEmojis(complexMessage);

        // Assert - Count by type
        result.UnicodeEmojis.Count.ShouldBe(6); // 🔥📚📖🎯📊🏆
        result.ShortcodeEmojis.Count.ShouldBe(3); // :tomato: :calendar: :chart_with_upwards_trend:
        result.CustomEmojis.Count.ShouldBe(3); // <:fire:123456> <a:spinning_book:789012> <:trophy:345678>
        
        // Assert - Total count
        result.TotalCount.ShouldBe(12);
        
        // Assert - Specific emojis detected
        result.UnicodeEmojis.ShouldContain("🔥");
        result.UnicodeEmojis.ShouldContain("📚");
        result.UnicodeEmojis.ShouldContain("📖");
        result.UnicodeEmojis.ShouldContain("🎯");
        result.UnicodeEmojis.ShouldContain("📊");
        result.UnicodeEmojis.ShouldContain("🏆");
        
        result.ShortcodeEmojis.ShouldContain(":tomato:");
        result.ShortcodeEmojis.ShouldContain(":calendar:");
        result.ShortcodeEmojis.ShouldContain(":chart_with_upwards_trend:");
        
        result.CustomEmojis.ShouldContain("<:fire:123456>");
        result.CustomEmojis.ShouldContain("<a:spinning_book:789012>");
        result.CustomEmojis.ShouldContain("<:trophy:345678>");
    }

    [Theory]
    [InlineData("📚", ":book:", "<:book:123>", 1, 1, 1)] // Same concept, different formats
    [InlineData("🍅", ":tomato:", "<:tomato:456>", 1, 1, 1)] // Pomodoro timer representations
    [InlineData("🔥", ":fire:", "<:fire:789>", 1, 1, 1)] // Motivation/energy representations
    [InlineData("🎯", ":target:", "<:target:012>", 1, 1, 1)] // Goal representations
    public void DetectEmojis_SameConceptDifferentFormats_ShouldDetectAllFormats(
        string unicode, string shortcode, string custom, 
        int expectedUnicode, int expectedShortcode, int expectedCustom)
    {
        // Arrange
        var message = $"Study progress: {unicode} {shortcode} {custom}";

        // Act
        var result = _emojiService.DetectEmojis(message);

        // Assert
        result.UnicodeEmojis.Count.ShouldBe(expectedUnicode);
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcode);
        result.CustomEmojis.Count.ShouldBe(expectedCustom);
    }

    [Theory]
    [InlineData("No emojis here just text", 0)]
    [InlineData("", 0)] // Empty string
    [InlineData("   ", 0)] // Whitespace only
    [InlineData("Regular punctuation: ! @ # $ % ^ & * ( ) - _ + = { } [ ] | \\ : ; \" ' < > ? / . ,", 0)]
    [InlineData("Numbers and letters: 123 ABC abc", 0)]
    public void DetectEmojis_NoEmojisInText_ShouldReturnEmptyResult(
        string input, int expectedTotal)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.TotalCount.ShouldBe(expectedTotal);
        result.UnicodeEmojis.ShouldBeEmpty();
        result.ShortcodeEmojis.ShouldBeEmpty();
        result.CustomEmojis.ShouldBeEmpty();
    }

    [Fact]
    public void DetectEmojis_PerformanceWithLargeText_ShouldExecuteQuickly()
    {
        // Arrange
        var largeText = string.Join(" ", Enumerable.Repeat("📚 Study session with :tomato: timer and <:fire:123> motivation!", 1000));
        
        // Act & Assert - Should complete within reasonable time
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _emojiService.DetectEmojis(largeText);
        stopwatch.Stop();
        
        // Assert performance (should be under 100ms for 1000 repetitions)
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(100);
        
        // Assert correctness
        result.UnicodeEmojis.Count.ShouldBe(1000);
        result.ShortcodeEmojis.Count.ShouldBe(1000);
        result.CustomEmojis.Count.ShouldBe(1000);
    }

    [Theory]
    [InlineData("📚📚📚", 3)] // Same Unicode emoji repeated
    [InlineData(":book::book::book:", 3)] // Same shortcode repeated
    [InlineData("<:fire:123><:fire:123><:fire:123>", 3)] // Same custom emoji repeated
    [InlineData("📚:book:<:book:123>", 1, 1, 1)] // Same concept, different formats
    public void DetectEmojis_RepeatedEmojis_ShouldCountEachOccurrence(string input, params int[] expectedCounts)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        switch (expectedCounts.Length)
        {
            // Assert
            case 1:
                result.TotalCount.ShouldBe(expectedCounts[0]);
                break;
                         case 3:
                 result.UnicodeEmojis.Count.ShouldBe(expectedCounts[0]);
                 result.ShortcodeEmojis.Count.ShouldBe(expectedCounts[1]);
                 result.CustomEmojis.Count.ShouldBe(expectedCounts[2]);
                 break;
        }
    }

    [Theory]
    [InlineData("Email me at user:password@example.com", 0)] // Colon in email
    [InlineData("Time format 12:30:45", 0)] // Colons in time
    [InlineData("Ratio 3:2:1", 0)] // Colons in ratios
    [InlineData("Real emoji :book: here", 1)] // Actual shortcode
    public void DetectEmojis_FalsePositives_ShouldNotDetectNonEmojiColons(
        string input, int expectedShortcodes)
    {
        // Act
        var result = _emojiService.DetectEmojis(input);

        // Assert
        result.ShortcodeEmojis.Count.ShouldBe(expectedShortcodes);
    }
} 