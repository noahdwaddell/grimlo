using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace Grimlo.Services;

/// <summary>
/// Captures the desktop display, downscales to a responsive size,
/// and compresses to JPEG bytes for multimodal vision inspection.
/// </summary>
public static class ScreenCaptureService
{
    public static byte[] CaptureDesktopJpeg(int maxDimension = 1280, long quality = 75L)
    {
        // SystemParameters.VirtualScreen covers all monitors in a multi-monitor setup
        int left = (int)SystemParameters.VirtualScreenLeft;
        int top = (int)SystemParameters.VirtualScreenTop;
        int width = (int)SystemParameters.VirtualScreenWidth;
        int height = (int)SystemParameters.VirtualScreenHeight;

        // Fallback to primary screen bounds if virtual bounds are invalid
        if (width <= 0 || height <= 0)
        {
            left = 0;
            top = 0;
            width = (int)SystemParameters.PrimaryScreenWidth;
            height = (int)SystemParameters.PrimaryScreenHeight;
        }

        using var screenBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(screenBitmap))
        {
            g.CopyFromScreen(left, top, 0, 0, screenBitmap.Size, CopyPixelOperation.SourceCopy);
        }

        // Scale down preserving aspect ratio to save tokens and keep requests fast
        int targetWidth = width;
        int targetHeight = height;

        if (width > maxDimension || height > maxDimension)
        {
            if (width >= height)
            {
                targetWidth = maxDimension;
                targetHeight = Math.Max(1, (int)Math.Round((double)height * maxDimension / width));
            }
            else
            {
                targetHeight = maxDimension;
                targetWidth = Math.Max(1, (int)Math.Round((double)width * maxDimension / height));
            }
        }

        using var targetBitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(targetBitmap))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(screenBitmap, 0, 0, targetWidth, targetHeight);
        }

        using var ms = new MemoryStream();
        ImageCodecInfo? jpegCodec = GetEncoder(ImageFormat.Jpeg);
        if (jpegCodec != null)
        {
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            targetBitmap.Save(ms, jpegCodec, encoderParams);
        }
        else
        {
            targetBitmap.Save(ms, ImageFormat.Jpeg);
        }

        return ms.ToArray();
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
        foreach (ImageCodecInfo codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return null;
    }
}
