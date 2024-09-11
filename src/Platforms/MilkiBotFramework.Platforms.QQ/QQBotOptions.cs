﻿using System.ComponentModel;

namespace MilkiBotFramework.Platforms.QQ;

// ReSharper disable once InconsistentNaming
public class QQBotOptions : BotOptions
{
    [Description("QQ API设置")]
    public QConnection Connection { get; set; } = new();
}