using System.Diagnostics;
using System.Security.Principal;

namespace OfficeNetwork.Windows;

public sealed record DiagnosticResult(string Name, bool Passed, string Detail);

public sealed class WindowsNetworkService
{
    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public async Task<IReadOnlyList<DiagnosticResult>> DiagnoseAsync(string serverName, int port = 5077)
    {
        var results = new List<DiagnosticResult>();
        results.Add(new("Administrator", IsAdministrator(), IsAdministrator() ? "Running elevated." : "Run as administrator for server setup."));

        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(serverName, 2500);
            results.Add(new("Server reachable", reply.Status == System.Net.NetworkInformation.IPStatus.Success, reply.Status.ToString()));
        }
        catch (Exception ex) { results.Add(new("Server reachable", false, ex.Message)); }

        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            await tcp.ConnectAsync(serverName, port).WaitAsync(TimeSpan.FromSeconds(3));
            results.Add(new("Chat service", true, $"TCP {port} is reachable."));
        }
        catch (Exception ex) { results.Add(new("Chat service", false, ex.Message)); }

        return results;
    }

    public async Task ConfigureServerAsync(string rootPath)
    {
        if (!IsAdministrator()) throw new InvalidOperationException("Server setup requires Administrator permission.");
        Directory.CreateDirectory(rootPath);
        foreach (var folder in new[] { "Working Files", "Submitted Files", "Final Files" }) Directory.CreateDirectory(Path.Combine(rootPath, folder));

        await RunPowerShellAsync($"New-NetFirewallRule -DisplayName 'Choice Flame Network Chat' -Direction Inbound -Protocol TCP -LocalPort 5077 -Action Allow -Profile Private -ErrorAction SilentlyContinue");
        await RunPowerShellAsync("Set-NetFirewallRule -DisplayGroup 'File and Printer Sharing' -Enabled True -Profile Private -ErrorAction SilentlyContinue");
        await RunPowerShellAsync("Set-NetConnectionProfile -NetworkCategory Private -ErrorAction SilentlyContinue");
    }

    public async Task CreateShareAsync(string shareName, string path)
    {
        if (!IsAdministrator()) throw new InvalidOperationException("Creating Windows shares requires Administrator permission.");
        var safeShare = shareName.Replace("'", "''");
        var safePath = path.Replace("'", "''");
        await RunPowerShellAsync($"if (-not (Get-SmbShare -Name '{safeShare}' -ErrorAction SilentlyContinue)) {{ New-SmbShare -Name '{safeShare}' -Path '{safePath}' -FullAccess 'Administrators' }}");
    }

    public async Task ConfigureUserPermissionsAsync(string rootPath, string userName, bool isDirector)
    {
        if (!IsAdministrator()) throw new InvalidOperationException("Permission setup requires Administrator permission.");
        var safeUser = new string(userName.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.').ToArray());
        if (string.IsNullOrWhiteSpace(safeUser)) throw new InvalidOperationException("Invalid Windows username.");

        var root = rootPath.Replace("'", "''");
        if (isDirector)
        {
            await RunPowerShellAsync($"icacls '{root}' /grant '{safeUser}:(OI)(CI)F' /T /C");
            return;
        }

        var working = Path.Combine(rootPath, "Working Files", safeUser).Replace("'", "''");
        var submitted = Path.Combine(rootPath, "Submitted Files", safeUser).Replace("'", "''");
        var final = Path.Combine(rootPath, "Final Files").Replace("'", "''");
        Directory.CreateDirectory(Path.Combine(rootPath, "Working Files", safeUser));
        Directory.CreateDirectory(Path.Combine(rootPath, "Submitted Files", safeUser));

        await RunPowerShellAsync($"icacls '{working}' /grant '{safeUser}:(OI)(CI)F' /T /C");
        await RunPowerShellAsync($"icacls '{submitted}' /grant '{safeUser}:(OI)(CI)RX' /T /C");
        await RunPowerShellAsync($"icacls '{final}' /grant '{safeUser}:(OI)(CI)RX' /T /C");
    }

    private static async Task RunPowerShellAsync(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }
}
