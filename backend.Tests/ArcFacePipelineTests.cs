using Tiaano.Vms.Api.Services.Face;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Locates the ONNX weights and the synthetic face fixtures, and lets the model-backed tests skip
/// cleanly on machines where the weights were never fetched.
/// </summary>
internal static class FaceTestAssets
{
    public static string? ModelDirectory { get; } = FindModelDirectory();

    public static string AssetDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "TestAssets");

    public static bool ModelsPresent =>
        ModelDirectory is not null
        && File.Exists(Path.Combine(ModelDirectory, "det_10g.onnx"))
        && File.Exists(Path.Combine(ModelDirectory, "w600k_r50.onnx"));

    public static byte[] Image(string name) => File.ReadAllBytes(Path.Combine(AssetDirectory, name));

    public static ScrfdDetector Detector() =>
        new(Path.Combine(ModelDirectory!, "det_10g.onnx"));

    public static ArcFaceEmbedder Embedder() =>
        new(Path.Combine(ModelDirectory!, "w600k_r50.onnx"));

    private static string? FindModelDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "MlModels");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

/// <summary>A fact that skips when the face models have not been downloaded on this machine.</summary>
public sealed class RequiresFaceModelsFactAttribute : FactAttribute
{
    public RequiresFaceModelsFactAttribute()
    {
        if (!FaceTestAssets.ModelsPresent)
            Skip = "Face models are not present. Run scripts/fetch-face-models.ps1.";
    }
}

/// <summary>
/// The geometry that has to reproduce InsightFace exactly. These need no model weights, so they run
/// everywhere and are the first thing to check if recognition quality ever regresses.
/// </summary>
public class FaceGeometryTests
{
    [Fact]
    public void Similarity_Transform_Recovers_A_Known_Rotation_Scale_And_Translation()
    {
        // Build "observed" landmarks by pushing the template through a transform we choose, then check
        // the estimator recovers the mapping back onto the template.
        const double angle = 0.37;
        const float scale = 2.4f;
        const float shiftX = 130f, shiftY = -45f;

        var template = FaceGeometry.ArcFaceTemplate;
        var observed = new float[template.Length];
        for (var i = 0; i < template.Length / 2; i++)
        {
            var x = template[i * 2];
            var y = template[i * 2 + 1];
            observed[i * 2] = (float)(scale * (x * Math.Cos(angle) - y * Math.Sin(angle))) + shiftX;
            observed[i * 2 + 1] = (float)(scale * (x * Math.Sin(angle) + y * Math.Cos(angle))) + shiftY;
        }

        var transform = FaceGeometry.EstimateSimilarity(observed, template);

        for (var i = 0; i < template.Length / 2; i++)
        {
            var (x, y) = transform.Apply(observed[i * 2], observed[i * 2 + 1]);
            Assert.Equal(template[i * 2], x, 2);
            Assert.Equal(template[i * 2 + 1], y, 2);
        }
    }

    [Fact]
    public void Affine_Inverse_Round_Trips()
    {
        var transform = new AffineTransform(1.7f, -0.4f, 22f, 0.4f, 1.7f, -9f);
        var inverse = transform.Invert();

        var (x, y) = transform.Apply(31f, 57f);
        var (bx, by) = inverse.Apply(x, y);

        Assert.Equal(31f, bx, 3);
        Assert.Equal(57f, by, 3);
    }

    [Fact]
    public void Anchor_Centers_Follow_The_Scrfd_Row_Major_Two_Anchor_Layout()
    {
        var centers = FaceGeometry.AnchorCenters(640, 640, 32, 2);

        // 20x20 cells, two anchors each, x/y per anchor.
        Assert.Equal(20 * 20 * 2 * 2, centers.Length);

        // First cell is the origin, repeated once per anchor.
        Assert.Equal(0f, centers[0]);
        Assert.Equal(0f, centers[1]);
        Assert.Equal(0f, centers[2]);
        Assert.Equal(0f, centers[3]);

        // Second cell steps along x by one stride, still on row 0.
        Assert.Equal(32f, centers[4]);
        Assert.Equal(0f, centers[5]);

        // The 21st cell wraps to the second row.
        Assert.Equal(0f, centers[20 * 2 * 2]);
        Assert.Equal(32f, centers[20 * 2 * 2 + 1]);
    }

    [Fact]
    public void NonMaxSuppression_Keeps_The_Best_Box_And_Drops_Its_Overlaps()
    {
        var boxes = new List<(float, float, float, float)>
        {
            (10, 10, 110, 110),   // weaker, heavily overlapping the winner
            (12, 12, 112, 112),   // the winner
            (400, 400, 500, 500)  // disjoint, must survive
        };
        var scores = new List<float> { 0.7f, 0.9f, 0.8f };

        var kept = FaceGeometry.NonMaxSuppression(boxes, scores, 0.4f);

        Assert.Equal([1, 2], kept.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void L2Normalize_Makes_Self_Similarity_Exactly_One()
    {
        var v = new float[512];
        var rng = new Random(11);
        for (var i = 0; i < v.Length; i++) v[i] = (float)(rng.NextDouble() * 2 - 1);

        FaceGeometry.L2Normalize(v);

        Assert.Equal(1.0, FaceGeometry.CosineSimilarity(v, v), 5);
    }
}

/// <summary>
/// End-to-end checks against the real SCRFD and ArcFace weights, using synthetic StyleGAN2 portraits
/// so no real person's biometric data lives in the repository.
/// </summary>
public class ArcFacePipelineTests
{
    [RequiresFaceModelsFact]
    public void Detects_A_Single_Face_With_Plausible_Geometry()
    {
        using var detector = FaceTestAssets.Detector();
        var image = RgbImage.Decode(FaceTestAssets.Image("face-a.jpg"));

        var faces = detector.Detect(image);

        var face = Assert.Single(faces);
        Assert.True(face.Score > 0.7f, $"Detection score was only {face.Score}.");

        // A head-and-shoulders portrait: the face should occupy a large, roughly square central region.
        Assert.InRange(face.Width / image.Width, 0.2f, 0.95f);
        Assert.InRange(face.Height / face.Width, 0.8f, 1.8f);

        // Eyes above nose above mouth is the ordering the ArcFace template assumes.
        var leftEyeY = face.Landmarks[1];
        var noseY = face.Landmarks[5];
        var mouthY = face.Landmarks[7];
        Assert.True(leftEyeY < noseY && noseY < mouthY,
            "Landmarks are not in the expected eye/nose/mouth order.");
    }

    [RequiresFaceModelsFact]
    public void Alignment_Puts_Detected_Landmarks_Onto_The_Template()
    {
        using var detector = FaceTestAssets.Detector();
        var image = RgbImage.Decode(FaceTestAssets.Image("face-a.jpg"));
        var face = detector.Detect(image).OrderByDescending(f => f.Area).First();

        var transform = FaceGeometry.EstimateSimilarity(face.Landmarks, FaceGeometry.ArcFaceTemplate);

        // A similarity transform cannot absorb facial asymmetry, so residuals are expected — but if the
        // implementation were wrong they would be tens of pixels, not a few.
        for (var i = 0; i < 5; i++)
        {
            var (x, y) = transform.Apply(face.Landmarks[i * 2], face.Landmarks[i * 2 + 1]);
            var dx = x - FaceGeometry.ArcFaceTemplate[i * 2];
            var dy = y - FaceGeometry.ArcFaceTemplate[i * 2 + 1];
            var residual = Math.Sqrt(dx * dx + dy * dy);
            Assert.True(residual < 8.0, $"Landmark {i} landed {residual:F2}px from the template.");
        }
    }

    [RequiresFaceModelsFact]
    public void Embedding_Is_512_Dimensions_And_Unit_Length()
    {
        var embedding = Embed("face-a.jpg");

        Assert.Equal(512, embedding.Length);

        double norm = 0;
        foreach (var v in embedding) norm += (double)v * v;
        Assert.Equal(1.0, Math.Sqrt(norm), 4);
    }

    [RequiresFaceModelsFact]
    public void Same_Photo_Embeds_Identically()
    {
        var first = Embed("face-a.jpg");
        var second = Embed("face-a.jpg");

        Assert.Equal(1.0, FaceGeometry.CosineSimilarity(first, second), 5);
    }

    /// <summary>The decisive test: two different people must not be confused at the shipped threshold.</summary>
    [RequiresFaceModelsFact]
    public void Different_People_Score_Far_Below_The_Match_Threshold()
    {
        var a = Embed("face-a.jpg");
        var b = Embed("face-b.jpg");

        var similarity = FaceGeometry.CosineSimilarity(a, b);

        Assert.True(similarity < new FaceRecognitionOptions().MatchThreshold,
            $"Two different faces scored {similarity:F3}, at or above the match threshold.");
    }

    /// <summary>
    /// Re-encoding degrades the image the way a camera and JPEG pipeline would. The embedding has to
    /// survive that, otherwise real captures of one person will never match each other.
    /// </summary>
    [RequiresFaceModelsFact]
    public void Embedding_Survives_Heavy_Recompression()
    {
        var original = Embed("face-a.jpg");
        var degraded = EmbedBytes(Recompress(FaceTestAssets.Image("face-a.jpg"), quality: 30));

        var similarity = FaceGeometry.CosineSimilarity(original, degraded);

        Assert.True(similarity > 0.9,
            $"The same face after recompression only scored {similarity:F3}.");
    }

    private static float[] Embed(string fileName) => EmbedBytes(FaceTestAssets.Image(fileName));

    private static float[] EmbedBytes(byte[] bytes)
    {
        using var detector = FaceTestAssets.Detector();
        using var embedder = FaceTestAssets.Embedder();
        var image = RgbImage.Decode(bytes);
        var face = detector.Detect(image).OrderByDescending(f => f.Area).First();
        return embedder.Embed(image, face.Landmarks);
    }

    private static byte[] Recompress(byte[] source, int quality)
    {
        using var image = SixLabors.ImageSharp.Image.Load(source);
        using var output = new MemoryStream();
        image.Save(output, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = quality });
        return output.ToArray();
    }
}
