using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

class GenerateCollage
{
    static void Main(string[] args)
    {
        try
        {
            // Defaults
            int width = 1920;
            int height = 1080;
            Color bgColor = Color.Black;
            Color borderColor = Color.White;
            int numImages = -1; // -1 means use all (or a reasonable limit)
            string sourceFolder = null;

            // Parse command-line arguments
            foreach (string arg in args)
            {
                if (arg.StartsWith("/dim:", StringComparison.OrdinalIgnoreCase))
                {
                    string dim = arg.Substring(5);
                    string[] parts = dim.Split('x', 'X');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out width) &&
                        int.TryParse(parts[1], out height) &&
                        width > 0 && height > 0)
                    {
                        // ok
                    }
                    else
                    {
                        Console.WriteLine("Invalid /dim: value. Expected format: /dim:WIDTHxHEIGHT");
                        return;
                    }
                }
                else if (arg.StartsWith("/bg:", StringComparison.OrdinalIgnoreCase))
                {
                    string name = arg.Substring(4);
                    bgColor = ParseColor(name, Color.Black);
                }
                else if (arg.StartsWith("/border:", StringComparison.OrdinalIgnoreCase))
                {
                    string name = arg.Substring(8);
                    borderColor = ParseColor(name, Color.White);
                }
                else if (arg.StartsWith("/num:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(arg.Substring(5), out numImages) || numImages < 1)
                    {
                        Console.WriteLine("Invalid /num: value. Must be a positive integer.");
                        return;
                    }
                }
                else if (arg.StartsWith("/source:", StringComparison.OrdinalIgnoreCase))
                {
                    sourceFolder = arg.Substring(8).Trim('"');
                }
                else
                {
                    Console.WriteLine("Unknown argument: " + arg);
                    PrintUsage();
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                Console.WriteLine("A valid /source: folder path is required.");
                PrintUsage();
                return;
            }

            // Collect image files
            string[] extensions = { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.tif", "*.tiff" };
            var imageFiles = new List<string>();
            foreach (string ext in extensions)
            {
                imageFiles.AddRange(Directory.GetFiles(sourceFolder, ext, SearchOption.TopDirectoryOnly));
            }

            if (imageFiles.Count == 0)
            {
                Console.WriteLine("No supported image files found in the source folder.");
                return;
            }

            // Select the images to use
            var rng = new Random();
            List<string> selected;
            if (numImages > 0)
            {
                selected = imageFiles.OrderBy(x => rng.Next()).Take(Math.Min(numImages, imageFiles.Count)).ToList();
            }
            else
            {
                // Use all, but cap at a reasonable number to avoid extremely long runtimes
                selected = imageFiles.OrderBy(x => rng.Next()).Take(Math.Min(60, imageFiles.Count)).ToList();
            }

            Console.WriteLine($"Using {selected.Count} image(s) from \"{sourceFolder}\"");
            Console.WriteLine($"Canvas size: {width}x{height}");
            Console.WriteLine($"Background: {bgColor.Name}, Border: {borderColor.Name}");

            // Create the canvas
            using (var canvas = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                // Fill background
                g.Clear(bgColor);

                // Approximate maximum size for each photo so they look like scattered prints
                // (roughly 25-45% of the shorter canvas side)
                int maxPhotoSize = (int)(Math.Min(width, height) * 0.42);

                foreach (string file in selected)
                {
                    try
                    {
                        using (var img = Image.FromFile(file))
                        {
                            // Compute scaled size while preserving aspect ratio
                            float scale = Math.Min(
                                (float)maxPhotoSize / img.Width,
                                (float)maxPhotoSize / img.Height);
                            // Add a little random variation in size
                            scale *= 0.75f + (float)rng.NextDouble() * 0.45f;

                            int scaledW = Math.Max(40, (int)(img.Width * scale));
                            int scaledH = Math.Max(40, (int)(img.Height * scale));

                            // Border thickness (relative to photo size)
                            int borderThickness = Math.Max(4, Math.Min(scaledW, scaledH) / 25);

                            // Total size including border
                            int totalW = scaledW + borderThickness * 2;
                            int totalH = scaledH + borderThickness * 2;

                            // Random rotation angle (degrees). Full range looks more natural for scattered photos.
                            float angle = (float)(rng.NextDouble() * 360.0 - 180.0);

                            // Random position. Allow the photo to partially go off-canvas for a realistic look.
                            // We place the center of the photo somewhere on the canvas (with a small margin).
                            float centerX = rng.Next(totalW / 4, width - totalW / 4);
                            float centerY = rng.Next(totalH / 4, height - totalH / 4);

                            // Save graphics state
                            var state = g.Save();

                            // Move origin to the center of the photo, rotate, then draw
                            g.TranslateTransform(centerX, centerY);
                            g.RotateTransform(angle);

                            // Draw the white (or chosen) border rectangle centered at origin
                            using (var borderBrush = new SolidBrush(borderColor))
                            {
                                g.FillRectangle(borderBrush,
                                    -totalW / 2f, -totalH / 2f,
                                    totalW, totalH);
                            }

                            // Draw the scaled image on top of the border, also centered
                            g.DrawImage(img,
                                -scaledW / 2f, -scaledH / 2f,
                                scaledW, scaledH);

                            // Restore graphics state
                            g.Restore(state);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Skipping \"{Path.GetFileName(file)}\": {ex.Message}");
                    }
                }

                // Generate a unique output filename
                string outputName = $"collage_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.jpg";
                string outputPath = Path.Combine(Directory.GetCurrentDirectory(), outputName);

                // Save as high-quality JPEG
                var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 92L);

                canvas.Save(outputPath, jpegEncoder, encoderParams);

                Console.WriteLine($"Collage saved to: {outputPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }

    static Color ParseColor(string name, Color fallback)
    {
        try
        {
            // Support both named colors and simple hex (#RRGGBB or RRGGBB)
            if (name.StartsWith("#"))
                name = name.Substring(1);

            if (name.Length == 6 &&
                int.TryParse(name.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int r) &&
                int.TryParse(name.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int g) &&
                int.TryParse(name.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int b))
            {
                return Color.FromArgb(r, g, b);
            }

            Color c = Color.FromName(name);
            if (c.IsKnownColor || c.A > 0)
                return c;
        }
        catch { }

        return fallback;
    }

    static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
        foreach (ImageCodecInfo codec in codecs)
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        return null;
    }

    static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  GenerateCollage.exe /dim:WIDTHxHEIGHT /bg:COLOR /border:COLOR [/num:N] /source:FOLDER");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  GenerateCollage.exe /dim:1920x1080 /bg:Black /border:White /num:20 /source:D:\\images");
        Console.WriteLine("  GenerateCollage.exe /dim:1280x720 /bg:#1a1a2e /border:Ivory /source:C:\\Photos");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - /num is optional. If omitted, up to 60 images are used.");
        Console.WriteLine("  - Colors can be named (Black, White, Navy ...) or hex (#RRGGBB).");
        Console.WriteLine("  - Supported image formats: jpg, jpeg, png, bmp, gif, tif, tiff.");
        Console.WriteLine("  - Output is a uniquely named .jpg file in the current directory.");
    }
}