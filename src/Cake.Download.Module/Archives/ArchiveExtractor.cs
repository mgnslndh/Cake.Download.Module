using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Cake.Core.Diagnostics;
using Cake.Download.Module.Directives;
using static Cake.Download.Module.Archives.ArchivePaths;

namespace Cake.Download.Module.Archives;

/// <summary>
/// Extracts zip and tar archives verbatim, refusing any entry or link that would land outside the destination and
/// keeping Unix permission bits.
/// </summary>
internal static class ArchiveExtractor
{
    private const int PermissionBits = 0x1FF;
    private const int FileTypeBits = 0xF000;
    private const int SymbolicLinkType = 0xA000;
    private const int MaxLinkLength = 4096;

    public static void Extract(string archivePath, ArchiveFormat format, string destination, ICakeLog log)
    {
        if (format == ArchiveFormat.File)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Only archives can be extracted.");
        }

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination);
        var links = new ArchiveLinks(root);
        if (format == ArchiveFormat.Zip)
        {
            ExtractZip(archivePath, root, links);
        }
        else
        {
            ExtractTar(archivePath, root, gzip: format == ArchiveFormat.TarGz, links, log);
        }

        links.Create(log);
    }

    private static void ExtractZip(string archivePath, string root, ArchiveLinks links)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var target = ResolveInside(root, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                links.AddEntry(entry.FullName, target);
                Directory.CreateDirectory(target);
                continue;
            }

            // Zips made on Unix store the file type in the high bits; a symbolic link's content is its target.
            var attributes = entry.ExternalAttributes >> 16;
            if ((attributes & FileTypeBits) == SymbolicLinkType)
            {
                links.AddSymbolicLink(entry.FullName, ReadLinkName(entry));
                continue;
            }

            links.AddEntry(entry.FullName, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);

            var mode = attributes & PermissionBits;
            if (!OperatingSystem.IsWindows() && mode != 0)
            {
                File.SetUnixFileMode(target, (UnixFileMode)mode);
            }
        }
    }

    private static string ReadLinkName(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        var buffer = new byte[MaxLinkLength + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0)
        {
            length += read;
        }

        if (length > MaxLinkLength)
        {
            throw Unsafe(entry.FullName, $"its link target is longer than {MaxLinkLength} bytes");
        }

        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static void ExtractTar(string archivePath, string root, bool gzip, ArchiveLinks links, ICakeLog log)
    {
        using var file = File.OpenRead(archivePath);
        using var stream = gzip ? new GZipStream(file, CompressionMode.Decompress) : (Stream)file;
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    var directory = ResolveInside(root, entry.Name);
                    links.AddEntry(entry.Name, directory);
                    Directory.CreateDirectory(directory);
                    break;
                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                case TarEntryType.ContiguousFile:
                    var target = ResolveInside(root, entry.Name);
                    links.AddEntry(entry.Name, target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(target, (UnixFileMode)((int)entry.Mode & PermissionBits));
                    }

                    break;
                case TarEntryType.SymbolicLink:
                    links.AddSymbolicLink(entry.Name, entry.LinkName);
                    break;
                case TarEntryType.HardLink:
                    links.AddHardLink(entry.Name, entry.LinkName);
                    break;
                default:
                    // PAX/GNU metadata entries are consumed by TarReader; devices and FIFOs are skipped.
                    log.Verbose("Skipping archive entry '{0}': {1} entries are not extracted.", entry.Name, entry.EntryType);
                    break;
            }
        }
    }
}
