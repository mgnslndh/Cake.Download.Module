using Cake.Core.IO;
using Cake.Core.Tooling;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class RecordingToolLocator : IToolLocator
{
    public List<FilePath> Registered { get; } = [];

    public void RegisterFile(FilePath path) => Registered.Add(path);

    public FilePath? Resolve(string tool) =>
        Registered.LastOrDefault(path => string.Equals(path.GetFilename().FullPath, tool, StringComparison.OrdinalIgnoreCase));

    public FilePath? Resolve(IEnumerable<string> toolExeNames) =>
        toolExeNames.Select(Resolve).FirstOrDefault(path => path is not null);
}
