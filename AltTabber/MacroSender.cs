using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AltTabber
{
    public static class MacroSender
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD    = 1;
        private const uint KEYEVENTF_KEYUP   = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        // Wait up to 5 seconds for the target window to become foreground
        public static async Task WaitForForegroundAsync(IntPtr targetHwnd, CancellationToken token)
        {
            for (int i = 0; i < 50; i++)
            {
                token.ThrowIfCancellationRequested();
                if (GetForegroundWindow() == targetHwnd) return;
                await Task.Delay(100, token);
            }
        }

        public static async Task SendKeyAsync(IntPtr targetHwnd, string key, int repeatCount, int delayMs, CancellationToken token)
        {
            if (string.IsNullOrEmpty(key)) return;

            ushort vk = ResolveVirtualKey(key);
            if (vk == 0) return;

            // Use scan code — games using DirectInput/Raw Input only see scan codes
            ushort scan = (ushort)MapVirtualKey(vk, 0);

            // Wait until the game is actually the foreground window before injecting
            await WaitForForegroundAsync(targetHwnd, token);

            for (int i = 0; i < repeatCount; i++)
            {
                token.ThrowIfCancellationRequested();

                // Key down (scan code, wVk must be 0 for hardware-like injection)
                SendInput(1, new[] { MakeKey(scan, KEYEVENTF_SCANCODE) }, Marshal.SizeOf<INPUT>());
                await Task.Delay(30, token); // hold duration
                // Key up
                SendInput(1, new[] { MakeKey(scan, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP) }, Marshal.SizeOf<INPUT>());

                await Task.Delay(delayMs, token);
            }
        }

        private static INPUT MakeKey(ushort scan, uint flags) => new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags, dwExtraInfo = IntPtr.Zero }
            }
        };

        private static ushort ResolveVirtualKey(string key)
        {
            if (key.Length == 1)
            {
                short result = VkKeyScan(key[0]);
                if (result != -1)
                    return (ushort)(result & 0xFF);
            }

            return key.ToUpperInvariant() switch
            {
                "F1"    => 0x70, "F2"  => 0x71, "F3"  => 0x72, "F4"  => 0x73,
                "F5"    => 0x74, "F6"  => 0x75, "F7"  => 0x76, "F8"  => 0x77,
                "F9"    => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
                "SPACE" => 0x20, "ENTER" => 0x0D, "TAB" => 0x09,
                "HOME"  => 0x24, "END"  => 0x23, "PGUP" => 0x21, "PGDN" => 0x22,
                _ => 0
            };
        }
    }
}
