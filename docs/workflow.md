# Challenge Workflow

## Overview
This document describes the complete workflow for running a pomodoro challenge from creation to end.

## Pre-Challenge Setup

### 1. Server Configuration
1. **Bot Setup**: Admin runs `/setup` command with server details
2. **Category Selection**: Choose Discord category for challenge channels
3. **Role Configuration**: Set config role and ping role
4. **Language Setup**: Configure server language (en/nl)

### 2. Emoji Configuration
1. **Global Emojis**: Add default pomodoro and bonus emojis
2. **Theme Emojis**: Add challenge-specific emojis
3. **Reward Emojis**: Configure reward emojis for goal achievement
4. **Point Values**: Set appropriate point values for each emoji

## Challenge Creation Phase

### 3. Challenge Setup
1. **Create Challenge**: Admin runs `/challenge create` with:
   - Semester number (1-5: 1-4 regular semesters, 5 summer) displayed as Q[N] in thread names
   - Theme name
   - Start date (Monday)
   - End date (Sunday)
   - Week count
2. **Validation**: Bot validates dates and week count
3. **Channel Creation**: Bot creates challenge channel in designated category
4. **Database Setup**: Challenge and week records created

### 4. Pre-Start Preparation
1. **Explanation Post**: Admin posts challenge rules and emoji guide
2. **Theme Introduction**: Explanation of semester theme
3. **Emoji Guide**: Visual guide showing point values
4. **Schedule Overview**: When threads will be created and leaderboards posted

### 4.1 Alternative: Import Existing Challenge
Instead of creating new challenge, import existing one:
1. **Challenge Import**: Admin runs `/challenge import` on existing channel
2. **Thread Detection**: Bot scans for threads matching pattern (Q[semester]-week[N], e.g., Q3-week1)
3. **Week 0 Creation**: Bot creates week 0 record for goal collection (no specific thread pattern)
4. **Retroactive Processing**: Bot processes all existing messages for emoji points
5. **Database Population**: Creates challenge, weeks, and user progress records
6. **Forward Operation**: Bot continues with normal weekly operations from current point

## Challenge Execution Phase

### 5. Challenge Start (Monday Week 1)
1. **Start Command**: Admin runs `/challenge start`
2. **Goal Thread**: Bot creates "Q[semester]-inzet" (goal setting) thread for week 0
3. **Week 1 Thread**: Bot creates "Q[semester]-week1" thread
4. **Role Ping**: Configured role is pinged in both threads
5. **Status Update**: Challenge marked as active

### 6. Week 1 Activities
- **Goal Setting**: Students post goal emojis in goal thread (week 0)
- **Progress Tracking**: Students post emoji messages in week 1 thread
- **Real-time Processing**: Bot tracks all emoji messages immediately
- **Daily Aggregation**: Progress calculated and stored daily

### 7. Weekly Cycle (Weeks 2-N)

#### Monday (New Week Start)
1. **Thread Creation**: Bot creates "Q[semester]-week[N]" thread at 09:00 (Amsterdam time)
2. **Role Ping**: Ping configured role in new thread
3. **Week Transition**: Previous week marked as complete
4. **Goal Collection**: Bot processes goal emojis from previous week's messages

**Week Date Calculation**: Week start/end dates computed from challenge dates and week number, not stored in database

#### Tuesday (Leaderboard Day)
1. **Week Rescan**: Rescan entire previous week using `RescanWeekAsync()` to catch any message edits
2. **Point Recalculation**: Reprocess all messages with `forceReprocess=true` to update any changes
3. **Leaderboard Generation**: Bot runs at 12pm (configurable)
4. **Point Calculation**: Sum all emoji values from MessageLogs for previous week
5. **Goal Achievement**: Check if users met their goals (pomodoro points + bonus points >= goal points)
6. **Reward Assignment**: Random reward emoji for goal achievers
7. **Ranking**: Sort users by total points (descending)
8. **Leaderboard Post**: Formatted leaderboard posted in current week thread

#### Wednesday-Sunday (Progress Tracking)
1. **Message Processing**: Continuous emoji parsing and point calculation
2. **Goal Setting**: Students can set goals for next week in messages
3. **Progress Updates**: Real-time progress tracking

### 8. Message Processing Flow

#### Real-time Processing (Normal Operation)
1. **Message Detection**: Bot monitors all messages in active threads via Discord events
2. **Emoji Parsing**: Extract all emojis from message content
3. **Validation**: Check if emojis are configured for current challenge
4. **Point Calculation**: Calculate pomodoro and bonus points separately (combined for goal achievement)
5. **Database Update**: Store minimal data (message ID, points breakdown)
6. **Deduplication**: Skip already processed messages using message ID

#### Message Edit Processing
- **Scope**: Process edits for any week within active challenges
- **Update Logic**: Recalculate points and update existing database records
- **Inactive Challenges**: Stop processing edits once challenge becomes inactive
- **Deactivation Trigger**: Challenge deactivated after end date + final leaderboard posted

#### Retroactive Processing (Import Command)
1. **Thread Discovery**: Scan channel for threads matching pattern (Q[semester]-week[N], e.g., Q3-week1)
2. **Message Batching**: Retrieve messages in Discord API batches of 100
3. **Rate Limiting**: Add delays between batches to respect API limits
4. **Chronological Processing**: Process messages from oldest to newest
5. **Error Handling**: Skip individual failures, continue with remaining messages
6. **Database Population**: Create all challenge, week, and progress records

### 9. Goal Management
1. **Goal Setting**: Students post goal emojis in messages during previous week
2. **Goal Parsing**: Bot counts goal emojis and calculates total point value
3. **Goal Tracking**: Monitor progress towards goals throughout week
4. **Achievement Check**: Verify goal completion at week end (pomodoro + bonus points >= goal)
5. **Reward Distribution**: Assign random reward emoji for achievers

## End-of-Challenge Phase

### 10. Challenge End
1. **Final Week**: Last week follows normal weekly cycle
2. **Final Leaderboard**: Posted on Tuesday after last week
3. **Challenge Deactivation**: Challenge marked as inactive, stops message processing
4. **Challenge Summary**: Overall statistics and achievements
5. **Data Preservation**: Challenge data preserved in database for historical purposes

### 11. Post-Challenge Activities
1. **Results Export**: Export challenge data for analysis
2. **Challenge Deactivation**: Run `/challenge deactivate` to deactivate challenge
3. **Feedback Collection**: Gather feedback for improvements
4. **Preparation**: Begin setup for next semester's challenge

**Note**: Deactivating preserves all Discord content (channels, threads, messages) while stopping automated bot operations and message processing.

## Command Timeline

### Setup Phase
- `/setup` - Initial server configuration
- `/emoji add` - Configure emoji systems
- `/challenge create` - Create new challenge
- `/challenge import` - Import existing challenge from Discord channel

### Active Phase
- `/challenge start` - Begin the challenge
- `/thread create` - Manual thread creation (if needed)
- `/thread ping` - Ping roles in threads
- `/leaderboard` - Manual leaderboard generation

### Maintenance Phase
- `/emoji edit` - Modify emoji configurations
- `/config` - Adjust server settings
- `/challenge deactivate` - Deactivate challenge without deleting content
- `/debug` - Troubleshoot issues

## Automated Scheduling

### Weekly Schedule
- **Monday 09:00**: Create new week thread (Amsterdam time)
- **Monday 09:01**: Ping configured role (Amsterdam time)
- **Tuesday 12:00**: Generate and post leaderboard (Amsterdam time)
- **Sunday 23:59**: Close week for processing (Amsterdam time)

### Daily Tasks
- **00:00**: Aggregate previous day's progress (Amsterdam time)
- **12:00**: Update user statistics (Amsterdam time)
- **18:00**: Goal achievement notifications (Amsterdam time)

## Error Handling Scenarios

### Common Issues
1. **Missing Permissions**: Bot cannot create threads/channels
2. **Invalid Emojis**: Custom emojis become unavailable
3. **Timing Conflicts**: Manual commands during automated tasks
4. **Database Errors**: Connection issues or data corruption

### Recovery Procedures
1. **Permission Fixes**: Request admin to update bot permissions
2. **Emoji Replacement**: Disable invalid emojis, add alternatives
3. **Manual Intervention**: Use manual commands to fix timing issues
4. **Database Recovery**: Restore from backups, manual data fixes

## Best Practices

### For Administrators
- Test emoji configurations before challenge start
- Monitor bot permissions regularly
- Have backup plans for technical issues
- Communicate schedule changes to students

### For Students
- Use emojis consistently for accurate tracking
- Set realistic weekly goals
- Report technical issues to administrators
- Participate in feedback collection

## Success Metrics
- **Participation Rate**: Number of active users per week
- **Goal Achievement**: Percentage of users meeting goals (pomodoro + bonus points >= goal)
- **Engagement**: Messages per user per week
- **System Reliability**: Uptime during challenge period 