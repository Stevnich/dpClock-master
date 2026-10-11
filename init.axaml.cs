using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace dpClock_master;

public partial class MainWindow : Window
{
    #region CONSTRUCTOR

    public MainWindow()
    {
        InitializeComponent();
        ConfigureChrome();
        INITCLOCK();
        INITSTOPWATCH();
        INITTIMER();

        Loaded += fileGui_Load;            // WinForms: this.Load
        PositionChanged += THIS_MOVE;      // WinForms: this.Move
        Closing += THIS_KILL;              // WinForms: this.FormClosing

        // Minimizing does not always raise PositionChanged in Avalonia, so also watch WindowState.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                Dispatcher.UIThread.Post(() => THIS_MOVE(this, EventArgs.Empty));
        };
    }

    private void fileGui_Load(object? sender, RoutedEventArgs e)
    {
        // (empty in the legacy version)
    }

    #endregion

    #region macOS WINDOW CHROME

    private void ConfigureChrome()
    {
        if (OperatingSystem.IsMacOS())
        {
            // Real traffic lights and real global menu bar.
            TrafficLights.IsVisible = false;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    #endregion

    #region CLOCK (notify strip + background tick)

    // Legacy: TIMETICK (1000 ms). It now ticks 4x per second but only reacts when the displayed
    // second changes, so a second can never be skipped (which could miss the Sleep Time target).
    private readonly DispatcherTimer TIMETICK = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private void INITCLOCK()
    {
        notify_heading_currClock.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        TIMETICK.Tick += _TIMETICK;
        TIMETICK.Start();
    }

    private void _TIMETICK(object? sender, EventArgs e)
    {
        string now = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (now == notify_heading_currClock.Text) return;
        notify_heading_currClock.Text = now;

        // Sleep Time is enabled ("Disable" button showing) and the target time has been reached
        if (!isDudepower_ClockEnabled && tab_Dudepower_Clock_targetTime.Text == now)
            _ = RunSleepTaskAsync();
    }

    #endregion

    #region CODE FOR STOPWATCH

    // Legacy used DispatcherTimer-like ticks and added 8 cs per 55 ms tick, which drifts.
    // Now System.Diagnostics.Stopwatch measures the time and the timers only redraw.
    private readonly DispatcherTimer STOPWATCHTICK = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly DispatcherTimer OVSTOPWATCHTICK = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Stopwatch STOPWATCHCLOCK = new();     // big label (total time)
    private readonly Stopwatch OVSTOPWATCHCLOCK = new();   // small label (time since last "Round")
    private readonly ObservableCollection<LapEntry> lapEntries = new();

    private bool stopwatch_timeStartStop, ovTimerTrue;
    private int recTime = 0;

    private void INITSTOPWATCH()
    {
        STOPWATCHTICK.Tick += _STOPWATCHTICK;
        OVSTOPWATCHTICK.Tick += _OVSTOPWATCHTICK;

        // "Round / Reset" only makes sense once the stopwatch has been started.
        tab_stopwatch_btn_recReset.IsEnabled = false;

        // Replaces the ListBox of padded strings: real columns that line up with the headers.
        lapList.ItemsSource = lapEntries;
        lapList.ItemTemplate = new FuncDataTemplate<LapEntry>((lap, _) => BuildLapRow(lap));
    }

    private void _STOPWATCHTICK(object? sender, EventArgs e) => DrawTime();

    private void _OVSTOPWATCHTICK(object? sender, EventArgs e) => OvDrawTime();

    private void DrawTime()
    {
        tab_stopwatch_heading_time.Text = FormatStopwatch(STOPWATCHCLOCK.Elapsed);
    }

    private void OvDrawTime()
    {
        tab_stopwatch_heading_overallTime.Text = FormatStopwatch(OVSTOPWATCHCLOCK.Elapsed);
    }

    private void _tab_stopwatch_btn_startStop(object? sender, RoutedEventArgs e)
    {
        if (stopwatch_timeStartStop)
        {
            // pause
            notify_icon_stopwatch.IsVisible = true;
            tab_stopwatch_btn_recReset.IsEnabled = true;
            tab_stopwatch_btn_recReset.Content = "Reset";
            tab_stopwatch_btn_startStop.Content = "Continue";

            OVSTOPWATCHTICK.Stop();
            STOPWATCHTICK.Stop();
            OVSTOPWATCHCLOCK.Stop();
            STOPWATCHCLOCK.Stop();
            DrawTime();
            if (ovTimerTrue) OvDrawTime();

            stopwatch_timeStartStop = false;
        }
        else
        {
            // start / continue
            notify_icon_stopwatch.IsVisible = true;
            tab_stopwatch_btn_recReset.IsEnabled = true;
            tab_stopwatch_btn_recReset.Content = "Round";
            tab_stopwatch_btn_startStop.Content = "Pause";

            // The lap clock always runs, so the first round shows the time since start
            // (legacy showed 00:00:00 for the first lap).
            STOPWATCHCLOCK.Start();
            OVSTOPWATCHCLOCK.Start();
            if (ovTimerTrue) OVSTOPWATCHTICK.Start();
            STOPWATCHTICK.Start();

            stopwatch_timeStartStop = true;
        }
    }

    private void _tab_stopwatch_btn_recReset(object? sender, RoutedEventArgs e)
    {
        string? mode = tab_stopwatch_btn_recReset.Content as string;

        if (mode == "Reset")
        {
            ovTimerTrue = false;
            notify_icon_stopwatch.IsVisible = false;
            tab_stopwatch_btn_recReset.IsEnabled = false;
            tab_stopwatch_pnl_lapList.IsVisible = false;
            tab_stopwatch_heading_overallTime.IsVisible = false;
            lapEntries.Clear();
            recTime = 0;
            ovResetTime();
            tab_stopwatch_btn_recReset.Content = "Round";
            tab_stopwatch_btn_startStop.Content = "Start";
            tab_stopwatch_heading_time.Text = "00:00:00";
            // (legacy moved the controls back to their original Location here; the Grid layout does that now)
            resetTime();
        }
        else if (mode == "Round")
        {
            recTime += 1;
            ovTimerTrue = true;
            tab_stopwatch_heading_overallTime.IsVisible = true;
            tab_stopwatch_pnl_lapList.IsVisible = true;

            lapEntries.Add(new LapEntry(
                recTime,
                FormatStopwatch(STOPWATCHCLOCK.Elapsed),
                FormatStopwatch(OVSTOPWATCHCLOCK.Elapsed)));
            lapList.ScrollIntoView(lapEntries.Count - 1);

            OVSTOPWATCHTICK.Stop();
            ovResetTime();
            tab_stopwatch_heading_overallTime.Text = "00:00:00";
            OVSTOPWATCHCLOCK.Start();
            OVSTOPWATCHTICK.Start();
            // (legacy moved the controls to the top and bottom here; the Grid layout does that now)
        }
    }

    private void resetTime()
    {
        STOPWATCHCLOCK.Reset();
    }

    private void ovResetTime()
    {
        OVSTOPWATCHCLOCK.Reset();
    }

    private static string FormatStopwatch(TimeSpan t) =>
        $"{(int)t.TotalMinutes:00}:{t.Seconds:00}:{t.Milliseconds / 10:00}";

    private static Control BuildLapRow(LapEntry? lap)
    {
        // Column widths match the header grid in the XAML (90, 108, *).
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,108,*") };

        void Add(string text, int column)
        {
            var tb = new TextBlock { Text = text };
            Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
        }

        Add(lap?.Round.ToString(CultureInfo.InvariantCulture) ?? "", 0);
        Add(lap?.Total ?? "", 1);
        Add(lap?.Lap ?? "", 2);
        return grid;
    }

    #endregion

    #region CODE FOR TIMER

    private readonly DispatcherTimer TIMERTIME = new() { Interval = TimeSpan.FromSeconds(1) };
    private TimeSpan remainingTime;
    private bool isTimerStart = true, isTimerPause = true;

    private void INITTIMER()
    {
        // Legacy subscribed Timer_Tick inside CountdownTimerForm(), i.e. once per Start click,
        // so the countdown sped up after every Cancel/Start. Subscribe once here instead.
        TIMERTIME.Tick += Timer_Tick;
    }

    private void _tab_timer_btn_timerStart(object? sender, RoutedEventArgs e)
    {
        if (isTimerStart)
        {
            CountdownTimerForm();
            notify_icon_timer.IsVisible = true;
            tab_timer_timeControl.IsVisible = false;
            tab_timer_heading_timerLabel.IsVisible = true;
            tab_timer_btn_timerPause.IsVisible = true;
            tab_timer_btn_timerStart.Content = "Cancel";
            isTimerStart = false;
        }
        else
        {
            TIMERTIME.Stop();
            notify_icon_timer.IsVisible = false;
            tab_timer_heading_timerLabel.Text = "00:00:00";
            tab_timer_heading_timerLabel.IsVisible = false;
            tab_timer_timeControl.IsVisible = true;
            tab_timer_btn_timerPause.IsVisible = false;
            tab_timer_btn_timerPause.Content = "Pause";   // legacy kept "Continue" after cancelling while paused
            isTimerPause = true;
            tab_timer_btn_timerStart.Content = "Start";
            isTimerStart = true;
        }
    }

    private void _tab_timer_btn_timerPause(object? sender, RoutedEventArgs e)
    {
        if (isTimerPause)
        {
            TIMERTIME.Stop();
            tab_timer_btn_timerPause.Content = "Continue";
            isTimerPause = false;
        }
        else
        {
            TIMERTIME.Start();
            tab_timer_btn_timerPause.Content = "Pause";
            isTimerPause = true;
        }
    }

    public void CountdownTimerForm()
    {
        remainingTime = new TimeSpan(
            ValueOf(tab_timer_timeControl_hourTimeEditor),
            ValueOf(tab_timer_timeControl_minuteTimeEditor),
            ValueOf(tab_timer_timeControl_secondTimeEditor));

        tab_timer_heading_timerLabel.Text = FormatHms(remainingTime);
        TIMERTIME.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        remainingTime = remainingTime.Subtract(TimeSpan.FromSeconds(1));
        if (remainingTime < TimeSpan.Zero) remainingTime = TimeSpan.Zero;
        tab_timer_heading_timerLabel.Text = FormatHms(remainingTime);

        if (remainingTime.TotalSeconds <= 0)
        {
            TIMERTIME.Stop();
            _ = MessageDialog.ShowAsync(this, "dpClock", "Countdown completed!");
        }
    }

    #endregion

    #region CODE FOR Dudepower_Clock

    private enum SleepTask { Reminder, ShutDown, Command }

    private bool isDudepower_ClockEnabled = true;   // true = currently OFF, the next click turns it ON (as in legacy)
    private SleepTask sleepTask = SleepTask.Reminder;

    // _tab_Dudepower_Clock_drop_task_showDrop is no longer needed: the button has a MenuFlyout in the XAML.

    private void _tab_Dudepower_Clock_btn_enableDudepower_Clock(object? sender, RoutedEventArgs e)
    {
        if (isDudepower_ClockEnabled)
        {
            // Local value: legacy reused the Timer tab's remainingTime field here and overwrote a running countdown.
            var target = new TimeSpan(
                ValueOf(tab_Dudepower_Clock_timeControl_hourTimeEditor),
                ValueOf(tab_Dudepower_Clock_timeControl_minuteTimeEditor),
                ValueOf(tab_Dudepower_Clock_timeControl_secondTimeEditor));

            tab_Dudepower_Clock_btn_enableDudepower_Clock.Content = "Disable";
            SetPrimary(tab_Dudepower_Clock_btn_enableDudepower_Clock, false);   // legacy: BackColor 40,45,75
            tab_Dudepower_Clock_targetTime.Text = FormatHms(target);
            tab_Dudepower_Clock_targetTime.IsVisible = true;
            tab_Dudepower_Clock_timeControl.IsVisible = false;
            notify_icon_Dudepower_Clock.IsVisible = true;
            isDudepower_ClockEnabled = false;
        }
        else
        {
            tab_Dudepower_Clock_btn_enableDudepower_Clock.Content = "Enable";
            SetPrimary(tab_Dudepower_Clock_btn_enableDudepower_Clock, true);    // legacy: BackColor 40,40,40
            tab_Dudepower_Clock_targetTime.IsVisible = false;
            tab_Dudepower_Clock_timeControl.IsVisible = true;
            notify_icon_Dudepower_Clock.IsVisible = false;
            isDudepower_ClockEnabled = true;
        }
    }

    private void _drop_task_option_reminder(object? sender, RoutedEventArgs e)
    {
        sleepTask = SleepTask.Reminder;
        tab_Dudepower_Clock_drop_task_label.Text = "Reminder";
        tab_Dudepower_Clock_input_command.IsVisible = false;
        tab_Dudepower_Clock_input_reminder.IsVisible = true;
    }

    private void _drop_task_option_shutDown(object? sender, RoutedEventArgs e)
    {
        sleepTask = SleepTask.ShutDown;
        tab_Dudepower_Clock_drop_task_label.Text = "Shut Down PC";
        tab_Dudepower_Clock_input_command.IsVisible = false;
        tab_Dudepower_Clock_input_reminder.IsVisible = false;
    }

    private void _drop_task_option_command(object? sender, RoutedEventArgs e)
    {
        sleepTask = SleepTask.Command;
        tab_Dudepower_Clock_drop_task_label.Text = "Command";
        tab_Dudepower_Clock_input_command.IsVisible = true;
        tab_Dudepower_Clock_input_reminder.IsVisible = false;
    }

    // What _TIMETICK did inline in the legacy version when the target time was reached.
    private async Task RunSleepTaskAsync()
    {
        string target = tab_Dudepower_Clock_targetTime.Text ?? "";

        switch (sleepTask)
        {
            case SleepTask.Reminder:
                await MessageDialog.ShowAsync(this, "Reminder at: " + target,
                    tab_Dudepower_Clock_input_reminder.Text ?? "");
                break;

            case SleepTask.ShutDown:
                try { ShutDownPc(); }
                catch (Exception x)
                {
                    await MessageDialog.ShowAsync(this, "dpClock - Shut Down Error",
                        "An error occurred when shutting down: \n" + x.Message);
                }
                break;

            case SleepTask.Command:
                try { RunCommand(tab_Dudepower_Clock_input_command.Text ?? ""); }
                catch (Exception x)
                {
                    await MessageDialog.ShowAsync(this, "dpClock - Command Error",
                        "An error occurred when running the command: \n" + x.Message);
                }
                break;
        }
    }

    private static void ShutDownPc()
    {
        if (OperatingSystem.IsWindows())
            Process.Start("shutdown", "/s /t 0");                       // legacy command
        else if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("osascript")
            {
                ArgumentList = { "-e", "tell application \"System Events\" to shut down" }
            });
        else
            Process.Start("systemctl", "poweroff");
    }

    private static void RunCommand(string command)
    {
        if (OperatingSystem.IsWindows())
            Process.Start("CMD.exe", command);                          // legacy behaviour
        else
            Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } });
    }

    #endregion

    #region TRAY / WINDOW STATE

    // True only when a tray icon with an actual icon exists (see Icon in App.axaml);
    // otherwise hiding the window would leave no way to bring it back.
    private static bool TrayAvailable =>
        Avalonia.Application.Current is { } app
        && TrayIcon.GetIcons(app) is { } icons
        && icons.Any(i => i.Icon is not null);

    // Legacy: FormClosing minimized the form. Here closing hides the window to the tray;
    // use the tray menu's "End Process" to really quit.
    private void THIS_KILL(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        if (TrayAvailable)
        {
            e.Cancel = true;
            Hide();
        }
    }

    // Legacy: Move event hid the form once it was minimized and showed a balloon tip.
    // Avalonia's TrayIcon has no balloon tip, so the window just disappears into the tray.
    private void THIS_MOVE(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && TrayAvailable)
            Hide();
    }

    public void SHOWWINDOW()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    // Tray menu handlers (_backTaskContext_openWindow / _backTaskContext_endProcess) live in App.axaml.cs.

    #endregion

    #region MENU

    private void UseConfiguration_Click(object? sender, EventArgs e) { /* no logic in the legacy version yet */ }
    private void ImportPlaylist_Click(object? sender, EventArgs e) { /* no logic in the legacy version yet */ }
    private void Settings_Click(object? sender, EventArgs e) { /* no logic in the legacy version yet */ }

    #endregion

    #region HELPERS

    private static int ValueOf(NumericUpDown n) => Convert.ToInt32(n.Value ?? 0);

    // TimeSpan's "hh" only shows 0-23, but the hour editor goes to 99, so format by total hours.
    private static string FormatHms(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    private static void SetPrimary(Button button, bool on)
    {
        if (on) { if (!button.Classes.Contains("primary")) button.Classes.Add("primary"); }
        else button.Classes.Remove("primary");
    }

    #endregion
}

/// <summary>One row of the lap list (Round, value under "Lap Time", value under "Overall Time").</summary>
public sealed record LapEntry(int Round, string Total, string Lap);
