using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Comuki.Host.OpenApi;

/// <summary>
/// Fills in the standard error-response shape on every MVC action the
/// <c>Microsoft.AspNetCore.OpenApi</c> source-generator emits, so the
/// per-endpoint <c>[ProducesResponseType(typeof(ProblemDetails), 4xx/5xx)]</c>
/// attributes that only duplicated the global body are now redundant and
/// can be removed one-for-one from controllers (the brief for
/// <c>add-mission-cowork</c> removed ~70 in this wave). Endpoints that
/// already declare a response for the same status keep their entry —
/// this transformer only adds slots the controller forgot, never overwrites.
/// <para>
/// Shape baked into the document mirrors the host's runtime
/// <c>ProblemDetails</c> body:
/// 400 ValidationProblemDetails (carries the validation <c>errors</c>
/// dictionary the FluentValidation pipeline emits), 404 not_found,
/// 409 conflict, 500 unhandled, and 401/403 only when the action carries
/// <c>[Authorize]</c> (read off the action's reflected metadata, the same
/// gate ASP.NET Core uses for the runtime). A controller can still
/// override a specific status by declaring its own
/// <c>[ProducesResponseType]</c> on the action — that wins, the
/// transformer only fills empty slots.
/// </para>
/// </summary>
public sealed class ProblemDetailsOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ProblemDetailsOperationHelpers.EnsureProblemDetailsSchemas(context);

        ProblemDetailsOperationHelpers.EnsureResponse(operation, "400", "Validation failed", validationSchema: true);
        ProblemDetailsOperationHelpers.EnsureResponse(operation, "404", "Resource not found");
        ProblemDetailsOperationHelpers.EnsureResponse(operation, "409", "Conflict");
        ProblemDetailsOperationHelpers.EnsureResponse(operation, "500", "Unhandled error");

        if (ProblemDetailsOperationHelpers.RequiresAuthorization(context))
        {
            ProblemDetailsOperationHelpers.EnsureResponse(operation, "401", "Unauthenticated");
            ProblemDetailsOperationHelpers.EnsureResponse(operation, "403", "Forbidden");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Document / response / metadata plumbing that
/// <see cref="ProblemDetailsOperationTransformer"/> delegates to. File-scoped
/// so the three helpers live next to the only caller; the transformer itself
/// stays a thin orchestration entry point with no private statics.
/// </summary>
file static class ProblemDetailsOperationHelpers
{
    /// <summary>Validation body schema — same shape <c>ValidationProblemDetails</c> emits.</summary>
    private const string Validation400 = "ValidationProblemDetails";

    /// <summary>Standard <c>ProblemDetails</c> schema name (one per document).</summary>
    private const string ProblemDetails = "ProblemDetails";

    /// <summary>
    /// Registers the two wire schemas the transformer references (idempotent —
    /// <c>OpenApiDocument.Components</c> already-keyed dict).
    /// </summary>
    /// <param name="context">Operation-transformer context; the <see cref="OpenApiDocument"/> carries the document-wide components.</param>
    public static void EnsureProblemDetailsSchemas(OpenApiOperationTransformerContext context)
    {
        if (context.Document?.Components is null)
        {
            return;
        }

        var components = context.Document.Components;
        components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        if (!components.Schemas.ContainsKey(ProblemDetails))
        {
            components.Schemas[ProblemDetails] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "RFC 9457 ProblemDetails body the host writes for every non-2xx response.",
            };
        }

        if (!components.Schemas.ContainsKey(Validation400))
        {
            components.Schemas[Validation400] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "ValidationProblemDetails body: same shape as ProblemDetails with the validation `errors` dictionary populated.",
            };
        }
    }

    /// <summary>
    /// Adds a ProblemDetails response under <paramref name="statusCode"/> if the
    /// operation does not already declare one for that slot.
    /// </summary>
    /// <param name="operation">The OpenAPI operation under construction.</param>
    /// <param name="statusCode">HTTP status string ("400", "404", ...).</param>
    /// <param name="description">Human-readable title — same wording TypedResults.Problem uses at runtime.</param>
    /// <param name="validationSchema">True when the response body is <c>ValidationProblemDetails</c>; defaults to <c>ProblemDetails</c>.</param>
    public static void EnsureResponse(OpenApiOperation operation, string statusCode, string description, bool validationSchema = false)
    {
        operation.Responses ??= [];

        if (operation.Responses.ContainsKey(statusCode))
        {
            return;
        }

        var schemaName = validationSchema ? Validation400 : ProblemDetails;
        var response = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                ["application/problem+json"] = new()
                {
                    Schema = new OpenApiSchemaReference(schemaName, null),
                },
            },
        };

        operation.Responses.Add(statusCode, response);
    }

    /// <summary>
    /// True when the endpoint is gated by either ASP.NET Core's
    /// <c>[Authorize]</c> or the host's permission-typed
    /// <c>[RequiresPermission]</c>. The MVC
    /// <c>EndpointMetadataApiDescriptionProvider</c> folds both into the
    /// flat <see cref="Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor.EndpointMetadata"/>
    /// collection — scanning it is the same gate the runtime middleware
    /// uses to demand credentials.
    /// </summary>
    /// <param name="context">Operation-transformer context; the <c>Description</c>
    /// carries the <see cref="Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription"/>.</param>
    public static bool RequiresAuthorization(OpenApiOperationTransformerContext context)
    {
        var metadata = context.Description?.ActionDescriptor?.EndpointMetadata;
        if (metadata is null)
        {
            return false;
        }

        foreach (var item in metadata)
        {
            // AllowAnonymousAttribute wins: the endpoint is intentionally
            // open even when the controller has [Authorize] (e.g. the
            // login endpoint on an auth-required controller).
            if (item is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute)
            {
                return false;
            }

            if (item is Microsoft.AspNetCore.Authorization.AuthorizeAttribute)
            {
                return true;
            }

            // The host's custom gate — full type-name match avoids an
            // out-of-scope module reference from this OpenAPI helper.
            if (item.GetType().FullName == "Comuki.Modules.Identity.Application.Permissions.RequiresPermissionAttribute")
            {
                return true;
            }
        }

        return false;
    }
}
