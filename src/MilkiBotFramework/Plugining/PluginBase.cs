﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MilkiBotFramework.Plugining;

public abstract class PluginBase
{
    protected internal PluginMetadata Metadata { get; internal set; }
    public bool IsInitialized { get; internal set; }
    public abstract PluginType PluginType { get; }

    protected internal virtual Task OnInitialized() => Task.CompletedTask;
    protected internal virtual Task OnUninitialized() => Task.CompletedTask;
    protected internal virtual Task OnExecuting() => Task.CompletedTask;
    protected internal virtual Task OnExecuted() => Task.CompletedTask;

    protected Task<T> ReadValueAsync<T>(string key)
    {
        throw new NotImplementedException();
    }

    protected Task<T[]> ReadArrayAsync<T>(string key)
    {
        throw new NotImplementedException();
    }

    protected Task WriteValueAsync(string key, object value)
    {
        throw new NotImplementedException();
    }

    protected Task WriteArrayAsync<T>(string key, IEnumerable<T> value)
    {
        throw new NotImplementedException();
    }
}