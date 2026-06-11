using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.Admin;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Admin)]
public sealed class UserAdminController : Controller
{
    private readonly UserAdminApiClient _userAdminApiClient;
    private readonly ILogger<UserAdminController> _logger;

    public UserAdminController(
        UserAdminApiClient userAdminApiClient,
        ILogger<UserAdminController> logger)
    {
        _userAdminApiClient = userAdminApiClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] UserAdminFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        try
        {
            var roles = await _userAdminApiClient.GetRolesAsync(cancellationToken);
            var users = await _userAdminApiClient.GetAsync(filters, cancellationToken);

            return View(new UserAdminIndexViewModel
            {
                Filters = filters,
                Roles = roles,
                Users = users
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load platform users");
            ModelState.AddModelError(string.Empty, "Users could not be loaded. Check that the API and SQL script are available.");
            return View(new UserAdminIndexViewModel { Filters = filters });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return View(new PlatformUserFormViewModel
        {
            AvailableRoles = await LoadRolesAsync(cancellationToken)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        PlatformUserFormViewModel form,
        CancellationToken cancellationToken)
    {
        form.AvailableRoles = await LoadRolesAsync(cancellationToken);
        EnsureDefaultRoles(form);

        if (!ModelState.IsValid)
        {
            return View(form);
        }

        try
        {
            await _userAdminApiClient.CreateAsync(form, cancellationToken);
            TempData["SuccessMessage"] = "User saved successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not create platform user");
            ModelState.AddModelError(string.Empty, "The user could not be saved.");
            return View(form);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var user = await _userAdminApiClient.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return View(new PlatformUserFormViewModel
        {
            Id = user.Id,
            ExternalUserId = user.ExternalUserId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            IsActive = user.IsActive,
            Roles = user.Roles,
            AvailableRoles = await LoadRolesAsync(cancellationToken)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        PlatformUserFormViewModel form,
        CancellationToken cancellationToken)
    {
        form.AvailableRoles = await LoadRolesAsync(cancellationToken);
        EnsureDefaultRoles(form);

        if (!ModelState.IsValid)
        {
            return View(form);
        }

        try
        {
            await _userAdminApiClient.UpdateAsync(form, cancellationToken);
            TempData["SuccessMessage"] = "User updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not update platform user {PlatformUserId}", form.Id);
            ModelState.AddModelError(string.Empty, "The user could not be updated.");
            return View(form);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        try
        {
            await _userAdminApiClient.DeleteAsync(id, cancellationToken);
            TempData["SuccessMessage"] = "User deleted.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not delete platform user {PlatformUserId}", id);
            TempData["ErrorMessage"] = "The user could not be deleted.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<IReadOnlyList<PlatformRoleViewModel>> LoadRolesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _userAdminApiClient.GetRolesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load platform roles");
            return Array.Empty<PlatformRoleViewModel>();
        }
    }

    private static void EnsureDefaultRoles(PlatformUserFormViewModel form)
    {
        form.Roles = form.Roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (form.Roles.Count == 0)
        {
            form.Roles.Add("User");
        }
    }
}
