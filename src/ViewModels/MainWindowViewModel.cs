using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Fido.Models;
using Fido.Mvvm;

namespace Fido.ViewModels;

/// <summary>Status call kind for the GO / NO-GO strip.</summary>
public enum StatusKind { None, Go, NoGo }

/// <summary>State for the main window: inputs, open mode, busy/status/log.</summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private string _branchName = "";
    private string _solutionName = "";
    private bool _isSolutionMode = true;
    private bool _isFolderMode;
    private bool _isBusy;
    private string _statusText = "";
    private StatusKind _statusKind = StatusKind.None;
    private bool _isClosingCountdown;
    private string _countdownText = "";
    private bool _liveLineActive;

    public string BranchName
    {
        get => _branchName;
        set => SetField(ref _branchName, value);
    }

    public string SolutionName
    {
        get => _solutionName;
        set => SetField(ref _solutionName, value);
    }

    public bool IsSolutionMode
    {
        get => _isSolutionMode;
        set
        {
            if (SetField(ref _isSolutionMode, value) && value)
            {
                _isFolderMode = false;
                OnPropertyChanged(nameof(IsFolderMode));
            }
        }
    }

    public bool IsFolderMode
    {
        get => _isFolderMode;
        set
        {
            if (SetField(ref _isFolderMode, value) && value)
            {
                _isSolutionMode = false;
                OnPropertyChanged(nameof(IsSolutionMode));
            }
        }
    }

    public OpenMode CurrentMode => IsFolderMode ? OpenMode.Folder : OpenMode.Solution;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
                OnPropertyChanged(nameof(NotBusy));
        }
    }

    public bool NotBusy => !_isBusy;

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public StatusKind StatusKind
    {
        get => _statusKind;
        private set
        {
            if (SetField(ref _statusKind, value))
            {
                OnPropertyChanged(nameof(HasStatus));
                OnPropertyChanged(nameof(IsStatusGo));
                OnPropertyChanged(nameof(IsStatusNoGo));
            }
        }
    }

    public bool HasStatus => _statusKind != StatusKind.None;
    public bool IsStatusGo => _statusKind == StatusKind.Go;
    public bool IsStatusNoGo => _statusKind == StatusKind.NoGo;

    /// <summary>True while the post-launch auto-close countdown is running; shows the "keep open" bar.</summary>
    public bool IsClosingCountdown
    {
        get => _isClosingCountdown;
        private set => SetField(ref _isClosingCountdown, value);
    }

    /// <summary>The countdown caption shown next to the "Keep open" button (e.g. "Closing Fido in 7s").</summary>
    public string CountdownText
    {
        get => _countdownText;
        private set => SetField(ref _countdownText, value);
    }

    /// <summary>Shows/updates the close-countdown bar with the seconds remaining.</summary>
    public void ShowCountdown(int secondsRemaining)
    {
        CountdownText = $"Closing Fido in {secondsRemaining}s";
        IsClosingCountdown = true;
    }

    /// <summary>Hides the close-countdown bar (the countdown was cancelled or has elapsed).</summary>
    public void StopCountdown() => IsClosingCountdown = false;

    private string _openButtonText = "Open";

    /// <summary>Caption of the primary Open button — "Open in &lt;default editor&gt;".</summary>
    public string OpenButtonText
    {
        get => _openButtonText;
        private set => SetField(ref _openButtonText, value);
    }

    /// <summary>The non-default editors, shown as secondary launch buttons with their shortcut hints.</summary>
    public ObservableCollection<EditorLaunchOption> SecondaryEditors { get; } = new();

    /// <summary>True when there's at least one non-default editor to surface a secondary button for.</summary>
    public bool HasSecondaryEditors => SecondaryEditors.Count > 0;

    /// <summary>
    /// Rebuilds the editor launch options from config: sets the Open button caption to the default
    /// editor and lists the rest as secondary buttons (the first nine get a Ctrl+N gesture).
    /// </summary>
    public void SetEditors(IReadOnlyList<Models.Editor> editors, int defaultIndex)
    {
        SecondaryEditors.Clear();
        if (editors.Count == 0)
        {
            OpenButtonText = "Open (no editor configured)";
            OnPropertyChanged(nameof(HasSecondaryEditors));
            return;
        }

        var def = Math.Clamp(defaultIndex, 0, editors.Count - 1);
        OpenButtonText = $"Open in {editors[def].Name}";

        for (var i = 0; i < editors.Count; i++)
        {
            if (i == def) continue;
            var gesture = i < 9 ? $"Ctrl+{i + 1}" : "";
            SecondaryEditors.Add(new EditorLaunchOption(i, editors[i].Name, gesture, IsDefault: false));
        }
        OnPropertyChanged(nameof(HasSecondaryEditors));
    }

    /// <summary>Recently used branch names (newest first) shown as the branch box's MRU suggestions.</summary>
    public ObservableCollection<string> RecentBranches { get; } = new();

    /// <summary>Recently used solution names (newest first) shown as the solution box's MRU suggestions.</summary>
    public ObservableCollection<string> RecentSolutions { get; } = new();

    /// <summary>Replaces the MRU suggestion lists from persisted config.</summary>
    public void LoadMru(IEnumerable<string> branches, IEnumerable<string> solutions)
    {
        Replace(RecentBranches, branches);
        Replace(RecentSolutions, solutions);

        static void Replace(ObservableCollection<string> target, IEnumerable<string> source)
        {
            target.Clear();
            foreach (var item in source) target.Add(item);
        }
    }

    /// <summary>Color-coded flight-log lines.</summary>
    public ObservableCollection<LogLine> Log { get; } = new();

    public void AppendLog(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
            AddLine(LogLine.Infer(message));
        else
            Dispatcher.UIThread.Post(() => AddLine(LogLine.Infer(message)));
    }

    /// <summary>Appends a line at an explicit colour level (bypasses prefix inference).</summary>
    public void AppendLog(string message, LogLevel level)
    {
        if (Dispatcher.UIThread.CheckAccess())
            AddLine(new LogLine(message, level));
        else
            Dispatcher.UIThread.Post(() => AddLine(new LogLine(message, level)));
    }

    /// <summary>Appends a finished line, closing off any in-place live line (see <see cref="SetLiveLog"/>).</summary>
    private void AddLine(LogLine line)
    {
        _liveLineActive = false;
        Log.Add(line);
    }

    /// <summary>
    /// Writes a single line at the end of the log and overwrites it on each subsequent call, so a value can
    /// tick in place (the close countdown reads 10 → 9 → 8… on one line, not a line per second). The next
    /// <see cref="AppendLog(string)"/> closes the live line and starts a fresh one.
    /// </summary>
    public void SetLiveLog(string message, LogLevel level)
    {
        void Apply()
        {
            var line = new LogLine(message, level);
            if (_liveLineActive && Log.Count > 0)
                Log[^1] = line;
            else
            {
                Log.Add(line);
                _liveLineActive = true;
            }
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    public void ClearLog()
    {
        void Apply()
        {
            _liveLineActive = false;
            Log.Clear();
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    public void SetStatus(string message, StatusKind kind = StatusKind.None)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            StatusText = message;
            StatusKind = kind;
        }
        else
        {
            Dispatcher.UIThread.Post(() => { StatusText = message; StatusKind = kind; });
        }
    }
}
