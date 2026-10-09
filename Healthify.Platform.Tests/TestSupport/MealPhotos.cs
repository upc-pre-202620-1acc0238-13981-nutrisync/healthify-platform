using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>IN-7. Photos for the tests: tiny containers with metadata a leak would reveal.</summary>
public static class MealPhotos
{
    public const string ExifSecret = "GPS-LIMA-12.0464S-77.0428W";
    public const string CommentSecret = "Foto de Ana Torres";
    public const string XmpSecret = "xmp-owner-ana@example.com";

    /// <summary>The bytes of the "pixels": they must survive the stripping untouched.</summary>
    public static readonly byte[] ScanData = [0x12, 0x34, 0xFF, 0x00, 0x56, 0xFF, 0xD0, 0x78, 0x9A];

    /// <summary>SOI, APP0 JFIF, APP1 EXIF, APP1 XMP, COM, DQT, SOF0, SOS + entropy data, EOI.</summary>
    public static byte[] JpegWithMetadata()
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        Segment(bytes, 0xE0, [.."JFIF\0"u8, 0x01, 0x02, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        Segment(bytes, 0xE1, [.."Exif\0\0"u8, ..System.Text.Encoding.ASCII.GetBytes(ExifSecret)]);
        Segment(bytes, 0xE1, [.."http://ns.adobe.com/xap/1.0/\0"u8, ..System.Text.Encoding.ASCII.GetBytes(XmpSecret)]);
        Segment(bytes, 0xFE, System.Text.Encoding.ASCII.GetBytes(CommentSecret));
        Segment(bytes, 0xDB, [0x00, .. Enumerable.Repeat((byte)1, 64)]);
        Segment(bytes, 0xC0, [0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00]);
        Segment(bytes, 0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]);
        bytes.AddRange(ScanData);
        bytes.AddRange([0xFF, 0xD9]);
        return bytes.ToArray();
    }

    /// <summary>RIFF/WEBP with VP8X (flags EXIF + XMP + ICC), ICCP, VP8, EXIF and XMP chunks.</summary>
    public static byte[] WebPWithMetadata()
    {
        var chunks = new List<byte>();
        Chunk(chunks, "VP8X", [0x2C, 0, 0, 0, 0x0F, 0, 0, 0x0F, 0, 0]);
        Chunk(chunks, "ICCP", System.Text.Encoding.ASCII.GetBytes("icc-profile-of-the-phone"));
        Chunk(chunks, "VP8 ", ScanData);
        Chunk(chunks, "EXIF", System.Text.Encoding.ASCII.GetBytes(ExifSecret));
        Chunk(chunks, "XMP ", System.Text.Encoding.ASCII.GetBytes(XmpSecret));
        var webp = new List<byte>();
        webp.AddRange("RIFF"u8.ToArray());
        webp.AddRange(BitConverter.GetBytes((uint)(4 + chunks.Count)));
        webp.AddRange("WEBP"u8.ToArray());
        webp.AddRange(chunks);
        return webp.ToArray();
    }

    public static byte[] Png()
    {
        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    }

    public static bool Contains(byte[] haystack, string text)
    {
        var needle = System.Text.Encoding.ASCII.GetBytes(text);
        return haystack.AsSpan().IndexOf(needle) >= 0;
    }

    private static void Segment(List<byte> bytes, byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        bytes.AddRange([0xFF, marker, (byte)(length >> 8), (byte)length]);
        bytes.AddRange(payload);
    }

    private static void Chunk(List<byte> bytes, string fourCc, byte[] payload)
    {
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(fourCc));
        bytes.AddRange(BitConverter.GetBytes((uint)payload.Length));
        bytes.AddRange(payload);
        if (payload.Length % 2 == 1) bytes.Add(0);
    }
}

/// <summary>IN-7. The temporary photo analyses in memory, saved by their own unit of work.</summary>
public sealed class InMemoryMealPhotoAnalyses : IMealPhotoAnalysisRepository, IUnitOfWork
{
    private readonly List<MealPhotoAnalysis> _pending = [];
    private readonly List<MealPhotoAnalysis> _stored = [];

    public IReadOnlyList<MealPhotoAnalysis> Stored
    {
        get
        {
            lock (_stored) return _stored.ToList();
        }
    }

    public void Seed(MealPhotoAnalysis analysis)
    {
        lock (_stored) _stored.Add(analysis);
    }

    public Task AddAsync(MealPhotoAnalysis entity, CancellationToken cancellationToken = default)
    {
        lock (_pending) _pending.Add(entity);
        return Task.CompletedTask;
    }

    public Task<MealPhotoAnalysis?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<MealPhotoAnalysis?>(null);
    }

    public Task<MealPhotoAnalysis?> FindByIdAsync(Guid analysisId, CancellationToken cancellationToken = default)
    {
        lock (_stored) return Task.FromResult(_stored.FirstOrDefault(a => a.Id == analysisId));
    }

    public void Update(MealPhotoAnalysis entity)
    {
    }

    public void Remove(MealPhotoAnalysis entity)
    {
        lock (_stored) _stored.Remove(entity);
    }

    public Task<IEnumerable<MealPhotoAnalysis>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_stored) return Task.FromResult<IEnumerable<MealPhotoAnalysis>>(_stored.ToList());
    }

    public Task<int> DeleteByPatientIdAsync(int patientId, CancellationToken cancellationToken = default)
    {
        lock (_stored) return Task.FromResult(_stored.RemoveAll(a => a.PatientId == patientId));
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_stored) return Task.FromResult(_stored.RemoveAll(a => a.ExpiresAt <= now));
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        lock (_pending)
        lock (_stored)
        {
            _stored.AddRange(_pending);
            _pending.Clear();
        }

        return Task.CompletedTask;
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        return work(cancellationToken);
    }
}
