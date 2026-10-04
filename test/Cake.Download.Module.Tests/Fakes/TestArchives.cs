using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed record ArchiveEntrySpec(string Name, string Content = "", UnixFileMode? Mode = null)
{
    public bool IsDirectory { get; init; }

    public string? SymlinkTarget { get; init; }

    public string? HardlinkTarget { get; init; }

    public bool IsFifo { get; init; }

    public static ArchiveEntrySpec Directory(string name) => new(name) { IsDirectory = true };

    public static ArchiveEntrySpec Symlink(string name, string target) => new(name) { SymlinkTarget = target };

    public static ArchiveEntrySpec Hardlink(string name, string target) => new(name) { HardlinkTarget = target };

    public static ArchiveEntrySpec Fifo(string name) => new(name) { IsFifo = true };
}

internal static class TestArchives
{
    private const UnixFileMode DefaultMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static byte[] Zip(params ArchiveEntrySpec[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var spec in entries)
            {
                var entry = archive.CreateEntry(spec.IsDirectory ? spec.Name.TrimEnd('/') + "/" : spec.Name);
                if (spec.SymlinkTarget is not null)
                {
                    entry.ExternalAttributes = unchecked((int)(0xA1FFu << 16));
                }
                else if (spec.Mode is { } mode)
                {
                    entry.ExternalAttributes = unchecked((int)(((uint)mode | 0x8000u) << 16));
                }

                if (!spec.IsDirectory)
                {
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.Write(spec.SymlinkTarget ?? spec.Content);
                }
            }
        }

        return buffer.ToArray();
    }

    public static byte[] Tar(bool gzip, params ArchiveEntrySpec[] entries)
    {
        using var buffer = new MemoryStream();
        using (Stream output = gzip ? new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true) : new NonClosingStream(buffer))
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var spec in entries)
            {
                PaxTarEntry entry;
                if (spec.IsDirectory)
                {
                    entry = new PaxTarEntry(TarEntryType.Directory, spec.Name);
                }
                else if (spec.SymlinkTarget is not null)
                {
                    entry = new PaxTarEntry(TarEntryType.SymbolicLink, spec.Name) { LinkName = spec.SymlinkTarget };
                }
                else if (spec.HardlinkTarget is not null)
                {
                    entry = new PaxTarEntry(TarEntryType.HardLink, spec.Name) { LinkName = spec.HardlinkTarget };
                }
                else if (spec.IsFifo)
                {
                    entry = new PaxTarEntry(TarEntryType.Fifo, spec.Name);
                }
                else
                {
                    entry = new PaxTarEntry(TarEntryType.RegularFile, spec.Name)
                    {
                        Mode = spec.Mode ?? DefaultMode,
                        DataStream = new MemoryStream(Encoding.UTF8.GetBytes(spec.Content)),
                    };
                }

                writer.WriteEntry(entry);
            }
        }

        return buffer.ToArray();
    }

    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
