using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace AltTabber
{
    public class CaptchaDetector
    {
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        // Minimum confidence score to trigger the alarm
        private const int ConfidenceThreshold = 70;

        // Fires once each time the captcha transitions from hidden -> visible
        public event Action? CaptchaAppeared;

        private bool _wasDetected = false;

        public async Task RunAsync(CancellationToken token)
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(3000, token);

                    int screenW = GetSystemMetrics(SM_CXSCREEN);
                    int screenH = GetSystemMetrics(SM_CYSCREEN);

                    // Capture once, reuse for both OCR and colour analysis
                    using var bmp = CaptureScreen(screenW, screenH);
                    if (bmp == null) continue;

                    int score = 0;

                    // --- Layer 1: OCR keyword scoring ---
                    using var softBmp = await BitmapToSoftwareBitmapAsync(bmp);
                    if (softBmp != null)
                    {
                        var result = await engine.RecognizeAsync(softBmp);
                        string text = result.Text;

                        if (text.Contains("Daeva Verification", StringComparison.OrdinalIgnoreCase))     score += 50;
                        if (text.Contains("chance(s) remaining", StringComparison.OrdinalIgnoreCase))    score += 25;
                        if (text.Contains("Enter the text you see", StringComparison.OrdinalIgnoreCase)) score += 20;
                        if (text.Contains("Failing to complete", StringComparison.OrdinalIgnoreCase))    score += 15;
                        if (text.Contains("time limit", StringComparison.OrdinalIgnoreCase))             score += 10;
                    }

                    // --- Layer 2: Colour signature analysis ---
                    // Only scan the centre region where the dialog appears
                    int cx = screenW / 2, cy = screenH / 2;
                    int rx = screenW / 5, ry = screenH / 3;
                    score += AnalyseColors(bmp, cx - rx, cy - ry, cx + rx, cy + ry);

                    bool detected = score >= ConfidenceThreshold;

                    if (detected && !_wasDetected)
                        CaptchaAppeared?.Invoke();

                    _wasDetected = detected;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* swallow transient errors */ }
            }
        }

        // Checks for the 3 distinctive Daeva Verification colours:
        // Dark dialog BG ~#2d2d2d, yellow countdown timer, cyan "chances remaining" text
        private static int AnalyseColors(Bitmap bmp, int x1, int y1, int x2, int y2)
        {
            int darkCount = 0, yellowCount = 0, cyanCount = 0, total = 0;

            // Sample every 8th pixel for speed
            for (int y = y1; y < y2; y += 8)
            {
                for (int x = x1; x < x2; x += 8)
                {
                    if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                    Color c = bmp.GetPixel(x, y);
                    total++;

                    // Dark grey dialog background: R,G,B all ~30-60
                    if (c.R is >= 25 and <= 65 && c.G is >= 25 and <= 65 && c.B is >= 25 and <= 65)
                        darkCount++;

                    // Yellow/amber countdown timer: high R, medium G, low B
                    if (c.R >= 220 && c.G is >= 140 and <= 210 && c.B <= 60)
                        yellowCount++;

                    // Cyan "chances remaining" text: low R, high G, high B
                    if (c.R <= 80 && c.G >= 180 && c.B >= 180)
                        cyanCount++;
                }
            }

            if (total == 0) return 0;

            int score = 0;
            double darkRatio   = (double)darkCount   / total;
            double yellowRatio = (double)yellowCount / total;
            double cyanRatio   = (double)cyanCount   / total;

            // Dark BG should be dominant in the centre when the dialog is open
            if (darkRatio   >= 0.25)  score += 20;
            if (yellowRatio >= 0.002) score += 15;
            if (cyanRatio   >= 0.002) score += 15;

            return score;
        }

        private static Bitmap? CaptureScreen(int w, int h)
        {
            try
            {
                var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                g.CopyFromScreen(0, 0, 0, 0, new Size(w, h));
                return bmp;
            }
            catch { return null; }
        }

        private static async Task<SoftwareBitmap?> BitmapToSoftwareBitmapAsync(Bitmap bmp)
        {
            try
            {
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                byte[] bytes = ms.ToArray();

                var iras = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(iras.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                }
                iras.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(iras);
                return await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }
            catch { return null; }
        }
    }
}
