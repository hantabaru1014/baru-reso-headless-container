//
//  SPDX-FileName: DisableDiscord.cs
//  SPDX-FileCopyrightText: Copyright (c) Jarl Gullberg
//  SPDX-License-Identifier: AGPL-3.0-or-later
//

using HarmonyLib;

namespace Headless.Patches.DiscordConnector;

/// <summary>
/// Disables the Discord platform interface.
/// </summary>
[HarmonyPatch(typeof(FrooxEngine.Interfacing.DiscordConnector), nameof(FrooxEngine.Interfacing.DiscordConnector.Initialize))]
public static class DisableDiscord
{
    /// <summary>
    /// Overrides the Discord connector's initialization routine, disabling it outright.
    /// </summary>
    /// <param name="__result">The result of the original method.</param>
    /// <returns>Always false.</returns>
    public static bool Prefix(out Task<bool> __result)
    {
        __result = Task.FromResult(false);
        return false;
    }
}
