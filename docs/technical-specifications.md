# Technical Specifications

## Message Processing Patterns

### Discord Message Events
Use Discord.NET event system to detect message changes:

**Message Types:**
- `MessageReceived` - New message posted
- `MessageUpdated` - Existing message edited
- `MessageDeleted` - Message removed, stop counting its emojis

**Edit Detection:**
- Discord API provides event type in message events
- Use `MessageUpdated` event to detect edits
- Use `MessageDeleted` event to remove message from tracking
- Message ID remains constant for edits

### Discord Emoji Detection
Discord supports multiple emoji formats that need to be detected:

**Standard Unicode Emojis:**
- Format: Unicode characters (📚, ✏️, 🎯, 🍅, etc.)
- Detection: Use dedicated Unicode emoji library
- *Note: Values shown are examples only and will be configured per server/challenge*

**Emoji Shortcodes:**
- Format: `:shortcode:` (e.g., `:tomato:`, `:book:`, `:fire:`)
- Detection: Parse shortcode format, convert to Unicode when needed
- Note: Discord automatically converts shortcodes to Unicode in messages
- *Note: Values shown are examples only and will be configured per server/challenge*

**Custom Discord Emojis:**
- Format: `<:name:id>` (static) or `<a:name:id>` (animated)
- Regex Pattern: `<a?:[^:]+:\d+>`
- Examples: `<:custom_book:123456789>`, `<a:spinning_star:987654321>`
- *Note: Values shown are examples only and will be configured per server/challenge*

### Message Tracking Strategy
- **Individual Message Storage**: Each message stored separately with points breakdown
- **Deduplication**: MessageId prevents processing same message twice during normal operation
- **Edit Handling**: Update existing MessageLog record when message edited
- **Delete Handling**: Remove MessageLog record when message deleted
- **Reprocessing Support**: `forceReprocess` parameter allows reprocessing existing messages during rescans
- **Week Rescanning**: `RescanWeekAsync()` method reprocesses entire week before leaderboard generation
- **Leaderboard Generation**: Query and sum MessageLog records for user/week

### Goal Setting System
Goals are set using designated "goal emojis" rather than text messages:

**Goal Emoji Classification:**
- Emojis with `EmojiType = goal` in the database
- Goal emojis have specific point values
- Goal emojis are configured per challenge/server
- Can be Unicode, shortcode, or custom format

**Goal Calculation:**
- Count goal emojis from user's messages throughout any week
- Sum the GoalPoints from MessageLogs for user/week to get weekly goal
- No special message tracking needed - just query and sum when needed

**Example Goal Emojis:**
- 🎯 (target) or `:dart:` = 10 points
- 📈 (trending up) or `:chart_with_upwards_trend:` = 5 points  
- 🔥 (fire) or `:fire:` = 20 points
- 💪 (flexed bicep) or `:muscle:` = 15 points
- *Note: Values and emojis shown are examples only and will be configured per server/challenge*

## Scheduling Configuration

### Thread Creation Timing
- **Day**: Monday
- **Time**: 09:00 (configurable timezone, defaults to Amsterdam)
- **Implementation**: Use scheduled background service (IHostedService)

### Leaderboard Timing
- **Day**: Tuesday
- **Time**: 12:00 (configurable timezone, defaults to Amsterdam)
- **Pre-processing**: Rescan entire previous week before generation

## Emoji System Configuration

### Emoji Types
Four types of emojis supported:
- **Pomodoro**: Counts toward weekly progress
- **Bonus**: Bonus points for achievements  
- **Reward**: Displayed on leaderboard for goal achievers
- **Goal**: Used for setting weekly goals

Database uses PostgreSQL enum: `EmojiTypeEnum` (defined in database schema)

### Emoji Validation Rules
- **Point Values**: Must be positive integers (1-999)
- **Custom Emoji Format**: Must match `<a?:[^:]+:\d+>` pattern
- **Unicode Emoji**: Must be valid Unicode emoji character
- **Shortcode Format**: Must be valid Discord shortcode (`:name:`)
- **Emoji Existence**: Custom emojis must exist in Discord server
- **Duplicate Prevention**: Same emoji cannot have multiple active entries per challenge

## Point Calculation System

### Message Processing Logic
- **Pomodoro Points**: Sum of pomodoro emoji point values
- **Bonus Points**: Sum of bonus emoji point values  
- **Goal Points**: Sum of goal emoji point values (for goal setting)
- **Achievement Check**: Pomodoro points + bonus points >= goal points

### Weekly Goal Calculation
- Count goal emojis from user's messages in previous week
- Sum point values of all goal emojis
- Store result as user's goal for current week

## Reward System

### Goal Achievement Criteria
- **Achievement**: User gets reward if (pomodoro points + bonus points) >= goal points
- **Calculation**: Sum of user's pomodoro and bonus points for the week
- **Comparison**: Total points must meet or exceed the goal set for that week

### Random Reward Selection
- **Algorithm**: Simple random selection from available reward emojis
- **Scope**: Only reward emojis for current challenge
- **Fallback**: No reward emoji if none configured for challenge

## Week Date Calculations

### Week Start/End Computation
- **Week 1**: Challenge start date
- **Week N**: Challenge start date + (N-1) * 7 days
- **Week 0**: Challenge start date - 7 days (goal collection period)
- **Week End**: Start date + 6 days (always Sunday)

### Week 0 Special Handling
- **Purpose**: Goal collection period
- **Date Range**: 7 days before challenge start
- **Thread**: Created with challenge start, not subject to normal weekly cycle
- **Leaderboard**: No leaderboard generated for week 0

## Data Validation Rules

### Challenge Validation
- **Start Date**: Must be Monday (`startDate.DayOfWeek == DayOfWeek.Monday`)
- **End Date**: Must be Sunday (`endDate.DayOfWeek == DayOfWeek.Sunday`)
- **Week Count**: Must equal `(endDate - startDate).Days / 7 + 1`
- **Semester**: Must be 1-5 (1-4: regular semesters, 5: summer semester)
- **Theme**: 1-255 characters, no special characters

### Emoji Validation
- **Point Values**: Range 1-999
- **Custom Format**: Must match Discord custom emoji pattern
- **Unicode**: Must be valid Unicode emoji
- **Shortcode Format**: Must be valid Discord shortcode
- **Emoji Type**: Must be valid enum value (pomodoro, bonus, reward, goal)
- **Uniqueness**: One active entry per emoji per challenge

### Message Validation
- **Thread Context**: Must be in valid challenge thread
- **User Permissions**: Must have send message permissions
- **Content Length**: Discord's 2000 character limit
- **Emoji Count**: Maximum 50 emojis per message (reasonable limit)

## Error Codes

### Command Errors
- `INVALID_DATE_FORMAT`: Date not in YYYY-MM-DD format
- `INVALID_DATE_RANGE`: Start/end dates don't match week count
- `INVALID_EMOJI_FORMAT`: Emoji doesn't match expected pattern
- `INVALID_EMOJI_TYPE`: Emoji type not in enum (pomodoro, bonus, reward, goal)
- `EMOJI_NOT_FOUND`: Custom emoji doesn't exist in server
- `INSUFFICIENT_PERMISSIONS`: User lacks required permissions
- `CHALLENGE_NOT_FOUND`: Referenced challenge doesn't exist
- `WEEK_NOT_FOUND`: Referenced week doesn't exist

### Processing Errors
- `MESSAGE_PROCESSING_FAILED`: Error parsing message emojis
- `GOAL_CALCULATION_FAILED`: Error calculating user goal
- `LEADERBOARD_GENERATION_FAILED`: Error generating leaderboard
- `THREAD_CREATION_FAILED`: Error creating Discord thread

## Rate Limiting Strategy

### Discord API Rate Limits
- **Follow Discord API Documentation**: Use official rate limit guidelines
- **Global Rate Limit**: 50 requests per second across all endpoints
- **Per-Route Limits**: Vary by endpoint (documented in Discord API docs)
- **Rate Limit Headers**: Use `X-RateLimit-*` headers for dynamic limits

### Implementation Strategy
- **Exponential Backoff**: Start with small delay, double on each retry
- **Respect Headers**: Use Discord's rate limit headers for timing
- **Queue Requests**: Implement request queuing for high-volume operations
- **Retry Logic**: Maximum 3 retries for rate-limited requests

## Thread Management

### Thread Creation Validation
- **Duplicate Prevention**: Check if thread already exists before creating
- **Name Uniqueness**: Validate thread name doesn't conflict with existing
- **Database Consistency**: Ensure ThreadId is unique in database
- **Error Handling**: Handle conflicts gracefully with appropriate error messages

### Thread Pattern Detection
- **Pattern**: `^Q\d+-week\d+$` (case insensitive)
- **Examples**: `Q3-week1`, `Q2-week12`, `Q5-week3` (where Q[N] represents semester 1-5)
- **Goal Thread**: `^Q\d+-inzet$` or similar pattern (Dutch for "goal")

## Import Command Processing

### Message Processing Order
1. **Chronological**: Process messages from oldest to newest
2. **Batch Size**: 100 messages per batch (Discord API limit)
3. **Rate Limiting**: Follow Discord API documentation for delays
4. **Error Handling**: Skip individual message failures, continue processing

## Timezone Handling

### Server Timezone
- **Default**: Europe/Amsterdam (Amsterdam timezone)
- **Configurable**: Can be overridden per server via configuration
- **Storage**: All timestamps stored in UTC
- **Display**: Convert to server timezone for user-facing times

### Scheduling Adjustments
- **Monday 09:00**: Convert from configured timezone (default Amsterdam) to UTC for scheduling
- **Tuesday 12:00**: Convert from configured timezone (default Amsterdam) to UTC for scheduling
- **DST Handling**: Use proper timezone libraries for DST transitions

## Testing Patterns

### Time Provider Abstraction
Use `ITimeProvider` interface for all time-dependent operations:

```csharp
public interface ITimeProvider
{
    DateTime Now { get; }
    DateTime UtcNow { get; }
    DateOnly Today { get; }
    DateOnly UtcToday { get; }
}

public class SystemTimeProvider : ITimeProvider
{
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    public DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);
}
```

### Testing Strategy
- **Unit Tests Required**: All business logic must have comprehensive unit test coverage
- **Critical Test Areas**:
  - Discord command handlers
  - Message processing and event handling
  - Background scheduling services
  - Leaderboard generation algorithms
  - Challenge management operations
  - Goal tracking and achievement systems
  - Week detection and thread pattern matching
- **Mock Dependencies**: Use mocks for Discord API, database contexts, external services
- **Time Manipulation**: Use MockTimeProvider for testing scheduled operations
- **Integration Tests**: Use in-memory database for data persistence testing 