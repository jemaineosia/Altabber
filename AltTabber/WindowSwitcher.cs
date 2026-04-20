using System;
using System.Runtime.InteropServices;

namespace AltTabber
{
    public static class WindowSwitcher
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        public static void BringToFront(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
        }

        public static IntPtr GetActiveWindow() => GetForegroundWindow();
    }
}
