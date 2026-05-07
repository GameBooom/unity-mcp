// Copyright (C) Funplay. Licensed under MIT.

namespace Funplay.Editor.Tools.Scripting
{
    /// <summary>
    /// Implement this in a code snippet passed to <c>execute_code</c> to opt into
    /// the structured execution path: automatic Undo registration, change tracking,
    /// and structured log capture.
    ///
    /// Template:
    /// <code>
    /// using UnityEngine;
    /// using UnityEditor;
    /// using Funplay.Editor.Tools.Scripting;
    ///
    /// public class CommandScript : IFunplayCommand
    /// {
    ///     public void Execute(ExecutionContext ctx)
    ///     {
    ///         var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
    ///         ctx.RegisterObjectCreation(go);          // Undo + tracking
    ///         ctx.Log("Created {0}", go.name);
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IFunplayCommand
    {
        void Execute(ExecutionContext ctx);
    }
}
