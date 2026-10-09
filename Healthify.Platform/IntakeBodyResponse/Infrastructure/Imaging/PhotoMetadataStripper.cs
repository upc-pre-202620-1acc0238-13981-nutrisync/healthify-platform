using System.Buffers.Binary;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Imaging;

/// <summary>
///     IN-7. <see cref="IPhotoMetadataStripper" /> for JPEG and WebP, by rewriting the container without decoding the
///     image: the pixels are copied as they are, the metadata blocks are left out.
/// </summary>
/// <remarks>
///     - JPEG: every APPn segment except APP0 «JFIF» and APP14 «Adobe» (both only describe how to decode) and every
///     COM segment is dropped: APP1 (EXIF, with location, device and date, and XMP), APP2 (ICC, FlashPix), APP13
///     (IPTC/Photoshop), the rest of the application segments and comments. Segments are also parsed between the
///     scans of a progressive image, so nothing hides after the first scan.
///     - WebP: the <c>EXIF</c>, <c>XMP </c> and <c>ICCP</c> chunks are dropped, their flags in <c>VP8X</c> cleared and
///     the RIFF size recomputed.
///     The orientation of a JPEG lives in its EXIF block, so a rotated photo arrives rotated; recognizing a dish
///     does not depend on it. Works in memory only: nothing is written anywhere.
/// </remarks>
public sealed class PhotoMetadataStripper : IPhotoMetadataStripper
{
    private const byte MarkerPrefix = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte EndOfImage = 0xD9;
    private const byte StartOfScan = 0xDA;
    private const byte App0 = 0xE0;
    private const byte App14 = 0xEE;
    private const byte Comment = 0xFE;

    private const byte VP8XIccFlag = 0x20;
    private const byte VP8XExifFlag = 0x08;
    private const byte VP8XXmpFlag = 0x04;

    public MealPhoto Strip(MealPhoto photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        var stripped = photo.IsJpeg ? StripJpeg(photo.Bytes) : StripWebP(photo.Bytes);
        return new MealPhoto(stripped);
    }

    /// <exception cref="ArgumentException">Not a well-formed JPEG.</exception>
    public static byte[] StripJpeg(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != MarkerPrefix || jpeg[1] != StartOfImage)
            throw new ArgumentException("Not a JPEG image.", nameof(jpeg));

        using var output = new MemoryStream(jpeg.Length);
        output.Write(jpeg, 0, 2);
        var position = 2;

        while (position < jpeg.Length)
        {
            if (jpeg[position] != MarkerPrefix)
                throw new ArgumentException("Malformed JPEG: a segment does not start with a marker.", nameof(jpeg));

            // Fill bytes (several 0xFF in a row) are allowed before a marker.
            while (position < jpeg.Length && jpeg[position] == MarkerPrefix) position++;
            if (position >= jpeg.Length) throw new ArgumentException("Malformed JPEG: truncated.", nameof(jpeg));
            var marker = jpeg[position++];

            if (marker == EndOfImage)
            {
                output.WriteByte(MarkerPrefix);
                output.WriteByte(EndOfImage);
                return output.ToArray();
            }

            // Standalone markers carry no length.
            if (marker is 0x01 or >= 0xD0 and <= 0xD7)
            {
                output.WriteByte(MarkerPrefix);
                output.WriteByte(marker);
                continue;
            }

            if (position + 2 > jpeg.Length) throw new ArgumentException("Malformed JPEG: truncated.", nameof(jpeg));
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(position, 2));
            if (length < 2 || position + length > jpeg.Length)
                throw new ArgumentException("Malformed JPEG: a segment overruns the image.", nameof(jpeg));
            var segment = jpeg.AsSpan(position, length);
            position += length;

            if (IsMetadata(marker, segment)) continue;

            output.WriteByte(MarkerPrefix);
            output.WriteByte(marker);
            output.Write(segment);

            if (marker != StartOfScan) continue;

            // Entropy-coded data runs until the next real marker: 0xFF followed by neither 0x00 (stuffing) nor a
            // restart marker. It is copied as it is; parsing resumes at that marker.
            var dataStart = position;
            while (position < jpeg.Length)
            {
                if (jpeg[position] == MarkerPrefix && position + 1 < jpeg.Length)
                {
                    var next = jpeg[position + 1];
                    if (next != 0x00 && next != MarkerPrefix && next is not (>= 0xD0 and <= 0xD7)) break;
                }

                position++;
            }

            output.Write(jpeg, dataStart, position - dataStart);
        }

        // An image without its end marker still decodes in practice; it is closed here.
        output.WriteByte(MarkerPrefix);
        output.WriteByte(EndOfImage);
        return output.ToArray();
    }

    /// <exception cref="ArgumentException">Not a well-formed WebP.</exception>
    public static byte[] StripWebP(byte[] webp)
    {
        if (webp.Length < 12 || !webp.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !webp.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            throw new ArgumentException("Not a WebP image.", nameof(webp));

        var declared = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(4, 4));
        var end = (int)Math.Min(webp.Length, 8L + declared);

        using var body = new MemoryStream(webp.Length);
        body.Write("WEBP"u8);
        var position = 12;
        var keptImage = false;

        while (position + 8 <= end)
        {
            var fourCc = webp.AsSpan(position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(position + 4, 4));
            var padded = size + (size & 1);
            if (position + 8L + size > end)
                throw new ArgumentException("Malformed WebP: a chunk overruns the image.", nameof(webp));

            var chunkLength = (int)Math.Min(8L + padded, end - position);
            var chunk = webp.AsSpan(position, chunkLength).ToArray();
            position += chunkLength;

            if (fourCc.SequenceEqual("EXIF"u8) || fourCc.SequenceEqual("XMP "u8) || fourCc.SequenceEqual("ICCP"u8))
                continue;

            if (fourCc.SequenceEqual("VP8X"u8) && chunk.Length > 8)
                chunk[8] = (byte)(chunk[8] & ~(VP8XIccFlag | VP8XExifFlag | VP8XXmpFlag));

            if (fourCc.SequenceEqual("VP8 "u8) || fourCc.SequenceEqual("VP8L"u8) || fourCc.SequenceEqual("ANMF"u8))
                keptImage = true;

            body.Write(chunk);
        }

        if (!keptImage) throw new ArgumentException("Malformed WebP: no image data.", nameof(webp));

        var payload = body.ToArray();
        var result = new byte[8 + payload.Length];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)payload.Length);
        payload.CopyTo(result, 8);
        return result;
    }

    /// <summary>APPn other than APP0 «JFIF» and APP14 «Adobe», and comments.</summary>
    private static bool IsMetadata(byte marker, ReadOnlySpan<byte> segment)
    {
        if (marker == Comment) return true;
        if (marker is < App0 or > 0xEF) return false;

        var identifier = segment.Length > 2 ? segment[2..] : ReadOnlySpan<byte>.Empty;
        if (marker == App0) return !identifier.StartsWith("JFIF\0"u8);
        if (marker == App14) return !identifier.StartsWith("Adobe"u8);
        return true;
    }
}
