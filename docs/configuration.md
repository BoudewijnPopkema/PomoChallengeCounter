# Configuration

## Environment Setup

### Prerequisites
- Docker and Docker Compose
- Discord Bot Token
- Discord Application with OAuth2 permissions

### Required Permissions
Bot requires the following Discord permissions:
- `Send Messages`
- `Use Slash Commands`
- `Read Message History`
- `Manage Threads`
- `Create Public Threads`
- `Create Private Threads`
- `Add Reactions`
- `Use External Emojis`
- `Mention Everyone`

## Database Configuration

### PostgreSQL in Docker
Database runs in Docker container automatically:
- **Database Name**: pomodoro_bot
- **User**: pomodoro_user
- **Password**: Set via `DATABASE_PASSWORD` environment variable
- **Host**: postgres (internal Docker network)
- **Port**: 5432

### Entity Framework Configuration
- **Code First Approach**: Database schema created from models
- **Migrations**: Automatic database updates on startup
- **Connection String**: Built from environment variables
- **Indexing**: Optimized queries for performance

## Application Configuration

### Configuration Philosophy
- **Static Values**: Hardcoded in the application (no appsettings.json)
- **Environment Variables**: Only for secrets and external dependencies
- **No Runtime Configuration**: Values set at compile time or startup

### Static Configuration (Hardcoded)
All application behavior is hardcoded:
- **Command Prefix**: `/` (slash commands)
- **Default Language**: `en` (English)
- **Message Cache Size**: 1000
- **Thread Creation Time**: `09:00` (Monday morning)
- **Leaderboard Time**: `12:00` (Tuesday noon)
- **Daily Aggregation Time**: `00:00` (midnight)
- **Timezone**: Europe/Amsterdam (configurable per server, all times in configured timezone)
- **Logging Level**: Information (console + file)

### Environment-Only Configuration
Only external dependencies and secrets use environment variables.

## Discord Application Setup

### 1. Create Discord Application
1. Go to [Discord Developer Portal](https://discord.com/developers/applications)
2. Click "New Application"
3. Enter application name: "PomoChallengeCounter"
4. Save application

### 2. Bot Configuration
1. Navigate to "Bot" section
2. Click "Add Bot"
3. Copy bot token for configuration
4. Enable required intents:
   - Message Content Intent
   - Server Members Intent
   - Presence Intent

### 3. OAuth2 Setup
1. Navigate to "OAuth2" section
2. Select scopes: `bot`, `applications.commands`
3. Select permissions (see Required Permissions above)
4. Generate invite URL

### 4. Slash Commands
Commands are registered automatically on bot startup for each server.

## Server-Specific Configuration

### Initial Setup
Each Discord server requires initial configuration:

1. **Invite Bot**: Use OAuth2 URL with proper permissions
2. **Run Setup**: Execute `/setup` command
3. **Configure Category**: Select category for challenge channels
4. **Set Roles**: Configure config and ping roles
5. **Set Language**: Choose server language (en/nl)

### Localization Setup
Commands use Discord's built-in localization system:
- **Command Registration**: Commands registered with both English and Dutch names/descriptions
- **User Language Detection**: Bot responds based on user's Discord language setting
- **Translation Files**: Located in `src/Localization/` folder
- **File Structure**: `en.json` (English), `nl.json` (Dutch)
- **Fallback**: English used when Dutch translation missing

#### Translation File Example
```json
// src/Localization/en.json
{
  "commands": {
    "setup": {
      "name": "setup",
      "description": "Initial bot setup for the server"
    },
    "challenge": {
      "create": {
        "name": "create",
        "description": "Create a new pomodoro challenge"
      }
    }
  },
  "responses": {
    "setup_success": "Bot setup completed successfully!",
    "challenge_created": "Challenge '{0}' created successfully!"
  },
  "errors": {
    "no_permission": "You don't have permission to use this command",
    "invalid_date": "Invalid date format. Use YYYY-MM-DD"
  }
}
```

```json
// src/Localization/nl.json
{
  "commands": {
    "setup": {
      "name": "setup",
      "description": "Initiële bot setup voor de server"
    },
    "challenge": {
      "create": {
        "name": "creeer",
        "description": "Maak een nieuwe pomodoro uitdaging"
      }
    }
  },
  "responses": {
    "setup_success": "Bot setup succesvol voltooid!",
    "challenge_created": "Uitdaging '{0}' succesvol aangemaakt!"
  },
  "errors": {
    "no_permission": "Je hebt geen toestemming om deze command te gebruiken",
    "invalid_date": "Ongeldig datum formaat. Gebruik YYYY-MM-DD"
  }
}
```

### Default Emojis
No default emojis are pre-configured. All emojis must be configured through Discord commands after bot deployment.

### Role Configuration
- **Admin Role**: Discord Administrator permission
- **Config Role**: Configurable role for bot management
- **Ping Role**: Role pinged for new threads (typically @students)

## Docker Deployment (Recommended)

### Docker Compose Setup
Complete application setup with single command:

```yaml
# docker-compose.yml
version: '3.8'
services:
  bot:
    build: .
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - DISCORD_TOKEN=${DISCORD_TOKEN}
      - DISCORD_CLIENT_ID=${DISCORD_CLIENT_ID}
      - DISCORD_CLIENT_SECRET=${DISCORD_CLIENT_SECRET}
      - DATABASE_HOST=postgres
      - DATABASE_NAME=pomodoro_bot
      - DATABASE_USER=pomodoro_user
      - DATABASE_PASSWORD=${DATABASE_PASSWORD}
    depends_on:
      - postgres
    restart: unless-stopped
    volumes:
      - ./logs:/app/logs

  postgres:
    image: postgres:15
    environment:
      - POSTGRES_DB=pomodoro_bot
      - POSTGRES_USER=pomodoro_user
      - POSTGRES_PASSWORD=${DATABASE_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    restart: unless-stopped
    ports:
      - "5432:5432"  # Optional: for database access

volumes:
  postgres_data:
```

### Dockerfile
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["PomoChallengeCounter.csproj", "."]
RUN dotnet restore "./PomoChallengeCounter.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "PomoChallengeCounter.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PomoChallengeCounter.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PomoChallengeCounter.dll"]
```

### Environment Variables
Create `.env` file for secrets and external dependencies:
```bash
# .env - Discord Bot Secrets
DISCORD_TOKEN=your_bot_token_here
DISCORD_CLIENT_ID=your_client_id_here
DISCORD_CLIENT_SECRET=your_client_secret_here

# Database Configuration
DATABASE_HOST=postgres
DATABASE_NAME=pomodoro_bot
DATABASE_USER=pomodoro_user
DATABASE_PASSWORD=your_secure_database_password

# Optional: Override hardcoded settings
ASPNETCORE_ENVIRONMENT=Production
```

### .env.example Template
```bash
# .env.example - Template for environment variables
DISCORD_TOKEN=
DISCORD_CLIENT_ID=
DISCORD_CLIENT_SECRET=
DATABASE_PASSWORD=
```

### Quick Start
```bash
# Clone repository
git clone <repository_url>
cd PomoChallengeCounter

# Create environment file
cp .env.example .env
# Edit .env with your Discord bot credentials

# Start application
docker-compose up -d

# View logs
docker-compose logs -f bot

# Stop application
docker-compose down
```

## Monitoring Configuration

### Health Checks
```csharp
services.AddHealthChecks()
    .AddNpgSql(connectionString)
    .AddCheck<DiscordHealthCheck>("discord")
    .AddCheck<SchedulerHealthCheck>("scheduler");
```

### Logging Configuration
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "theme": "Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme::Code, Serilog.Sinks.Console"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/pomodoro-bot-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  }
}
```

## Security Configuration

### Secrets Management
- Use environment variables for sensitive data
- Never commit tokens to version control
- Use Azure Key Vault or similar for production
- Rotate tokens regularly

### Rate Limiting
```json
{
  "RateLimiting": {
    "EnableLimiting": true,
    "GlobalRules": [
      {
        "Endpoint": "*",
        "Period": "1m",
        "Limit": 100
      }
    ]
  }
}
```

### Input Validation
- Validate all user inputs
- Sanitize emoji codes
- Check date ranges
- Verify permissions on every command

## Performance Configuration

### Caching
```json
{
  "Redis": {
    "Configuration": "localhost:6379",
    "InstanceName": "PomoBot",
    "DefaultDatabase": 0
  },
  "Caching": {
    "DefaultExpiration": "00:15:00",
    "SlidingExpiration": "00:05:00"
  }
}
```

### Database Optimization
- Connection pooling enabled
- Query optimization with indexes
- Batch operations for bulk updates
- Async operations throughout

## Backup Configuration

### Database Backups
```bash
# Daily backup script
pg_dump -h localhost -U pomodoro_user pomodoro_bot > backup_$(date +%Y%m%d).sql

# Retention policy (keep 30 days)
find /backups -name "backup_*.sql" -type f -mtime +30 -delete
```

### Configuration Backups
- Export server configurations
- Store emoji configurations
- Backup role mappings
- Version control for settings

## Troubleshooting

### Debug Configuration
For debugging, set environment variables:
```bash
# .env - Add these for debugging
ASPNETCORE_ENVIRONMENT=Development
ENABLE_DEBUG_COMMANDS=true
LOG_LEVEL=Debug
DETAILED_ERRORS=true
```

### Monitoring Endpoints
- `/health` - Health check status
- `/debug` - Debug information (admin only)
- `/metrics` - Performance metrics 