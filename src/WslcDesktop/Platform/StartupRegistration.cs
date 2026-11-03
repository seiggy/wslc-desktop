using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;
using WslcDesktop.Services;

namespace WslcDesktop.Platform;

/// <summary>
/// Start with Windows for each install form. Velopack and unpackaged builds use an HKCU Run entry (pointing at
/// the Velopack launcher stub so it survives updates); MSIX uses the manifest StartupTask. The OS is the source
/// of truth: <see cref="GetStatusAsync"/> reads it rather than a saved flag.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static InstallForm Form { get; } = DetectForm();

    public static async Task<StartupStatus> GetStatusAsync()
    {
        try
        {
            return Form == InstallForm.Msix ? await GetTaskStatusAsync() : GetRunStatus();
        }
        catch (Exception e)
        {
            return new StartupStatus(StartupState.Unavailable, e.Message);
        }
    }

    public static async Task<StartupStatus> SetEnabledAsync(bool enable)
    {
        try
        {
            if (Form == InstallForm.Msix)
            {
                var task = await StartupTask.GetAsync(StartupPolicy.StartupTaskId);
                if (enable)
                {
                    await task.RequestEnableAsync();
                }
                else
                {
                    task.Disable();
                }
            }
            else if (enable)
            {
                using var run = Registry.CurrentUser.CreateSubKey(RunKey);
                run.SetValue(StartupPolicy.RunValueName, ExpectedCommand(), RegistryValueKind.String);
            }
            else
            {
                Remove();
            }
        }
        catch (Exception e)
        {
            return new StartupStatus(StartupState.Unavailable, e.Message);
        }

        return await GetStatusAsync();
    }

    /// <summary>Keeps an enabled Run entry pointing at the current exe after the app folder moved or was reinstalled.</summary>
    public static void RepairIfStale()
    {
        if (Form == InstallForm.Msix)
        {
            return;
        }

        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            var current = run?.GetValue(StartupPolicy.RunValueName) as string;
            if (current is not null && !StartupPolicy.SameExecutable(current, ExpectedCommand()))
            {
                run!.SetValue(StartupPolicy.RunValueName, ExpectedCommand(), RegistryValueKind.String);
            }
        }
        catch (Exception)
        {
            // Best effort; the Settings page shows the real state.
        }
    }

    private static StartupStatus GetRunStatus()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(StartupPolicy.RunValueName) is not string)
        {
            return new StartupStatus(StartupState.Off, string.Empty);
        }

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return StartupPolicy.IsApprovedByUser(approved?.GetValue(StartupPolicy.RunValueName) as byte[])
            ? new StartupStatus(StartupState.On, string.Empty)
            : new StartupStatus(StartupState.DisabledByUser,
                "Turned off in Windows. Re-enable WSLc Desktop Manager under Settings > Apps > Startup.");
    }

    private static async Task<StartupStatus> GetTaskStatusAsync()
    {
        var task = await StartupTask.GetAsync(StartupPolicy.StartupTaskId);
        return task.State switch
        {
            StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => new StartupStatus(StartupState.On, string.Empty),
            StartupTaskState.DisabledByUser => new StartupStatus(StartupState.DisabledByUser,
                "Turned off in Windows. Re-enable WSLc Desktop Manager under Settings > Apps > Startup."),
            StartupTaskState.DisabledByPolicy => new StartupStatus(StartupState.DisabledByPolicy,
                "Your organization's policy prevents this app from starting with Windows."),
            _ => new StartupStatus(StartupState.Off, string.Empty),
        };
    }

    private static void Remove()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            run?.DeleteValue(StartupPolicy.RunValueName, throwOnMissingValue: false);
        }

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(StartupPolicy.RunValueName, throwOnMissingValue: false);
    }

    private static string ExpectedCommand()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("The app path is unknown.");
        return StartupPolicy.RunCommand(Form, path, Form == InstallForm.Velopack ? VelopackStub(path) : null);
    }

    // Velopack lays out <root>\current\app.exe plus a launcher stub in <root> that always starts the newest version.
    private static string? VelopackStub(string processPath)
    {
        var root = Directory.GetParent(Path.GetDirectoryName(processPath)!)?.FullName;
        return root is null
            ? null
            : Directory.EnumerateFiles(root, "*.exe")
                .FirstOrDefault(f => !Path.GetFileName(f).Equals("Update.exe", StringComparison.OrdinalIgnoreCase));
    }

    private static InstallForm DetectForm()
    {
        if (IsPackaged())
        {
            return InstallForm.Msix;
        }

        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        var parent = dir is null ? null : Directory.GetParent(dir)?.FullName;
        return parent is not null
            && Path.GetFileName(dir)!.Equals("current", StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(parent, "Update.exe"))
            ? InstallForm.Velopack
            : InstallForm.Unpackaged;
    }

    private static bool IsPackaged()
    {
        var length = 0;
        // APPMODEL_ERROR_NO_PACKAGE (15700) means an unpackaged process.
        return GetCurrentPackageFullName(ref length, null) != 15700;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, char[]? name);
}
