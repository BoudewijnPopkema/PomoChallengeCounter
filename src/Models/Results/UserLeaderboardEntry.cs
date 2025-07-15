namespace PomoChallengeCounter.Models.Results;

public class UserLeaderboardEntry
{
    public ulong UserId { get; set; }
    public int PomodoroPoints { get; set; }
    public int BonusPoints { get; set; }
    public int TotalPoints => PomodoroPoints + BonusPoints;
    public int GoalPoints { get; set; }
    public bool GoalAchieved => TotalPoints >= GoalPoints;
    public int MessageCount { get; set; }
    public string? RewardEmoji { get; set; }
} 