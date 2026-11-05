// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using Avalonia.Platform.Storage;

// Deliberately implements only the calls exercised by the real window. Unexpected
// provider operations fail instead of silently succeeding in this regression.
internal sealed class RecordingStorage
{
    public IStorageProvider Provider { get; }
    public FilePickerSaveOptions? LastSave;
    public FilePickerOpenOptions? LastOpen;
    public string? SavePath, OpenPath;
    public int SaveCalls;

    public RecordingStorage() => Provider = StorageProxy.Create<IStorageProvider>((method, args) =>
    {
        switch (method.Name)
        {
            case nameof(IStorageProvider.SaveFilePickerAsync):
                LastSave = (FilePickerSaveOptions)args![0]!; SaveCalls++;
                return Task.FromResult(SavePath == null ? null : FileAt(SavePath));
            case nameof(IStorageProvider.OpenFilePickerAsync):
                LastOpen = (FilePickerOpenOptions)args![0]!;
                return Task.FromResult<IReadOnlyList<IStorageFile>>(OpenPath == null ? [] : [FileAt(OpenPath)]);
            default: throw new NotSupportedException(method.Name);
        }
    });

    private static IStorageFile FileAt(string path) => StorageProxy.Create<IStorageFile>((method, _) => method.Name switch
    {
        "get_Path" => new Uri(Path.GetFullPath(path)),
        "get_Name" => Path.GetFileName(path),
        nameof(IStorageFile.OpenReadAsync) => Task.FromResult<Stream>(File.OpenRead(path)),
        nameof(IStorageFile.OpenWriteAsync) => Task.FromResult<Stream>(File.Open(path, FileMode.OpenOrCreate, FileAccess.Write)),
        nameof(IDisposable.Dispose) => null,
        _ => throw new NotSupportedException(method.Name)
    });
}

public class StorageProxy : DispatchProxy
{
    private Func<MethodInfo, object?[]?, object?> _invoke = null!;
    internal static T Create<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
    {
        var proxy = Create<T, StorageProxy>(); ((StorageProxy)(object)proxy)._invoke = invoke; return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _invoke(targetMethod!, args);
}
