namespace PomoChallengeCounter.Models.Results;

public class ChallengeLeaderboardEntry
{
    public ulong UserId { get; set; }
    
    // Total challenge stats
    public int TotalPomodoroPoints { get; set; }
    public int TotalBonusPoints { get; set; }
    public int TotalPoints => TotalPomodoroPoints + TotalBonusPoints;
    public int TotalGoalPoints { get; set; }
    public bool TotalGoalAchieved => TotalPoints >= TotalGoalPoints;
    public int TotalMessageCount { get; set; }
    
    // Weekly stats for current week
    public int WeeklyPomodoroPoints { get; set; }
    public int WeeklyBonusPoints { get; set; }
    public int WeeklyPoints => WeeklyPomodoroPoints + WeeklyBonusPoints;
    public int WeeklyGoalPoints { get; set; }
    public bool WeeklyGoalAchieved => WeeklyPoints >= WeeklyGoalPoints;
    public int WeeklyMessageCount { get; set; }
    
    public string? RewardEmoji { get; set; }
} 