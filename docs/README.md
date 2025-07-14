# PomoChallengeCounter Discord Bot

## Overview
A Discord bot built in .NET with PostgreSQL database to automate pomodoro challenge tracking for university discord servers. This bot replaces manual chat copying with automated message tracking through Discord API.

## Key Features
- **Multi-language support** (Dutch/English)
- **Semester-based challenges** with themes
- **Automated weekly thread creation**
- **Point tracking** with emoji system
- **Leaderboard generation** 
- **Goal setting and tracking**
- **Configurable permissions**
- **Reward system**

## Tech Stack
- .NET (C#)
- PostgreSQL (Code First)
- Discord.NET API
- Entity Framework Core

## Project Structure
```
PomoChallengeCounter/
├── docs/           # Documentation
├── src/            # Source code
├── legacy/         # Old Java implementation
└── README.md
```

## Documentation Files
- [`requirements.md`](requirements.md) - Detailed requirements and problem analysis
- [`database-schema.md`](database-schema.md) - PostgreSQL database design with Entity Framework
- [`commands.md`](commands.md) - Complete Discord slash commands reference
- [`features.md`](features.md) - Comprehensive feature specifications
- [`configuration.md`](configuration.md) - Setup, deployment, and configuration guide
- [`workflow.md`](workflow.md) - Complete challenge workflow from start to finish
- [`architecture.md`](architecture.md) - Architecture guidelines and best practices
- [`technical-specifications.md`](technical-specifications.md) - Technical patterns, validation rules, and error codes

## Getting Started
1. Read the requirements and feature docs
2. Review architecture guidelines for coding standards
3. Set up Docker environment (see configuration.md)
4. Configure Discord bot credentials
5. Start implementing core features

## Contributing
This is an open-source project supporting multiple languages and server configurations. 