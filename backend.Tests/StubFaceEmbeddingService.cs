using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
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

    public string ModelId => "test-stub-512";

    public int Dimensions => 512;

    public double MatchThreshold => 0.6;

    public double Similarity(float[] a, float[] b) => FaceGeometry.CosineSimilarity(a, b);

    public int CountFaces(byte[] imageBytes)
    {
        if (imageBytes.Length < 12)
            return 0;

        using var image = Image.Load(imageBytes);
        return FaceCountForWidth(image.Width);
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
        var seed = BitConverter.ToInt32(SHA256.HashData(imageBytes), 0);
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

    public static string NoFaceBase64() => SolidJpeg(32, 32, new Rgb24(40, 40, 40));

    public static string TwoFacesBase64() => SolidJpeg(100, 64, new Rgb24(180, 90, 40));

    public static string ThreeFacesBase64() => SolidJpeg(160, 64, new Rgb24(40, 120, 180));

    public static byte[] Bytes(string identity)
    {
        var raw = Base64(identity);
        return Convert.FromBase64String(raw);
    }

    private static string Base64WithSize(string identity, int width, int height)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity));
        using var image = new Image<Rgb24>(width, height, new Rgb24(hash[0], hash[1], hash[2]));

        for (var x = 0; x < Math.Min(32, width); x++)
            image[x, 0] = new Rgb24(hash[(x % 29) + 3], hash[(x % 23) + 5], hash[(x % 19) + 7]);

        using var buffer = new MemoryStream();
        image.Save(buffer, new JpegEncoder { Quality = 95 });
        return Convert.ToBase64String(buffer.ToArray());
    }

    private static string SolidJpeg(int width, int height, Rgb24 color)
    {
        using var image = new Image<Rgb24>(width, height, color);
        using var buffer = new MemoryStream();
        image.Save(buffer, new JpegEncoder { Quality = 95 });
        return Convert.ToBase64String(buffer.ToArray());
    }
}
