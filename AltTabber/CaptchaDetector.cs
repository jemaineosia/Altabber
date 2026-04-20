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

        // Fires once each time the captcha transitions from hidden → visible
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

                    using var softBmp = await CaptureScreenAsync();
                    if (softBmp == null) continue;

                    var result = await engine.RecognizeAsync(softBmp);
                    bool detected = result.Text.Contains("Daeva", StringComparison.OrdinalIgnoreCase);

                    // Only fire on the rising edge (appeared, not while it stays on screen)
                    if (detected && !_wasDetected)
                        CaptchaAppeared?.Invoke();

                    _wasDetected = detected;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* swallow transient capture/OCR errors */ }
            }
        }

        private static async Task<SoftwareBitmap?> CaptureScreenAsync()
        {
            try
            {
                int w = GetSystemMetrics(SM_CXSCREEN);
                int h = GetSystemMetrics(SM_CYSCREEN);

                using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(0, 0, 0, 0, new Size(w, h));

                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                byte[] bytes = ms.ToArray();

                // Write into a WinRT stream for BitmapDecoder
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
