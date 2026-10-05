namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

/// <summary>
///     IN-7. The photo of a dish, as the patient sent it for recognition. Lives in memory for the length of one
///     request and is never stored: no column, no file and no log line holds its bytes.
/// </summary>
/// <remarks>
///     Business rule: Meal Photo Is JPEG Or WebP (IN-7). The format is read from the signature of the bytes, never
///     from the declared content type, so a renamed file of any other kind is refused.
///     <see cref="ToString" /> is overridden so the bytes are never printed.
/// </remarks>
public sealed class MealPhoto
{
    public const string Jpeg = "image/jpeg";
    public const string WebP = "image/webp";

    public MealPhoto(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            throw new ArgumentException("A meal photo needs its bytes.", nameof(bytes));

        MimeType = DetectFormat(bytes)
                   ?? throw new ArgumentException("A meal photo must be a JPEG or a WebP image.", nameof(bytes));
        Bytes = bytes;
    }

    public byte[] Bytes { get; }

    /// <summary><see cref="Jpeg" /> or <see cref="WebP" />, from the signature.</summary>
    public string MimeType { get; }

    public bool IsJpeg => MimeType == Jpeg;

    /// <summary>The media type the signature says, or null when it is neither JPEG nor WebP.</summary>
    public static string? DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return Jpeg;
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return WebP;
        return null;
    }

    public override string ToString()
    {
        return $"{MimeType}, {Bytes.Length} bytes";
    }
}

/// <summary>
///     IN-7. Another dish the photo could show, as the AI proposed it, resolved to the local catalog when the catalog
///     carries it. Up to three per analysis.
/// </summary>
public sealed record MealPhotoAlternative
{
    public const int MaximumNameLength = 200;

    public MealPhotoAlternative(string name, decimal grams, int? referenceFoodId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An alternative needs a name.", nameof(name));
        if (name.Trim().Length > MaximumNameLength)
            throw new ArgumentException($"An alternative name cannot exceed {MaximumNameLength} characters.",
                nameof(name));
        if (grams is < MealPhotoPortion.MinimumGrams or > MealPhotoPortion.MaximumGrams)
            throw new ArgumentException("An alternative portion must be between 5 and 2000 grams.", nameof(grams));
        if (referenceFoodId is <= 0)
            throw new ArgumentException("An alternative resolves to a positive food identifier, or to none.",
                nameof(referenceFoodId));

        Name = name.Trim();
        Grams = decimal.Round(grams, 0);
        ReferenceFoodId = referenceFoodId;
    }

    public string Name { get; }
    public decimal Grams { get; }

    /// <summary>The catalog entry it stands for, or null when the local catalog does not carry it.</summary>
    public int? ReferenceFoodId { get; }
}

/// <summary>IN-7. Bounds of a portion the AI may estimate from a photo (the schema says the same).</summary>
public static class MealPhotoPortion
{
    public const decimal MinimumGrams = 5m;
    public const decimal MaximumGrams = 2000m;
}
