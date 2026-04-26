using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace Fenstr;

internal record UpdateInfo(
    Version NewVersion,
    string ReleaseUrl,
    string? InstallerDownloadUrl,
    bool IsInstalled);

internal static class UpdateChecker
{
    private static readonly Version s_currentVersion =
        typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private static readonly HttpClient s_http = CreateHttpClient();

    private static bool s_checked;

    public static UpdateInfo? LatestUpdate { get; private set; }

    public static event Action<UpdateInfo>? UpdateAvailable;

    public static void CheckInBackground(DispatcherQueue dispatcher)
    {
        if (s_checked) return;
        s_checked = true;

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var release = await s_http.GetFromJsonAsync(
                    "https://api.github.com/repos/patrickiel/Fenstr/releases/latest",
                    UpdateJsonContext.Default.GitHubRelease,
                    cts.Token);

                if (release == null) return;

                var tagVersion = release.TagName.TrimStart('v');
                if (!Version.TryParse(tagVersion, out var remote)) return;

                var current = new Version(s_currentVersion.Major, s_currentVersion.Minor,
                    Math.Max(s_currentVersion.Build, 0));
                var remoteNorm = new Version(remote.Major, remote.Minor,
                    Math.Max(remote.Build, 0));

                if (remoteNorm <= current) return;

                string? installerUrl = null;
                foreach (var asset in release.Assets)
                {
                    if (asset.Name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = asset.BrowserDownloadUrl;
                        break;
                    }
                }

                var info = new UpdateInfo(remoteNorm, release.HtmlUrl, installerUrl, IsInstalledInstance());
                LatestUpdate = info;
                dispatcher.TryEnqueue(() => UpdateAvailable?.Invoke(info));
            }
            catch
            {
            }
        });
    }

    public static async Task ApplyUpdateAsync()
    {
        var update = LatestUpdate;
        if (update == null) return;

        if (update.IsInstalled && update.InstallerDownloadUrl != null)
        {
            var tempPath = Path.Combine(Path.GetTempPath(),
                $"Fenstr-v{update.NewVersion}-setup.exe");

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var response = await s_http.GetAsync(update.InstallerDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, useAsync: true);
            await response.Content.CopyToAsync(fs, cts.Token);

            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });

            SnapGuard.Restore();
            Application.Current.Exit();
        }
        else
        {
            Process.Start(new ProcessStartInfo(update.ReleaseUrl) { UseShellExecute = true });
        }
    }

    private static bool IsInstalledInstance()
    {
        try
        {
            const string uninstallKey =
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B7E3F1A2-8C4D-4E5F-9A6B-1D2E3F4A5B6C}_is1";
            using var key = Registry.LocalMachine.OpenSubKey(uninstallKey);
            if (key == null) return false;

            var installLocation = key.GetValue("InstallLocation") as string;
            if (string.IsNullOrEmpty(installLocation)) return true;

            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var installDir = installLocation.TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(baseDir, installDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var version = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Fenstr/{version.Major}.{version.Minor}.{version.Build}");
        return client;
    }
}

internal record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

internal record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

[JsonSerializable(typeof(GitHubRelease))]
internal partial class UpdateJsonContext : JsonSerializerContext { }
