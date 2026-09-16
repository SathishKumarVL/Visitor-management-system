using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Tiaano.Vms.Api.Services.Face;

/// <summary>
/// A decoded image as tightly packed interleaved RGB bytes.
/// </summary>
/// <remarks>
/// Detection and alignment work on this rather than on ImageSharp types so the pixel maths stays
/// testable without an imaging library, and so there is one explicit place where channel order is
/// decided. Channel order matters: the ONNX models were trained on RGB input.
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

    public static RgbImage Decode(byte[] encoded)
    {
        using var image = Image.Load<Rgb24>(encoded);
        return FromImageSharp(image);
    }

    /// <summary>
    /// Resizes into the top-left of a black canvas of the requested size, preserving aspect ratio.
    /// Returns the scale that was applied so detections can be mapped back to original coordinates.
    /// </summary>
    /// <remarks>
    /// This letterboxing reproduces what InsightFace does before SCRFD. A triangle filter is used
    /// because it is the closest match to OpenCV's default bilinear resize.
    /// </remarks>
    public (RgbImage Canvas, float Scale) LetterboxTopLeft(int targetWidth, int targetHeight)
    {
        var scale = Math.Min((float)targetWidth / Width, (float)targetHeight / Height);
        var newWidth = Math.Max(1, (int)(Width * scale));
        var newHeight = Math.Max(1, (int)(Height * scale));

        using var image = ToImageSharp();
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(newWidth, newHeight),
            Sampler = KnownResamplers.Triangle,
            Mode = ResizeMode.Stretch
        }));

        var canvas = new RgbImage(targetWidth, targetHeight);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var offset = y * targetWidth * 3;
                for (var x = 0; x < row.Length; x++)
                {
                    canvas.Pixels[offset + x * 3] = row[x].R;
                    canvas.Pixels[offset + x * 3 + 1] = row[x].G;
                    canvas.Pixels[offset + x * 3 + 2] = row[x].B;
                }
            }
        });

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

    private Image<Rgb24> ToImageSharp()
    {
        var image = new Image<Rgb24>(Width, Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var offset = y * Width * 3;
                for (var x = 0; x < Width; x++)
                    row[x] = new Rgb24(
                        Pixels[offset + x * 3],
                        Pixels[offset + x * 3 + 1],
                        Pixels[offset + x * 3 + 2]);
            }
        });
        return image;
    }

    private static RgbImage FromImageSharp(Image<Rgb24> image)
    {
        var result = new RgbImage(image.Width, image.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var offset = y * image.Width * 3;
                for (var x = 0; x < row.Length; x++)
                {
                    result.Pixels[offset + x * 3] = row[x].R;
                    result.Pixels[offset + x * 3 + 1] = row[x].G;
                    result.Pixels[offset + x * 3 + 2] = row[x].B;
                }
            }
        });
        return result;
    }
}
