namespace Fido.ViewModels;

/// <summary>
/// One entry in the Console button's run menu, offered by the branch's own <c>.fido/cfg.yaml</c>: a
/// script the repo nominated, or <c>aspire start</c>. Picking it opens the Console tool at the selected
/// target and runs <see cref="Command"/> there. <see cref="ToolIndex"/> — a position in
/// <see cref="Models.AppConfig.Editors"/> — says which configured Console tool to run it through, so the
/// menu works the same whether Console is the hero button or one of the grid buttons.
/// </summary>
/// <param name="Label">Menu caption: the script's file name, or the command itself.</param>
/// <param name="Command">The command line handed to the terminal, run at the target folder.</param>
public sealed record ConsoleRunOption(string Label, string Command)
{
    /// <summary>Position in <see cref="Models.AppConfig.Editors"/> of the Console tool this runs through.</summary>
    public int ToolIndex { get; init; } = -1;

    /// <summary>
    /// A run file the repo config nominated: its name is the caption, and the command quotes it when it
    /// contains spaces so the terminal still sees a single argument.
    /// </summary>
    public static ConsoleRunOption ForRunFile(string name) =>
        new(name, name.Contains(' ') ? $"\"{name}\"" : name);

    /// <summary>The <c>Aspire start</c> option: launch the repo's .NET Aspire app host.</summary>
    public static ConsoleRunOption AspireStart { get; } = new("aspire start", "aspire start");
}
