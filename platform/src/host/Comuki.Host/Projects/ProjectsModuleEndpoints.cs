using Comuki.Host.Projects.Models;
using Comuki.Modules.Projects.Application.Projects;
using Comuki.Modules.Projects.Application.Projects.Archive;
using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Application.Projects.Queries;
using Comuki.Modules.Projects.Application.Projects.Update;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Application.Settings.Update;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;

namespace Comuki.Host.Projects;

/// <summary>
/// Thin REST surface of the Projects module (issue #12 T4.8): endpoints
/// map requests to commands, run the FluentValidation validator and hand
/// off to a handler — no business logic here. Typed exceptions become
/// ProblemDetails responses (404 / 409 / 400) in one place.
/// </summary>
public static class ProjectsModuleEndpoints
{
    /// <summary>Maps the projects endpoints under <see cref="ApiRoutes.Projects"/>.</summary>
    /// <param name="app"></param>
    /// <returns></returns>
    public static IEndpointRouteBuilder MapProjectsEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO(auth): project:admin — wire [RequiresPermission] host-wide with the auth slice
        var group = app.MapGroup(ApiRoutes.Projects).WithTags("Projects");

        group.MapPost("", CreateAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/{projectId:guid}", GetAsync);
        group.MapPatch("/{projectId:guid}", UpdateAsync);
        group.MapDelete("/{projectId:guid}", ArchiveAsync);
        group.MapGet("/{projectId:guid}/settings", GetSettingsAsync);
        group.MapPut("/{projectId:guid}/settings", UpdateSettingsAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateProjectRequest request,
        CreateProjectHandler handler,
        IValidator<CreateProjectCommand> validator,
        CancellationToken cancellationToken)
    {
        return await ProjectsEndpointRunner.ExecuteAsync(async () =>
        {
            var command = ProjectsEndpointMapper.ToCommand(request);
            await validator.ValidateAndThrowAsync(command, cancellationToken);
            var view = await handler.HandleAsync(command, cancellationToken);

            return Results.Created($"{ApiRoutes.Projects}/{view.Id}", view);
        });
    }

    private static async Task<IResult> ListAsync(
        bool includeArchived,
        ListProjectsHandler handler,
        CancellationToken cancellationToken)
    {
        return await ProjectsEndpointRunner.ExecuteAsync(
            async () => Results.Ok(await handler.HandleAsync(includeArchived, cancellationToken)));
    }

    private static async Task<IResult> GetAsync(
        Guid projectId,
        GetProjectHandler handler,
        CancellationToken cancellationToken)
    {
        return await ProjectsEndpointRunner.ExecuteAsync(
            async () => Results.Ok(await handler.HandleAsync(new ProjectId(projectId), cancellationToken)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        UpdateProjectHandler handler,
        IValidator<UpdateProjectCommand> validator,
        CancellationToken cancellationToken)
    {
        return await ProjectsEndpointRunner.ExecuteAsync(async () =>
        {
            var command = ProjectsEndpointMapper.ToCommand(projectId, request);
            await validator.ValidateAndThrowAsync(command, cancellationToken);

            return Results.Ok(await handler.HandleAsync(command, cancellationToken));
        });
    }

    private static async Task<IResult> ArchiveAsync(
        Guid projectId,
        ArchiveProjectHandler handler,
        CancellationToken cancellationToken)
    {
        return await ProjectsEndpointRunner.ExecuteAsync(async () =>
        {
            await handler.HandleAsync(new ArchiveProjectCommand(new ProjectId(projectId)), cancellationToken);

            return Results.NoContent();
        });
    }

    private static async Task<IResult> GetSettingsAsync(
        Guid projectId,
        GetProjectSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        return await ProjectsEndpointRunner.ExecuteAsync(
            async () => Results.Ok(await handler.HandleAsync(new ProjectId(projectId), cancellationToken)));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        Guid projectId,
        UpdateSettingsRequest request,
        UpdateSettingsHandler handler,
        IValidator<UpdateSettingsCommand> validator,
        CancellationToken cancellationToken)
    {
        return await ProjectsEndpointRunner.ExecuteAsync(async () =>
        {
            var command = ProjectsEndpointMapper.ToCommand(projectId, request);
            await validator.ValidateAndThrowAsync(command, cancellationToken);

            return Results.Ok(await handler.HandleAsync(command, cancellationToken));
        });
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
            request.ProfilesGitRef);
    }

    public static UpdateProjectCommand ToCommand(Guid projectId, UpdateProjectRequest request)
    {
        return new UpdateProjectCommand(
            new ProjectId(projectId),
            request.Name,
            request.Description,
            request.ProfilesGitUrl,
            request.ProfilesGitRef);
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
            request.HardBudgetUsdMicros);
    }
}

/// <summary>Typed exceptions → ProblemDetails — one place for every endpoint of the module.</summary>
file static class ProjectsEndpointRunner
{
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ProjectNotFoundException exception)
        {
            return Results.Problem(
                title: "Project not found",
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["projectId"] = exception.ProjectId.ToString() });
        }
        catch (ProjectSettingsConflictException exception)
        {
            return Results.Problem(
                title: "Settings version conflict",
                detail: exception.Message + "; re-read the settings and retry",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["projectId"] = exception.ProjectId.ToString(),
                    ["currentVersion"] = exception.CurrentVersion,
                });
        }
        catch (ProjectConflictException exception)
        {
            return Results.Problem(
                title: "Project conflict",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (ValidationException exception)
        {
            var errors = exception.Errors
                .GroupBy(static failure => failure.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    static grouping => grouping.Key,
                    static grouping => grouping.Select(static failure => failure.ErrorMessage).ToArray());

            return Results.ValidationProblem(errors);
        }
    }
}
