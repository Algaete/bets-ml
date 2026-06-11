using System.ComponentModel.DataAnnotations;

namespace CornersPrediction.Web.Models.Admin;

public sealed class UserAdminIndexViewModel
{
    public UserAdminFiltersViewModel Filters { get; init; } = new();
    public IReadOnlyList<PlatformUserViewModel> Users { get; init; } = Array.Empty<PlatformUserViewModel>();
    public IReadOnlyList<PlatformRoleViewModel> Roles { get; init; } = Array.Empty<PlatformRoleViewModel>();
}

public sealed class UserAdminFiltersViewModel
{
    public string? Search { get; set; }
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class PlatformUserFormViewModel
{
    public long Id { get; set; }
    public string? ExternalUserId { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public List<string> Roles { get; set; } = ["User"];
    public IReadOnlyList<PlatformRoleViewModel> AvailableRoles { get; set; } = Array.Empty<PlatformRoleViewModel>();
}

public sealed class PlatformUserViewModel
{
    public long Id { get; set; }
    public string? ExternalUserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class PlatformRoleViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
