namespace Comuki.Modules.Procedures.Domain.Validation.Helpers;

/// <summary>A parsed <c>major.minor</c> catalog version.</summary>
/// <param name="Major">Major version — bumped on contract-changing alterations or retractions.</param>
/// <param name="Minor">Minor version — bumped on additive-only changes.</param>
public readonly record struct CatalogVersion(int Major, int Minor);
