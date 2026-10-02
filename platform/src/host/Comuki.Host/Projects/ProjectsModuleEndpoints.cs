using Comuki.Host.Projects.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Projects.Application.Projects.Archive;
using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Application.Projects.Queries;
using Comuki.Modules.Projects.Application.Projects.Update;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Application.Settings.Update;
using Comuki.Modules.Projects.Application.Views;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;

namespace Comuki.Host.Projects;

/// <summary>
/// Thin REST surface of the Projects module (issue #12 T4.8): endpoints
/// map requests to commands, run the FluentValidation validator and hand
/// off to a handler — no business logic here, and no error plumbing either:
/// typed exceptions surface as ProblemDetails through the composition-root
/// <c>ProviderExceptionHandler</c> and its per-error-type handlers
/// (<c>Errors/Handlers/Projects</c>).
///
/// Permissions are enforced by the host-wide filter
/// (<see cref="RequiresPermissionAttribute"/>) — read endpoints demand
/// <c>project:read</c>, mutating endpoints demand <c>project:admin</c>.
/// The object axis (out-of-scope project rows) is enforced by the
/// project's global query filter; the filter surface misses as 404,
/// never 403.
/// </summary>
public static class ProjectsModuleEndpoints
{
    /// <summary>Maps the projects endpoints under <see cref="ApiRoutes.Projects"/>.</summary>
    public static IEndpointRouteBuilder MapProjectsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiRoutes.Projects).WithTags("Projects");

        group.MapPost("", CreateAsync)
            .Produces<ProjectView>(StatusCodes.Status201Created);
        group.MapGet("", ListAsync)
            .Produces<IReadOnlyList<ProjectView>>(StatusCodes.Status200OK);
        group.MapGet("/{projectId:guid}", GetAsync)
            .Produces<ProjectView>(StatusCodes.Status200OK);
        group.MapPatch("/{projectId:guid}", UpdateAsync)
            .Produces<ProjectView>(StatusCodes.Status200OK);
        group.MapDelete("/{projectId:guid}", ArchiveAsync)
            .Produces(StatusCodes.Status204NoContent);
        group.MapGet("/{projectId:guid}/settings", GetSettingsAsync)
            .Produces<ProjectSettingsView>(StatusCodes.Status200OK);
        group.MapPut("/{projectId:guid}/settings", UpdateSettingsAsync)
            .Produces<ProjectSettingsView>(StatusCodes.Status200OK);

        return app;
    }

    [RequiresPermission("project:admin")]
    [EnforceLimit("projects")]
    private static async Task<IResult> CreateAsync(
        CreateProjectRequest request,
        CreateProjectHandler handler,
        IValidator<CreateProjectCommand> validator,
        CancellationToken cancellationToken)
    {
        var command = ProjectsEndpointMapper.ToCommand(request);
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var view = await handler.HandleAsync(command, cancellationToken);

        return Results.Created($"{ApiRoutes.Projects}/{view.Id}", view);
    }

    [RequiresPermission("project:read")]
    private static async Task<IResult> ListAsync(
        bool includeArchived,
        ListProjectsHandler handler,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await handler.HandleAsync(includeArchived, cancellationToken));
    }

    [RequiresPermission("project:read")]
    private static async Task<IResult> GetAsync(
        Guid projectId,
        GetProjectHandler handler,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await handler.HandleAsync(new ProjectId(projectId), cancellationToken));
    }

    [RequiresPermission("project:admin")]
    private static async Task<IResult> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        UpdateProjectHandler handler,
        IValidator<UpdateProjectCommand> validator,
        CancellationToken cancellationToken)
    {
        var command = ProjectsEndpointMapper.ToCommand(projectId, request);
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        return Results.Ok(await handler.HandleAsync(command, cancellationToken));
    }

    [RequiresPermission("project:admin")]
    private static async Task<IResult> ArchiveAsync(
        Guid projectId,
        ArchiveProjectHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new ArchiveProjectCommand(new ProjectId(projectId)), cancellationToken);

        return Results.NoContent();
    }

    [RequiresPermission("project:read")]
    private static async Task<IResult> GetSettingsAsync(
        Guid projectId,
        GetProjectSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await handler.HandleAsync(new ProjectId(projectId), cancellationToken));
    }

    [RequiresPermission("project:admin")]
    private static async Task<IResult> UpdateSettingsAsync(
        Guid projectId,
        UpdateSettingsRequest request,
        UpdateSettingsHandler handler,
        IValidator<UpdateSettingsCommand> validator,
        CancellationToken cancellationToken)
    {
        var command = ProjectsEndpointMapper.ToCommand(projectId, request);
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        return Results.Ok(await handler.HandleAsync(command, cancellationToken));
    }
}

/// <summary>Request → command mapping — the only translation between wire and application shapes.</summary>
file static class ProjectsEndpointMapper
{
    public static CreateProjectCommand ToCommand(CreateProjectRequest request)
    {
        return new CreateProjectCommand(
            request.Name,
            request.Slug,
            request.Description,
            request.ProfilesGitUrl,
            request.ProfilesGitRef,
            request.Icon,
            request.Color,
            request.Tags,
            EnvClass: null,
            request.SourceGitUrl,
            request.SourceGitRef);
    }

    public static UpdateProjectCommand ToCommand(Guid projectId, UpdateProjectRequest request)
    {
        return new UpdateProjectCommand(
            new ProjectId(projectId),
            request.Name,
            request.Description,
            request.ProfilesGitUrl,
            request.ProfilesGitRef,
            request.Icon,
            request.Color,
            request.Tags,
            EnvClass: null,
            request.SourceGitUrl,
            request.SourceGitRef);
    }

    public static UpdateSettingsCommand ToCommand(Guid projectId, UpdateSettingsRequest request)
    {
        return new UpdateSettingsCommand(
            new ProjectId(projectId),
            request.Version,
            request.MinIdle,
            request.MaxConcurrent,
            request.IdleTtlSeconds,
            request.ApproveRequired,
            request.KnowledgeEnabled,
            request.VerifyEnabled,
            request.ProxyEnabled,
            request.SoftBudgetUsdMicros,
            request.HardBudgetUsdMicros,
            request.DomainType,
            request.CustomDomainTypesJson,
            request.GitCredentialRef);
    }
}
