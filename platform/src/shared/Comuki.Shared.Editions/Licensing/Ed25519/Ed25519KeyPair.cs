namespace Comuki.Shared.Editions.Licensing.Ed25519;

/// <summary>
/// A fresh Ed25519 keypair as produced by
/// <see cref="Ed25519LicenseSigner.GenerateKeyPair"/>. Two distinct
/// 32-byte arrays: <see cref="PublicKey"/> is safe to embed in
/// production builds and ship with the operator's docs, while
/// <see cref="PrivateKeySeed"/> is the signing half and must never
/// leave an offline signer.
/// <para>
/// Replaces the previous <c>(byte[] PublicKey, byte[] PrivateKeySeed)</c>
/// tuple return, which lost the domain name at every call site. Tests,
/// the dev mint tool, and the operator-runbook all reference this type
/// by name now.
/// </para>
/// </summary>
/// <param name="PublicKey">The 32-byte Ed25519 verifying public key.</param>
/// <param name="PrivateKeySeed">The 32-byte Ed25519 private-key seed (the signing half — keep secret).</param>
public sealed record Ed25519KeyPair(byte[] PublicKey, byte[] PrivateKeySeed);
