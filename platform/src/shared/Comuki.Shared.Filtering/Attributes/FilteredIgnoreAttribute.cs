// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
namespace Comuki.Shared.Filtering.Attributes;

/// <summary>
///     Keeps a property out of the filter DSL. The field registry never registers it, so no
///     filter expression can name it, no sort criterion resolves to it, and it appears in
///     neither the <c>x-filterable</c> nor the <c>x-sortable</c> schema extension — which is
///     what the frontend's generated filter builder is built from.
/// </summary>
/// <remarks>
///     <para>
///         <b>The contract is a deny-list.</b> Every public instance property of an entity is
///         filterable; a property leaves the registry only when its CLR type has no operators
///         (<c>byte[]</c>, <c>object</c>, a strongly-typed id), when it is <c>[NotMapped]</c>,
///         or when it carries this attribute. Mark what a caller must not be able to probe:
///         password hashes, session and permission stamps, anything whose value is itself a
///         credential.
///     </para>
///     <para>
///         <b>What one forgotten mark costs — and the cost is not symmetric.</b> Under an
///         allow-list a forgotten mark makes a field unsearchable: visible the first time
///         somebody tries to search by it, harmless until then. Under this deny-list a
///         forgotten mark publishes a new property to everyone who can call the list endpoint,
///         and nothing says so. The damage is not "the value shows up in the response" — the
///         operators are an oracle. <c>?filter=passwordHash^=AQAAAA</c> parses, translates and
///         runs; an empty page means "no", a non-empty page means "yes", and one request per
///         character reads the stored hash out of a paging endpoint without a single failed
///         authorization. <c>==</c> and <c>[]=</c> do the same for any guessable value.
///     </para>
///     <para>
///         The trade is deliberate: the default stays legible (a new column is searchable,
///         which is what a list endpoint is for) and the price is concentrated in the few
///         entities that hold secrets. Pay it down where it matters by pinning the exposed set
///         of such an entity in a test — assert the exact field list of the account aggregate,
///         so a new property there is a red test rather than a silent exposure.
///     </para>
///     <para>
///         <b>Not <c>[NotMapped]</c>.</b> That attribute belongs to EF and means "this property
///         has no column"; the registry honours it because a field with no column cannot be
///         translated to SQL, not because it is a privacy marker. On a property that IS
///         persisted the two are opposites — <c>[NotMapped]</c> on <c>User.PasswordHash</c>
///         would drop the column from the model and break authentication outright. Use this
///         attribute when the column must exist and only the DSL must not see it.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FilteredIgnoreAttribute : Attribute;
