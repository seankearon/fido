namespace Fido.Models;

/// <summary>What gets handed to the editor: either a <c>.sln</c> file or a folder.</summary>
public sealed record LaunchTarget(string Path, bool IsSolution);
