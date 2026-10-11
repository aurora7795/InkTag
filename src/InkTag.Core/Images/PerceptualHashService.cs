using System;
using System.IO;
using System.Numerics;
using SkiaSharp;

namespace InkTag.Core.Images;

/// <summary>
/// Provides fast, hardware-accelerated perceptual image hashing (dHash) and Hamming distance visual comparison for comic covers.
/// </summary>
public static class PerceptualHashService
{
    private const int HashWidth = 9;
    private const int HashHeight = 8;

    /// <summary>
    /// Computes a 64-bit difference hash (dHash) from an image byte array.
    /// </summary>
    public static ulong ComputeDHash(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0) return 0;
        using var ms = new MemoryStream(imageBytes);
        return ComputeDHash(ms);
    }

    /// <summary>
    /// Computes a 64-bit difference hash (dHash) from an image stream.
    /// Reduces the image to a 9x8 grayscale grid (area-averaged) and computes horizontal luminance gradients.
    /// </summary>
    public static ulong ComputeDHash(Stream imageStream)
    {
        if (imageStream == null || !imageStream.CanRead) return 0;

        try
        {
            using var bitmap = SKBitmap.Decode(imageStream);
            if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0) return 0;

            var luma = ToLuma(bitmap);
            var cells = AreaAverage(luma, bitmap.Width, bitmap.Height);

            ulong hash = 0UL;
            int bitPosition = 0;

            for (int y = 0; y < HashHeight; y++)
            {
                for (int x = 0; x < HashWidth - 1; x++)
                {
                    if (cells[y * HashWidth + x] > cells[y * HashWidth + x + 1])
                    {
                        hash |= (1UL << bitPosition);
                    }
                    bitPosition++;
                }
            }

            return hash;
        }
        catch
        {
            // If image decoding fails, return 0
            return 0;
        }
    }

    /// <summary>
    /// Converts a decoded bitmap to 8-bit luminance using Rec.709 weights.
    /// </summary>
    private static byte[] ToLuma(SKBitmap source)
    {
        // Normalise to a known 4-byte layout; decoders usually already return one of these two.
        bool isBgra = source.ColorType == SKColorType.Bgra8888;
        bool isRgba = source.ColorType == SKColorType.Rgba8888;
        using var converted = isBgra || isRgba ? null : source.Copy(SKColorType.Rgba8888);
        var bmp = converted ?? source;
        int rIndex = isBgra ? 2 : 0;
        int bIndex = isBgra ? 0 : 2;

        var pixels = bmp.GetPixelSpan();
        var luma = new byte[bmp.Width * bmp.Height];
        for (int i = 0, p = 0; i < luma.Length; i++, p += 4)
        {
            // Rec.709 weights (0.2126 / 0.7152 / 0.0722) as 8-bit fixed point, summing to 256.
            luma[i] = (byte)((54 * pixels[p + rIndex] + 183 * pixels[p + 1] + 19 * pixels[p + bIndex]) >> 8);
        }
        return luma;
    }

    /// <summary>
    /// Downsamples luminance to a 9x8 grid by exact area averaging (a box filter that weights partial pixels),
    /// which is deterministic and free of the aliasing that point/cubic sampling shows at large reductions.
    /// </summary>
    private static byte[] AreaAverage(byte[] luma, int width, int height)
    {
        var cells = new byte[HashWidth * HashHeight];
        for (int cy = 0; cy < HashHeight; cy++)
        {
            double y0 = (double)cy * height / HashHeight;
            double y1 = (double)(cy + 1) * height / HashHeight;
            for (int cx = 0; cx < HashWidth; cx++)
            {
                double x0 = (double)cx * width / HashWidth;
                double x1 = (double)(cx + 1) * width / HashWidth;
                double sum = 0, area = 0;
                for (int yy = (int)y0; yy < Math.Ceiling(y1) && yy < height; yy++)
                {
                    double wy = Math.Min(yy + 1, y1) - Math.Max(yy, y0);
                    for (int xx = (int)x0; xx < Math.Ceiling(x1) && xx < width; xx++)
                    {
                        double wx = Math.Min(xx + 1, x1) - Math.Max(xx, x0);
                        sum += luma[yy * width + xx] * wx * wy;
                        area += wx * wy;
                    }
                }
                cells[cy * HashWidth + cx] = (byte)Math.Round(sum / area);
            }
        }
        return cells;
    }

    /// <summary>
    /// Computes the Hamming distance (number of differing bits) between two 64-bit hashes using hardware-accelerated POPCNT.
    /// </summary>
    public static int ComputeHammingDistance(ulong hashA, ulong hashB)
    {
        return BitOperations.PopCount(hashA ^ hashB);
    }

    /// <summary>
    /// Computes visual similarity between two hashes on a scale of 0.0 (completely different) to 1.0 (identical).
    /// </summary>
    public static double CalculateSimilarity(ulong hashA, ulong hashB)
    {
        if (hashA == 0 || hashB == 0) return 0.0;
        int distance = ComputeHammingDistance(hashA, hashB);
        return Math.Max(0.0, 1.0 - ((double)distance / 64.0));
    }

    /// <summary>
    /// Determines if two cover hashes match within the specified similarity threshold (default 90% / distance &lt;= 6).
    /// </summary>
    public static bool IsVisualMatch(ulong hashA, ulong hashB, double threshold = 0.90)
    {
        return CalculateSimilarity(hashA, hashB) >= threshold;
    }
}
