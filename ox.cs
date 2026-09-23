using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace GenerateCollage
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // ---------------------------------------------------------------
            // Parse arguments
            // ---------------------------------------------------------------
            int width, height, num = 10;
            Color bgColor = Color.White, borderColor = Color.Black;
            string sourceFolder = null;

            foreach (string raw in args)
            {
                string a = raw.TrimStart('/', '-');
                int colon = a.IndexOf(':');
                if (colon < 0) continue;
                string key = a.Substring(0, colon).ToLowerInvariant();
                string val = a.Substring(colon + 1);

                switch (key)
                {
                    case "dim":
                        int x = val.IndexOf('x');
                        if (x < 0 ||
                            !int.TryParse(val.Substring(0, x), out width) ||
                            !int.TryParse(val.Substring(x + 1), out height) ||
                            width <= 0 || height <= 0)
                        {
                            Console.WriteLine("Invalid /dim value: " + val);
                            return 1;
                        }
                        CanvasWidth = width;
                        CanvasHeight = height;
                        break;

                    case "bg":
                        bgColor = ParseColor(val);
                        break;

                    case "border":
                        borderColor = ParseColor(val);
                        break;

                    case "num":
                        if (!int.TryParse(val, out num) || num <= 0)
                        {
                            Console.WriteLine("Invalid /num value: " + val);
                            return 1;
                        }
                        break;

                    case "source":
                        sourceFolder = val;
                        break;
                }
            }

            if (CanvasWidth <= 0 || CanvasHeight <= 0)
            {
                Console.WriteLine("Usage: GenerateCollage.exe /dim:1920x1080 /bg:Black /border:white /num:20 /source:D:\\images");
                return 1;
            }

            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                Console.WriteLine("Source folder not found: " + sourceFolder);
                return 1;
            }

            // ---------------------------------------------------------------
            // Gather image files
            // ---------------------------------------------------------------
            string[] extensions = { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.tif", "*.tiff" };
            List<string> files = new List<string>();
            foreach (string pattern in extensions)
                files.AddRange(Directory.GetFiles(sourceFolder, pattern, SearchOption.TopDirectoryOnly));

            if (files.Count == 0)
            {
                Console.WriteLine("No images found in: " + sourceFolder);
                return 1;
            }

            Random rng = new Random();

            // ---------------------------------------------------------------
            // Create canvas
            // ---------------------------------------------------------------
            using (Bitmap canvas = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(bgColor);

                // Randomize order so we don't always take the first N
                for (int i = files.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    string tmp = files[i]; files[i] = files[j]; files[j] = tmp;
                }

                // Max area each image may occupy, as a fraction of the canvas
                float maxFrac = Math.Max(0.35f, Math.Min(0.45f, 2.5f / num));

                int drawn = 0;
                foreach (string file in files)
                {
                    if (drawn >= num) break;

                    Image img;
                    try { img = Image.FromFile(file); }
                    catch { continue; } // skip corrupt / locked files

                    using (img)
                    {
                        // --- Compute target size (scaled to fit) ---
                        float fraction = maxFrac * (0.6f + 0.4f * (float)rng.NextDouble());
                        float maxW = CanvasWidth * fraction;
                        float maxH = CanvasHeight * fraction;

                        float scale = Math.Min(maxW / img.Width, maxH / img.Height);
                        int w = Math.Max(1, (int)(img.Width * scale));
                        int h = Math.Max(1, (int)(img.Height * scale));

                        // --- Random position: allow partial bleed off the edges ---
                        int posX = rng.Next(-(w / 3), Math.Max(1, CanvasWidth - (w * 2) / 3));
                        int posY = rng.Next(-(h / 3), Math.Max(1, CanvasHeight - (h * 2) / 3));

                        // --- Random rotation ---
                        float angle = (float)(rng.NextDouble() * 40.0 - 20.0); // -20 to +20 degrees

                        RectangleF destRect = new RectangleF(0, 0, w, h);

                        g.TranslateTransform(posX + w / 2f, posY + h / 2f);
                        g.RotateTransform(angle);
                        g.TranslateTransform(-w / 2f, -h / 2f);

                        // --- Draw a shadow for a more realistic "piled on desktop" look ---
                        using (GraphicsPath shadowPath = new GraphicsPath())
                        {
                            shadowPath.AddRectangle(destRect);
                            using (PathGradientBrush shadowBrush = new PathGradientBrush(shadowPath))
                            {
                                shadowBrush.CenterColor = Color.FromArgb(90, 0, 0, 0);
                                shadowBrush.SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) };
                                g.TranslateTransform(6, 6);
                                g.FillPath(shadowBrush, shadowPath);
                                g.TranslateTransform(-6, -6);
                            }
                        }

                        // --- Draw border ---
                        using (Pen borderPen = new Pen(borderColor, Math.Max(3f, w * 0.015f)))
                        {
                            borderPen.Alignment = PenAlignment.Inset;
                            g.DrawRectangle(borderPen, 0, 0, w, h);
                        }

                        // --- Draw image inside border ---
                        g.DrawImage(img, destRect);

                        g.ResetTransform();
                        drawn++;
                    }
                }

                if (drawn == 0)
                {
                    Console.WriteLine("No images could be loaded.");
                    return 1;
                }

                // ---------------------------------------------------------------
                // Unique output filename: Collage_YYYYMMDD_HHMMSS_xxxx.jpg
                // ---------------------------------------------------------------
                string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
                string outPath;
                do
                {
                    string name = string.Format("Collage_{0:yyyyMMdd_HHmmss}_{1:x4}.jpg",
                        DateTime.Now, rng.Next(0x10000));
                    outPath = Path.Combine(exeDir, name);
                } while (File.Exists(outPath));

                SaveJpeg(canvas, outPath, 92L);

                Console.WriteLine("Saved {0} images to: {1}", drawn, outPath);
            }

            return 0;
        }

        private static int CanvasWidth;
        private static int CanvasHeight;

        private static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            EncoderParameters eps = new EncoderParameters(1);
            eps.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            ImageCodecInfo jpegCodec = null;
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
                if (codec.MimeType == "image/jpeg") { jpegCodec = codec; break; }
            if (jpegCodec != null)
                bmp.Save(path, jpegCodec, eps);
            else
                bmp.Save(path, ImageFormat.Jpeg);
        }

        private static Color ParseColor(string name)
        {
            // Try named colors first via reflection on Color structure properties
            var prop = typeof(Color).GetProperty(name.Trim(),
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.IgnoreCase);
            if (prop != null && (Color)prop.GetValue(null, null) is Color)
            {
                Color c = (Color)prop.GetValue(null, null);
                if (c.A == 255 || c.IsEmpty == false) return c;
            }

            // Fallback: hex string like FF8000 or #FF8000
            string hex = name.TrimStart('#');
            try
            {
                return ColorTranslator.FromHtml("#" + hex);
            }
            catch
            {
                Console.WriteLine("Unknown color: {0}, using White.", name);
                return Color.White;
            }
        }
    }
}
