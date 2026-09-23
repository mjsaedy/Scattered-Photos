using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// Generates a collage that looks like photographs scattered randomly on a desktop.
/// Each source image is scaled, given a colored border, rotated at a random angle,
/// and placed at a random position on the canvas. Overlaps are expected and desired.
///
/// Usage:
///   GenerateCollage.exe /dim:1920x1080 /bg:Black /border:White /source:D:\images /num:20
///
/// Arguments:
///   /dim:WxH       Canvas width and height in pixels (required).
///   /bg:Color      Background color name, e.g. Black, Tan, #556B2F (required).
///   /border:Color  Border color around each photo (required).
///   /source:Path   Folder containing source images (required).
///   /num:N         Number of images to place; defaults to 10 (optional).
///
/// Output is written to the current directory as  collage_yyyyMMdd_HHmmss.jpg
/// </summary>
class GenerateCollage
{
    // Supported image extensions.
    static readonly string[] ImageExtensions =
    {
        "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.tif", "*.tiff", "*.webp"
    };

    static int Main(string[] args)
    {
        // ---- Defaults ----
        int width = 0;
        int height = 0;
        Color bgColor = Color.Empty;
        Color borderColor = Color.Empty;
        string sourceFolder = null;
        int numImages = 10;

        // ---- Parse command line ----
        foreach (string raw in args)
        {
            string arg = raw.Trim();
            if (arg.Length < 2 || arg[0] != '/' && arg[0] != '-')
                continue;

            int colon = arg.IndexOf(':');
            if (colon < 0) continue;

            string key = arg.Substring(1, colon - 1).ToLowerInvariant();
            string val = arg.Substring(colon + 1);

            switch (key)
            {
                case "dim":
                    string[] parts = val.ToLowerInvariant().Split('x');
                    if (parts.Length != 2
                        || !int.TryParse(parts[0], out width)
                        || !int.TryParse(parts[1], out height)
                        || width <= 0 || height <= 0)
                    {
                        Console.Error.WriteLine("Error: /dim must be WIDTHxHEIGHT (e.g. 1920x1080).");
                        return 1;
                    }
                    break;

                case "bg":
                    bgColor = ParseColor(val);
                    if (bgColor.IsEmpty)
                    {
                        Console.Error.WriteLine($"Error: could not parse background color '{val}'.");
                        return 1;
                    }
                    break;

                case "border":
                    borderColor = ParseColor(val);
                    if (borderColor.IsEmpty)
                    {
                        Console.Error.WriteLine($"Error: could not parse border color '{val}'.");
                        return 1;
                    }
                    break;

                case "source":
                    sourceFolder = val;
                    break;

                case "num":
                    if (!int.TryParse(val, out numImages) || numImages <= 0)
                    {
                        Console.Error.WriteLine("Error: /num must be a positive integer.");
                        return 1;
                    }
                    break;

                default:
                    Console.Error.WriteLine($"Warning: unknown argument '{arg}' ignored.");
                    break;
            }
        }

        // ---- Validate required arguments ----
        if (width == 0 || height == 0)
        {
            Console.Error.WriteLine("Error: /dim:WxH is required.");
            PrintUsage();
            return 1;
        }
        if (bgColor.IsEmpty)
        {
            Console.Error.WriteLine("Error: /bg:Color is required.");
            PrintUsage();
            return 1;
        }
        if (borderColor.IsEmpty)
        {
            Console.Error.WriteLine("Error: /border:Color is required.");
            PrintUsage();
            return 1;
        }
        if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            Console.Error.WriteLine($"Error: /source folder not found: '{sourceFolder}'");
            return 1;
        }

        // ---- Discover source images ----
        var imageFiles = new List<string>();
        foreach (string ext in ImageExtensions)
        {
            try
            {
                imageFiles.AddRange(
                    Directory.GetFiles(sourceFolder, ext, SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning scanning {ext}: {ex.Message}");
            }
        }

        // De-duplicate (some extensions overlap on case-insensitive file systems).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (string f in imageFiles)
        {
            if (seen.Add(f)) unique.Add(f);
        }
        imageFiles = unique;

        if (imageFiles.Count == 0)
        {
            Console.Error.WriteLine("No image files found in the source folder.");
            return 1;
        }

        Console.WriteLine($"Found {imageFiles.Count} image(s) in '{sourceFolder}'.");

        // ---- Shuffle and pick the requested count (Fisher–Yates) ----
        Random rng = new Random();
        for (int i = imageFiles.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            string tmp = imageFiles[i];
            imageFiles[i] = imageFiles[j];
            imageFiles[j] = tmp;
        }

        int count = Math.Min(numImages, imageFiles.Count);
        if (count < numImages)
            Console.WriteLine($"Only {count} image(s) available; using all of them.");

        // ---- Create the canvas ----
        using (Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(bgColor);
            g.InterpolationMode   = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode       = SmoothingMode.HighQuality;
            g.PixelOffsetMode     = PixelOffsetMode.HighQuality;
            g.CompositingQuality  = CompositingQuality.HighQuality;

            // Border thickness scales with canvas size, clamped to a sensible range.
            int borderWidth = Math.Max(4, Math.Min(width, height) / 120);

            // Size range for each placed photo, expressed as a fraction of canvas width.
            // Gives a nice variety so the scatter looks natural.
            const double MinFrac = 0.12;
            const double MaxFrac = 0.38;

            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                string file = imageFiles[i];
                try
                {
                    // Load via stream so the file is not locked.
                    Bitmap srcImg;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        using (Image tmp = Image.FromStream(fs))
                        {
                            srcImg = new Bitmap(tmp); // detach from the stream
                        }
                    }

                    using (srcImg)
                    {
                        // Random target width as a fraction of canvas width.
                        double frac = MinFrac + rng.NextDouble() * (MaxFrac - MinFrac);
                        int targetW = (int)(width * frac);
                        int targetH = (int)((double)srcImg.Height * targetW / srcImg.Width);

                        // Clamp to canvas so a single photo isn't absurdly huge.
                        if (targetH > height)
                        {
                            targetH = height;
                            targetW = (int)((double)srcImg.Width * targetH / srcImg.Height);
                        }

                        int bmpW = targetW + 2 * borderWidth;
                        int bmpH = targetH + 2 * borderWidth;

                        // Build the photo-with-border bitmap.
                        using (Bitmap photo = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppArgb))
                        using (Graphics gp = Graphics.FromImage(photo))
                        {
                            gp.Clear(borderColor);
                            gp.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gp.DrawImage(srcImg, borderWidth, borderWidth, targetW, targetH);

                            // Random placement: center of the photo lands anywhere on canvas.
                            // Allow the photo to overhang the edges for a natural "scattered" look.
                            float cx = (float)(rng.NextDouble() * width);
                            float cy = (float)(rng.NextDouble() * height);

                            // Random rotation in degrees, full circle.
                            //float angle = (float)(rng.NextDouble() * 360.0);
                            // Random rotation between -45 and +45
                            float angle = (float)(rng.NextDouble() * 90.0 - 45.0);

                            // Optional: add a subtle drop shadow for extra realism.
                            DrawShadow(g, bmpW, bmpH, cx, cy, angle, borderWidth);

                            // Draw the rotated photo.
                            GraphicsState state = g.Save();
                            g.TranslateTransform(cx, cy);
                            g.RotateTransform(angle);
                            g.DrawImage(photo, -bmpW / 2.0f, -bmpH / 2.0f, bmpW, bmpH);
                            g.Restore(state);
                        }
                    }

                    placed++;
                    Console.Write($"\rPlaced {placed}/{count} image(s)...");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"\nSkipping '{Path.GetFileName(file)}': {ex.Message}");
                }
            }

            Console.WriteLine();

            // ---- Save with a unique filename ----
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string outName = $"collage_{width}x{height}_{timestamp}.jpg";
            string outPath = Path.Combine(Environment.CurrentDirectory, outName);

            // JPEG encoder for lossless output.
            ImageCodecInfo jpgEncoder = GetEncoder(ImageFormat.Jpeg);
            EncoderParameters ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, 85L);

            canvas.Save(outPath, jpgEncoder, ep);

            Console.WriteLine($"Collage saved to: {outPath}");
        }

        return 0;
    }

    /// <summary>
    /// Draws a soft drop shadow beneath a rotated rectangle to enhance the
    /// "photograph lying on a desk" appearance.
    /// </summary>
    static void DrawShadow(Graphics g, int bmpW, int bmpH,
                           float cx, float cy, float angleDeg, int borderWidth)
    {
        int shadowOffset = Math.Max(3, borderWidth);

        // Build a shadow bitmap: a solid dark, semi-transparent rectangle.
        using (Bitmap shadow = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppArgb))
        using (Graphics gs = Graphics.FromImage(shadow))
        {
            gs.Clear(Color.FromArgb(70, 0, 0, 0));

            GraphicsState state = g.Save();
            g.TranslateTransform(cx + shadowOffset, cy + shadowOffset);
            g.RotateTransform(angleDeg);
            g.DrawImage(shadow, -bmpW / 2.0f, -bmpH / 2.0f, bmpW, bmpH);
            g.Restore(state);
        }
    }

    /// <summary>
    /// Parses a color from a name ("Red", "Tan"), a hex string ("#FF8800", "#AARRGGBB"),
    /// or a known .NET Color name. Returns Color.Empty on failure.
    /// </summary>
    static Color ParseColor(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Color.Empty;

        string t = text.Trim();

        // Hex forms: #RGB, #RRGGBB, #AARRGGBB
        if (t.StartsWith("#") || t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            string hex = t.StartsWith("#") ? t.Substring(1) : t.Substring(2);
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v))
            {
                if (hex.Length <= 6)
                    return Color.FromArgb((int)(0xFF000000 | v));     // #RRGGBB
                else
                    return Color.FromArgb((int)v);                    // #AARRGGBB
            }
        }

        // Try .NET named color (case-insensitive).
        try
        {
            Color c = Color.FromName(t);
            if (c.IsKnownColor || c.A > 0)
                return c;
        }
        catch { }

        // Last resort: ColorTranslator (handles HTML names too).
        try
        {
            return ColorTranslator.FromHtml(t);
        }
        catch { }

        return Color.Empty;
    }

    static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        return null;
    }

    static void PrintUsage()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  GenerateCollage.exe /dim:WxH /bg:Color /border:Color /source:Path [/num:N]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Example:");
        Console.Error.WriteLine("  GenerateCollage.exe /dim:1920x1080 /bg:Black /border:White /source:D:\\images /num:20");
    }
}