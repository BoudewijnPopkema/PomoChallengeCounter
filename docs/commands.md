# Discord Commands

## Permission Levels
- **Admin**: Discord administrator permissions
- **Config**: Configurable role with bot configuration permissions
- **Public**: No special permissions required

## Server Configuration Commands

### `/setup`
**Permission**: Admin  
**Description**: Initial bot setup for the server  
**Parameters**:
- `language` (optional): Server language (en/nl, default: en)
- `category`: Discord category for challenges
- `config_role` (optional): Role for bot configuration permissions
- `ping_role` (optional): Role to ping for new threads

**Example**:
```
/setup category:#pomodoro-challenges config_role:@moderators ping_role:@students
```

### `/config category`
**Permission**: Admin/Config  
**Description**: Set the category where challenges are created  
**Parameters**:
- `category`: Discord category channel

### `/config language`
**Permission**: Admin/Config  
**Description**: Set server language  
**Parameters**:
- `language`: Language code (en/nl)

### `/config roles`
**Permission**: Admin  
**Description**: Configure bot permission roles  
**Parameters**:
- `config_role` (optional): Role for config permissions
- `ping_role` (optional): Role to ping for threads

## Challenge Management Commands

### `/challenge create`
**Permission**: Admin/Config  
**Description**: Create a new pomodoro challenge  
**Parameters**:
- `semester`: Semester number (1-5: regular semesters 1-4, summer semester 5)
- `theme`: Challenge theme name
- `start_date`: Challenge start date (YYYY-MM-DD, must be Monday)
- `end_date`: Challenge end date (YYYY-MM-DD, must be Sunday)

**Note**: Week count is calculated automatically from the date range.

**Example**:
```
/challenge create semester:3 theme:"Space Exploration" start_date:2024-01-08 end_date:2024-03-31
```
This will automatically calculate 12 weeks from the date range.

### `/challenge start`
**Permission**: Admin/Config  
**Description**: Start the current challenge  
**Parameters**:
- `challenge_id` (optional): Specific challenge ID (defaults to current)

### `/challenge stop`
**Permission**: Admin/Config  
**Description**: Stop the current challenge  
**Parameters**:
- `challenge_id` (optional): Specific challenge ID (defaults to current)

### `/challenge info`
**Permission**: Admin/Config  
**Description**: Display challenge information  
**Parameters**:
- `challenge_id` (optional): Specific challenge ID (defaults to current)

### `/challenge list`
**Permission**: Admin/Config  
**Description**: List all challenges for the server  
**Parameters**: None

### `/challenge import`
**Permission**: Admin/Config  
**Description**: Import existing challenge from Discord channel  
**Parameters**:
- `channel`: Discord channel containing the challenge
- `semester`: Semester number (1-5: regular semesters 1-4, summer semester 5)
- `theme`: Challenge theme name

**Example**:
```
/challenge import channel:#q3-space-exploration semester:3 theme:"Space Exploration"
```

**Behavior**:
- Scans channel for threads matching pattern (Q3-week1, Q3-week2, etc.)
- Processes existing messages retroactively to count emoji points
- Creates database records for challenge, weeks, and user progress
- Only creates new threads and leaderboards going forward
- Useful for migrating from manual process to bot tracking

### `/challenge deactivate`
**Permission**: Admin/Config  
**Description**: Deactivate challenge without deleting Discord content  
**Parameters**:
- `challenge_id` (optional): Specific challenge ID (defaults to current)

**Example**:
```
/challenge deactivate challenge_id:5
```

**Behavior**:
- Marks challenge as inactive (IsActive = false)
- Stops automated thread creation and leaderboard posting
- Stops message processing for this challenge
- Does NOT delete Discord channels, threads, or messages
- Challenge data remains in database for historical purposes

## Emoji Configuration Commands

### `/emoji add`
**Permission**: Admin/Config  
**Description**: Add emoji to the system  
**Parameters**:
- `emoji`: Discord emoji (Unicode, shortcode, or custom)
- `points`: Point value (positive integer)
- `type`: Emoji type (pomodoro/bonus/reward/goal)
- `scope`: Emoji scope (global/theme)
- `challenge_id` (optional): Challenge ID for theme-specific emojis

**Example**:
```
/emoji add emoji:📚 points:1 type:pomodoro scope:global
/emoji add emoji::fire: points:2 type:bonus scope:global
/emoji add emoji:🎯 points:10 type:goal scope:global
/emoji add emoji:<:rocket:123456789> points:0 type:reward scope:theme challenge_id:1
```

### `/emoji remove`
**Permission**: Admin/Config  
**Description**: Remove emoji from the system  
**Parameters**:
- `emoji_id`: Emoji ID to remove

### `/emoji list`
**Permission**: Admin/Config  
**Description**: List all configured emojis  
**Parameters**:
- `type` (optional): Filter by type (pomodoro/bonus/reward/goal/all)
- `scope` (optional): Filter by scope (global/theme/all)
- `challenge_id` (optional): Filter by challenge

### `/emoji edit`
**Permission**: Admin/Config  
**Description**: Edit existing emoji configuration  
**Parameters**:
- `emoji_id`: Emoji ID to edit
- `points` (optional): New point value
- `active` (optional): Enable/disable emoji

## Thread Management Commands

### `/thread create`
**Permission**: Admin/Config  
**Description**: Manually create a new week thread  
**Parameters**:
- `week_number`: Week number for the thread
- `challenge_id` (optional): Challenge ID (defaults to current)

### `/thread ping`
**Permission**: Admin/Config  
**Description**: Ping the configured role in current thread  
**Parameters**: None

### `/leaderboard`
**Permission**: Admin/Config  
**Description**: Generate and post weekly leaderboard  
**Parameters**:
- `week_number` (optional): Specific week (defaults to previous week)
- `challenge_id` (optional): Challenge ID (defaults to current)



## Administrative Commands

### `/stats`
**Permission**: Admin/Config  
**Description**: View server statistics  
**Parameters**: None

### `/debug`
**Permission**: Admin  
**Description**: Debug bot status and configuration  
**Parameters**: None

### `/help`
**Permission**: Public  
**Description**: Show help information  
**Parameters**:
- `command` (optional): Specific command help

## Command Groups
Commands are organized into logical groups:
- **Config**: Server configuration and setup
- **Challenge**: Challenge management
- **Emoji**: Emoji system management
- **Thread**: Thread management
- **Admin**: Administrative tools

## Error Handling
All commands include proper error handling with user-friendly messages:
- Permission denied errors
- Invalid parameter errors
- Database connection errors
- Discord API errors

## Localization
Commands use Discord's built-in localization features:
- **Languages**: English (default) and Dutch (nl)
- **Command Registration**: Commands registered with localized names and descriptions
- **Response Localization**: All bot responses localized based on user's Discord language
- **Translation Management**: Translations stored in JSON files, easily editable
- **Code Language**: All code written in English, only localized string values in other languages
- **Fallback**: English used when Dutch translation missing 