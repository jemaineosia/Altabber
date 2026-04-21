using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace AltTabber
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _cts;
        private CancellationTokenSource _captchaCts = new();
        private IntPtr _thisWindowHandle;
        private readonly Random _random = new();
        private int _captchaCount = 0;
        private bool _isRunning = false;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                _thisWindowHandle = new WindowInteropHelper(this).Handle;
                RefreshProcessList();
                StartCaptchaDetector();
            };
            Closing += (s, e) => _captchaCts.Cancel();
        }

        private void StartCaptchaDetector()
        {
            var detector = new CaptchaDetector();
            detector.CaptchaAppeared += () => Dispatcher.Invoke(() =>
            {
                _captchaCount++;
                CaptchaCounterText.Text = $"Captcha Shows Counter: {_captchaCount}";

                if (_isRunning && AlarmSoundCheckBox.IsChecked == true)
                    _ = Task.Run(() =>
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            Console.Beep(1000, 300);
                            Thread.Sleep(150);
                        }
                    });
            });
            _ = detector.RunAsync(_captchaCts.Token);
        }

        private void RefreshProcesses_Click(object sender, RoutedEventArgs e)
            => RefreshProcessList();

        private void RefreshProcessList()
        {
            var processes = Process.GetProcesses()
                .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                .OrderBy(p => p.ProcessName)
                .Select(p => new ProcessItem(p))
                .ToList();

            ProcessComboBox.ItemsSource = processes;
            ProcessComboBox.DisplayMemberPath = "DisplayName";

            if (processes.Count > 0)
                ProcessComboBox.SelectedIndex = 0;
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessComboBox.SelectedItem is not ProcessItem selected)
            {
                MessageBox.Show("Please select a target process.", "No Process Selected");
                return;
            }

            if (!TryGetTotalSeconds(TargetMinutesBox.Text, TargetSecondsBox.Text, out int targetSeconds) || targetSeconds <= 0)
            {
                MessageBox.Show("Please enter a valid Timer 1 minimum duration.", "Invalid Timer 1 Min");
                return;
            }

            if (!TryGetTotalSeconds(TargetMaxMinutesBox.Text, TargetMaxSecondsBox.Text, out int targetMaxSeconds) || targetMaxSeconds <= 0)
            {
                MessageBox.Show("Please enter a valid Timer 1 maximum duration.", "Invalid Timer 1 Max");
                return;
            }

            if (targetMaxSeconds < targetSeconds)
            {
                MessageBox.Show("Timer 1 Max must be greater than or equal to Min.", "Invalid Timer 1 Range");
                return;
            }

            if (!TryGetTotalSeconds(MyAppMinutesBox.Text, MyAppSecondsBox.Text, out int myAppSeconds) || myAppSeconds <= 0)
            {
                MessageBox.Show("Please enter a valid Timer 2 duration.", "Invalid Timer 2");
                return;
            }

            SetInputsEnabled(false);
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StartTimeText.Text = $"Started at: {DateTime.Now:hh:mm:ss tt}";
            _captchaCount = 0;
            CaptchaCounterText.Text = "Captcha Shows Counter: 0";
            _isRunning = true;
            _cts = new CancellationTokenSource();

            try
            {
                await RunSwitchLoop(selected, targetSeconds, targetMaxSeconds, myAppSeconds, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Status: Stopped.");
                UpdateCountdown("");
            }
            finally
            {
                SetInputsEnabled(true);
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StartTimeText.Text = "";
                _captchaCount = 0;
                CaptchaCounterText.Text = "Captcha Shows Counter: 0";
                _isRunning = false;
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
            => _cts?.Cancel();

        private async Task RunSwitchLoop(ProcessItem target, int targetMinSeconds, int targetMaxSeconds, int myAppSeconds, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int targetSeconds = (targetMinSeconds == targetMaxSeconds)
                    ? targetMinSeconds
                    : _random.Next(targetMinSeconds, targetMaxSeconds + 1);

                UpdateStatus($"Status: Working on {target.ProcessName}... (timer: {targetSeconds / 60}m {targetSeconds % 60}s)");
                await CountdownAsync(targetSeconds, "Switching to YOUR app in", token);

                Dispatcher.Invoke(() =>
                {
                    WindowState = WindowState.Normal;
                    WindowSwitcher.BringToFront(_thisWindowHandle);
                });
                UpdateStatus("Status: YOUR app is now active.");

                await CountdownAsync(myAppSeconds, $"Switching back to {target.ProcessName} in", token);

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var process = Process.GetProcessById(target.ProcessId);
                        WindowSwitcher.BringToFront(process.MainWindowHandle);
                        UpdateStatus($"Status: Switched back to {target.ProcessName}.");
                    }
                    catch
                    {
                        UpdateStatus("Status: Target process no longer found. Stopping.");
                        _cts?.Cancel();
                    }
                });
            }
        }

        private async Task CountdownAsync(int totalSeconds, string label, CancellationToken token)
        {
            for (int remaining = totalSeconds; remaining > 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                int mins = remaining / 60;
                int secs = remaining % 60;
                UpdateCountdown($"{label}: {mins:D2}:{secs:D2}");
                await Task.Delay(1000, token);
            }
            UpdateCountdown("");
        }

        private static bool TryGetTotalSeconds(string minutesText, string secondsText, out int total)
        {
            total = 0;
            if (!int.TryParse(minutesText, out int mins) || mins < 0) return false;
            if (!int.TryParse(secondsText, out int secs) || secs < 0 || secs > 59) return false;
            total = (mins * 60) + secs;
            return true;
        }

        private void SetInputsEnabled(bool enabled)
        {
            Dispatcher.Invoke(() =>
            {
                ProcessComboBox.IsEnabled = enabled;
                RefreshButton.IsEnabled = enabled;
                TargetMinutesBox.IsEnabled = enabled;
                TargetSecondsBox.IsEnabled = enabled;
                TargetMaxMinutesBox.IsEnabled = enabled;
                TargetMaxSecondsBox.IsEnabled = enabled;
                MyAppMinutesBox.IsEnabled = enabled;
                MyAppSecondsBox.IsEnabled = enabled;
            });
        }

        private void UpdateStatus(string message)
            => Dispatcher.Invoke(() => StatusText.Text = message);

        private void UpdateCountdown(string message)
            => Dispatcher.Invoke(() => CountdownText.Text = message);
    }

    public class ProcessItem
    {
        public int ProcessId { get; }
        public string ProcessName { get; }
        public string DisplayName { get; }

        public ProcessItem(Process p)
        {
            ProcessId = p.Id;
            ProcessName = p.ProcessName;
            DisplayName = $"{p.ProcessName} (PID: {p.Id}) - {p.MainWindowTitle}";
        }
    }
}
