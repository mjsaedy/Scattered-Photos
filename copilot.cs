using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace GenerateCollage
{
    class Program
    {
        static Random rnd = new Random();

        static void Main(string[] args)
        {
            string dimArg    = GetArg(args, "/dim:");
            string bgArg     = GetArg(args, "/bg:");
            string borderArg = GetArg(args, "/border:");
            string numArg    = GetArg(args, "/num:");
            string sourceArg = GetArg(args, "/source:");

            if (string.IsNullOrEmpty(dimArg) ||
                string.IsNullOrEmpty(bgArg) ||
                string.IsNullOrEmpty(borderArg) ||
                string.IsNullOrEmpty(sourceArg))
            {
                Console.WriteLine("Usage: GenerateCollage.exe /dim:WxH /bg:Color /border:Color [/num:N] /source:Folder");
                return;
            }

            // Parse dimensions
            var dimParts = dimArg.Split('x');
            if (dimParts.Length != 2 ||
                !int.TryParse(dimParts[0], out int canvasW) ||
                !int.TryParse(dimParts[1], out int canvasH))
            {
                Console.WriteLine("Invalid /dim argument. Use e.g. /dim:1920x1080");
                return;
            }

            // Colors
            Color bgColor     = Color.FromName(bgArg);
            Color borderColor = Color.FromName(borderArg);

            // Number of images (default: 10)
            int numImages = 10;
            if (!string.IsNullOrEmpty(numArg) && !int.TryParse(numArg, out numImages))
            {
                Console.WriteLine("Invalid /num argument.");
                return;
            }

            // Source folder
            string folder = sourceArg;
            if (!Directory.Exists(folder))
            {
                Console.WriteLine("Source folder does not exist.");
                return;
            }

            var allFiles = Directory.GetFiles(folder)
                                    .Where(f =>
                                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

            if (allFiles.Count == 0)
            {
                Console.WriteLine("No images found in source folder.");
                return;
            }

            // Randomize which images are selected
            var files = allFiles
                        .OrderBy(_ => rnd.Next())
                        .Take(numImages)
                        .ToList();

            using (Bitmap canvas = new Bitmap(canvasW, canvasH))
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(bgColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                foreach (var file in files)
                {
                    try
                    {
                        using (Image img = Image.FromFile(file))
                        {
                            // Random scale factor (0.3 to 0.8)
                            double scale = rnd.NextDouble() * 0.5 + 0.3;
                            int newW = (int)(img.Width * scale);
                            int newH = (int)(img.Height * scale);

                            if (newW < 1 || newH < 1)
                                continue;

                            // Create scaled bitmap
                            using (Bitmap scaled = new Bitmap(newW, newH))
                            using (Graphics sg = Graphics.FromImage(scaled))
                            {
                                sg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                                sg.Clear(Color.Transparent);
                                sg.DrawImage(img, new Rectangle(0, 0, newW, newH));

                                // Draw border on the scaled image (so it rotates with the photo)
                                using (Pen borderPen = new Pen(borderColor, 5))
                                {
                                    sg.DrawRectangle(borderPen, 0, 0, newW - 1, newH - 1);
                                }

                                // Random rotation
                                float angle = rnd.Next(-25, 25);

                                // Rotate the scaled image (with border)
                                using (Bitmap rotated = RotateImage(scaled, angle))
                                {
                                    int x = rnd.Next(0, Math.Max(1, canvasW - rotated.Width));
                                    int y = rnd.Next(0, Math.Max(1, canvasH - rotated.Height));

                                    g.DrawImage(rotated, x, y);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error with file '{0}': {1}", file, ex.Message);
                    }
                }

                // Unique output filename
                string outputName = "collage_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg";
                string outputPath = Path.Combine(Environment.CurrentDirectory, outputName);

                canvas.Save(outputPath, ImageFormat.Jpeg);
                Console.WriteLine("Saved: " + outputPath);
            }
        }

        static string GetArg(string[] args, string prefix)
        {
            foreach (var a in args)
            {
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return a.Substring(prefix.Length);
            }
            return null;
        }

        static Bitmap RotateImage(Bitmap bmp, float angle)
        {
            float rad = angle * (float)Math.PI / 180f;
            double cos = Math.Abs(Math.Cos(rad));
            double sin = Math.Abs(Math.Sin(rad));

            int newW = (int)(bmp.Width * cos + bmp.Height * sin);
            int newH = (int)(bmp.Width * sin + bmp.Height * cos);

            Bitmap rotated = new Bitmap(newW, newH);
            rotated.SetResolution(bmp.HorizontalResolution, bmp.VerticalResolution);

            using (Graphics g = Graphics.FromImage(rotated))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Move origin to center of new image
                g.TranslateTransform(newW / 2f, newH / 2f);
                g.RotateTransform(angle);
                g.TranslateTransform(-bmp.Width / 2f, -bmp.Height / 2f);

                g.DrawImage(bmp, new Point(0, 0));
            }

            return rotated;
        }
    }
}
