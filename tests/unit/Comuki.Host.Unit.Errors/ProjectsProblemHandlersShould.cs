using Comuki.Host.Errors;
using Comuki.Host.Errors.Handlers.Projects;
using Comuki.Modules.Projects.Application.DomainTypes;
using Comuki.Modules.Projects.Application.Projects;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// Wire rows for the Projects module's domain errors (domain-error-contract
/// task 2.2). Every literal below — status, type URN, title, detail
/// sentence (including the retry suffix) and extensions — is pinned from
/// the <c>ProjectsEndpointRunner</c> arms these handlers replace, so the
/// runner deletion cannot drift from the shape the FE already consumes.
/// The one deliberate delta: the <c>type</c> URN is now derived from the
/// code (design D4) instead of the hand-written per-catch spelling.
/// </summary>
public sealed class ProjectsProblemHandlersShould
{
    [Fact(DisplayName = "Given an unknown project id, when the NotFound handler answers, then the row pins the runner's 404 shape")]
    public void ProjectNotFoundPinsRunnerShape()
    {
        var projectId = new ProjectId(Guid.Parse("0d1c3a57-8e2b-4f18-9d4a-6b7e5c2a1f00"));
        var handler = new ProjectNotFoundProblemHandler();

        var answer = handler.Answer(new ProjectNotFoundException(projectId));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Type.ShouldBe("urn:comuki:error:project.not_found");
        answer.Title.ShouldBe("Project not found");
        answer.Detail.ShouldBe($"project '{projectId}' not found");
        answer.Code.ShouldBe("project.not_found");
        answer.Extensions.Count.ShouldBe(2);
        answer.Extensions["code"].ShouldBe("project.not_found");
        answer.Extensions["projectId"].ShouldBe(projectId.ToString());
        answer.ValidationErrors.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a stale settings version, when the SettingsConflict handler answers, then the row keeps the retry payload")]
    public void ProjectSettingsConflictPinsRetryPayload()
    {
        var projectId = new ProjectId(Guid.Parse("1e2d4b68-9f3c-4029-8e5b-7c8f6d3b2a11"));
        var handler = new ProjectSettingsConflictProblemHandler();

        var answer = handler.Answer(new ProjectSettingsConflictException(projectId, 4, 7));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Type.ShouldBe("urn:comuki:error:project.settings_conflict");
        answer.Title.ShouldBe("Settings version conflict");
        answer.Detail.ShouldBe(
            $"settings of project {projectId} changed: expected version 4, current version 7; re-read the settings and retry");
        answer.Code.ShouldBe("project.settings_conflict");
        answer.Extensions.Count.ShouldBe(3);
        answer.Extensions["code"].ShouldBe("project.settings_conflict");
        answer.Extensions["projectId"].ShouldBe(projectId.ToString());
        answer.Extensions["currentVersion"].ShouldBe(7);
    }

    [Fact(DisplayName = "Given a taken slug, when the Conflict handler answers, then the row pins the runner's 409 shape")]
    public void ProjectConflictPinsRunnerShape()
    {
        var handler = new ProjectConflictProblemHandler();

        var answer = handler.Answer(new ProjectConflictException("slug 'acme' is already taken"));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Type.ShouldBe("urn:comuki:error:project.conflict");
        answer.Title.ShouldBe("Project conflict");
        answer.Detail.ShouldBe("slug 'acme' is already taken");
        answer.Code.ShouldBe("project.conflict");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("project.conflict");
    }

    [Fact(DisplayName = "Given the Projects rows registered, when a ProjectNotFoundException resolves, then the registry serves the module row")]
    public void ProjectsRowsResolveThroughRegistry()
    {
        var registry = new ProblemHandlerRegistry(
        [
            new ProjectNotFoundProblemHandler(),
            new ProjectSettingsConflictProblemHandler(),
            new ProjectConflictProblemHandler(),
        ],
        NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new ProjectConflictException("slug 'acme' is already taken"));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Title.ShouldBe("Project conflict");
    }

    [Fact(DisplayName = "Given an unmapped domain type, when resolved through the registry, then the DomainException default answers 422 with its own code")]
    public void ProjectDomainTypeNotMappedRidesTheDomainDefault()
    {
        var registry = new ProblemHandlerRegistry([], NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new ProjectDomainTypeNotMappedException("billing", "Custom", "missing"));

        answer.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        answer.Type.ShouldBe("urn:comuki:error:project.domain_type_not_mapped");
        answer.Title.ShouldBe("Domain rule violated");
        answer.Detail.ShouldBe("domain type 'billing' is not mapped under project mode 'Custom' (missing)");
        answer.Code.ShouldBe("project.domain_type_not_mapped");
        answer.Extensions["code"].ShouldBe("project.domain_type_not_mapped");
    }
}
