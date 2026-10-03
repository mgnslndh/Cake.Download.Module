using System.Text.Json;
using Cake.Core;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Installation;

/// <summary>
/// Owns <c>&lt;tools&gt;/&lt;package&gt;.&lt;version&gt;/</c>: decides whether an install is current, stages new installs
/// next to it and publishes them with an atomic directory move. Concurrent builds are handled optimistically: the
/// first matching publish wins and later ones discard their staging.
/// </summary>
internal sealed class InstallStore
{
    private const int MaxPublishAttempts = 5;

    private static readonly TimeSpan StaleStagingAge = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _toolsDirectory;
    private readonly TimeProvider _time;
    private readonly Action<TimeSpan> _sleep;

    public InstallStore(string toolsDirectory, TimeProvider time)
        : this(toolsDirectory, time, Thread.Sleep)
    {
    }

    public InstallStore(string toolsDirectory, TimeProvider time, Action<TimeSpan> sleep)
    {
        _toolsDirectory = toolsDirectory ?? throw new ArgumentNullException(nameof(toolsDirectory));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _sleep = sleep ?? throw new ArgumentNullException(nameof(sleep));
    }

    public static InstallMarker? ReadMarker(string directory)
    {
        var path = Path.Combine(directory, FileSelector.MarkerFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallMarker>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover folder is cleaned up by a later install.
        }
    }

    public string GetInstallDirectory(DownloadPlan plan) => Path.Combine(_toolsDirectory, plan.FolderName);

    public bool IsCurrent(DownloadPlan plan) => ReadMarker(GetInstallDirectory(plan))?.Matches(plan) == true;

    public InstallMarker CreateMarker(DownloadPlan plan, string sha256) => InstallMarker.For(plan, sha256, _time.GetUtcNow());

    public StagingArea CreateStaging(DownloadPlan plan)
    {
        Directory.CreateDirectory(_toolsDirectory);
        var cutoff = (_time.GetUtcNow() - StaleStagingAge).UtcDateTime;
        foreach (var leftover in Directory.EnumerateDirectories(_toolsDirectory, $".{plan.FolderName}.tmp-*"))
        {
            if (Directory.GetLastWriteTimeUtc(leftover) < cutoff)
            {
                TryDeleteDirectory(leftover);
            }
        }

        return new StagingArea(Path.Combine(_toolsDirectory, $".{plan.FolderName}.tmp-{Guid.NewGuid():N}"));
    }

    public void Publish(DownloadPlan plan, StagingArea staging, InstallMarker marker, bool replaceCurrent = false)
    {
        File.WriteAllText(Path.Combine(staging.ContentDirectory, FileSelector.MarkerFileName), JsonSerializer.Serialize(marker, JsonOptions));

        var final = GetInstallDirectory(plan);
        for (var attempt = 1; ; attempt++)
        {
            if (Directory.Exists(final))
            {
                if (!replaceCurrent && IsCurrent(plan))
                {
                    return;
                }

                replaceCurrent = false;
                TryDeleteDirectory(final);
            }

            try
            {
                Directory.Move(staging.ContentDirectory, final);
                return;
            }
            catch (Exception exception) when (attempt < MaxPublishAttempts && exception is IOException or UnauthorizedAccessException)
            {
                _sleep(TimeSpan.FromMilliseconds(200 * attempt));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new CakeException($"Could not move the new install of {plan.Package} {plan.Version} into {final}: {exception.Message}", exception);
            }
        }
    }
}
