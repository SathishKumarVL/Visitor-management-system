using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Tiaano.Vms.Api.Services.Face;

/// <summary>
/// ArcFace embedding network (InsightFace <c>w600k_r50</c>) over ONNX Runtime.
/// </summary>
/// <remarks>
/// The model only accepts a 112x112 crop warped onto the canonical landmark template. Feeding it a
/// plain bounding-box crop produces a vector that looks reasonable but matches poorly, so alignment
/// is done here rather than left to callers.
/// </remarks>
public sealed class ArcFaceEmbedder : IDisposable
{
    public const int CropSize = 112;
    public const int Dimensions = 512;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    public ArcFaceEmbedder(string modelPath)
    {
        _session = new InferenceSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();
    }

    /// <summary>Warps the face onto the ArcFace template and returns a unit-length embedding.</summary>
    public float[] Embed(RgbImage image, float[] landmarks)
    {
        var transform = FaceGeometry.EstimateSimilarity(landmarks, FaceGeometry.ArcFaceTemplate);
        var aligned = image.WarpAffine(transform, CropSize, CropSize);
        return EmbedAligned(aligned);
    }

    /// <summary>Embeds a crop that has already been aligned to 112x112.</summary>
    public float[] EmbedAligned(RgbImage aligned)
    {
        if (aligned.Width != CropSize || aligned.Height != CropSize)
            throw new ArgumentException($"ArcFace expects a {CropSize}x{CropSize} crop.", nameof(aligned));

        var tensor = new DenseTensor<float>([1, 3, CropSize, CropSize]);
        var plane = CropSize * CropSize;
        var buffer = tensor.Buffer.Span;

        for (var i = 0; i < plane; i++)
        {
            buffer[i] = (aligned.Pixels[i * 3] - 127.5f) / 127.5f;
            buffer[plane + i] = (aligned.Pixels[i * 3 + 1] - 127.5f) / 127.5f;
            buffer[plane * 2 + i] = (aligned.Pixels[i * 3 + 2] - 127.5f) / 127.5f;
        }

        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        var output = results.First(r => r.Name == _outputName).AsTensor<float>();

        var embedding = new float[Dimensions];
        for (var i = 0; i < Dimensions; i++) embedding[i] = output[0, i];

        // The network emits an unnormalised vector; cosine comparison needs unit length.
        FaceGeometry.L2Normalize(embedding);
        return embedding;
    }

    public void Dispose() => _session.Dispose();
}
