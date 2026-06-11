using CornersPrediction.Domain.Admin;

namespace CornersPrediction.Application.Admin;

public sealed record PlatformUserDto(
    long Id,
    string? ExternalUserId,
    string Email,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record PlatformRoleDto(
    string Name,
    string Description);

public sealed record CreatePlatformUserRequest(
    string? ExternalUserId,
    string Email,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string>? Roles);

public sealed record UpdatePlatformUserRequest(
    string? ExternalUserId,
    string Email,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string>? Roles);

public sealed record PlatformUserFiltersRequest(
    string? Search,
    string? Role,
    bool? IsActive);

public interface IUserAdminRepository
{
    Task<PlatformUser> AddAsync(PlatformUser user, CancellationToken cancellationToken);
    Task<int> UpdateAsync(long id, PlatformUser user, CancellationToken cancellationToken);
    Task<int> DeleteAsync(long id, CancellationToken cancellationToken);
    Task<PlatformUser?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformUser>> GetAsync(PlatformUserFiltersRequest filters, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformRoleDto>> GetRolesAsync(CancellationToken cancellationToken);
}

public interface ICreatePlatformUserUseCase
{
    Task<PlatformUserDto> CreateAsync(CreatePlatformUserRequest request, CancellationToken cancellationToken);
}

public interface IUpdatePlatformUserUseCase
{
    Task<int> UpdateAsync(long id, UpdatePlatformUserRequest request, CancellationToken cancellationToken);
}

public interface IDeletePlatformUserUseCase
{
    Task<int> DeleteAsync(long id, CancellationToken cancellationToken);
}

public interface IGetPlatformUserByIdUseCase
{
    Task<PlatformUserDto?> GetAsync(long id, CancellationToken cancellationToken);
}

public interface IGetPlatformUsersUseCase
{
    Task<IReadOnlyList<PlatformUserDto>> GetAsync(PlatformUserFiltersRequest filters, CancellationToken cancellationToken);
}

public interface IGetPlatformRolesUseCase
{
    Task<IReadOnlyList<PlatformRoleDto>> GetAsync(CancellationToken cancellationToken);
}

public sealed class CreatePlatformUserUseCase : ICreatePlatformUserUseCase
{
    private readonly IUserAdminRepository _repository;

    public CreatePlatformUserUseCase(IUserAdminRepository repository)
    {
        _repository = repository;
    }

    public async Task<PlatformUserDto> CreateAsync(
        CreatePlatformUserRequest request,
        CancellationToken cancellationToken)
    {
        UserAdminValidation.Validate(request.Email, request.DisplayName);

        var user = new PlatformUser
        {
            ExternalUserId = UserAdminValidation.NormalizeOptional(request.ExternalUserId),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            IsActive = request.IsActive,
            Roles = UserAdminValidation.NormalizeRoles(request.Roles),
            CreatedAt = DateTime.UtcNow
        };

        var saved = await _repository.AddAsync(user, cancellationToken);
        return UserAdminMapper.ToDto(saved);
    }
}

public sealed class UpdatePlatformUserUseCase : IUpdatePlatformUserUseCase
{
    private readonly IUserAdminRepository _repository;

    public UpdatePlatformUserUseCase(IUserAdminRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> UpdateAsync(
        long id,
        UpdatePlatformUserRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("User id must be greater than zero.");
        }

        UserAdminValidation.Validate(request.Email, request.DisplayName);

        var user = new PlatformUser
        {
            Id = id,
            ExternalUserId = UserAdminValidation.NormalizeOptional(request.ExternalUserId),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            IsActive = request.IsActive,
            Roles = UserAdminValidation.NormalizeRoles(request.Roles),
            UpdatedAt = DateTime.UtcNow
        };

        return await _repository.UpdateAsync(id, user, cancellationToken);
    }
}

public sealed class DeletePlatformUserUseCase : IDeletePlatformUserUseCase
{
    private readonly IUserAdminRepository _repository;

    public DeletePlatformUserUseCase(IUserAdminRepository repository)
    {
        _repository = repository;
    }

    public Task<int> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("User id must be greater than zero.");
        }

        return _repository.DeleteAsync(id, cancellationToken);
    }
}

public sealed class GetPlatformUserByIdUseCase : IGetPlatformUserByIdUseCase
{
    private readonly IUserAdminRepository _repository;

    public GetPlatformUserByIdUseCase(IUserAdminRepository repository)
    {
        _repository = repository;
    }

    public async Task<PlatformUserDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("User id must be greater than zero.");
        }

        var user = await _repository.GetByIdAsync(id, cancellationToken);
        return user is null ? null : UserAdminMapper.ToDto(user);
    }
}

public sealed class GetPlatformUsersUseCase : IGetPlatformUsersUseCase
{
    private readonly IUserAdminRepository _repository;

    public GetPlatformUsersUseCase(IUserAdminRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<PlatformUserDto>> GetAsync(
        PlatformUserFiltersRequest filters,
        CancellationToken cancellationToken)
    {
        var users = await _repository.GetAsync(filters, cancellationToken);
        return users.Select(UserAdminMapper.ToDto).ToArray();
    }
}

public sealed class GetPlatformRolesUseCase : IGetPlatformRolesUseCase
{
    private readonly IUserAdminRepository _repository;

    public GetPlatformRolesUseCase(IUserAdminRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<PlatformRoleDto>> GetAsync(CancellationToken cancellationToken)
    {
        return _repository.GetRolesAsync(cancellationToken);
    }
}

internal static class UserAdminMapper
{
    public static PlatformUserDto ToDto(PlatformUser user)
    {
        return new PlatformUserDto(
            user.Id,
            user.ExternalUserId,
            user.Email,
            user.DisplayName,
            user.IsActive,
            user.Roles,
            user.CreatedAt,
            user.UpdatedAt);
    }
}

internal static class UserAdminValidation
{
    public static void Validate(string email, string displayName)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.");
        }
    }

    public static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static List<string> NormalizeRoles(IReadOnlyList<string>? roles)
    {
        var normalized = (roles ?? Array.Empty<string>())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => PlatformRoleNames.All.FirstOrDefault(allowed =>
                allowed.Equals(role.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(role => role is not null)
            .Select(role => role!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.Add(PlatformRoleNames.User);
        }

        return normalized;
    }
}
