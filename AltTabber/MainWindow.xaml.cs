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
        private IntPtr _thisWindowHandle;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                _thisWindowHandle = new WindowInteropHelper(this).Handle;
                RefreshProcessList();
            };
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
                MessageBox.Show("Please enter a valid Timer 1 duration.", "Invalid Timer 1");
                return;
            }

            if (!TryGetTotalSeconds(MyAppMinutesBox.Text, MyAppSecondsBox.Text, out int myAppSeconds) || myAppSeconds <= 0)
            {
                MessageBox.Show("Please enter a valid Timer 2 duration.", "Invalid Timer 2");
                return;
            }

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            _cts = new CancellationTokenSource();

            try
            {
                await RunSwitchLoop(selected, targetSeconds, myAppSeconds, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Status: Stopped.");
                UpdateCountdown("");
            }
            finally
            {
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
            => _cts?.Cancel();

        private async Task RunSwitchLoop(ProcessItem target, int targetSeconds, int myAppSeconds, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                UpdateStatus($"Status: Working on {target.ProcessName}...");
                await CountdownAsync(targetSeconds, "Switching to YOUR app in", token);

                Dispatcher.Invoke(() =>
                {
                    WindowSwitcher.BringToFront(_thisWindowHandle);
                    WindowState = WindowState.Normal;
                });
                UpdateStatus("Status: YOUR app is now active (resting macro...");

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
