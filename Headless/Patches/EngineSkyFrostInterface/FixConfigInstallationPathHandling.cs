//
//  SPDX-FileName: FixConfigInstallationPathHandling.cs
//  SPDX-FileCopyrightText: Copyright (c) Jarl Gullberg
//  SPDX-License-Identifier: AGPL-3.0-or-later
//

using HarmonyLib;

namespace Headless.Patches.EngineSkyFrostInterface;

/// <summary>
/// Replaces the config file installation method with one that handles relative paths appropriately. As it stands, the
/// method assumes that the current working directory is the Resonite installation path, which does not hold true for
/// Crystite.
/// </summary>
[HarmonyPatch(typeof(FrooxEngine.EngineSkyFrostInterface), "InstallConfigFile")]
public static class FixConfigInstallationPathHandling
{
    /// <summary>
    /// Prefixes the target method.
    /// </summary>
    /// <param name="path">The path to write the content to.</param>
    /// <param name="content">The content to write to the path.</param>
    /// <returns>Always false.</returns>
    public static bool Prefix(string path, string content)
    {
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException();
        Directory.CreateDirectory(directory);

        File.WriteAllText(path, content);

        return false;
    }
}
