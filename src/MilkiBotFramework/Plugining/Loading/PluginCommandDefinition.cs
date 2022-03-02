﻿using System;
using System.Collections.Generic;
using System.Reflection;

namespace MilkiBotFramework.Plugining.Loading
{
    public sealed class PluginCommandDefinition
    {
        public PluginCommandDefinition(string command,
            string description,
            MethodInfo methodInfo,
            CommandReturnType commandReturnType,
            IReadOnlyList<ParameterDefinition> parameterDefinitions)
        {
            Command = command;
            Description = description;
            MethodInfo = methodInfo;
            CommandReturnType = commandReturnType;
            ParameterDefinitions = parameterDefinitions;
        }

        public string Command { get; }
        public string Description { get; }
        public MethodInfo MethodInfo { get; }
        public CommandReturnType CommandReturnType { get; }
        public IReadOnlyList<ParameterDefinition> ParameterDefinitions { get; }
        public ModelBindingDefinition? ModelBindingDefinition { get; internal set; }
    }

    public class ModelBindingDefinition
    {
        public Type TargetType { get; init; }
        public List<ParameterDefinition> ParameterDefinitions { get; init; }
    }
}
