﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MilkiBotFramework.Plugining.Attributes;
using MilkiBotFramework.Plugining.Loading;
using MilkiBotFramework.Utils;

namespace MilkiBotFramework.Plugining;

public partial class PluginManager
{
    public string PluginBaseDirectory { get; internal set; }

    //todo: Same command; Same guid
    public async Task InitializeAllPlugins()
    {
        var sw = Stopwatch.StartNew();
        if (!Directory.Exists(PluginBaseDirectory)) Directory.CreateDirectory(PluginBaseDirectory);
        var directories = Directory.GetDirectories(PluginBaseDirectory);

        foreach (var directory in directories)
        {
            var files = Directory.GetFiles(directory, "*.dll");
            var contextName = Path.GetFileName(directory);
            CreateContextAndAddPlugins(contextName, files);
        }

        var entryAsm = Assembly.GetEntryAssembly();
        if (entryAsm != null)
        {
            var dir = Path.GetDirectoryName(entryAsm.Location)!;
            var context = AssemblyLoadContext.Default.Assemblies;
            CreateContextAndAddPlugins(null, context
                .Where(k => !k.IsDynamic && k.Location.StartsWith(dir))
                .Select(k => k.Location)
            );
        }

        foreach (var loaderContext in _loaderContexts.Values)
        {
            var serviceProvider = loaderContext.BuildServiceProvider();

            foreach (var assemblyContext in loaderContext.AssemblyContexts.Values)
            {
                var failList = new List<PluginInfo>();
                foreach (var pluginInfo in assemblyContext.PluginInfos
                             .Where(o => o.Lifetime == PluginLifetime.Singleton))
                {
                    try
                    {
                        var instance = (PluginBase)serviceProvider.GetService(pluginInfo.Type);
                        InitializePlugin(instance, pluginInfo);
                    }
                    catch (Exception ex)
                    {
                        failList.Add(pluginInfo);
                        _logger.LogError(ex, "Error while initializing plugin " + pluginInfo.Metadata.Name);
                    }
                }

                if (failList.Count <= 0) continue;
                foreach (var pluginInfo in failList)
                {
                    assemblyContext.PluginInfos.Remove(pluginInfo);
                }

                if (assemblyContext.PluginInfos.Count == 0)
                {

                }
            }
        }

        _logger.LogInformation($"Plugin initialization done in {sw.Elapsed.TotalSeconds:N3}s!");
    }

    private void CreateContextAndAddPlugins(string? contextName, IEnumerable<string> files)
    {
        var assemblyResults = AssemblyHelper.AnalyzePluginsInAssemblyFilesByDnlib(_logger, files);
        if (assemblyResults.Count <= 0 || assemblyResults.All(k => k.TypeResults.Length == 0))
            return;

        var isRuntimeContext = contextName == null;

        var ctx = !isRuntimeContext
            ? new AssemblyLoadContext(contextName, true)
            : AssemblyLoadContext.Default;
        var loaderContext = new LoaderContext
        {
            AssemblyLoadContext = ctx,
            ServiceCollection = new ServiceCollection(),
            Name = contextName ?? "Runtime Context",
            IsRuntimeContext = isRuntimeContext
        };

        foreach (var assemblyResult in assemblyResults)
        {
            var assemblyPath = assemblyResult.AssemblyPath;
            var assemblyFullName = assemblyResult.AssemblyFullName;
            var assemblyFilename = Path.GetFileName(assemblyPath);
            var typeResults = assemblyResult.TypeResults;

            if (typeResults.Length == 0)
            {
                if (isRuntimeContext) continue;

                try
                {
                    var inEntryAssembly =
                        AssemblyLoadContext.Default.Assemblies.FirstOrDefault(k =>
                            k.FullName == assemblyFullName);
                    if (inEntryAssembly != null)
                    {
                        ctx.LoadFromAssemblyName(inEntryAssembly.GetName());
                        _logger.LogDebug($"Dependency loaded {assemblyFilename} (Host)");
                    }
                    else
                    {
                        ctx.LoadFromAssemblyPath(assemblyPath);
                        _logger.LogDebug($"Dependency loaded {assemblyFilename} (Plugin)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to load dependency {assemblyFilename}: {ex.Message}");
                } // add dependencies

                continue;
            }

            bool isValid = false;

            try
            {
                Assembly? asm = isRuntimeContext
                    ? Assembly.GetEntryAssembly()
                    : ctx.LoadFromAssemblyPath(assemblyPath);
                if (asm != null)
                {
                    var asmContext = new AssemblyContext
                    {
                        Assembly = asm
                    };

                    foreach (var typeResult in typeResults)
                    {
                        var typeFullName = typeResult.TypeFullName!;
                        var baseType = typeResult.BaseType!;
                        string typeName = "";
                        PluginInfo? pluginInfo = null;
                        try
                        {
                            var type = asm.GetType(typeFullName);
                            if (type == null) throw new Exception("Can't resolve type: " + typeFullName);

                            typeName = type.Name;
                            pluginInfo = GetPluginInfo(type, baseType);
                            var metadata = pluginInfo.Metadata;

                            switch (pluginInfo.Lifetime)
                            {
                                case PluginLifetime.Singleton:
                                    loaderContext.ServiceCollection.AddSingleton(type);
                                    break;
                                case PluginLifetime.Scoped:
                                    loaderContext.ServiceCollection.AddScoped(type);
                                    break;
                                case PluginLifetime.Transient:
                                    loaderContext.ServiceCollection.AddTransient(type);
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                            _logger.LogInformation($"Add plugin \"{metadata.Name}\": " +
                                                   $"Author={string.Join(",", metadata.Authors)}; " +
                                                   $"Version={metadata.Version}; " +
                                                   $"Lifetime={pluginInfo.Lifetime} " +
                                                   $"({pluginInfo.BaseType.Name})");
                            isValid = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error occurs while loading plugin: " + typeName);
                        }

                        if (pluginInfo != null)
                        {
                            asmContext.PluginInfos.Add(pluginInfo);
                            _plugins.Add(pluginInfo);
                        }
                    }

                    if (isValid)
                    {
                        loaderContext.AssemblyContexts.Add(assemblyFilename, asmContext);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }

            if (!isValid)
            {
                if (!isRuntimeContext)
                    _logger.LogWarning($"\"{assemblyFilename}\" 不是合法的插件扩展。");
            }
        }

        InitializeLoaderContext(loaderContext);
    }

    private void InitializeLoaderContext(LoaderContext loaderContext)
    {
        if (loaderContext.AssemblyLoadContext != AssemblyLoadContext.Default)
        {
            var existAssemblies = loaderContext.AssemblyLoadContext.Assemblies.Select(k => k.FullName).ToHashSet();

            foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
            {
                if (!assembly.IsDynamic && !existAssemblies.Contains(assembly.FullName))
                {
                    loaderContext.AssemblyLoadContext.LoadFromAssemblyName(assembly.GetName());
                }
            }
        }

        var allTypes = _serviceCollection
            .Where(o => o.Lifetime == ServiceLifetime.Singleton);
        foreach (var serviceDescriptor in allTypes)
        {
            var ns = serviceDescriptor.ServiceType.Namespace;
            if (serviceDescriptor.ImplementationType == serviceDescriptor.ServiceType)
            {
                if (/*ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || */
                    ns.StartsWith("Microsoft.Extensions.Options", StringComparison.Ordinal) ||
                    ns.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal))
                    continue;
                var instance = _serviceProvider.GetService(serviceDescriptor.ImplementationType);
                if (instance == null)
                    loaderContext.ServiceCollection.AddSingleton(serviceDescriptor.ImplementationType, _ => null!);
                else
                    loaderContext.ServiceCollection.AddSingleton(serviceDescriptor.ImplementationType, instance);
            }
            else
            {
                if (/*ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||*/
                    ns.StartsWith("Microsoft.Extensions.Options", StringComparison.Ordinal) ||
                    ns.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal))
                    continue;
                var instance = _serviceProvider.GetService(serviceDescriptor.ServiceType);
                if (instance == null)
                    loaderContext.ServiceCollection.AddSingleton(serviceDescriptor.ServiceType, _ => null!);
                else
                    loaderContext.ServiceCollection.AddSingleton(serviceDescriptor.ServiceType, instance);
            }
        }

        var configLoggerProvider = _serviceProvider.GetService<ConfigLoggerProvider>();
        if (configLoggerProvider != null)
            loaderContext.ServiceCollection.AddLogging(o => configLoggerProvider.ConfigureLogger!(o));

        loaderContext.BuildServiceProvider();
        _loaderContexts.Add(loaderContext.Name, loaderContext);
    }

    private static void InitializePlugin(PluginBase instance, PluginInfo pluginInfo)
    {
        instance.Metadata = pluginInfo.Metadata;
        instance.IsInitialized = true;
        instance.OnInitialized();
    }

    private PluginInfo GetPluginInfo(Type type, Type baseType)
    {
        PluginLifetime lifetime;
        if (baseType == StaticTypes.ServicePlugin)
        {
            lifetime = PluginLifetime.Singleton;
        }
        else
        {
            lifetime = type.GetCustomAttribute<PluginLifetimeAttribute>()?.Lifetime ??
                       throw new ArgumentNullException(nameof(PluginLifetimeAttribute.Lifetime),
                           "The plugin lifetime is undefined: " + type.FullName);
        }

        var identifierAttribute = type.GetCustomAttribute<PluginIdentifierAttribute>() ??
                                  throw new Exception("The plugin identifier is undefined: " + type.FullName);
        var guid = identifierAttribute.Guid;
        var index = identifierAttribute.Index;
        var name = identifierAttribute.Name ?? type.Name;
        var description = type.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "Nothing here.";
        var version = type.GetCustomAttribute<VersionAttribute>()?.Version ?? "0.0.1-alpha";
        var authors = type.GetCustomAttribute<AuthorAttribute>()?.Author ?? DefaultAuthors;

        var metadata = new PluginMetadata(Guid.Parse(guid), name, description, version, authors);

        var methodSets = new HashSet<string>();
        var commands = new Dictionary<string, CommandInfo>();
        foreach (var methodInfo in type.GetMethods())
        {
            if (methodSets.Contains(methodInfo.Name))
                throw new ArgumentException(
                    "Duplicate method name with CommandHandler definition is not supported.", methodInfo.Name);

            methodSets.Add(methodInfo.Name);
            var commandHandlerAttribute = methodInfo.GetCustomAttribute<CommandHandlerAttribute>();
            if (commandHandlerAttribute == null) continue;

            var command = commandHandlerAttribute.Command ?? methodInfo.Name.ToLower();
            var methodDescription = methodInfo.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";

            var parameterInfos = new List<CommandParameterInfo>();
            var parameters = methodInfo.GetParameters();
            foreach (var parameter in parameters)
            {
                var targetType = parameter.ParameterType;
                var attrs = parameter.GetCustomAttributes(false);
                var parameterInfo = GetParameterInfo(attrs, targetType, parameter);
                parameterInfos.Add(parameterInfo);
            }

            CommandReturnType returnType;
            var retType = methodInfo.ReturnType;
            if (retType == StaticTypes.Void)
                returnType = CommandReturnType.Void;
            else if (retType == StaticTypes.Task)
                returnType = CommandReturnType.Task;
            else if (retType == StaticTypes.ValueTask)
                returnType = CommandReturnType.ValueTask;
            else if (retType == StaticTypes.IResponse)
                returnType = CommandReturnType.IResponse;
            else
            {
                if (retType.IsGenericType)
                {
                    var genericDef = retType.GetGenericTypeDefinition();
                    if (genericDef == StaticTypes.Task_ &&
                        retType.GenericTypeArguments[0] == StaticTypes.IResponse)
                        returnType = CommandReturnType.Task_IResponse;
                    else if (genericDef == StaticTypes.ValueTask_ &&
                             retType.GenericTypeArguments[0] == StaticTypes.IResponse)
                        returnType = CommandReturnType.ValueTask_IResponse;
                    else if (genericDef == StaticTypes.IEnumerable_ &&
                             retType.GenericTypeArguments[0] == StaticTypes.IResponse)
                        returnType = CommandReturnType.IEnumerable_IResponse;
                    else if (genericDef == StaticTypes.IAsyncEnumerable_ &&
                             retType.GenericTypeArguments[0] == StaticTypes.IResponse)
                        returnType = CommandReturnType.IAsyncEnumerable_IResponse;
                    else
                        returnType = CommandReturnType.Dynamic;
                }
                else
                    returnType = CommandReturnType.Dynamic;
            }

            var commandInfo = new CommandInfo(command, methodDescription, methodInfo, returnType,
                parameterInfos.ToArray());

            commands.Add(command, commandInfo);
        }

        return new PluginInfo
        {
            Metadata = metadata,
            BaseType = baseType,
            Type = type,
            Lifetime = lifetime,
            Index = index,
            Commands = new ReadOnlyDictionary<string, CommandInfo>(commands)
        };
    }

    private CommandParameterInfo GetParameterInfo(object[] attrs, Type targetType,
        ParameterInfo parameter)
    {
        var parameterInfo = new CommandParameterInfo
        {
            ParameterName = parameter.Name!,
            ParameterType = targetType,
        };

        bool isReady = false;
        foreach (var attr in attrs)
        {
            if (attr is OptionAttribute option)
            {
                parameterInfo.Abbr = option.Abbreviate;
                parameterInfo.DefaultValue = parameter.DefaultValue == DBNull.Value
                    ? option.DefaultValue
                    : parameter.DefaultValue;
                parameterInfo.Name = option.Name;
                parameterInfo.ValueConverter = _commandLineAnalyzer.DefaultParameterConverter;
                isReady = true;
            }
            else if (attr is ArgumentAttribute argument)
            {
                parameterInfo.DefaultValue = parameter.DefaultValue == DBNull.Value
                    ? argument.DefaultValue
                    : parameter.DefaultValue;

                parameterInfo.IsArgument = true;
                parameterInfo.ValueConverter = _commandLineAnalyzer.DefaultParameterConverter;
                isReady = true;
            }
            else if (attr is DescriptionAttribute description)
            {
                parameterInfo.Description = description.Description;
                //parameterInfo.HelpAuthority = help.Authority;
            }
        }

        if (!isReady)
        {
            parameterInfo.IsServiceArgument = true;
            parameterInfo.IsArgument = true;
        }

        return parameterInfo;
    }
}