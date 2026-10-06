using System.Security.Cryptography;
using SkiaSharp;
using Tiaano.Vms.Api.Services.Face;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Deterministic stand-in for the ArcFace pipeline used by the HTTP-level tests.
/// </summary>
/// <remarks>
/// Face-count convention for synthetic JPEGs (no real biometrics):
/// width ≤ 40 → 0 faces, ≤ 80 → 1 face, ≤ 120 → 2 faces, else → 3+ faces.
/// Recognition quality itself is covered by ArcFacePipelineTests against the real weights.
/// </remarks>
internal sealed class StubFaceEmbeddingService : IFaceEmbeddingService
{
    public bool IsAvailable => true;

    public bool IsDetectionAvailable => true;

    public string ModelId => "test-stub-512-skia";

    public int Dimensions => 512;

    public double MatchThreshold => 0.6;

    public double Similarity(float[] a, float[] b) => FaceGeometry.CosineSimilarity(a, b);

    public int CountFaces(byte[] imageBytes)
    {
        if (imageBytes.Length < 12)
            return 0;

        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null) return 0;
        return FaceCountForWidth(bitmap.Width);
    }

    public void EnsureExactlyOneFace(byte[] imageBytes)
    {
        var count = CountFaces(imageBytes);
        if (count == 1) return;
        throw new InvalidOperationException(FacePhotoRules.MessageForCount(count));
    }

    public FaceEmbedding Embed(byte[] imageBytes)
    {
        EnsureExactlyOneFace(imageBytes);
        var vector = VectorFor(imageBytes);
        return new FaceEmbedding(vector, 0.99f, 1, 200f);
    }

    internal static int FaceCountForWidth(int width) => width switch
    {
        <= 40 => 0,
        <= 80 => 1,
        <= 120 => 2,
        _ => 3
    };

    private float[] VectorFor(byte[] imageBytes)
    {
        // Hash decoded pixels (not compressed bytes) so JPEG encoder swaps do not change identity.
        using var bitmap = SKBitmap.Decode(imageBytes)
            ?? throw new InvalidOperationException("Stub photo could not be decoded.");
        var pixels = bitmap.Pixels;
        var payload = new byte[8 + pixels.Length * 4];
        BitConverter.TryWriteBytes(payload.AsSpan(0, 4), bitmap.Width);
        BitConverter.TryWriteBytes(payload.AsSpan(4, 4), bitmap.Height);
        for (var i = 0; i < pixels.Length; i++)
            BitConverter.TryWriteBytes(payload.AsSpan(8 + i * 4, 4), (int)(uint)pixels[i]);

        var seed = BitConverter.ToInt32(SHA256.HashData(payload), 0);
        var rng = new Random(seed);
        var vector = new float[Dimensions];
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(rng.NextDouble() * 2 - 1);
        FaceGeometry.L2Normalize(vector);
        return vector;
    }
}

/// <summary>Builds small, valid JPEGs so tests can stand in for distinct people / face counts.</summary>
internal static class TestPhotos
{
    /// <summary>
    /// A solid-colour JPEG unique to <paramref name="identity"/> (64×64 → stub face count 1).
    /// </summary>
    public static string Base64(string identity) => Base64WithSize(identity, 64, 64);

    public static string NoFaceBase64() => SolidJpeg(32, 32, new SKColor(40, 40, 40));

    public static string TwoFacesBase64() => SolidJpeg(100, 64, new SKColor(180, 90, 40));

    public static string ThreeFacesBase64() => SolidJpeg(160, 64, new SKColor(40, 120, 180));

    public static byte[] Bytes(string identity)
    {
        var raw = Base64(identity);
        return Convert.FromBase64String(raw);
    }

    private static string Base64WithSize(string identity, int width, int height)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity));
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(new SKColor(hash[0], hash[1], hash[2]));

        for (var x = 0; x < Math.Min(32, width); x++)
            bitmap.SetPixel(x, 0, new SKColor(hash[(x % 29) + 3], hash[(x % 23) + 5], hash[(x % 19) + 7]));

        return Convert.ToBase64String(EncodeJpeg(bitmap, quality: 95));
    }

    private static string SolidJpeg(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(color);
        return Convert.ToBase64String(EncodeJpeg(bitmap, quality: 95));
    }

    internal static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    internal static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static byte[] EncodeWebp(SKBitmap bitmap, int quality = 90)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
        return data.ToArray();
    }
}
