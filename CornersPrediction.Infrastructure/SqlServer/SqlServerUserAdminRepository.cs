using System.Data;
using CornersPrediction.Application.Admin;
using CornersPrediction.Domain.Admin;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerUserAdminRepository : IUserAdminRepository
{
    private readonly string _connectionString;

    public SqlServerUserAdminRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<PlatformUser> AddAsync(PlatformUser user, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = BuildUserParameters(user);
        parameters.Add("InsertedId", dbType: DbType.Int64, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_InsertPlatformUser",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        user.Id = parameters.Get<long>("InsertedId");
        return user;
    }

    public async Task<int> UpdateAsync(long id, PlatformUser user, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = BuildUserParameters(user);
        parameters.Add("Id", id, DbType.Int64);
        parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_UpdatePlatformUser",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        return parameters.Get<int>("RowsAffected");
    }

    public async Task<int> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Id", id, DbType.Int64);
        parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_DeletePlatformUser",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        return parameters.Get<int>("RowsAffected");
    }

    public async Task<PlatformUser?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Id", id, DbType.Int64);

        var command = new CommandDefinition(
            "dbo.sp_GetPlatformUserById",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<PlatformUserRow>(command);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<PlatformUser>> GetAsync(
        PlatformUserFiltersRequest filters,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Search", Normalize(filters.Search), DbType.String, size: 320);
        parameters.Add("Role", Normalize(filters.Role), DbType.String, size: 50);
        parameters.Add("IsActive", filters.IsActive, DbType.Boolean);

        var command = new CommandDefinition(
            "dbo.sp_GetPlatformUsers",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<PlatformUserRow>(command);
        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<PlatformRoleDto>> GetRolesAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            "dbo.sp_GetPlatformRoles",
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var roles = await connection.QueryAsync<PlatformRoleDto>(command);
        return roles.ToArray();
    }

    private static DynamicParameters BuildUserParameters(PlatformUser user)
    {
        var parameters = new DynamicParameters();
        parameters.Add("ExternalUserId", user.ExternalUserId, DbType.String, size: 450);
        parameters.Add("Email", user.Email, DbType.String, size: 320);
        parameters.Add("DisplayName", user.DisplayName, DbType.String, size: 200);
        parameters.Add("IsActive", user.IsActive, DbType.Boolean);
        parameters.Add("RolesCsv", string.Join(",", user.Roles), DbType.String);
        return parameters;
    }

    private static PlatformUser ToDomain(PlatformUserRow row)
    {
        return new PlatformUser
        {
            Id = row.Id,
            ExternalUserId = row.ExternalUserId,
            Email = row.Email,
            DisplayName = row.DisplayName,
            IsActive = row.IsActive,
            Roles = ParseRoles(row.RolesCsv),
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static List<string> ParseRoles(string? rolesCsv)
    {
        return string.IsNullOrWhiteSpace(rolesCsv)
            ? []
            : rolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class PlatformUserRow
    {
        public long Id { get; init; }
        public string? ExternalUserId { get; init; }
        public string Email { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public string? RolesCsv { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }
}
