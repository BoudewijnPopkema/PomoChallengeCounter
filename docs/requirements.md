# Requirements

## Core Problem
Current manual process of copying Discord chat to Java application for pomodoro challenge tracking is slow and inefficient. Need automation through Discord API.

## Target Users
- University Discord servers running semester-based pomodoro challenges
- Students tracking study sessions (30-minute intervals)
- Server administrators managing challenges

## Core Requirements

### 1. Multi-language Support
- **Primary**: English (default) and Dutch
- **Implementation**: Discord's built-in localization features for command registration
- **User Detection**: Bot responds based on user's Discord language setting
- **Translation Management**: JSON files for easy translation editing
- **Code Language**: All code written in English, only localized string values in Dutch
- **Fallback**: English used when Dutch translation missing

### 2. Challenge Structure
- **Duration**: Semester-based (~3 months)
- **Themes**: Each semester has a unique theme
- **Timing**: Challenges start on Monday, end on Sunday
- **Validation**: Number of weeks must match date range

### 3. Weekly Workflow
- **Monday**: New thread created (format: Q[semester]-week[N], e.g., Q3-week2)
- **Tuesday 12pm**: Previous week leaderboard posted, sum of emojis for that week per student and their total throughout that challenge so far
- **Throughout week**: Students post emoji messages for pomodoros
- **End of week**: Students set goals for next week (can be done at any point during the week as long as it is in that week's thread)

### 4. Point System
- **Pomodoros**: 30-minute study intervals
- **Emojis**: Represent pomodoros (configurable values)
- **Bonus emojis**: Additional rewards (self-reported)
- **Calculation**: Sum of pomodoro emojis + bonus emojis

### 5. Permission System
- **Admin permissions**: Full bot configuration access
- **Configurable role**: Grant config permissions to specific roles
- **User restrictions**: No commands available without elevated permissions

### 6. Database Requirements
- **Technology**: PostgreSQL with Code First approach
- **Persistence**: All challenge data, user progress, configurations
- **Performance**: Handle semester-long data efficiently

## Technical Requirements

### Discord Integration
- **API**: Discord.NET for C# integration
- **Message processing**: Automatic emoji parsing via events for new messages, edits and deletions
- **Thread management**: Automated creation/management
- **Scheduling**: Automated weekly tasks
- **Challenge deactivation**: Stop processing edits when challenge becomes inactive
- **Week rescanning**: Rescan entire week before leaderboard generation
- **Import capability**: Retroactive processing of existing messages

### Configuration Management
- **Channel categories**: Designate challenge locations
- **Emoji systems**: Default + theme-specific sets
- **Reward systems**: Theme-specific reward emojis for goal achievement (pomodoro + bonus points >= goal)
- **Role management**: Ping configurations

### Data Validation
- **Date validation**: Challenge dates must be valid
- **Week calculation**: Ensure week count matches date range
- **Emoji validation**: Verify emoji existence and permissions

## Non-Functional Requirements
- **Reliability**: 99%+ uptime during challenge periods
- **Scalability**: Support multiple servers simultaneously
- **Maintainability**: Clear code structure for open-source contributions
- **Usability**: Intuitive command interface for admins 