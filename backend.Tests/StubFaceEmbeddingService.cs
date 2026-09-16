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
/// The API tests are about tenant scoping, enrolment and visitor linkage, not about recognition
/// quality — and loading 180 MB of weights per test run to prove routing would be wasteful. This maps
/// image bytes to a stable pseudo-random unit vector, so identical photos match exactly and different
/// photos do not match at all. Recognition accuracy itself is covered by ArcFacePipelineTests against
/// the real weights.
/// </remarks>
internal sealed class StubFaceEmbeddingService : IFaceEmbeddingService
{
    public bool IsAvailable => true;

    public string ModelId => "test-stub-512";

    public int Dimensions => 512;

    public double MatchThreshold => 0.6;

    public double Similarity(float[] a, float[] b) => FaceGeometry.CosineSimilarity(a, b);

    public FaceEmbedding Embed(byte[] imageBytes)
    {
        if (imageBytes.Length < 12)
            throw new FaceEmbeddingException("No face was detected in the photo.");

        var vector = VectorFor(imageBytes);
        return new FaceEmbedding(vector, 0.99f, 1, 200f);
    }

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

/// <summary>Builds small, valid JPEGs so tests can stand in for distinct people.</summary>
internal static class TestPhotos
{
    /// <summary>
    /// A solid-colour JPEG unique to <paramref name="identity"/>. Distinct identities produce distinct
    /// bytes, which the stub embedder turns into distinct face templates.
    /// </summary>
    public static string Base64(string identity)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity));
        using var image = new Image<Rgb24>(64, 64, new Rgb24(hash[0], hash[1], hash[2]));

        // Vary a pixel run as well so two identities that collide on the first three bytes still differ.
        for (var x = 0; x < 32; x++)
            image[x, 0] = new Rgb24(hash[(x % 29) + 3], hash[(x % 23) + 5], hash[(x % 19) + 7]);

        using var buffer = new MemoryStream();
        image.Save(buffer, new JpegEncoder { Quality = 95 });
        return Convert.ToBase64String(buffer.ToArray());
    }
}
