using System;

namespace Fido.ViewModels;

/// <summary>
/// One selectable row in a <c>ChooserDialog</c>: a bold title, an optional dim subtitle, and an
/// optional commit hash shown as a GitHub link (or plain text when no URL is available).
/// </summary>
public sealed class ChooserItem
{
    public ChooserItem(string title, string? subtitle = null, string? commitShort = null, string? commitUrl = null)
    {
        Title = title;
        Subtitle = subtitle ?? "";
        HasSubtitle = !string.IsNullOrEmpty(subtitle);

        CommitShort = commitShort ?? "";
        HasCommit = !string.IsNullOrEmpty(commitShort);
        CommitUri = Uri.TryCreate(commitUrl, UriKind.Absolute, out var uri) ? uri : null;
        HasCommitLink = HasCommit && CommitUri is not null;
        HasCommitTextOnly = HasCommit && CommitUri is null;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public bool HasSubtitle { get; }

    public string CommitShort { get; }
    public bool HasCommit { get; }
    public Uri? CommitUri { get; }

    /// <summary>True when the commit hash should render as a clickable link.</summary>
    public bool HasCommitLink { get; }

    /// <summary>True when there's a hash but no URL, so it renders as plain text.</summary>
    public bool HasCommitTextOnly { get; }
}

/// <summary>Backing data for the generic single-select chooser dialog.</summary>
public sealed class ChooserViewModel
{
    public ChooserViewModel(string prompt, IReadOnlyList<ChooserItem> items, string? deleteLabel = null)
    {
        Prompt = prompt;
        Items = items;
        SelectedIndex = items.Count > 0 ? 0 : -1;

        DeleteButtonText = deleteLabel ?? "";
        CanDelete = !string.IsNullOrEmpty(deleteLabel);
    }

    public string Prompt { get; }
    public IReadOnlyList<ChooserItem> Items { get; }

    /// <summary>Bound TwoWay to the ListBox; read back after the dialog closes.</summary>
    public int SelectedIndex { get; set; }

    /// <summary>Caption of the optional destructive action button (e.g. "Delete worktree &amp; branch").</summary>
    public string DeleteButtonText { get; }

    /// <summary>True when a delete action was supplied, so the dialog shows its button.</summary>
    public bool CanDelete { get; }
}
