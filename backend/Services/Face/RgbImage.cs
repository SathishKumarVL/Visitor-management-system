using SkiaSharp;

namespace Tiaano.Vms.Api.Services.Face;

/// <summary>
/// A decoded image as tightly packed interleaved RGB bytes.
/// </summary>
/// <remarks>
/// Detection and alignment work on this rather than on SkiaSharp types so the pixel maths stays
/// testable without an imaging library, and so there is one explicit place where channel order is
/// decided. Channel order matters: the ONNX models were trained on RGB input.
/// Decode uses SkiaSharp (MIT). Letterbox resize uses the same bilinear sampler as
/// <see cref="WarpAffine"/> so SCRFD preprocess stays consistent with ArcFace alignment maths.
/// </remarks>
public sealed class RgbImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Interleaved R,G,B bytes, row-major, no row padding.</summary>
    public byte[] Pixels { get; }

    public RgbImage(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height * 3)
            throw new ArgumentException("Pixel buffer does not match the stated dimensions.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public RgbImage(int width, int height) : this(width, height, new byte[width * height * 3]) { }

    /// <summary>
    /// Decodes JPEG / PNG / WebP (and other formats SkiaSharp supports) into tightly packed RGB.
    /// </summary>
    public static RgbImage Decode(byte[] encoded)
    {
        if (encoded is null || encoded.Length == 0)
            throw new InvalidOperationException("The photo could not be read as an image.");

        try
        {
            using var bitmap = SKBitmap.Decode(encoded)
                ?? throw new InvalidOperationException("The photo could not be read as an image.");
            return FromSkia(bitmap);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"The photo could not be read as an image: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Resizes into the top-left of a black canvas of the requested size, preserving aspect ratio.
    /// Returns the scale that was applied so detections can be mapped back to original coordinates.
    /// </summary>
    /// <remarks>
    /// This letterboxing reproduces what InsightFace does before SCRFD. Bilinear sampling matches
    /// OpenCV's default INTER_LINEAR resize (previously approximated via ImageSharp Triangle).
    /// </remarks>
    public (RgbImage Canvas, float Scale) LetterboxTopLeft(int targetWidth, int targetHeight)
    {
        var scale = Math.Min((float)targetWidth / Width, (float)targetHeight / Height);
        var newWidth = Math.Max(1, (int)(Width * scale));
        var newHeight = Math.Max(1, (int)(Height * scale));

        var resized = ResizeBilinear(newWidth, newHeight);
        var canvas = new RgbImage(targetWidth, targetHeight);
        for (var y = 0; y < newHeight; y++)
        {
            Array.Copy(
                resized.Pixels,
                y * newWidth * 3,
                canvas.Pixels,
                y * targetWidth * 3,
                newWidth * 3);
        }

        return (canvas, scale);
    }

    /// <summary>
    /// Centres this image on a larger black canvas, returning the offset it was placed at.
    /// </summary>
    /// <remarks>
    /// Used to bring an oversized face back into the detector's scale range. Padding rather than
    /// downscaling keeps the face pixels untouched, so landmark precision is unaffected.
    /// </remarks>
    public (RgbImage Canvas, int OffsetX, int OffsetY) PadCentered(float factor)
    {
        if (factor <= 1f) return (this, 0, 0);

        var width = (int)(Width * factor);
        var height = (int)(Height * factor);
        var canvas = new RgbImage(width, height);
        var offsetX = (width - Width) / 2;
        var offsetY = (height - Height) / 2;

        for (var y = 0; y < Height; y++)
        {
            var from = y * Width * 3;
            var to = ((y + offsetY) * width + offsetX) * 3;
            Array.Copy(Pixels, from, canvas.Pixels, to, Width * 3);
        }

        return (canvas, offsetX, offsetY);
    }

    /// <summary>
    /// Applies an affine transform into a new image of the given size, sampling bilinearly with a
    /// black border. Equivalent to OpenCV's warpAffine with INTER_LINEAR and BORDER_CONSTANT.
    /// </summary>
    public RgbImage WarpAffine(AffineTransform transform, int outputWidth, int outputHeight)
    {
        var inverse = transform.Invert();
        var result = new RgbImage(outputWidth, outputHeight);

        for (var y = 0; y < outputHeight; y++)
        {
            for (var x = 0; x < outputWidth; x++)
            {
                var (sx, sy) = inverse.Apply(x, y);
                var target = (y * outputWidth + x) * 3;
                SampleBilinear(sx, sy, result.Pixels, target);
            }
        }

        return result;
    }

    /// <summary>
    /// Stretch-resize with bilinear sampling. Destination pixel (x,y) maps to source
    /// (x * Width / newWidth, y * Height / newHeight), matching a direct OpenCV INTER_LINEAR stretch.
    /// </summary>
    private RgbImage ResizeBilinear(int newWidth, int newHeight)
    {
        var result = new RgbImage(newWidth, newHeight);
        var xScale = (float)Width / newWidth;
        var yScale = (float)Height / newHeight;

        for (var y = 0; y < newHeight; y++)
        {
            var sy = y * yScale;
            for (var x = 0; x < newWidth; x++)
            {
                var sx = x * xScale;
                SampleBilinear(sx, sy, result.Pixels, (y * newWidth + x) * 3);
            }
        }

        return result;
    }

    private void SampleBilinear(float x, float y, byte[] destination, int destinationOffset)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;

        for (var c = 0; c < 3; c++)
        {
            var v00 = PixelOrZero(x0, y0, c);
            var v10 = PixelOrZero(x0 + 1, y0, c);
            var v01 = PixelOrZero(x0, y0 + 1, c);
            var v11 = PixelOrZero(x0 + 1, y0 + 1, c);

            var top = v00 + (v10 - v00) * fx;
            var bottom = v01 + (v11 - v01) * fx;
            var value = top + (bottom - top) * fy;

            destination[destinationOffset + c] = (byte)Math.Clamp(MathF.Round(value), 0f, 255f);
        }
    }

    private float PixelOrZero(int x, int y, int channel)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return 0f;
        return Pixels[(y * Width + x) * 3 + channel];
    }

    private static RgbImage FromSkia(SKBitmap bitmap)
    {
        var result = new RgbImage(bitmap.Width, bitmap.Height);
        var colors = bitmap.Pixels;
        for (var i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            var o = i * 3;
            result.Pixels[o] = c.Red;
            result.Pixels[o + 1] = c.Green;
            result.Pixels[o + 2] = c.Blue;
        }

        return result;
    }
}
