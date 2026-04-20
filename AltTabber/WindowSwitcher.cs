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

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        // Disables the foreground lock timeout so focus stealing works outside of debugger
        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
        private const uint SPIF_SENDCHANGE = 0x0002;

        public static void BringToFront(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;

            // Remove the foreground lock so this works outside the debugger
            SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, SPIF_SENDCHANGE);

            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);
            else
                ShowWindow(hWnd, SW_SHOW);

            IntPtr foregroundHwnd = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foregroundHwnd, IntPtr.Zero);
            uint currentThread = GetCurrentThreadId();

            bool attached = false;
            if (foregroundThread != currentThread)
            {
                AttachThreadInput(currentThread, foregroundThread, true);
                attached = true;
            }

            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);

            if (attached)
                AttachThreadInput(currentThread, foregroundThread, false);

            SwitchToThisWindow(hWnd, true);
        }

        public static IntPtr GetActiveWindow() => GetForegroundWindow();
    }
}
