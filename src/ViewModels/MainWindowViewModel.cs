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
            Log.Add(LogLine.Infer(message));
        else
            Dispatcher.UIThread.Post(() => Log.Add(LogLine.Infer(message)));
    }

    public void ClearLog()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Log.Clear();
        else
            Dispatcher.UIThread.Post(Log.Clear);
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
