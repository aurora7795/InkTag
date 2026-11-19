using System;
using System.IO;
using InkTag.Core.Images;
using SkiaSharp;
using Xunit;

namespace InkTag.Tests;

public class PerceptualHashTests
{
    [Fact]
    public void ComputeDHash_RejectsOversizedImage_WithoutCrashing()
    {
        // Construct a minimal valid GIF header declaring 10,000 x 10,000 dimensions (100 MP > 40 MP ceiling)
        // Header: "GIF89a" (6 bytes)
        // Canvas width: 10,000 (0x2710 -> 0x10, 0x27)
        // Canvas height: 10,000 (0x2710 -> 0x10, 0x27)
        // Packed byte: 0x80 (GCT flag, 1 bit per pixel)
        // Background: 0, Aspect: 0
        // Global Color Table: 2 RGB entries (6 bytes)
        // Image Descriptor: 0x2C, Left=0, Top=0, Width=10,000, Height=10,000, Packed=0
        // LZW Min Code Size: 2, Block terminator: 0, Trailer: 0x3B
        byte[] oversizedGif = new byte[]
        {
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a
            0x10, 0x27,                         // Width: 10,000
            0x10, 0x27,                         // Height: 10,000
            0x80, 0x00, 0x00,                   // Packed, BG, Aspect
            0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, // 2 colors (black, white)
            0x2C,                               // Image separator
            0x00, 0x00, 0x00, 0x00,             // Left=0, Top=0
            0x10, 0x27,                         // Width: 10,000
            0x10, 0x27,                         // Height: 10,000
            0x00,                               // Packed
            0x02, 0x00,                         // LZW min code size, empty block
            0x3B                                // Trailer
        };

        ulong hash = PerceptualHashService.ComputeDHash(oversizedGif);

        // Must reject safely and return 0 without allocating a 100 MP bitmap
        Assert.Equal(0UL, hash);
    }

    [Fact]
    public void ComputeDHash_HandlesNullEmptyAndCorruptStreamsGracefully()
    {
        Assert.Equal(0UL, PerceptualHashService.ComputeDHash((byte[])null!));
        Assert.Equal(0UL, PerceptualHashService.ComputeDHash(Array.Empty<byte>()));
        Assert.Equal(0UL, PerceptualHashService.ComputeDHash((Stream)null!));
        Assert.Equal(0UL, PerceptualHashService.ComputeDHash(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 })));
    }

    [Fact]
    public void ComputeDHash_ValidImage_ComputesAccurateDHash()
    {
        using var bitmap = new SKBitmap(64, 96);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(32, 0, 32, 96, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        byte[] bytes = data.ToArray();

        ulong hash = PerceptualHashService.ComputeDHash(bytes);
        Assert.NotEqual(0UL, hash);
    }
}
