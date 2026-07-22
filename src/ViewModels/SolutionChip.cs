namespace Fido.ViewModels;

/// <summary>
/// One chip in the "Rider / Visual Studio open" row: a detected solution file, or the trailing
/// <c>Folder</c> chip (<see cref="SolutionPath"/> null) that opens the working tree itself. The choice
/// only affects solution-capable tools; every other tool opens the folder regardless.
/// </summary>
public sealed class SolutionChip
{
    private SolutionChip(string label, string? solutionPath)
    {
        Label = label;
        SolutionPath = solutionPath;
    }

    /// <summary>Chip caption — the solution file name, or <c>Folder</c>.</summary>
    public string Label { get; }

    /// <summary>Full path of the solution file; null for the Folder chip.</summary>
    public string? SolutionPath { get; }

    public bool IsFolder => SolutionPath is null;

    public static SolutionChip ForSolution(string path) =>
        new(System.IO.Path.GetFileName(path), path);

    public static SolutionChip Folder { get; } = new("Folder", null);
}
