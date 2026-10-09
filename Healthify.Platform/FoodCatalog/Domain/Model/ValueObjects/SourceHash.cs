using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Healthify.Platform.FoodCatalog.Domain.Services;

namespace Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;

/// <summary>
///     A fingerprint of the upstream record a reference food was translated from.
/// </summary>
/// <remarks>
///     Business rules: No External Id Enters The Domain and Source Hash Stored For Upstream Changes
///     (Food Catalog, Subflow 6.1). It exists so that a later import can tell whether the upstream
///     record changed, and for nothing else. It is deliberately a one-way digest rather than the
///     provider identifier: an identifier would be an external concept living inside the domain, and
///     sooner or later something would read it back and use it as one. A digest cannot be.
///     It is stored, and it is never exposed in a REST resource.
/// </remarks>
public sealed record SourceHash
{
    /// <summary>SHA-256, lowercase hexadecimal.</summary>
    public const int Length = 64;

    /// <summary>Separator between the parts of the digest material.</summary>
    private const string PartSeparator = "::";

    public SourceHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A source hash is required.", nameof(value));

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != Length || !normalized.All(Uri.IsHexDigit))
            throw new ArgumentException(
                $"A source hash must be {Length} lowercase hexadecimal characters.", nameof(value));

        Value = normalized;
    }

    public string Value { get; }

    /// <summary>
    ///     Digest of the parts that identify an upstream record. Called from the anti-corruption
    ///     layer, which is the only place that has ever seen those parts.
    /// </summary>
    public static SourceHash Of(params string[] parts)
    {
        var material = string.Join(PartSeparator, parts);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return new SourceHash(Convert.ToHexString(digest).ToLowerInvariant());
    }

    /// <summary>
    ///     Digest for an item that has no upstream record at all. A local override still needs one,
    ///     because the hash is also what makes importing and seeding idempotent.
    /// </summary>
    public static SourceHash ForLocalOverride(string localName)
    {
        return Of("local-override", localName.Trim().ToLower(CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     IN-7. Digest for a food the AI estimated: <c>"ai:" + normalized name</c>, so the same dish (whatever its
    ///     case, accents or spacing) is always the same row, and the unique index decides between two that race.
    /// </summary>
    /// <remarks>
    ///     DECISIÓN IN-7: the change asks for <c>source_hash = "ai:" + nombre normalizado</c>; the column holds a 64
    ///     character hexadecimal digest and a raw name would break that invariant (and could exceed it), so the
    ///     material is that string and the stored value its SHA-256.
    /// </remarks>
    public static SourceHash ForAiEstimated(string localName)
    {
        return Of(AiMaterial(localName));
    }

    /// <summary>IN-7. The material of <see cref="ForAiEstimated" />: <c>ai:</c> and the name in lower case, without accents, single spaces.</summary>
    public static string AiMaterial(string localName)
    {
        return "ai:" + FoodNameMatcher.Normalize(localName);
    }

    public override string ToString()
    {
        return Value;
    }
}
