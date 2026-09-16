using Microsoft.Extensions.Options;

namespace Tiaano.Vms.Api.Services.Face;

public sealed class FaceRecognitionOptions
{
    public const string SectionName = "FaceRecognition";

    /// <summary>Turns the whole pipeline off. Registration still works, it just enrols no face.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Directory holding det_10g.onnx and w600k_r50.onnx, relative to the content root.</summary>
    public string ModelDirectory { get; set; } = "MlModels";

    /// <summary>Square input the detector is letterboxed into. 640 is the reference configuration.</summary>
    public int DetectionSize { get; set; } = 640;

    public float DetectionScoreThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Smallest usable face, measured across the detected box in source pixels. Tiny faces align badly
    /// and produce embeddings that match almost anything.
    /// </summary>
    public int MinimumFacePixels { get; set; } = 60;

    /// <summary>
    /// Cosine similarity at or above which two embeddings are treated as the same person. ArcFace is
    /// commonly operated around 0.4; higher is stricter.
    /// </summary>
    public double MatchThreshold { get; set; } = 0.42;
}

/// <summary>A face template extracted from an uploaded image.</summary>
public sealed record FaceEmbedding(float[] Vector, float DetectionScore, int FacesFound, float FaceWidthPixels);

/// <summary>Raised when an image cannot yield a usable face template.</summary>
public sealed class FaceEmbeddingException(string message) : Exception(message);

/// <summary>
/// Turns an uploaded photo into a comparable face template.
/// </summary>
/// <remarks>
/// This is an interface so the recognition models are not a hard dependency of the API: tests
/// substitute a deterministic implementation, and deployments without the ONNX weights on disk fall
/// back to <see cref="IsAvailable"/> being false rather than failing to start.
/// </remarks>
public interface IFaceEmbeddingService
{
    bool IsAvailable { get; }

    /// <summary>Identifier stored alongside every template so generations are never cross-compared.</summary>
    string ModelId { get; }

    int Dimensions { get; }

    double MatchThreshold { get; }

    /// <summary>Embeds the most prominent face in the image.</summary>
    /// <exception cref="FaceEmbeddingException">No usable face was found.</exception>
    FaceEmbedding Embed(byte[] imageBytes);

    double Similarity(float[] a, float[] b);
}

/// <summary>
/// InsightFace pipeline: SCRFD detection, ArcFace alignment and embedding, both through ONNX Runtime.
/// </summary>
/// <remarks>
/// Registered as a singleton because the two sessions hold roughly 180 MB of weights and are
/// thread-safe for concurrent inference. Loading is deferred to first use so application start does
/// not pay for it, and so a deployment missing the weights degrades instead of crashing.
/// </remarks>
public sealed class InsightFaceService : IFaceEmbeddingService, IDisposable
{
    /// <summary>Stored with every template. Bump this if the model or the alignment ever changes.</summary>
    public const string ArcFaceModelId = "arcface-512";

    private readonly FaceRecognitionOptions _options;
    private readonly ILogger<InsightFaceService> _logger;
    private readonly string _detectorPath;
    private readonly string _embedderPath;
    private readonly Lock _gate = new();

    private ScrfdDetector? _detector;
    private ArcFaceEmbedder? _embedder;
    private bool _loadFailed;

    public InsightFaceService(
        IOptions<FaceRecognitionOptions> options,
        IWebHostEnvironment environment,
        ILogger<InsightFaceService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var directory = Path.IsPathRooted(_options.ModelDirectory)
            ? _options.ModelDirectory
            : Path.Combine(environment.ContentRootPath, _options.ModelDirectory);

        _detectorPath = Path.Combine(directory, "det_10g.onnx");
        _embedderPath = Path.Combine(directory, "w600k_r50.onnx");
    }

    public string ModelId => ArcFaceModelId;

    public int Dimensions => ArcFaceEmbedder.Dimensions;

    public double MatchThreshold => _options.MatchThreshold;

    public bool IsAvailable =>
        _options.Enabled && !_loadFailed && File.Exists(_detectorPath) && File.Exists(_embedderPath);

    public double Similarity(float[] a, float[] b) => FaceGeometry.CosineSimilarity(a, b);

    public FaceEmbedding Embed(byte[] imageBytes)
    {
        if (!IsAvailable)
            throw new FaceEmbeddingException("Face recognition is not available on this server.");

        var (detector, embedder) = Load();

        RgbImage image;
        try
        {
            image = RgbImage.Decode(imageBytes);
        }
        catch (Exception ex)
        {
            throw new FaceEmbeddingException($"The photo could not be read as an image: {ex.Message}");
        }

        var faces = detector.Detect(image);
        if (faces.Count == 0)
            throw new FaceEmbeddingException("No face was detected in the photo.");

        // The visitor at the desk is the closest subject, so the largest box is the right one even
        // when colleagues are caught in the background.
        var face = faces.OrderByDescending(f => f.Area).First();

        if (face.Width < _options.MinimumFacePixels || face.Height < _options.MinimumFacePixels)
            throw new FaceEmbeddingException(
                "The face is too small in the photo. Move closer to the camera and try again.");

        var vector = embedder.Embed(image, face.Landmarks);
        return new FaceEmbedding(vector, face.Score, faces.Count, face.Width);
    }

    private (ScrfdDetector Detector, ArcFaceEmbedder Embedder) Load()
    {
        if (_detector is not null && _embedder is not null) return (_detector, _embedder);

        lock (_gate)
        {
            if (_detector is null || _embedder is null)
            {
                try
                {
                    _detector = new ScrfdDetector(
                        _detectorPath, _options.DetectionSize, _options.DetectionScoreThreshold);
                    _embedder = new ArcFaceEmbedder(_embedderPath);
                    _logger.LogInformation(
                        "Face recognition models loaded from {Directory}",
                        Path.GetDirectoryName(_detectorPath));
                }
                catch (Exception ex)
                {
                    _loadFailed = true;
                    _logger.LogError(ex, "Failed to load face recognition models");
                    throw new FaceEmbeddingException("Face recognition models could not be loaded.");
                }
            }

            return (_detector, _embedder);
        }
    }

    public void Dispose()
    {
        _detector?.Dispose();
        _embedder?.Dispose();
    }
}
