using System;
using System.Reflection;

namespace InkTag.Core;

/// <summary>
/// Provides centralized, dynamic version resolution for the InkTag suite.
/// </summary>
public static class InkTagVersion
{
    private static readonly Lazy<string> _versionString = new(() =>
    {
        var asm = typeof(InkTagVersion).Assembly;

        // 1. Try AssemblyInformationalVersionAttribute (e.g. "0.14.1" or "0.14.1+commit")
        var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (!string.IsNullOrWhiteSpace(infoAttr?.InformationalVersion))
        {
            string raw = infoAttr.InformationalVersion;
            int plusIdx = raw.IndexOf('+');
            string clean = plusIdx > 0 ? raw[..plusIdx] : raw;
            if (!string.IsNullOrWhiteSpace(clean))
            {
                return clean.Trim();
            }
        }

        // 2. Fallback to AssemblyName.Version
        var asmVer = asm.GetName().Version;
        if (asmVer != null && asmVer != new Version(0, 0, 0, 0))
        {
            return asmVer.Build >= 0
                ? $"{asmVer.Major}.${asmVer.Minor}.${asmVer.Build}"
                : $"{asmVer.Major}.${asmVer.Minor}";
        }

        return "dev";
    });

    /// <summary>
    /// Gets the dynamically resolved version string of the InkTag core assembly (e.g. "0.14.1").
    /// </summary>
    public static string Current => _versionString.Value;
}
