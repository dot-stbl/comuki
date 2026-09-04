using Comuki.Modules.Identity.Domain.Oidc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// OIDC state table mapping: single-use rows that bind the URL-safe
/// state token (id) to the PKCE verifier + returnTo. The id is the
/// primary key and a UUIDv7 — the index the IdP's <c>state</c>
/// callback looks up by. <c>expires_at</c> drives the purge sweep.
/// </summary>
public sealed class OidcStateConfiguration : IEntityTypeConfiguration<OidcState>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OidcState> builder)
    {
        builder.ToTable(IdentityDatabase.OidcStates, IdentityDatabase.Schema);
        builder.HasKey(static state => state.Id);

        builder.Property(static state => state.Id)
            .HasColumnName("id")
            .HasConversion(IdentityIdConverters.OidcStateIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static state => state.Provider)
            .HasColumnName("provider")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(static state => state.CodeVerifier)
            .HasColumnName("code_verifier")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static state => state.CodeChallengeMethod)
            .HasColumnName("code_challenge_method")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static state => state.RedirectUri)
            .HasColumnName("redirect_uri")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static state => state.ReturnTo)
            .HasColumnName("return_to")
            .HasMaxLength(512);

        builder.Property(static state => state.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static state => state.ExpiresAt)
            .HasColumnName("expires_at");

        builder.HasIndex(static state => state.ExpiresAt)
            .HasDatabaseName("ix_oidc_states_expires_at");
    }
}
