// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Threading.Tasks;

namespace GameBooom.Editor.Threading
{
    internal interface IEditorThreadHelper : IDisposable
    {
        bool IsMainThread { get; }
        Task ExecuteOnEditorThreadAsync(Action action);
        Task<T> ExecuteOnEditorThreadAsync<T>(Func<T> func);
        Task<T> ExecuteAsyncOnEditorThreadAsync<T>(Func<Task<T>> asyncFunc);
    }
}
