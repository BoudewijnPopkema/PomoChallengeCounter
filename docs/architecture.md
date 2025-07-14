# Architecture Guidelines

## Code Documentation Philosophy

### Comments Policy
- **Minimal Comments**: Code should be self-explanatory through clear naming and structure
- **Comment Only When**: Logic is genuinely complex or functional purpose is unclear
- **Avoid**: Obvious comments explaining what code does
- **Focus On**: Why code exists, not what it does

### Self-Explanatory Code
- Use descriptive method and variable names
- Keep methods small and focused (single responsibility)
- Prefer explicit over implicit behavior
- Use meaningful constants instead of magic numbers

## Project Structure

### Basic Organization
```
src/
├── Models/              # Database entities, DTOs
├── Services/            # Business logic
├── Commands/            # Discord slash commands
├── Handlers/            # Message and event handlers
├── Data/               # Database context, migrations
├── Localization/        # Translation files (en.json, nl.json)
├── Configuration/       # Static configuration classes
└── Program.cs          # Entry point, DI setup, hardcoded config
```

### Technical Implementation
See [`technical-specifications.md`](technical-specifications.md) for detailed implementation patterns, emoji detection algorithms, validation rules, and error codes.

### Keep It Simple
- Avoid over-engineering for future requirements
- Start with basic patterns, refactor when needed
- One feature per file when possible
- Clear folder structure over complex layering

## .NET Basics

### Dependency Injection
- Register services in `Program.cs`
- Use interfaces for testability
- Stick to simple service lifetimes (Singleton, Scoped, Transient)

### Entity Framework
- Use Code First with simple migrations
- Basic indexing on foreign keys
- Async methods for database operations
- Direct EF Core access in services (no repository pattern)

## Discord Bot Basics

### Commands & Messages
- Use Discord.NET slash commands, grouped logically (`/challenge create`)
- One command per file, organize code for readability
- Basic error handling with try-catch
- Simple permission checks
- Minimal message processing: store message ID, points breakdown, avoid re-retrieval

## Core Principles

### SOLID (Simplified)
- **Single Responsibility**: One class, one job
- **Open/Closed**: Easy to extend without breaking
- **Dependency Inversion**: Use interfaces for main services

### Error Handling
- Use try-catch around Discord API calls
- Log errors with context
- Return user-friendly messages
- Fail gracefully, don't crash the bot

## Code Quality

### Naming & Structure
- PascalCase for classes, methods, properties
- camelCase for variables, parameters
- Descriptive names over abbreviations
- Keep methods under 20 lines
- Return early to reduce nesting
- Avoid deep nesting (max 3 levels)

### Class Design
- Keep classes focused and small
- Use composition over inheritance
- Simple encapsulation

## Configuration & Security

### Configuration
- Hardcode static configuration in the application
- Environment variables only for secrets and external dependencies
- No appsettings.json file needed
- Startup validation (fail fast on config errors)
- Use Discord's built-in localization features for commands
- Store translations in JSON files, code in English only

### Security
- Validate user inputs
- Use parameterized queries (EF handles this)
- Validate Discord permissions
- Don't log sensitive information

## Testing & Database

### Testing
- Unit tests for business logic
- Integration tests with in-memory database
- Mock Discord API calls
- Arrange, Act, Assert pattern

### Database
- Normalize where it makes sense
- Use foreign keys for relationships
- Basic indexes on commonly queried fields
- Use migrations for schema changes
- No metadata fields that don't contribute to behavior or functionality
- Avoid storing computed fields that can be derived from other data

## Performance & Deployment

### Performance
- Focus on correctness first
- Use async/await for I/O operations
- Dispose resources properly
- Don't optimize prematurely
- Cache usernames in memory only, store user IDs in database
- Use IHostedService for background scheduling

### Deployment
- Docker-first approach with docker-compose
- Environment-based configuration via .env files
- PostgreSQL database in separate container
- Simple health checks
- Console logging for development, file logging for production

## Error Recovery

### Basic Resilience
- Retry failed Discord API calls (3 attempts max)
- Handle network timeouts gracefully
- Don't let exceptions crash the bot
- Log enough to debug issues

## Development Guidelines

### Start Simple
- Build MVP first
- Add complexity only when needed
- Refactor when patterns emerge
- Use Git with clear commit messages

### Code Reviews
- Check for business logic correctness
- Ensure code follows naming conventions
- Verify error handling exists
- Keep it simple and readable

## Common Pitfalls

### Over-Engineering
- Don't build for hypothetical scale
- Avoid complex patterns for simple problems
- Don't abstract too early
- Keep dependencies minimal

### Discord Bot Specific
- Don't spam the Discord API
- Handle rate limits gracefully
- Don't store sensitive data in logs
- Test with actual Discord server 