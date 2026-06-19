namespace Fido.Models;

/// <summary>What gets handed to Rider: either a <c>.sln</c> file or a folder.</summary>
public sealed record RiderTarget(string Path, bool IsSolution);
