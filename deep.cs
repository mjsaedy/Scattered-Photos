using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace GenerateCollage
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var options = ParseArguments(args);

                if (options == null)
                {
                    ShowUsage();
                    return;
                }

                // Get image files from source folder
                var imageFiles = GetImageFiles(options.SourcePath, options.NumberOfImages);
                
                if (imageFiles.Count == 0)
                {
                    Console.WriteLine("No image files found in the specified folder.");
                    return;
                }

                // Create the collage
                CreateCollage(options, imageFiles);

                //Console.WriteLine($"Collage created successfully: {options.OutputFileName}");
                var fname = Path.GetFileName(options.OutputFileName);
                Console.WriteLine($"Collage created successfully: {fname}");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static Options ParseArguments(string[] args)
        {
            var options = new Options();
            bool hasDimension = false;
            bool hasBg = false;
            bool hasBorder = false;
            bool hasSource = false;

            foreach (string arg in args)
            {
                if (arg.StartsWith("/dim:", StringComparison.OrdinalIgnoreCase))
                {
                    var dims = arg.Substring(5).Split('x');
                    if (dims.Length == 2 && 
                        int.TryParse(dims[0], out int width) && 
                        int.TryParse(dims[1], out int height))
                    {
                        options.Width = width;
                        options.Height = height;
                        hasDimension = true;
                    }
                }
                else if (arg.StartsWith("/bg:", StringComparison.OrdinalIgnoreCase))
                {
                    options.BackgroundColor = ParseColor(arg.Substring(4));
                    hasBg = true;
                }
                else if (arg.StartsWith("/border:", StringComparison.OrdinalIgnoreCase))
                {
                    options.BorderColor = ParseColor(arg.Substring(8));
                    hasBorder = true;
                }
                else if (arg.StartsWith("/source:", StringComparison.OrdinalIgnoreCase))
                {
                    options.SourcePath = arg.Substring(8);
                    hasSource = true;
                }
                else if (arg.StartsWith("/num:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(arg.Substring(5), out int num))
                    {
                        options.NumberOfImages = num;
                    }
                }
            }

            if (!hasDimension || !hasBg || !hasBorder || !hasSource)
            {
                return null;
            }

            if (options.NumberOfImages <= 0)
            {
                options.NumberOfImages = 20; // Default
            }

            // Generate unique output filename in current directory
            options.OutputFileName = $"collage_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString().Substring(0, 8)}.jpg";
            options.OutputFileName = Path.Combine(Environment.CurrentDirectory, options.OutputFileName);

            return options;
        }

        private static Color ParseColor(string colorName)
        {
            try
            {
                // Try to parse as named color
                var color = Color.FromName(colorName);
                if (color.IsKnownColor)
                    return color;

                // Try to parse as hex
                if (colorName.StartsWith("#"))
                {
                    return ColorTranslator.FromHtml(colorName);
                }

                // Try to parse RGB values
                var parts = colorName.Split(',');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out int r) &&
                    int.TryParse(parts[1], out int g) &&
                    int.TryParse(parts[2], out int b))
                {
                    return Color.FromArgb(r, g, b);
                }

                return Color.Black;
            }
            catch
            {
                return Color.Black;
            }
        }

        private static List<string> GetImageFiles(string sourcePath, int numberOfImages)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"Source folder not found: {sourcePath}");
            }

            // Supported image extensions
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" };
            
            var files = Directory.GetFiles(sourcePath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (files.Count == 0)
            {
                return files;
            }

            // If we have fewer files than requested, use all of them
            if (files.Count <= numberOfImages)
            {
                return files;
            }

            // Randomly select the requested number of files
            var random = new Random();
            return files.OrderBy(x => random.Next()).Take(numberOfImages).ToList();
        }

        private static void CreateCollage(Options options, List<string> imageFiles)
        {
            using (var canvas = new Bitmap(options.Width, options.Height))
            using (var graphics = Graphics.FromImage(canvas))
            {
                // Set high quality rendering
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;

                // Fill background
                using (var brush = new SolidBrush(options.BackgroundColor))
                {
                    graphics.FillRectangle(brush, 0, 0, options.Width, options.Height);
                }

                var random = new Random();

                foreach (string imagePath in imageFiles) {
                    Console.WriteLine($" +{Path.GetFileName(imagePath)}");
                    try {
                        using (var originalImage = Image.FromFile(imagePath)) {
                            // Calculate size to fit within canvas
                            int maxWidth = options.Width / 3;
                            int maxHeight = options.Height / 3;
                            
                            // Random size between 25% and 50% of canvas
                            double sizeFactor = 0.25 + (random.NextDouble() * 0.25);
                            int targetWidth = (int)(options.Width * sizeFactor);
                            int targetHeight = (int)(options.Height * sizeFactor);
                            
                            // Maintain aspect ratio
                            double aspectRatio = (double)originalImage.Width / originalImage.Height;
                            int width, height;
                            
                            if (aspectRatio > 1)
                            {
                                width = targetWidth;
                                height = (int)(targetWidth / aspectRatio);
                            }
                            else
                            {
                                height = targetHeight;
                                width = (int)(targetHeight * aspectRatio);
                            }

                            // Create resized image
                            using (var resizedImage = new Bitmap(width, height))
                            using (var g = Graphics.FromImage(resizedImage))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.DrawImage(originalImage, 0, 0, width, height);
                                
                                Bitmap borderedImage;
                                if (options.BorderColor != Color.Transparent)
                                    borderedImage = AddBorder(resizedImage, options.BorderColor);
                                else
                                    borderedImage = new Bitmap(resizedImage);

                                // Random position (ensure at least partially visible)
                                int x = random.Next(-width / 2, options.Width - width / 2);
                                int y = random.Next(-height / 2, options.Height - height / 2);
                                // Random rotation angle (between -30 and 30 degrees)
                                float angle = (float)(random.NextDouble() * 60 - 30);
                                // Rotate the image
                                using (var rotatedImage = RotateImage(borderedImage, angle)) {
                                    Bitmap finalImage;
                                    finalImage = new Bitmap(rotatedImage);
                                    // Draw the final image (with border) on the canvas
                                    graphics.DrawImage(finalImage, x, y);
                                    finalImage.Dispose();
                                    borderedImage.Dispose();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not process image {imagePath}: {ex.Message}");
                    }
                }

                // Save the collage
                canvas.Save(options.OutputFileName, ImageFormat.Jpeg);
            }
        }

        private static Bitmap RotateImage(Image image, float angle)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            // Calculate the bounds of the rotated image
            var rotatedBounds = GetRotatedBounds(new Rectangle(0, 0, image.Width, image.Height), angle);
            int newWidth = (int)Math.Ceiling(rotatedBounds.Width);
            int newHeight = (int)Math.Ceiling(rotatedBounds.Height);

            // Create final bitmap with correct size
            var result = new Bitmap(newWidth, newHeight);
            using (var gResult = Graphics.FromImage(result))
            {
                gResult.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gResult.SmoothingMode = SmoothingMode.AntiAlias;
                gResult.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gResult.CompositingQuality = CompositingQuality.HighQuality;

                // Clear with transparent background
                gResult.Clear(Color.Transparent);

                // Center the rotation
                gResult.TranslateTransform(newWidth / 2f, newHeight / 2f);
                gResult.RotateTransform(angle);
                gResult.TranslateTransform(-image.Width / 2f, -image.Height / 2f);
                gResult.DrawImage(image, 0, 0);
            }

            return result;
        }

        private static RectangleF GetRotatedBounds(Rectangle rect, float angle)
        {
            float rad = angle * (float)Math.PI / 180;
            float cos = (float)Math.Abs(Math.Cos(rad));
            float sin = (float)Math.Abs(Math.Sin(rad));

            float w = rect.Width * cos + rect.Height * sin;
            float h = rect.Width * sin + rect.Height * cos;

            return new RectangleF(0, 0, w, h);
        }

        private static Bitmap AddBorder(Image image, Color borderColor)
        {
            var result = new Bitmap(image.Width, image.Height);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                // Clear with transparent
                g.Clear(Color.Transparent);

                // Draw the image first
                g.DrawImage(image, 0, 0);

                // Then draw border on top
                using (var pen = new Pen(borderColor, 5))
                {
                    // Draw border slightly inside to keep it within the image bounds
                    g.DrawRectangle(pen, 2.5f, 2.5f, image.Width - 5, image.Height - 5);
                }
            }
            return result;
        }

        private static void ShowUsage()
        {
            Console.WriteLine("Usage: GenerateCollage.exe /dim:WIDTHxHEIGHT /bg:COLOR /border:COLOR /source:FOLDER [/num:NUMBER]");
            Console.WriteLine();
            Console.WriteLine("Parameters:");
            Console.WriteLine("  /dim:WIDTHxHEIGHT  - Dimensions of the output image (e.g., /dim:1920x1080)");
            Console.WriteLine("  /bg:COLOR          - Background color (name, hex, or RGB)");
            Console.WriteLine("  /border:COLOR      - Border color for images (name, hex, or RGB)");
            Console.WriteLine("  /source:FOLDER     - Source folder containing images");
            Console.WriteLine("  /num:NUMBER        - Number of images to use (default: 20)");
            Console.WriteLine();
            Console.WriteLine("Color formats:");
            Console.WriteLine("  Named colors: Red, Blue, Green, White, Black, etc.");
            Console.WriteLine("  Hex: #FF0000, #00FF00, #0000FF");
            Console.WriteLine("  RGB: 255,0,0");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("  GenerateCollage.exe /dim:1920x1080 /bg:Black /border:White /source:D:\\images /num:20");
        }

        private class Options
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public Color BackgroundColor { get; set; }
            public Color BorderColor { get; set; }
            public string SourcePath { get; set; }
            public int NumberOfImages { get; set; } = 20;
            public string OutputFileName { get; set; }
        }
    }
}