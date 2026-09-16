namespace Tiaano.Vms.Api.Services.Face;

/// <summary>
/// A 2x3 affine transform laid out as it is in OpenCV:
/// <c>[ M11 M12 M13 ; M21 M22 M23 ]</c>.
/// </summary>
public readonly record struct AffineTransform(
    float M11, float M12, float M13,
    float M21, float M22, float M23)
{
    public static AffineTransform Identity => new(1, 0, 0, 0, 1, 0);

    public (float X, float Y) Apply(float x, float y) =>
        (M11 * x + M12 * y + M13, M21 * x + M22 * y + M23);

    /// <summary>
    /// Inverse transform. Warping samples backwards — for every destination pixel we need the source
    /// coordinate it came from — so the inverse is what the resampler actually uses.
    /// </summary>
    public AffineTransform Invert()
    {
        var det = M11 * M22 - M12 * M21;
        if (Math.Abs(det) < 1e-12f)
            throw new InvalidOperationException("Face alignment transform is singular.");

        var i11 = M22 / det;
        var i12 = -M12 / det;
        var i21 = -M21 / det;
        var i22 = M11 / det;
        return new AffineTransform(
            i11, i12, -(i11 * M13 + i12 * M23),
            i21, i22, -(i21 * M13 + i22 * M23));
    }
}

/// <summary>
/// Geometry shared by detection and alignment, deliberately free of ONNX and image types. These are
/// the parts that must reproduce InsightFace's behaviour exactly, so they are kept unit testable.
/// </summary>
public static class FaceGeometry
{
    /// <summary>
    /// Canonical ArcFace landmark positions for a 112x112 crop, in the detector's landmark order:
    /// left eye, right eye, nose tip, left mouth corner, right mouth corner. Every face must be warped
    /// onto these coordinates — embeddings from differently aligned crops are not comparable.
    /// </summary>
    public static readonly float[] ArcFaceTemplate =
    [
        38.2946f, 51.6963f,
        73.5318f, 51.5014f,
        56.0252f, 71.7366f,
        41.5493f, 92.3655f,
        70.7299f, 92.2041f
    ];

    /// <summary>
    /// Least-squares similarity transform (uniform scale, rotation, translation) mapping
    /// <paramref name="source"/> onto <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// This is the 2D closed form of Umeyama, which is what scikit-image's SimilarityTransform gives
    /// InsightFace. Reflection is not modelled: the detector always emits landmarks in the same order
    /// regardless of head pose, so a mirrored solution is never the correct one.
    /// Points are flattened x,y pairs.
    /// </remarks>
    public static AffineTransform EstimateSimilarity(ReadOnlySpan<float> source, ReadOnlySpan<float> destination)
    {
        if (source.Length != destination.Length || source.Length < 4 || source.Length % 2 != 0)
            throw new ArgumentException("Point sets must be equal-length flattened x,y pairs.", nameof(source));

        var n = source.Length / 2;
        double srcMeanX = 0, srcMeanY = 0, dstMeanX = 0, dstMeanY = 0;
        for (var i = 0; i < n; i++)
        {
            srcMeanX += source[2 * i];
            srcMeanY += source[2 * i + 1];
            dstMeanX += destination[2 * i];
            dstMeanY += destination[2 * i + 1];
        }
        srcMeanX /= n; srcMeanY /= n; dstMeanX /= n; dstMeanY /= n;

        double dot = 0, cross = 0, norm = 0;
        for (var i = 0; i < n; i++)
        {
            double ax = source[2 * i] - srcMeanX, ay = source[2 * i + 1] - srcMeanY;
            double bx = destination[2 * i] - dstMeanX, by = destination[2 * i + 1] - dstMeanY;
            dot += ax * bx + ay * by;
            cross += ax * by - ay * bx;
            norm += ax * ax + ay * ay;
        }

        if (norm < 1e-12)
            throw new InvalidOperationException("Landmarks are degenerate; cannot align face.");

        // Rotation and uniform scale collapse into [[a, -b], [b, a]].
        var a = dot / norm;
        var b = cross / norm;
        var tx = dstMeanX - (a * srcMeanX - b * srcMeanY);
        var ty = dstMeanY - (b * srcMeanX + a * srcMeanY);

        return new AffineTransform((float)a, (float)-b, (float)tx, (float)b, (float)a, (float)ty);
    }

    /// <summary>
    /// SCRFD anchor centres for one FPN level, as flattened x,y pairs. Row-major over the feature map
    /// with each cell repeated once per anchor, which is the ordering the network's outputs assume.
    /// </summary>
    public static float[] AnchorCenters(int inputHeight, int inputWidth, int stride, int anchorsPerCell)
    {
        var rows = inputHeight / stride;
        var cols = inputWidth / stride;
        var centers = new float[rows * cols * anchorsPerCell * 2];

        var k = 0;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                for (var a = 0; a < anchorsPerCell; a++)
                {
                    centers[k++] = c * stride;
                    centers[k++] = r * stride;
                }
            }
        }
        return centers;
    }

    /// <summary>Converts left/top/right/bottom distances from an anchor centre into a box.</summary>
    public static (float X1, float Y1, float X2, float Y2) DistanceToBox(
        float centerX, float centerY, float left, float top, float right, float bottom) =>
        (centerX - left, centerY - top, centerX + right, centerY + bottom);

    /// <summary>
    /// Greedy non-maximum suppression over boxes ordered by descending score.
    /// </summary>
    /// <remarks>
    /// The inclusive +1 on width and height mirrors InsightFace's implementation. It shifts the overlap
    /// ratio very slightly and is kept so results track the reference behaviour.
    /// </remarks>
    public static List<int> NonMaxSuppression(
        IReadOnlyList<(float X1, float Y1, float X2, float Y2)> boxes,
        IReadOnlyList<float> scores,
        float iouThreshold)
    {
        var order = Enumerable.Range(0, boxes.Count).ToList();
        order.Sort((l, r) => scores[r].CompareTo(scores[l]));

        var areas = new float[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
            areas[i] = (boxes[i].X2 - boxes[i].X1 + 1) * (boxes[i].Y2 - boxes[i].Y1 + 1);

        var kept = new List<int>();
        var suppressed = new bool[boxes.Count];

        foreach (var i in order)
        {
            if (suppressed[i]) continue;
            kept.Add(i);

            foreach (var j in order)
            {
                if (j == i || suppressed[j]) continue;

                var xx1 = Math.Max(boxes[i].X1, boxes[j].X1);
                var yy1 = Math.Max(boxes[i].Y1, boxes[j].Y1);
                var xx2 = Math.Min(boxes[i].X2, boxes[j].X2);
                var yy2 = Math.Min(boxes[i].Y2, boxes[j].Y2);

                var w = Math.Max(0f, xx2 - xx1 + 1);
                var h = Math.Max(0f, yy2 - yy1 + 1);
                var intersection = w * h;
                var union = areas[i] + areas[j] - intersection;
                if (union > 0 && intersection / union > iouThreshold)
                    suppressed[j] = true;
            }
        }

        return kept;
    }

    /// <summary>Cosine similarity for vectors that are already unit length.</summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return -1;
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += (double)a[i] * b[i];
        return sum;
    }

    /// <summary>Scales a vector to unit length in place so cosine similarity is a plain dot product.</summary>
    public static void L2Normalize(float[] values)
    {
        double sum = 0;
        foreach (var v in values) sum += (double)v * v;
        var norm = Math.Sqrt(sum);
        if (norm < 1e-12) return;
        for (var i = 0; i < values.Length; i++) values[i] = (float)(values[i] / norm);
    }
}
