using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

class Program {
    static Random random = new Random();

    static void Main(string[] args) {
        if (args.Length == 0) {
            ShowUsage();
            return;
        }

        string sourceFolder = null;
        string outputFile = null;
        int canvasWidth = 1920;
        int canvasHeight = 1080;
        int numberOfImages = 20;
        Color backgroundColor = Color.Black;
        Color borderColor = Color.White;

        foreach (string arg in args) {
            if (arg.StartsWith("/dim:", StringComparison.OrdinalIgnoreCase)) {
                string value = arg.Substring(5);

                if (!ParseDimensions(value, out canvasWidth, out canvasHeight)) {
                    Console.WriteLine("Invalid dimensions: " + value);
                    return;
                }
            }
            else if (arg.StartsWith("/bg:", StringComparison.OrdinalIgnoreCase)) {
                string value = arg.Substring(4);

                if (!TryParseColor(value, out backgroundColor)) {
                    Console.WriteLine("Invalid background color: " + value);
                    return;
                }
            }
            else if (arg.StartsWith("/border:", StringComparison.OrdinalIgnoreCase)) {
                string value = arg.Substring(8);

                if (!TryParseColor(value, out borderColor)) {
                    Console.WriteLine("Invalid border color: " + value);
                    return;
                }
            }
            else if (arg.StartsWith("/source:", StringComparison.OrdinalIgnoreCase)) {
                sourceFolder = arg.Substring(8).Trim('"');
            }
            else if (arg.StartsWith("/num:", StringComparison.OrdinalIgnoreCase)) {
                if (!int.TryParse(arg.Substring(5), out numberOfImages) ||
                    numberOfImages < 1) {
                    Console.WriteLine("Invalid image count.");
                    return;
                }
            }
            else if (arg.StartsWith("/output:", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("/out:", StringComparison.OrdinalIgnoreCase)) {
                int p = arg.IndexOf(':');
                outputFile = arg.Substring(p + 1).Trim('"');
            }
            else if (arg.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("/?", StringComparison.OrdinalIgnoreCase)) {
                ShowUsage();
                return;
            }
            else {
                Console.WriteLine("Unknown argument: " + arg);
                ShowUsage();
                return;
            }
        }

        if (String.IsNullOrEmpty(sourceFolder)) {
            Console.WriteLine("Source folder was not specified.");
            ShowUsage();
            return;
        }

        if (!Directory.Exists(sourceFolder)) {
            Console.WriteLine("Folder does not exist: " + sourceFolder);
            return;
        }

        List<string> imageFiles = GetImageFiles(sourceFolder);

        if (imageFiles.Count == 0) {
            Console.WriteLine("No supported images were found.");
            return;
        }

        if (numberOfImages > imageFiles.Count) {
            numberOfImages = imageFiles.Count;
            Console.WriteLine("Only {0} images are available; using all of them.",
                numberOfImages);
        }

        if (String.IsNullOrEmpty(outputFile)) {
            outputFile = GetUniqueOutputFile(".");
        }
        else {
            outputFile = Path.GetFullPath(outputFile);

            if (File.Exists(outputFile)) {
                Console.WriteLine("Output file already exists: " + outputFile);
                return;
            }
        }

        Console.WriteLine("Creating collage...");
        Console.WriteLine("Canvas: {0} x {1}", canvasWidth, canvasHeight);
        Console.WriteLine("Images: {0}", numberOfImages);

        try {
            using (Bitmap canvas = new Bitmap(
                canvasWidth,
                canvasHeight,
                PixelFormat.Format24bppRgb)) {

                using (Graphics g = Graphics.FromImage(canvas)) {
                    g.Clear(backgroundColor);

                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;

                    // Shuffle the files and select the requested number.
                    Shuffle(imageFiles);

                    for (int i = 0; i < numberOfImages; i++) {
                        string file = imageFiles[i];

                        Console.WriteLine(
                            "[{0}/{1}] {2}",
                            i + 1,
                            numberOfImages,
                            Path.GetFileName(file));

                        try {
                            DrawImageOnCanvas(
                                g,
                                file,
                                canvasWidth,
                                canvasHeight,
                                borderColor);
                        }
                        catch (Exception ex) {
                            Console.WriteLine(
                                "  Skipped: {0}",
                                ex.Message);
                        }
                    }
                }

                SaveJpeg(canvas, outputFile, 85L);
            }

            Console.WriteLine();
            Console.WriteLine("Collage created:");
            Console.WriteLine(outputFile);
        }
        catch (Exception ex) {
            Console.WriteLine("Error: " + ex.Message);
        }
    }

    static void DrawImageOnCanvas(
        Graphics g,
        string file,
        int canvasWidth,
        int canvasHeight,
        Color borderColor) {

        using (Image source = Image.FromFile(file)) {
            // Try to honor the camera's EXIF orientation.
            TryCorrectOrientation(source);

            int sourceWidth = source.Width;
            int sourceHeight = source.Height;

            if (sourceWidth <= 0 || sourceHeight <= 0)
                return;

            /*
             * Determine a random target size.
             *
             * The image's largest dimension will normally occupy
             * roughly 18% to 40% of the canvas' largest dimension.
             * This creates a mixture of large and small photographs.
             */
            double canvasLargest = Math.Max(canvasWidth, canvasHeight);

            double sizeFactor = 0.18 + random.NextDouble() * 0.22;
            double targetLargest = canvasLargest * sizeFactor;

            double scale = targetLargest /
                Math.Max(sourceWidth, sourceHeight);

            int imageWidth = Math.Max(20, (int)Math.Round(sourceWidth * scale));
            int imageHeight = Math.Max(20, (int)Math.Round(sourceHeight * scale));

            // Border thickness scales somewhat with the image.
            int border = Math.Max(2, Math.Min(imageWidth, imageHeight) / 100);

            int totalWidth = imageWidth + border * 2;
            int totalHeight = imageHeight + border * 2;

            /*
             * Create a temporary bitmap containing:
             *
             *     border
             *     image
             *     border
             *
             * This allows the entire image + border to be rotated as
             * one object.
             */
            using (Bitmap item = new Bitmap(
                totalWidth,
                totalHeight,
                PixelFormat.Format32bppArgb)) {

                using (Graphics ig = Graphics.FromImage(item)) {
                    ig.SmoothingMode = SmoothingMode.HighQuality;
                    ig.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    ig.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ig.CompositingQuality = CompositingQuality.HighQuality;

                    ig.Clear(Color.Transparent);

                    // Draw the border.
                    using (SolidBrush brush =
                        new SolidBrush(borderColor)) {

                        ig.FillRectangle(
                            brush,
                            0,
                            0,
                            totalWidth,
                            totalHeight);
                    }

                    // Draw the photograph inside the border.
                    Rectangle destination = new Rectangle(
                        border,
                        border,
                        imageWidth,
                        imageHeight);

                    ig.DrawImage(
                        source,
                        destination,
                        0,
                        0,
                        source.Width,
                        source.Height,
                        GraphicsUnit.Pixel);
                }

                /*
                 * Random rotation.
                 *
                 * Most desktop-photo collages look more natural with
                 * relatively small rotations rather than extreme ones.
                 */
                float angle =
                    (float)(random.NextDouble() * 50.0 - 25.0);

                /*
                 * Choose a random center position.
                 *
                 * Allowing the center to be outside the canvas means
                 * that some photographs can be partially clipped at
                 * the edges, which makes the collage look less rigid.
                 */
                double edgeAllowance = 0.20;

                float centerX = (float)(
                    -canvasWidth * edgeAllowance +
                    random.NextDouble() *
                    canvasWidth * (1.0 + 2.0 * edgeAllowance));

                float centerY = (float)(
                    -canvasHeight * edgeAllowance +
                    random.NextDouble() *
                    canvasHeight * (1.0 + 2.0 * edgeAllowance));

                /*
                 * Add a subtle shadow.
                 *
                 * A real drop shadow makes overlapping photographs
                 * much easier to distinguish.
                 */
                DrawRotatedItem(
                    g,
                    item,
                    centerX + 5,
                    centerY + 5,
                    angle,
                    true,
                    Color.FromArgb(90, Color.Black));

                DrawRotatedItem(
                    g,
                    item,
                    centerX,
                    centerY,
                    angle,
                    false,
                    Color.Transparent);
            }
        }
    }

    static void DrawRotatedItem(
        Graphics g,
        Bitmap item,
        float centerX,
        float centerY,
        float angle,
        bool shadow,
        Color shadowColor) {

        GraphicsState state = g.Save();

        try {
            g.TranslateTransform(centerX, centerY);
            g.RotateTransform(angle);

            float x = -item.Width / 2.0f;
            float y = -item.Height / 2.0f;

            if (shadow) {
                using (SolidBrush brush = new SolidBrush(shadowColor)) {
                    g.FillRectangle(
                        brush,
                        x,
                        y,
                        item.Width,
                        item.Height);
                }
            }
            else {
                g.DrawImage(
                    item,
                    x,
                    y,
                    item.Width,
                    item.Height);
            }
        }
        finally {
            g.Restore(state);
        }
    }

    static List<string> GetImageFiles(string folder) {
        List<string> files = new List<string>();

        string[] allFiles;

        try {
            allFiles = Directory.GetFiles(folder);
        }
        catch (Exception ex) {
            Console.WriteLine("Cannot read folder: " + ex.Message);
            return files;
        }

        foreach (string file in allFiles) {
            string ext = Path.GetExtension(file);

            if (IsImageExtension(ext))
                files.Add(file);
        }

        return files;
    }

    static bool IsImageExtension(string extension) {
        switch (extension.ToLowerInvariant()) {
            case ".jpg":
            case ".jpeg":
            case ".png":
            case ".bmp":
            case ".gif":
            case ".tif":
            case ".tiff":
                return true;

            default:
                return false;
        }
    }

    static void Shuffle(List<string> list) {
        for (int i = list.Count - 1; i > 0; i--) {
            int j = random.Next(i + 1);

            string temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    static bool ParseDimensions(
        string value,
        out int width,
        out int height) {

        width = 0;
        height = 0;

        string[] parts = value.ToLowerInvariant().Split('x');

        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out width))
            return false;

        if (!int.TryParse(parts[1], out height))
            return false;

        return width > 0 && height > 0;
    }

    static bool TryParseColor(string value, out Color color) {
        color = Color.Empty;

        value = value.Trim();

        // First try known named colors.
        Color named = Color.FromName(value);

        if (named.IsKnownColor) {
            color = named;
            return true;
        }

        /*
         * Also support:
         *
         *   #RRGGBB
         *   RRGGBB
         *   #AARRGGBB
         *   AARRGGBB
         */
        string hex = value;

        if (hex.StartsWith("#"))
            hex = hex.Substring(1);

        try {
            if (hex.Length == 6) {
                int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                int b = Convert.ToInt32(hex.Substring(4, 2), 16);

                color = Color.FromArgb(r, g, b);
                return true;
            }

            if (hex.Length == 8) {
                int a = Convert.ToInt32(hex.Substring(0, 2), 16);
                int r = Convert.ToInt32(hex.Substring(2, 2), 16);
                int g = Convert.ToInt32(hex.Substring(4, 2), 16);
                int b = Convert.ToInt32(hex.Substring(6, 2), 16);

                color = Color.FromArgb(a, r, g, b);
                return true;
            }
        }
        catch {
        }

        return false;
    }

    static string GetUniqueOutputFile(string folder) {
        string file = Path.Combine(folder, "Collage.jpg");

        if (!File.Exists(file))
            return file;

        for (int i = 1; i < 1000000; i++) {
            file = Path.Combine(
                folder,
                String.Format("Collage_{0:000}.jpg", i));

            if (!File.Exists(file))
                return file;
        }

        throw new IOException("Unable to generate a unique output filename.");
    }

    static void SaveJpeg(Bitmap bitmap, string file, long quality) {
        ImageCodecInfo jpegCodec = GetEncoder(ImageFormat.Jpeg);

        if (jpegCodec == null)
            throw new Exception("JPEG encoder was not found.");

        using (EncoderParameters parameters = new EncoderParameters(1)) {
            using (EncoderParameter parameter =
                new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality,
                    quality)) {

                parameters.Param[0] = parameter;

                bitmap.Save(
                    file,
                    jpegCodec,
                    parameters);
            }
        }
    }

    static ImageCodecInfo GetEncoder(ImageFormat format) {
        ImageCodecInfo[] codecs =
            ImageCodecInfo.GetImageEncoders();

        foreach (ImageCodecInfo codec in codecs) {
            if (codec.FormatID == format.Guid)
                return codec;
        }

        return null;
    }

    static void TryCorrectOrientation(Image image) {
        const int OrientationId = 0x0112;

        try {
            if (!Array.Exists(
                image.PropertyIdList,
                delegate(int id) {
                    return id == OrientationId;
                }))
                return;

            PropertyItem property =
                image.GetPropertyItem(OrientationId);

            ushort orientation =
                BitConverter.ToUInt16(property.Value, 0);

            RotateFlipType type = RotateFlipType.RotateNoneFlipNone;

            switch (orientation) {
                case 2:
                    type = RotateFlipType.RotateNoneFlipX;
                    break;

                case 3:
                    type = RotateFlipType.Rotate180FlipNone;
                    break;

                case 4:
                    type = RotateFlipType.RotateNoneFlipY;
                    break;

                case 5:
                    type = RotateFlipType.Rotate90FlipX;
                    break;

                case 6:
                    type = RotateFlipType.Rotate90FlipNone;
                    break;

                case 7:
                    type = RotateFlipType.Rotate270FlipX;
                    break;

                case 8:
                    type = RotateFlipType.Rotate270FlipNone;
                    break;
            }

            if (type != RotateFlipType.RotateNoneFlipNone)
                image.RotateFlip(type);
        }
        catch {
            // EXIF orientation is optional. Ignore malformed EXIF data.
        }
    }

    static void ShowUsage() {
        Console.WriteLine();
        Console.WriteLine("GenerateCollage - Random photo collage generator");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine();
        Console.WriteLine(
            "  GenerateCollage.exe /dim:1920x1080 /bg:Black " +
            "/border:White /source:D:\\images /num:20");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine(
            "  /dim:WIDTHxHEIGHT    Output dimensions.");
        Console.WriteLine(
            "  /bg:COLOR             Background color.");
        Console.WriteLine(
            "  /border:COLOR         Image border color.");
        Console.WriteLine(
            "  /source:FOLDER        Folder containing images.");
        Console.WriteLine(
            "  /num:NUMBER           Number of images to use.");
        Console.WriteLine(
            "  /out:FILE             Optional output filename.");
        Console.WriteLine();
        Console.WriteLine("Colors can be named colors or hexadecimal:");
        Console.WriteLine("  /bg:Black");
        Console.WriteLine("  /bg:#202020");
        Console.WriteLine("  /border:#FFFFFF");
        Console.WriteLine();
        Console.WriteLine(
            "If /out is omitted, Collage.jpg, Collage_001.jpg, etc.");
        Console.WriteLine("will be generated in the source folder.");
        Console.WriteLine();
    }
}