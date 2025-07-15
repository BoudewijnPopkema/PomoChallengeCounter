FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/PomoChallengeCounter.csproj", "src/"]
RUN dotnet restore "src/PomoChallengeCounter.csproj"
COPY . .
WORKDIR "/src/src"
RUN dotnet build "PomoChallengeCounter.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PomoChallengeCounter.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create logs directory
RUN mkdir -p /app/logs

ENTRYPOINT ["dotnet", "PomoChallengeCounter.dll"] 