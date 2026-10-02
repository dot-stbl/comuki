namespace Comuki.Modules.Procedures.Domain.Catalog;

/// <summary>
/// One entry in a procedure-node-kind catalog: the descriptor plus the file
/// key (the catalog-stable identifier procedure graphs reference — same as
/// the descriptor <see cref="Kinds.NodeKindDescriptor.Key"/>). The catalog
/// is the source the compile gate resolves against; an entry may be new,
/// altered, or present-but-not-pinned to any published procedure yet.
/// </summary>
/// <param name="Key">File-stem key, identical to <see cref="Kinds.NodeKindDescriptor.Key"/>.</param>
/// <param name="Descriptor">The parsed typed descriptor.</param>
public sealed record NodeKindCatalogEntry(
    string Key,
    Kinds.NodeKindDescriptor Descriptor);
