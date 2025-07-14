# Database Schema

## Overview
PostgreSQL database with Entity Framework Core Code First approach for storing challenge data, user progress, and bot configurations.

## Core Entities

### Servers
Stores Discord server configurations
```sql
CREATE TABLE Servers (
    Id BIGINT PRIMARY KEY,                    -- Discord Guild ID
    Name VARCHAR(255) NOT NULL,               -- Server name
    Language VARCHAR(10) DEFAULT 'en',        -- Server language (en/nl)
    Timezone VARCHAR(50) DEFAULT 'Europe/Amsterdam', -- Server timezone for scheduling
    CategoryId BIGINT,                        -- Challenge category ID
    ConfigRoleId BIGINT,                      -- Role with config permissions
    PingRoleId BIGINT                         -- Role to ping for new threads
);
```

### Challenges
Semester-based pomodoro challenges
```sql
CREATE TABLE Challenges (
    Id SERIAL PRIMARY KEY,
    ServerId BIGINT REFERENCES Servers(Id),
    QuarterNumber INT NOT NULL,               -- Q1, Q2, Q3, Q4
    Theme VARCHAR(255) NOT NULL,              -- Challenge theme
    StartDate DATE NOT NULL,                  -- Monday start
    EndDate DATE NOT NULL,                    -- Sunday end
    WeekCount INT NOT NULL,                   -- Number of weeks
    ChannelId BIGINT,                         -- Discord channel ID
    IsCurrent BOOLEAN DEFAULT false,         -- Whether this is the current active challenge
    IsStarted BOOLEAN DEFAULT false,
    IsActive BOOLEAN DEFAULT true            -- Whether challenge is active (processing messages)
);
```


### Weeks
Individual weeks within challenges
```sql
CREATE TABLE Weeks (
    Id SERIAL PRIMARY KEY,
    ChallengeId INT REFERENCES Challenges(Id),
    WeekNumber INT NOT NULL,                  -- Week 0 (goals), 1, 2, 3...
    ThreadId BIGINT,                          -- Discord thread ID
    GoalThreadId BIGINT,                      -- Goal thread ID (week 0 only)
    LeaderboardPosted BOOLEAN DEFAULT false
);
```

**Week Number Logic:**
- **Week 0**: Goal collection period, no leaderboard generated
- **Week 1**: Starts on challenge start date (Monday), first week with leaderboard
- **Week N**: Start date = Challenge start date + (N-1) * 7 days
- **Last Week**: End date matches challenge end date (Sunday)

### Emojis
Configurable emoji system
```sql
-- Create enum for emoji types
CREATE TYPE EmojiTypeEnum AS ENUM ('pomodoro', 'bonus', 'reward', 'goal');

CREATE TABLE Emojis (
    Id SERIAL PRIMARY KEY,
    ServerId BIGINT REFERENCES Servers(Id),
    ChallengeId INT REFERENCES Challenges(Id), -- NULL for global emojis
    EmojiCode VARCHAR(255) NOT NULL,           -- :thumbsup: or <:custom:123> or Unicode
    PointValue INT NOT NULL,                   -- Points per emoji
    EmojiType EmojiTypeEnum NOT NULL,          -- Enum: pomodoro, bonus, reward, goal
    IsActive BOOLEAN DEFAULT true
);
```

### UserProgress
Daily user progress tracking
```sql
CREATE TABLE UserProgress (
    Id SERIAL PRIMARY KEY,
    UserId BIGINT NOT NULL,                   -- Discord User ID
    WeekId INT REFERENCES Weeks(Id),
    Date DATE NOT NULL,                       -- Progress date
    PomodoroPoints INT DEFAULT 0,             -- Daily pomodoro points
    BonusPoints INT DEFAULT 0,                -- Daily bonus points
    GoalPoints INT DEFAULT 0,                 -- Daily goal points
    MessageCount INT DEFAULT 0,               -- Number of messages
    
    UNIQUE(UserId, WeekId, Date)
);
```

### UserGoals
Weekly goal tracking
```sql
CREATE TABLE UserGoals (
    Id SERIAL PRIMARY KEY,
    UserId BIGINT NOT NULL,                   -- Discord User ID
    WeekId INT REFERENCES Weeks(Id),
    GoalPoints INT NOT NULL,                  -- Target points for week (computed from goal emojis)
    ActualPomodoroPoints INT DEFAULT 0,       -- Actual pomodoro points achieved
    ActualBonusPoints INT DEFAULT 0,          -- Actual bonus points achieved
    IsAchieved BOOLEAN DEFAULT false,         -- Goal achieved flag (pomodoro + bonus points >= goal)
    RewardEmoji VARCHAR(255),                 -- Assigned reward emoji
    
    UNIQUE(UserId, WeekId)
);
```

### MessageLogs
Minimal message processing tracking
```sql
CREATE TABLE MessageLogs (
    Id SERIAL PRIMARY KEY,
    MessageId BIGINT UNIQUE NOT NULL,         -- Discord Message ID
    UserId BIGINT NOT NULL,                   -- Discord User ID
    WeekId INT REFERENCES Weeks(Id),
    PomodoroPoints INT DEFAULT 0,             -- Points from pomodoro emojis
    BonusPoints INT DEFAULT 0,                -- Points from bonus emojis
    GoalPoints INT DEFAULT 0,                 -- Points from goal emojis
    IsGoalMessage BOOLEAN DEFAULT false       -- Goal setting message
);
```

## Indexes
```sql
-- Performance indexes
CREATE INDEX idx_servers_guild ON Servers(Id);
CREATE INDEX idx_challenges_server ON Challenges(ServerId);
CREATE INDEX idx_challenges_current ON Challenges(IsCurrent);
CREATE INDEX idx_challenges_active ON Challenges(IsActive);
CREATE INDEX idx_weeks_challenge ON Weeks(ChallengeId);
CREATE INDEX idx_emojis_server ON Emojis(ServerId);
CREATE INDEX idx_emojis_challenge ON Emojis(ChallengeId);
CREATE INDEX idx_progress_user_week ON UserProgress(UserId, WeekId);
CREATE INDEX idx_progress_date ON UserProgress(Date);
CREATE INDEX idx_goals_user_week ON UserGoals(UserId, WeekId);
CREATE INDEX idx_messages_processed ON MessageLogs(MessageId);
CREATE INDEX idx_messages_week ON MessageLogs(WeekId);
```

## Relationships
- **Server** 1:N **Challenges** (one server, many challenges)
- **Challenge** 1:N **Weeks** (one challenge, many weeks)
- **Week** 1:N **UserProgress** (one week, many user entries)
- **Week** 1:N **UserGoals** (one week, many user goals)
- **Server** 1:N **Emojis** (global emojis per server)
- **Challenge** 1:N **Emojis** (theme-specific emojis)

## Computed Fields
- **Week Start Date**: Challenge start date + (week number - 1) * 7 days
- **Week End Date**: Week start date + 6 days
- **Week 0**: Special case for goal collection, no specific date range

## Data Constraints
- Challenges must start on Monday, end on Sunday
- Week count must match date range
- Emoji codes must be valid Discord format (Unicode, shortcode, or custom)
- Point values must be positive integers
- Emoji types must be valid enum values
- User progress dates must fall within computed week ranges
- Inactive challenges have IsActive = false 