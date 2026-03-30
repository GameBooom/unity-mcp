// Copyright (C) GameBooom. Licensed under MIT.
using System;

namespace GameBooom.Editor.Tools
{
    /// <summary>
    /// Marks a tool function as read-only.
    /// Functions with this attribute do not modify the scene or project.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal class ReadOnlyToolAttribute : Attribute { }
}
