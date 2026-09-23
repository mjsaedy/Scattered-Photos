using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace CollageGenerator
{
    internal static class Program
    {
        private static readonly string[] ImageExtensions =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        private static int Main(string[] args)
        {
            // ---- Parse command line ------------------------------------------
            Size canvasSize;
            Color bgColor, borderColor;
            string sourceFolder;
            int count = int.MaxValue;   // default: use every image found

            try
            {
                canvasSize = ParseDimensions(GetArg(args, "dim"));
                bgColor    = ParseColor(GetArg(args, "bg"),   "bg");
                borderColor= ParseColor(GetArg(args, "border"),"border");

                sourceFolder = GetArg(args, "source");
                if (!Directory.Exists(sourceFolder))
                    return Fail("Source folder not found: " + sourceFolder);

                string numStr = TryGetArg(args, "num");
                if (numStr != null && (!int.TryParse(numStr, out count) || count <= 0))
                    return Fail("/num must be a positive integer.");
            }
            catch (ArgumentException ex)
            {
                return Fail(ex.Message);
            }

            // ---- Collect and shuffle candidate images ------------------------
            var files = Directory
                .EnumerateFiles(sourceFolder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(_ => Guid.NewGuid())          // random shuffle
                .Take(count)
                .ToList();

            if (files.Count == 0)
                return Fail("No image files found in " + sourceFolder);

            // ---- Create the canvas --------------------------------------------
            using (var canvas = new Bitmap(canvasSize.Width, canvasSize.Height, PixelFormat.Format24bppRgb))
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode      = SmoothingMode.AntiAlias;
                g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode    = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(bgColor);

                var rng = new Random();
                int   seed      = rng.Next();              // one shared seed per run
                float minDim   = Math.Min(canvasSize.Width, canvasSize.Height);

                foreach (string file in files)
                {
                    using (var img = LoadImage(file))
                    {
                        if (img == null) continue;         // skip unreadable/corrupt files

                        // Random target width between 25% and 65% of canvas width,
                        // then scale preserving aspect ratio to FIT that box.
                        float targetW = (float)(canvasSize.Width * (0.25 + rng.NextDouble() * 0.40));
                        float scale   = Math.Min(targetW / img.Width,
                                                 (float)canvasSize.Height * 0.85f / img.Height);
                        scale = Math.Min(scale, 1.0f);     // never upscale beyond reason
                        int w = Math.Max(16, (int)(img.Width  * scale));
                        int h = Math.Max(16, (int)(img.Height * scale));

                        // Random placement: center may sit anywhere on the canvas,
                        // allowing edges to bleed off-screen for a natural look.
                        float cx = (float)(rng.NextDouble() * canvasSize.Width);
                        float cy = (float)(rng.NextDouble() * canvasSize.Height);
                        float angle = (float)(rng.NextDouble() * 360.0);

                        DrawTiltedImage(g, img, cx, cy, w, h, angle, borderColor, minDim);
                    }
                }

                // ---- Save with a guaranteed-unique name -----------------------
                string outPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    string.Format("collage_{0:yyyyMMdd_HHmmss}_{1:N}.jpg",
                                  DateTime.Now, Guid.NewGuid()));

                SaveJpeg(canvas, outPath, 92L);
                Console.WriteLine("Collage written to: " + outPath);
            }

            return 0;
        }

        // Draws one image centered at (cx,cy), rotated by 'angle' degrees,
        // with a proportional border around it.
        private static void DrawTiltedImage(Graphics g, Image img,
            float cx, float cy, int w, int h, float angle, Color borderColor, float minDim)
        {
            float border = Math.Max(2f, Math.Min(w, h) * 0.02f);

            using (var pen = new Pen(borderColor, border))
            {
                var saved = g.Transform;
                try
                {
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(angle);

                    float hw = w / 2f, hh = h / 2f;

                    // Draw slightly oversized border rect behind the image so the
                    // border shows on all four sides even when antialiased.
                    g.DrawRectangle(pen,
                        -hw - border / 2f, -hh - border / 2f,
                        w + border, h + border);

                    g.DrawImage(img, -hw, -hh, w, h);
                }
                finally
                {
                    g.Transform = saved;   // restore even on failure
                }
            }
        }

        private static Image LoadImage(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    return Image.FromStream(fs);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Skipping '{0}': {1}", Path.GetFileName(path), ex.Message);
                return null;
            }
        }

        private static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

            using (var ep = new EncoderParameters(1))
            using (var p = new EncoderParameter(Encoder.Quality, quality))
            {
                ep.Param[0] = p;
                bmp.Save(path, jpegEncoder, ep);
            }
        }

        // ---- Argument helpers -------------------------------------------------

        private static string TryGetArg(string[] args, string name)
        {
            string prefix = "/" + name + ":";
            return args.FirstOrDefault(a =>
                a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                ?.Substring(prefix.Length);
        }

        private static string GetArg(string[] args, string name)
        {
            string val = TryGetArg(args, name);
            if (val == null)
                throw new ArgumentException("Missing required argument /" + name + ":...");
            return val;
        }

        private static Size ParseDimensions(string s)
        {
            var parts = s.Split('x', 'X');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out int w) ||
                !int.TryParse(parts[1], out int h) ||
                w <= 0 || h <= 0)
                throw new ArgumentException("/dim must be in the form WIDTHxHEIGHT (e.g. 1920x1080).");
            return new Size(w, h);
        }

        private static Color ParseColor(string s, string argName)
        {
            Color c = Color.FromName(s.Trim());
            if (c.A == 0 && c.R == 0 && c.G == 0 && c.B == 0 &&
                !string.Equals(s.Trim(), "Black", StringComparison.OrdinalIgnoreCase) &&
                !s.Trim().StartsWith("#"))
                throw new ArgumentException("Unknown color name for /" + argName + ": '" + s + "'");
            return c;
        }

        private static int Fail(string msg)
        {
            Console.Error.WriteLine(msg);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  GenerateCollage.exe /dim:1920x1080 /bg:Black /border:White /source:D:\\images [/num:20]");
            return 1;
        }
    }
}