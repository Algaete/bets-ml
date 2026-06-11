using System.Net.Http.Json;
using CornersPrediction.Web.Models.Admin;

namespace CornersPrediction.Web.Clients;

public sealed class UserAdminApiClient
{
    private readonly HttpClient _httpClient;

    public UserAdminApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<PlatformUserViewModel>> GetAsync(
        UserAdminFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var users = await _httpClient.GetFromJsonAsync<IReadOnlyList<PlatformUserViewModel>>(
            $"/api/admin/users{BuildQuery(filters)}",
            cancellationToken);

        return users ?? Array.Empty<PlatformUserViewModel>();
    }

    public async Task<IReadOnlyList<PlatformRoleViewModel>> GetRolesAsync(CancellationToken cancellationToken)
    {
        var roles = await _httpClient.GetFromJsonAsync<IReadOnlyList<PlatformRoleViewModel>>(
            "/api/admin/users/roles",
            cancellationToken);

        return roles ?? Array.Empty<PlatformRoleViewModel>();
    }

    public async Task<PlatformUserViewModel?> FindActiveByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var users = await GetAsync(
            new UserAdminFiltersViewModel
            {
                Search = email,
                IsActive = true
            },
            cancellationToken);

        return users.FirstOrDefault(user =>
            user.IsActive &&
            user.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<PlatformUserViewModel?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return await _httpClient.GetFromJsonAsync<PlatformUserViewModel>(
            $"/api/admin/users/{id}",
            cancellationToken);
    }

    public async Task CreateAsync(PlatformUserFormViewModel form, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/admin/users", ToPayload(form), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UpdateAsync(PlatformUserFormViewModel form, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/users/{form.Id}", ToPayload(form), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync($"/api/admin/users/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static object ToPayload(PlatformUserFormViewModel form)
    {
        return new
        {
            form.ExternalUserId,
            form.Email,
            form.DisplayName,
            form.IsActive,
            form.Roles
        };
    }

    private static string BuildQuery(UserAdminFiltersViewModel filters)
    {
        var query = new List<string>();
        Add(query, "search", filters.Search);
        Add(query, "role", filters.Role);
        if (filters.IsActive is not null)
        {
            Add(query, "isActive", filters.IsActive.Value.ToString());
        }

        return query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
    }

    private static void Add(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Backend API rejected the user admin request: {error}");
    }
}
