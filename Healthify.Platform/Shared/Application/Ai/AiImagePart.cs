using System.Security.Cryptography;
using System.Text;

namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>
///     IN-7. One image sent to the model with the text input (Gemini <c>inlineData</c>). Held in memory for the
///     duration of the call only: the pipeline audits its SHA-256 (<c>ai_generations.input_image_hash</c>), never
///     its bytes, and nothing logs it.
/// </summary>
/// <remarks>
///     The caller (the context that owns the function) is responsible for what the image carries: it strips the
///     metadata (EXIF, XMP, ICC…) before building the part, so the image leaves without any data of the patient.
///     <see cref="ToString" /> is overridden so a log line or a debugger never prints the bytes.
/// </remarks>
public sealed class AiImagePart
{
    public AiImagePart(ReadOnlyMemory<byte> bytes, string mimeType)
    {
        if (bytes.IsEmpty) throw new ArgumentException("An image part needs its bytes.", nameof(bytes));
        if (string.IsNullOrWhiteSpace(mimeType) ||
            !mimeType.Trim().StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An image part needs an image/* media type.", nameof(mimeType));

        Bytes = bytes;
        MimeType = mimeType.Trim().ToLowerInvariant();
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();
    }

    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>For instance <c>image/jpeg</c> or <c>image/webp</c>.</summary>
    public string MimeType { get; }

    /// <summary>SHA-256 of the bytes as sent, lowercase hexadecimal.</summary>
    public string Sha256 { get; }

    /// <summary>
    ///     The <c>input_image_hash</c> of a request: the SHA-256 of its one image or, with several, the SHA-256 of
    ///     their hashes joined by <c>:</c> in order. Null without images.
    /// </summary>
    public static string? CombinedHash(IReadOnlyList<AiImagePart>? images)
    {
        if (images is null || images.Count == 0) return null;
        if (images.Count == 1) return images[0].Sha256;

        var joined = string.Join(':', images.Select(i => i.Sha256));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
    }

    public override string ToString()
    {
        return $"{MimeType}, {Bytes.Length} bytes, sha256 {Sha256[..12]}…";
    }
}
