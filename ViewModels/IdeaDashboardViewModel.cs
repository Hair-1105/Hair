namespace HanaMedia.ViewModels;

public sealed class IdeaDashboardViewModel
{
    public int NewIdeas { get; init; }
    public int Review { get; init; }
    public int NeedRevision { get; init; }
    public int Approved { get; init; }
    public int InProgress { get; init; }
    public int Done { get; init; }
    public int OverdueTasks { get; init; }
    public int TotalTasks { get; init; }
    public int CompletedTasks { get; init; }
    public int RunningCampaigns { get; init; }
    public int PendingDirector { get; init; }
    public int CompletionPercent => TotalTasks == 0 ? 0 : (int)Math.Round(100m * CompletedTasks / TotalTasks);
    public IReadOnlyList<IdeaDashboardEmployee> Employees { get; init; } = [];
}

public sealed record IdeaDashboardEmployee(string Name, string Status, int OpenTasks, int DoneTasks)
{
    public int CompletionPercent => OpenTasks + DoneTasks == 0 ? 0 : (int)Math.Round(100m * DoneTasks / (OpenTasks + DoneTasks));
}
