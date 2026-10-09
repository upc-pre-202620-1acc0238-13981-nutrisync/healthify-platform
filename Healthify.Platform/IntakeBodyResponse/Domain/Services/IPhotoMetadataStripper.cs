using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Services;

/// <summary>
///     IN-7. Business rule: Photo Leaves Without Patient Data. Removes every metadata block of a meal photo (EXIF with
///     its location, device and date, XMP, ICC, IPTC, comments) before it is sent to the AI, keeping only what decodes
///     the image.
/// </summary>
public interface IPhotoMetadataStripper
{
    /// <returns>The same image without metadata, in the same format.</returns>
    /// <exception cref="ArgumentException">The bytes are not a well-formed image of the declared format.</exception>
    MealPhoto Strip(MealPhoto photo);
}
