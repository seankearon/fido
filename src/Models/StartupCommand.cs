namespace Fido.Models;

/// <summary>
/// What a command line asked for, parsed but not yet applied. A bare first argument or <c>--branch/-b</c> names
/// the branch, <c>--solution/-s</c> the solution filter, and a bare second argument or <c>--tool/-t</c> (legacy
/// <c>--editor/-e</c>) the tool; <c>--folder</c> starts the run on the Folder chip, and <c>--new-window/-n</c>
/// opens a window of its own even when another already has the branch.
/// <para>Kept apart from the window so the launch can read it before any UI exists: a branch another Fido
/// window already has is handed to that window instead (see <see cref="Services.InstanceHandoff"/>).</para>
/// </summary>
public sealed record StartupCommand(string? Branch, string? Solution, string? ToolSlug, bool PreferFolder, bool NewWindow)
{
    /// <summary>
    /// True when the command line asks for more than the branch — a tool, a solution filter, or the Folder
    /// chip. A window that already has the branch runs such a command line itself; a bare branch only asks to
    /// see it.
    /// </summary>
    public bool NamesMoreThanBranch => Solution is not null || !string.IsNullOrWhiteSpace(ToolSlug) || PreferFolder;

    public static StartupCommand Parse(IReadOnlyList<string> args)
    {
        string? branch = null;
        string? solution = null;
        string? toolSlug = null;
        var preferFolder = false;
        var newWindow = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--branch" or "-b" when i + 1 < args.Count:
                    branch = args[++i];
                    break;
                case "--solution" or "-s" when i + 1 < args.Count:
                    solution = args[++i];
                    break;
                case "--tool" or "-t" or "--editor" or "-e" when i + 1 < args.Count:
                    toolSlug = args[++i];
                    break;
                case "--folder":
                    // The Solution/Folder toggle is gone; honour existing scripts by starting the run
                    // on the Folder chip (only Rider/Visual Studio consult the choice anyway).
                    preferFolder = true;
                    break;
                case "--new-window" or "-n":
                    newWindow = true;
                    break;
                default:
                    // Bare positional arguments: the first is the branch, the second the tool id.
                    if (args[i].StartsWith('-')) break;
                    if (branch is null)
                        branch = args[i];
                    else
                        toolSlug ??= args[i];   // an explicit --tool still wins over the positional
                    break;
            }
        }
        return new StartupCommand(branch, solution, toolSlug, preferFolder, newWindow);
    }
}
