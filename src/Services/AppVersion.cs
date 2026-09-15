using System;
using System.Reflection;

namespace Fido.Services;

/// <summary>
/// The running build's version, for the badge beside the wordmark in the header.
///
/// The number is read from the assembly rather than from disk, so what the header shows is
/// genuinely what is running. A release build stamps it from <c>ver.txt</c> through the generated
/// <c>Directory.Build.props</c>; an ordinary build has no such file, so <c>src/Fido.csproj</c>
/// falls back to that same <c>ver.txt</c> — otherwise the SDK's <c>1.0.0</c> default would have the
/// header claiming a version Fido has never released.
/// </summary>
public static class AppVersion
{
    /// <summary>The bare version — <c>0.9.3</c>. Empty only if the assembly carries nothing usable.</summary>
    public static string Current { get; } = Read(typeof(AppVersion).Assembly);

    /// <summary>What the header renders — <c>v0.9.3</c>, or empty when there is no version to show.</summary>
    public static string Label { get; } = Current.Length == 0 ? "" : $"v{Current}";

    /// <summary>Reads both version attributes off <paramref name="assembly"/> and formats them.</summary>
    internal static string Read(Assembly assembly) =>
        Format(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
               assembly.GetName().Version);

    /// <summary>
    /// Prefers the informational version (the release build's own three-part number, and the only one
    /// that can carry a pre-release suffix), falling back to the assembly version. Build metadata — the
    /// <c>+&lt;sha&gt;</c> a source-linked build appends — is dropped: the header has room for a version,
    /// not for provenance.
    /// </summary>
    internal static string Format(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var plus = informationalVersion.IndexOf('+');
            var number = (plus < 0 ? informationalVersion : informationalVersion[..plus]).Trim();
            if (number.Length > 0) return number;
        }

        // AssemblyVersion is four-part and always padded, so trim it back to what was released.
        // Build is -1 when the version was written with only two parts.
        return assemblyVersion is null ? ""
             : assemblyVersion.Build < 0 ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}"
             : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    }
}
