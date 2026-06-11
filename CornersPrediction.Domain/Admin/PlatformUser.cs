namespace CornersPrediction.Domain.Admin;

public sealed class PlatformUser
{
    public long Id { get; set; }
    public string? ExternalUserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public static class PlatformRoleNames
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Bettor = "Bettor";
    public const string Analyst = "Analyst";

    public static readonly string[] All = [Admin, User, Bettor, Analyst];
}
