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
            // Default arguments
            int width = 1920;
            int height = 1080;
            Color bgColor = Color.Black;
            Color borderColor = Color.White;
            int numImages = 15;
            string sourcePath = string.Empty;

            // Parse Command Line Arguments
            var parsedArgs = ParseArgs(args);

            if (parsedArgs.ContainsKey("dim"))
            {
                var dims = parsedArgs["dim"].Split('x');
                if (dims.Length == 2)
                {
                    int.TryParse(dims[0], out width);
                    int.TryParse(dims[1], out height);
                }
            }

            if (parsedArgs.ContainsKey("bg"))
                bgColor = Color.FromName(parsedArgs["bg"]);

            if (parsedArgs.ContainsKey("border"))
                borderColor = Color.FromName(parsedArgs["border"]);

            if (parsedArgs.ContainsKey("num"))
                int.TryParse(parsedArgs["num"], out numImages);

            if (parsedArgs.ContainsKey("source"))
                sourcePath = parsedArgs["source"];

            if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath))
            {
                Console.WriteLine("Error: Valid source folder required. Usage: /source:\"C:\\Path\\To\\Images\"");
                return;
            }

            // Retrieve supported image files
            string[] extensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
            List<string> imageFiles = extensions
                .SelectMany(ext => Directory.GetFiles(sourcePath, ext, SearchOption.TopDirectoryOnly))
                .ToList();

            if (imageFiles.Count == 0)
            {
                Console.WriteLine("Error: No supported images found in source folder.");
                return;
            }

            // Select random images
            Random rand = new Random();
            var selectedFiles = imageFiles
                .OrderBy(x => rand.Next())
                .Take(numImages)
                .ToList();

            // Create Canvas
            using (Bitmap canvas = new Bitmap(width, height))
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Fill Background
                using (SolidBrush bgBrush = new SolidBrush(bgColor))
                {
                    g.FillRectangle(bgBrush, 0, 0, width, height);
                }

                // Sizing parameters relative to canvas size
                int maxImgDimension = Math.Min(width, height) / 3;
                int borderWidth = Math.Max(4, maxImgDimension / 40);

                foreach (string file in selectedFiles)
                {
                    try
                    {
                        using (Image srcImg = Image.FromFile(file))
                        {
                            // Calculate scaled dimensions maintaining aspect ratio
                            float scale = Math.Min((float)maxImgDimension / srcImg.Width, (float)maxImgDimension / srcImg.Height);
                            int imgWidth = (int)(srcImg.Width * scale);
                            int imgHeight = (int)(srcImg.Height * scale);

                            int framedWidth = imgWidth + (borderWidth * 2);
                            int framedHeight = imgHeight + (borderWidth * 2);

                            // Draw framed image to an offscreen bitmap
                            using (Bitmap framedImg = new Bitmap(framedWidth, framedHeight))
                            using (Graphics gFrame = Graphics.FromImage(framedImg))
                            {
                                gFrame.SmoothingMode = SmoothingMode.AntiAlias;

                                // Draw Border / Polaroid Frame
                                using (SolidBrush borderBrush = new SolidBrush(borderColor))
                                {
                                    gFrame.FillRectangle(borderBrush, 0, 0, framedWidth, framedHeight);
                                }

                                // Draw Image inside border
                                gFrame.DrawImage(srcImg, borderWidth, borderWidth, imgWidth, imgHeight);

                                // Random placement and rotation
                                float angle = rand.Next(-35, 36); // Rotation -35 to 35 degrees
                                int x = rand.Next(-framedWidth / 4, width - (framedWidth / 2));
                                int y = rand.Next(-framedHeight / 4, height - (framedHeight / 2));

                                // Apply transformation matrix for rotation
                                Matrix transform = new Matrix();
                                transform.RotateAt(angle, new PointF(x + framedWidth / 2f, y + framedHeight / 2f));
                                g.Transform = transform;

                                // Draw shadow effect
                                using (GraphicsPath path = new GraphicsPath())
                                {
                                    path.AddRectangle(new Rectangle(x + 5, y + 5, framedWidth, framedHeight));
                                    using (PathGradientBrush shadowBrush = new PathGradientBrush(path))
                                    {
                                        shadowBrush.CenterColor = Color.FromArgb(80, 0, 0, 0);
                                        shadowBrush.SurroundColors = new Color[] { Color.Transparent };
                                        g.FillPath(shadowBrush, path);
                                    }
                                }

                                // Draw rotated image onto main canvas
                                g.DrawImage(framedImg, x, y);
                                g.ResetTransform();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore unreadable or corrupted images
                    }
                }

                // Generate a unique output file name using a timestamp and GUID segment
                string outputFile = Path.Combine(Environment.CurrentDirectory, 
                    $"Collage_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString().Substring(0, 4)}.jpg");

                // Save image with 90% JPEG quality
                ImageCodecInfo jpgEncoder = GetEncoder(ImageFormat.Jpeg);
                EncoderParameters encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 90L);

                canvas.Save(outputFile, jpgEncoder, encoderParams);
                Console.WriteLine($"Collage successfully created: {outputFile}");
            }
        }

        // Helper to parse CLI switches like /dim:1920x1080
        static Dictionary<string, string> ParseArgs(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var arg in args)
            {
                if (arg.StartsWith("/") || arg.StartsWith("-"))
                {
                    var parts = arg.Substring(1).Split(new char[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        result[parts[0].Trim()] = parts[1].Trim('\"', ' ');
                    }
                }
            }
            return result;
        }

        // Helper to retrieve the Jpeg Encoder
        static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            return ImageCodecInfo.GetImageEncoders().FirstOrDefault(codec => codec.FormatID == format.Guid);
        }
    }
}