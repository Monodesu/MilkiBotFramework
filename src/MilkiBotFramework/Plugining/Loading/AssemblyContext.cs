﻿using System.Reflection;

namespace MilkiBotFramework.Plugining.Loading;

public class AssemblyContext
{
    public Assembly Assembly { get; init; }
    public IReadOnlyList<PluginInfo> PluginInfos { get; init; }
    public IReadOnlyList<Type> DbContextTypes { get; init; }
    public string Version { get; init; }
    public string? Product { get; init; }
}