// GenerateCollage.cs
//
// Compile with:
// csc.exe /target:exe /out:GenerateCollage.exe ^
//   /reference:System.Drawing.dll ^
//   /reference:System.Core.dll ^
//   GenerateCollage.cs

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

internal static class GenerateCollage
{
    private static void Main(string[] args)
    {
        try
        {
            Dictionary<string, string> options = ParseArguments(args);

            int canvasWidth;
            int canvasHeight;

            ParseDimensions(
                GetRequiredOption(options, "dim"),
                out canvasWidth,
                out canvasHeight);

            Color backgroundColor = ParseColor(
                GetRequiredOption(options, "bg"));

            Color borderColor = ParseColor(
                GetRequiredOption(options, "border"));

            string sourceFolder = GetRequiredOption(options, "source");

            if (!Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException(
                    "Source folder does not exist: " + sourceFolder);
            }

            int requestedNumber = int.MaxValue;

            if (options.ContainsKey("num"))
            {
                if (!int.TryParse(options["num"], out requestedNumber) ||
                    requestedNumber <= 0)
                {
                    throw new ArgumentException(
                        "/num must be a positive integer.");
                }
            }

            string[] supportedExtensions =
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".bmp",
                ".gif",
                ".tif",
                ".tiff"
            };

            List<string> imageFiles = Directory
                .GetFiles(sourceFolder)
                .Where(
                    file => supportedExtensions.Contains(
                        Path.GetExtension(file).ToLowerInvariant()))
                .OrderBy(
                    file => Guid.NewGuid())
                .Take(requestedNumber)
                .ToList();

            if (imageFiles.Count == 0)
            {
                throw new InvalidOperationException(
                    "No supported image files were found in the source folder.");
            }

            string outputFile = GetUniqueOutputFile();

            using (Bitmap canvas = new Bitmap(
                canvasWidth,
                canvasHeight,
                PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(canvas))
            using (Pen borderPen = new Pen(borderColor, 4.0f))
            {
                graphics.Clear(backgroundColor);

                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality =
                    CompositingQuality.HighQuality;

                Random random = new Random();

                foreach (string imageFile in imageFiles) {
                    Console.WriteLine($" +{Path.GetFileName(imageFile)}");
                    try
                    {
                        using (Image image = Image.FromFile(imageFile))
                        {
                            DrawScatteredImage(
                                graphics,
                                image,
                                borderPen,
                                canvasWidth,
                                canvasHeight,
                                random);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            "Skipping image '" +
                            imageFile +
                            "': " +
                            ex.Message);
                    }
                }

                canvas.Save(outputFile, ImageFormat.Jpeg);
            }

            Console.WriteLine("Created: " + outputFile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            Console.Error.WriteLine();
            PrintUsage();
            Environment.ExitCode = 1;
        }
    }

    private static void DrawScatteredImage(
        Graphics graphics,
        Image image,
        Pen borderPen,
        int canvasWidth,
        int canvasHeight,
        Random random)
    {
        // Select a random maximum size for the image.
        float maxWidth = canvasWidth *
                         RandomFloat(random, 0.25f, 0.50f);

        float maxHeight = canvasHeight *
                          RandomFloat(random, 0.25f, 0.50f);

        // Preserve the original aspect ratio.
        float scale = Math.Min(
            maxWidth / image.Width,
            maxHeight / image.Height);

        float drawWidth = Math.Max(1.0f, image.Width * scale);
        float drawHeight = Math.Max(1.0f, image.Height * scale);

        // Allow images to be partially outside the canvas.
        float centerX = RandomFloat(
            random,
            -drawWidth * 0.20f,
            canvasWidth + drawWidth * 0.20f);

        float centerY = RandomFloat(
            random,
            -drawHeight * 0.20f,
            canvasHeight + drawHeight * 0.20f);

        float angle = RandomFloat(random, -35.0f, 35.0f);

        GraphicsState savedState = graphics.Save();

        try
        {
            graphics.TranslateTransform(centerX, centerY);
            graphics.RotateTransform(angle);

            RectangleF destination = new RectangleF(
                -drawWidth / 2.0f,
                -drawHeight / 2.0f,
                drawWidth,
                drawHeight);

            // This overload is compatible with .NET Framework 4.7+.
            graphics.DrawImage(image, destination);

            graphics.DrawRectangle(
                borderPen,
                destination.X,
                destination.Y,
                destination.Width,
                destination.Height);
        }
        finally
        {
            graphics.Restore(savedState);
        }
    }

    private static float RandomFloat(
        Random random,
        float minimum,
        float maximum)
    {
        return minimum +
               (float)random.NextDouble() * (maximum - minimum);
    }

    private static Dictionary<string, string> ParseArguments(
        string[] args)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (string argument in args)
        {
            if (!argument.StartsWith("/") &&
                !argument.StartsWith("-"))
            {
                continue;
            }

            int colonIndex = argument.IndexOf(':');

            if (colonIndex < 0)
            {
                throw new ArgumentException(
                    "Invalid argument: " + argument);
            }

            string key = argument.Substring(
                1,
                colonIndex - 1).Trim();

            string value = argument.Substring(
                colonIndex + 1).Trim();

            if (key.Length == 0 || value.Length == 0)
            {
                throw new ArgumentException(
                    "Invalid argument: " + argument);
            }

            result[key] = value;
        }

        return result;
    }

    private static string GetRequiredOption(
        Dictionary<string, string> options,
        string name)
    {
        string value;

        if (!options.TryGetValue(name, out value))
        {
            throw new ArgumentException(
                "Missing required argument: /" + name + ":...");
        }

        return value;
    }

    private static void ParseDimensions(
        string value,
        out int width,
        out int height)
    {
        string[] parts = value
            .ToLowerInvariant()
            .Split('x');

        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out width) ||
            !int.TryParse(parts[1], out height) ||
            width <= 0 ||
            height <= 0)
        {
            throw new ArgumentException(
                "Dimensions must have the form WIDTHxHEIGHT, " +
                "for example: 1920x1080.");
        }
    }

    private static Color ParseColor(string value)
    {
        if (value.StartsWith("#"))
        {
            return ColorTranslator.FromHtml(value);
        }

        Color namedColor = Color.FromName(value);

        if (namedColor.IsKnownColor)
        {
            return namedColor;
        }

        throw new ArgumentException(
            "Unknown color: " + value);
    }

    private static string GetUniqueOutputFile()
    {
        string outputDirectory = Environment.CurrentDirectory;
        string outputFile;

        do
        {
            outputFile = Path.Combine(
                outputDirectory,
                "collage_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                "_" +
                Guid.NewGuid().ToString("N").Substring(0, 8) +
                ".jpg");
        }
        while (File.Exists(outputFile));

        return outputFile;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage:");

        Console.Error.WriteLine(
            "  GenerateCollage.exe " +
            "/dim:1920x1080 " +
            "/bg:Black " +
            "/border:White " +
            "/source:D:\\images " +
            "[/num:20]");

        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "/num is optional. If omitted, all supported images are used.");
    }
}
