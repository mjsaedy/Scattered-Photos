using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            
            if (!ValidateOptions(options))
            {
                ShowUsage();
                return;
            }
            
            var imageFiles = GetImageFiles(options.SourceFolder);
            if (imageFiles.Count == 0)
            {
                Console.WriteLine("No image files found in the specified folder.");
                return;
            }
            
            var selectedImages = SelectRandomImages(imageFiles, options.NumberOfImages);
            string outputPath = GenerateOutputPath(options);
            CreateCollage(selectedImages, options, outputPath);
            
            Console.WriteLine("Collage generated successfully: " + outputPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }

    static CollageOptions ParseArguments(string[] args)
    {
        var options = new CollageOptions
        {
            Width = 1920,
            Height = 1080,
            BackgroundColor = Color.Black,
            BorderColor = Color.White,
            NumberOfImages = 20
        };

        foreach (var arg in args)
        {
            if (arg.StartsWith("/dim:", StringComparison.OrdinalIgnoreCase))
            {
                var dim = arg.Substring(5).ToLower();
                var parts = dim.Split('x');
                if (parts.Length == 2)
                {
                    if (int.TryParse(parts[0], out int width)) options.Width = width;
                    if (int.TryParse(parts[1], out int height)) options.Height = height;
                }
            }
            else if (arg.StartsWith("/bg:", StringComparison.OrdinalIgnoreCase))
            {
                var colorName = arg.Substring(4);
                options.BackgroundColor = GetColorFromName(colorName);
            }
            else if (arg.StartsWith("/border:", StringComparison.OrdinalIgnoreCase))
            {
                var colorName = arg.Substring(8);
                options.BorderColor = GetColorFromName(colorName);
            }
            else if (arg.StartsWith("/num:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(arg.Substring(5), out int num)) options.NumberOfImages = num;
            }
            else if (arg.StartsWith("/source:", StringComparison.OrdinalIgnoreCase))
            {
                options.SourceFolder = arg.Substring(8);
            }
        }

        return options;
    }

    static Color GetColorFromName(string colorName)
    {
        try
        {
            return Color.FromName(colorName);
        }
        catch
        {
            if (colorName.StartsWith("#") && colorName.Length == 7)
            {
                int r = Convert.ToInt32(colorName.Substring(1, 2), 16);
                int g = Convert.ToInt32(colorName.Substring(3, 2), 16);
                int b = Convert.ToInt32(colorName.Substring(5, 2), 16);
                return Color.FromArgb(r, g, b);
            }
            return Color.FromName(colorName);
        }
    }

    static bool ValidateOptions(CollageOptions options)
    {
        return !string.IsNullOrEmpty(options.SourceFolder) &&
               options.Width > 0 &&
               options.Height > 0 &&
               options.NumberOfImages > 0;
    }

    static void ShowUsage()
    {
        Console.WriteLine("GenerateCollage - Image Collage Generator");
        Console.WriteLine();
        Console.WriteLine("Usage: GenerateCollage.exe /dim:WIDTHxHEIGHT /bg:BACKGROUND_COLOR /border:BORDER_COLOR /num:NUMBER /source:FOLDER_PATH");
        Console.WriteLine();
        Console.WriteLine("Example: GenerateCollage.exe /dim:1920x1080 /bg:Black /border:White /num:20 /source:D:\\images");
        Console.WriteLine();
        Console.WriteLine("Parameters:");
        Console.WriteLine("  /dim:WIDTHxHEIGHT    - Dimensions of the output image (e.g., 1920x1080)");
        Console.WriteLine("  /bg:COLOR           - Background color (e.g., Black, White, #FF0000)");
        Console.WriteLine("  /border:COLOR       - Border color around images (e.g., White, Black)");
        Console.WriteLine("  /num:N              - Number of images to include in collage");
        Console.WriteLine("  /source:PATH        - Path to folder containing source images");
    }

    static List<string> GetImageFiles(string folderPath)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
        var files = new List<string>();
        
        if (Directory.Exists(folderPath))
        {
            files.AddRange(Directory.GetFiles(folderPath, "*.*")
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLower())));
        }
        
        return files;
    }

    static List<string> SelectRandomImages(List<string> imageFiles, int count)
    {
        var random = new Random();
        var selected = new List<string>();
        var tempList = new List<string>(imageFiles);
        
        for (int i = 0; i < Math.Min(count, tempList.Count); i++)
        {
            int index = random.Next(tempList.Count);
            selected.Add(tempList[index]);
            tempList.RemoveAt(index);
        }
        
        return selected;
    }

    static string GenerateOutputPath(CollageOptions options)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = string.Format("Collage_{0}_{1}x{2}_{3}.jpg", 
            timestamp, options.Width, options.Height, options.NumberOfImages);
        return Path.Combine(".", filename);
    }

    static void CreateCollage(List<string> imagePaths, CollageOptions options, string outputPath)
    {
        var random = new Random();
        
        using (var bitmap = new Bitmap(options.Width, options.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(options.BackgroundColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            
            foreach (var imagePath in imagePaths)
            {
                using (var sourceImage = Image.FromFile(imagePath))
                {
                    float rotationAngle = (float)(random.NextDouble() * 360);
                    float scale = GetRandomScale(random, sourceImage, options);
                    Point position = GetRandomPosition(random, sourceImage, options, scale);
                    
                    DrawRotatedImage(graphics, sourceImage, position, scale, rotationAngle, options);
                }
            }
            
            bitmap.Save(outputPath, ImageFormat.Jpeg);
        }
    }

    static float GetRandomScale(Random random, Image image, CollageOptions options)
    {
        float maxScale = Math.Min(
            (float)options.Width / image.Width,
            (float)options.Height / image.Height);
        
        return maxScale * (0.1f + (float)random.NextDouble() * 0.8f);
    }

    static Point GetRandomPosition(Random random, Image image, CollageOptions options, float scale)
    {
        int scaledWidth = (int)(image.Width * scale);
        int scaledHeight = (int)(image.Height * scale);
        
        int x = random.Next(0, Math.Max(1, options.Width - scaledWidth));
        int y = random.Next(0, Math.Max(1, options.Height - scaledHeight));
        
        return new Point(x, y);
    }

    static void DrawRotatedImage(Graphics graphics, Image image, Point position, float scale, float angle, CollageOptions options)
    {
        int borderThickness = 5;
        
        using (var scaledImage = new Bitmap(
            (int)(image.Width * scale),
            (int)(image.Height * scale)))
        {
            using (var tempGraphics = Graphics.FromImage(scaledImage))
            {
                tempGraphics.Clear(options.BackgroundColor);
                tempGraphics.DrawImage(image, 0, 0, scaledImage.Width, scaledImage.Height);
                
                using (var borderPen = new Pen(options.BorderColor, borderThickness))
                {
                    tempGraphics.DrawRectangle(borderPen, 0, 0, scaledImage.Width - 1, scaledImage.Height - 1);
                }
            }
            
            graphics.TranslateTransform(position.X, position.Y);
            graphics.RotateTransform(angle);
            graphics.DrawImage(scaledImage, 0, 0);
            graphics.ResetTransform();
        }
    }

    class CollageOptions
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public Color BackgroundColor { get; set; }
        public Color BorderColor { get; set; }
        public int NumberOfImages { get; set; }
        public string SourceFolder { get; set; }
    }
}