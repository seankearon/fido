namespace Fido.Services;

/// <summary>
/// The collaborators <see cref="Views.MainWindow"/> depends on, bundled so tests can inject fakes
/// (an <see cref="IEditorLauncher"/> that records launches, a <see cref="IDialogService"/> that scripts
/// choices, a <see cref="ConfigService"/> rooted at a temp folder) while production keeps today's
/// real wiring via <see cref="CreateDefault"/>.
/// </summary>
internal sealed class FidoServices
{
    public ConfigService ConfigService { get; init; } = new();
    public GitService Git { get; init; } = new();
    public SolutionFinder Finder { get; init; } = new();
    public WorkingTreeFinder WorkingTreeFinder { get; init; } = new();
    public IEditorLauncher Launcher { get; init; } = new EditorLauncher();

    /// <summary>Dialog layer; when null the window installs a real <see cref="AvaloniaDialogService"/> owned by itself.</summary>
    public IDialogService? Dialogs { get; init; }

    public static FidoServices CreateDefault() => new();
}
