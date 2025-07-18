# Features

## Core Features

### 1. Multi-Language Support
- **Primary Languages**: English (en), Dutch (nl)
- **Implementation**: Per-server language configuration
- **Scope**: Command responses, help text, error messages
- **Future**: Extensible architecture for additional languages

### 2. Challenge Management
- **Semester Structure**: Challenges with themes organized by semester numbers (1-5)
  - **Semesters 1-4**: Regular academic semesters
  - **Semester 5**: Summer semester
  - **Thread Format**: Q[semester]-week[N] (e.g., Q3-week1, Q5-week3)
- **Flexible Duration**: Week count calculated automatically from date range with validation
- **State Management**: Create → Start → Active → Complete lifecycle
- **Import Capability**: Retroactively import existing challenges from Discord
- **Deactivation Option**: Deactivate challenge without deleting Discord content
- **Validation**: Start/end dates must be Monday/Sunday, week count calculated automatically

### 3. Automated Thread System
- **Weekly Threads**: Auto-created every Monday (format: Q[semester]-week[N], e.g., Q3-week2)
- **Goal Threads**: Special thread for week 0 goal setting
- **Week Dating**: Week dates computed from challenge dates and week number
- **Scheduling**: Configurable timing for thread creation
- **Notifications**: Role-based pings for new threads

### 4. Message Processing
- **Real-time Parsing**: Automatic emoji detection in messages via Discord events
- **Point Calculation**: Separate tracking of pomodoro and bonus points (combined for goal achievement)
- **Message Editing**: Process edited messages for active challenges only
- **Challenge Deactivation**: Stop processing edits when challenge becomes inactive
- **Deduplication**: Prevent double-counting of processed messages
- **Minimal Storage**: Only message ID, points breakdown, and essential data
- **Retroactive Processing**: Batch process existing messages during challenge import

### 5. Leaderboard System
- **Weekly Rankings**: Posted every Tuesday at 12pm
- **Sorting**: Ranked by total points (descending)
- **Goal Achievement**: Visual rewards for users who met goals (pomodoro + bonus points >= goal)
- **Detailed Breakdown**: Show calculation behind each user's score

### 6. Emoji Configuration
- **Dual System**: Global emojis (all challenges) + theme-specific emojis
- **Four Types**: Pomodoro, bonus, reward, and goal emojis
- **Flexible Values**: Configurable point values per emoji
- **Goal System**: Goal emojis used for weekly goal setting
- **Reward System**: Special emojis for goal achievement
- **Custom Support**: Discord custom emojis and standard Unicode

### 7. Permission System
- **Role-based Access**: Admin + configurable role permissions
- **Granular Control**: Different permission levels for different features
- **Security**: No elevated commands available to regular users
- **Flexible Configuration**: Per-server permission role setup

## Advanced Features

### 8. Progress Tracking
- **Daily Aggregation**: Track progress by day within weeks
- **User Statistics**: Personal progress views and historical data
- **Goal Monitoring**: Track goal setting and achievement rates
- **Performance Metrics**: Server-wide participation statistics

### 9. Scheduling System
- **Background Tasks**: Automated weekly thread creation
- **Timezone Support**: Server-configurable timezone handling (defaults to Amsterdam)
- **Retry Logic**: Resilient scheduling with failure recovery
- **Manual Override**: Admin controls for emergency situations

### 10. Data Persistence
- **PostgreSQL Backend**: Reliable data storage with ACID compliance
- **Migration Support**: Database schema versioning
- **Backup Integration**: Export/import functionality
- **Performance Optimization**: Indexed queries for fast lookups

### 11. Error Handling
- **Graceful Degradation**: Continue operating with partial functionality
- **User-Friendly Messages**: Clear error explanations
- **Logging System**: Comprehensive audit trail
- **Recovery Mechanisms**: Automatic retry for transient failures

### 12. Configuration Management
- **Server Isolation**: Each Discord server has independent configuration
- **Hot Reloading**: Configuration changes take effect immediately
- **Validation**: Comprehensive input validation for all settings
- **Default Values**: Sensible defaults for quick setup

## User Experience Features

### 13. Goal Setting
- **Emoji-Based**: Goals set using designated goal emojis
- **Automatic Calculation**: Bot counts goal emojis from previous week
- **Progress Visualization**: Real-time progress towards goals
- **Achievement Rewards**: Random reward emoji for goal completion (when pomodoro + bonus points >= goal)
- **Historical Tracking**: View past goal performance

### 14. Interactive Commands
- **Slash Commands**: Modern Discord interaction system
- **Autocomplete**: Smart parameter suggestions
- **Context Menus**: Quick access to common actions
- **Help System**: Comprehensive command documentation

### 15. Personalization
- **User Preferences**: Individual user settings
- **Display Customization**: Personalized progress views
- **Notification Control**: Opt-in/out of various notifications
- **Language Override**: Per-user language preferences (future)

## Technical Features

### 16. Scalability
- **Multi-Server Support**: Single bot instance, multiple servers
- **Efficient Caching**: Redis integration for performance
- **Load Balancing**: Horizontal scaling capability
- **Resource Management**: Optimized memory and CPU usage

### 17. Monitoring & Analytics
- **Health Checks**: Bot status monitoring
- **Performance Metrics**: Response time tracking
- **Usage Statistics**: Feature adoption analytics
- **Error Tracking**: Comprehensive error monitoring

### 18. Security
- **Input Validation**: Prevent injection attacks
- **Permission Checks**: Verify user permissions on every command
- **Rate Limiting**: Prevent abuse and spam
- **Data Protection**: Secure handling of user data

### 19. Integration
- **Discord API**: Full Discord.NET integration
- **Webhook Support**: External system notifications
- **Export Features**: Data export for external analysis
- **Plugin Architecture**: Extensible feature system

### 20. Maintenance
- **Hot Updates**: Deploy updates without downtime
- **Database Migrations**: Automated schema updates
- **Configuration Backup**: Server setting backup/restore
- **Diagnostic Tools**: Built-in debugging and troubleshooting

### 21. Migration & Import
- **Retroactive Import**: Import existing challenges from Discord channels
- **Thread Pattern Recognition**: Automatically detect week threads (Q[semester]-week[N] format, e.g., Q3-week1)
- **Message Processing**: Retroactively count emoji points from existing messages
- **Seamless Transition**: Continue normal operations after import
- **Non-destructive Deactivation**: Deactivate challenge without deleting Discord content

### 22. Message Processing Strategy
- **Real-time Processing**: New messages processed immediately via Discord events
- **Batch Processing**: Historical messages processed in chunks during import
- **Thread Detection**: Automatic pattern matching for week threads (Q[semester]-week[N] format, e.g., Q3-week1, Q3-week2)
- **Rate Limiting**: Respects Discord API limits with delays between message batches
- **Edit Processing**: Message edits processed for any week within active challenges
- **Challenge Deactivation**: Stop processing edits when challenge becomes inactive
- **Week Rescanning**: Rescan entire week before generating leaderboards for accuracy
- **Duplicate Prevention**: Skip already processed messages using message ID tracking
- **Error Recovery**: Continue processing despite individual message failures
- **Chronological Order**: Process messages from oldest to newest for accurate tracking

## Future Enhancements
- **Mobile App**: Companion mobile application
- **Web Dashboard**: Browser-based administration
- **API Access**: External application integration
- **Advanced Analytics**: Machine learning insights
- **Team Challenges**: Cross-server competitions 