using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpeedPhotos.App
{
    internal sealed class ExportPhotoPlacement
    {
        public int PhotoIndex { get; set; }
        public int Page { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Rotated { get; set; }
    }

    internal static class ExportService
    {
        private const double RenderDpi = 150.0;
        private const double DipsPerInch = 96.0;

        public static List<string> ExportJpegPages(string selectedFile, double pageWidthCm,
            double pageHeightCm, int pageCount, IList<ExportPhotoPlacement> placements,
            IList<string> photoPaths, bool preserveAspect, bool grayscale)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(selectedFile));
            string baseName = Path.GetFileNameWithoutExtension(selectedFile);
            var files = new List<string>();

            for (int page = 0; page < pageCount; page++)
            {
                string output = pageCount == 1
                    ? Path.Combine(directory, baseName + ".jpg")
                    : Path.Combine(directory, baseName + "-Hoja-" +
                        (page + 1).ToString("000", CultureInfo.InvariantCulture) + ".jpg");
                RenderTargetBitmap bitmap = RenderPage(page, pageWidthCm, pageHeightCm,
                    placements, photoPaths, preserveAspect, grayscale);
                SaveJpeg(bitmap, output);
                files.Add(output);
            }

            return files;
        }

        public static void ExportPdf(string outputFile, double pageWidthCm, double pageHeightCm,
            int pageCount, IList<ExportPhotoPlacement> placements, IList<string> photoPaths,
            bool preserveAspect, bool grayscale)
        {
            if (pageCount < 1) throw new InvalidOperationException("No hay hojas para exportar.");

            double pageWidthPoints = pageWidthCm * 72.0 / 2.54;
            double pageHeightPoints = pageHeightCm * 72.0 / 2.54;
            var pageImages = new List<byte[]>();
            int pixelWidth = 0;
            int pixelHeight = 0;

            for (int page = 0; page < pageCount; page++)
            {
                RenderTargetBitmap bitmap = RenderPage(page, pageWidthCm, pageHeightCm,
                    placements, photoPaths, preserveAspect, grayscale);
                pixelWidth = bitmap.PixelWidth;
                pixelHeight = bitmap.PixelHeight;
                pageImages.Add(EncodeJpeg(bitmap));
            }

            WritePdf(outputFile, pageImages, pixelWidth, pixelHeight,
                pageWidthPoints, pageHeightPoints);
        }

        private static RenderTargetBitmap RenderPage(int page, double pageWidthCm,
            double pageHeightCm, IList<ExportPhotoPlacement> placements,
            IList<string> photoPaths, bool preserveAspect, bool grayscale)
        {
            int pixelWidth = Math.Max(1, (int)Math.Round(pageWidthCm * RenderDpi / 2.54));
            int pixelHeight = Math.Max(1, (int)Math.Round(pageHeightCm * RenderDpi / 2.54));
            double dipWidth = pageWidthCm * DipsPerInch / 2.54;
            double dipHeight = pageHeightCm * DipsPerInch / 2.54;

            var visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, dipWidth, dipHeight));

                foreach (ExportPhotoPlacement placement in placements)
                {
                    if (placement.Page != page) continue;
                    if (placement.PhotoIndex < 0 || placement.PhotoIndex >= photoPaths.Count)
                        continue;

                    BitmapImage bitmap = LoadBitmap(photoPaths[placement.PhotoIndex]);
                    ImageSource source = grayscale
                        ? (ImageSource)new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0)
                        : bitmap;

                    double x = placement.X * DipsPerInch / 2.54;
                    double y = placement.Y * DipsPerInch / 2.54;
                    double width = placement.Width * DipsPerInch / 2.54;
                    double height = placement.Height * DipsPerInch / 2.54;
                    var target = new Rect(x, y, width, height);

                    drawing.PushClip(new RectangleGeometry(target));
                    if (placement.Rotated)
                        drawing.PushTransform(new RotateTransform(90, x + width / 2.0, y + height / 2.0));

                    Rect imageRect = target;
                    if (preserveAspect)
                    {
                        double ratio = (double)bitmap.PixelWidth / Math.Max(1, bitmap.PixelHeight);
                        double targetRatio = width / Math.Max(0.0001, height);
                        double fittedWidth = width;
                        double fittedHeight = height;
                        if (ratio > targetRatio) fittedHeight = width / ratio;
                        else fittedWidth = height * ratio;
                        imageRect = new Rect(x + (width - fittedWidth) / 2.0,
                            y + (height - fittedHeight) / 2.0, fittedWidth, fittedHeight);
                    }

                    drawing.DrawImage(source, imageRect);
                    if (placement.Rotated) drawing.Pop();
                    drawing.Pop();
                }
            }

            var result = new RenderTargetBitmap(pixelWidth, pixelHeight,
                RenderDpi, RenderDpi, PixelFormats.Pbgra32);
            result.Render(visual);
            result.Freeze();
            return result;
        }

        private static BitmapImage LoadBitmap(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Path.GetFullPath(path));
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static void SaveJpeg(BitmapSource bitmap, string path)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                encoder.Save(stream);
        }

        private static byte[] EncodeJpeg(BitmapSource bitmap)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private static void WritePdf(string outputFile, IList<byte[]> images, int imageWidth,
            int imageHeight, double pageWidthPoints, double pageHeightPoints)
        {
            using (var stream = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
            {
                var offsets = new List<long>();
                offsets.Add(0);
                stream.Write(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A,
                    0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 15);

                WriteObject(stream, offsets, 1,
                    "<< /Type /Catalog /Pages 2 0 R >>");
                var kids = new StringBuilder();
                for (int i = 0; i < images.Count; i++)
                    kids.Append(3 + i * 3).Append(" 0 R ");
                WriteObject(stream, offsets, 2,
                    "<< /Type /Pages /Kids [ " + kids + "] /Count " + images.Count + " >>");

                for (int i = 0; i < images.Count; i++)
                {
                    int pageId = 3 + i * 3;
                    int contentId = pageId + 1;
                    int imageId = pageId + 2;
                    string pageDictionary = String.Format(CultureInfo.InvariantCulture,
                        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.###} {1:0.###}] " +
                        "/Resources << /XObject << /Im0 {2} 0 R >> >> /Contents {3} 0 R >>",
                        pageWidthPoints, pageHeightPoints, imageId, contentId);
                    WriteObject(stream, offsets, pageId, pageDictionary);

                    string commands = String.Format(CultureInfo.InvariantCulture,
                        "q\n{0:0.###} 0 0 {1:0.###} 0 0 cm\n/Im0 Do\nQ\n",
                        pageWidthPoints, pageHeightPoints);
                    byte[] commandBytes = Encoding.ASCII.GetBytes(commands);
                    WriteStreamObject(stream, offsets, contentId, String.Empty, commandBytes);

                    string imageDictionary = String.Format(CultureInfo.InvariantCulture,
                        "/Type /XObject /Subtype /Image /Width {0} /Height {1} " +
                        "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode",
                        imageWidth, imageHeight);
                    WriteStreamObject(stream, offsets, imageId, imageDictionary, images[i]);
                }

                long xrefPosition = stream.Position;
                WriteAscii(stream, "xref\n0 " + offsets.Count + "\n");
                WriteAscii(stream, "0000000000 65535 f \n");
                for (int i = 1; i < offsets.Count; i++)
                    WriteAscii(stream, offsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
                WriteAscii(stream, "trailer\n<< /Size " + offsets.Count + " /Root 1 0 R >>\n");
                WriteAscii(stream, "startxref\n" + xrefPosition.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
            }
        }

        private static void WriteObject(Stream stream, IList<long> offsets, int id, string body)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, id + " 0 obj\n" + body + "\nendobj\n");
        }

        private static void WriteStreamObject(Stream stream, IList<long> offsets, int id,
            string dictionary, byte[] data)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, id + " 0 obj\n<< " + dictionary + " /Length " +
                data.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
            stream.Write(data, 0, data.Length);
            WriteAscii(stream, "\nendstream\nendobj\n");
        }

        private static void WriteAscii(Stream stream, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
