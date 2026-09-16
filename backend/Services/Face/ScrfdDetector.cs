using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Tiaano.Vms.Api.Services.Face;

/// <summary>A face found by the detector, in original image coordinates.</summary>
public sealed record DetectedFace(
    float Score,
    float X1, float Y1, float X2, float Y2,
    float[] Landmarks)
{
    public float Width => X2 - X1;
    public float Height => Y2 - Y1;
    public float Area => Math.Max(0, Width) * Math.Max(0, Height);
}

/// <summary>
/// SCRFD face detector (InsightFace <c>det_10g</c>), ported to ONNX Runtime.
/// </summary>
/// <remarks>
/// The network emits raw per-anchor distances rather than boxes, so the decoding here has to match
/// the reference implementation exactly or the landmarks drift and every embedding downstream is
/// subtly wrong. Three FPN levels at strides 8/16/32, two anchors per cell, and outputs ordered
/// scores-then-boxes-then-keypoints.
/// </remarks>
public sealed class ScrfdDetector : IDisposable
{
    private const int Strides = 3;
    private const int AnchorsPerCell = 2;
    private static readonly int[] FeatureStrides = [8, 16, 32];

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string[] _outputNames;
    private readonly int _inputSize;
    private readonly float _scoreThreshold;
    private readonly float _nmsThreshold;

    public ScrfdDetector(string modelPath, int inputSize = 640, float scoreThreshold = 0.5f, float nmsThreshold = 0.4f)
    {
        _session = new InferenceSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
        _outputNames = _session.OutputMetadata.Keys.ToArray();
        _inputSize = inputSize;
        _scoreThreshold = scoreThreshold;
        _nmsThreshold = nmsThreshold;

        if (_outputNames.Length != Strides * 3)
            throw new InvalidOperationException(
                $"Expected {Strides * 3} SCRFD outputs but the model declares {_outputNames.Length}.");
    }

    /// <summary>
    /// How much to pad by when a first pass finds nothing. A face filling the whole frame — someone
    /// leaning into a kiosk camera — falls outside the scale range SCRFD was trained on, and padding
    /// restores the ratio the network expects without resampling the face itself.
    /// </summary>
    private const float RetryPaddingFactor = 1.6f;

    public IReadOnlyList<DetectedFace> Detect(RgbImage image)
    {
        var faces = DetectOnce(image);
        if (faces.Count > 0) return faces;

        var (padded, offsetX, offsetY) = image.PadCentered(RetryPaddingFactor);
        if (offsetX == 0 && offsetY == 0) return faces;

        return DetectOnce(padded).Select(f => Shift(f, -offsetX, -offsetY)).ToList();
    }

    private static DetectedFace Shift(DetectedFace face, float dx, float dy)
    {
        var moved = new float[face.Landmarks.Length];
        for (var i = 0; i < moved.Length; i += 2)
        {
            moved[i] = face.Landmarks[i] + dx;
            moved[i + 1] = face.Landmarks[i + 1] + dy;
        }
        return face with
        {
            X1 = face.X1 + dx,
            Y1 = face.Y1 + dy,
            X2 = face.X2 + dx,
            Y2 = face.Y2 + dy,
            Landmarks = moved
        };
    }

    private IReadOnlyList<DetectedFace> DetectOnce(RgbImage image)
    {
        var (canvas, scale) = image.LetterboxTopLeft(_inputSize, _inputSize);
        var input = ToTensor(canvas);

        using var results = _session.Run(
            [NamedOnnxValue.CreateFromTensor(_inputName, input)]);

        var outputs = results.ToDictionary(r => r.Name, r => r.AsTensor<float>());

        var boxes = new List<(float X1, float Y1, float X2, float Y2)>();
        var landmarks = new List<float[]>();
        var scores = new List<float>();

        for (var level = 0; level < Strides; level++)
        {
            var stride = FeatureStrides[level];
            var score = outputs[_outputNames[level]];
            var boxDeltas = outputs[_outputNames[level + Strides]];
            var kpsDeltas = outputs[_outputNames[level + Strides * 2]];

            var centers = FaceGeometry.AnchorCenters(_inputSize, _inputSize, stride, AnchorsPerCell);
            var count = score.Dimensions[0];

            for (var i = 0; i < count; i++)
            {
                if (score[i, 0] < _scoreThreshold) continue;

                var cx = centers[i * 2];
                var cy = centers[i * 2 + 1];

                var box = FaceGeometry.DistanceToBox(
                    cx, cy,
                    boxDeltas[i, 0] * stride,
                    boxDeltas[i, 1] * stride,
                    boxDeltas[i, 2] * stride,
                    boxDeltas[i, 3] * stride);

                // Keypoint deltas are offsets from the same anchor centre, five x,y pairs.
                var points = new float[10];
                for (var p = 0; p < 5; p++)
                {
                    points[p * 2] = cx + kpsDeltas[i, p * 2] * stride;
                    points[p * 2 + 1] = cy + kpsDeltas[i, p * 2 + 1] * stride;
                }

                boxes.Add(box);
                landmarks.Add(points);
                scores.Add(score[i, 0]);
            }
        }

        if (boxes.Count == 0) return [];

        var kept = FaceGeometry.NonMaxSuppression(boxes, scores, _nmsThreshold);

        // Undo the letterbox scale so callers get coordinates in the original image.
        var faces = new List<DetectedFace>(kept.Count);
        foreach (var index in kept)
        {
            var box = boxes[index];
            var points = landmarks[index];
            var scaled = new float[10];
            for (var p = 0; p < 10; p++) scaled[p] = points[p] / scale;

            faces.Add(new DetectedFace(
                scores[index],
                box.X1 / scale, box.Y1 / scale, box.X2 / scale, box.Y2 / scale,
                scaled));
        }

        return faces;
    }

    /// <summary>NCHW float tensor, RGB, scaled to (value - 127.5) / 128 as the network expects.</summary>
    private DenseTensor<float> ToTensor(RgbImage image)
    {
        var tensor = new DenseTensor<float>([1, 3, image.Height, image.Width]);
        var plane = image.Height * image.Width;
        var buffer = tensor.Buffer.Span;

        for (var i = 0; i < plane; i++)
        {
            buffer[i] = (image.Pixels[i * 3] - 127.5f) / 128f;
            buffer[plane + i] = (image.Pixels[i * 3 + 1] - 127.5f) / 128f;
            buffer[plane * 2 + i] = (image.Pixels[i * 3 + 2] - 127.5f) / 128f;
        }

        return tensor;
    }

    public void Dispose() => _session.Dispose();
}
