using System.Windows;
using System.Net.Http;
using System.Diagnostics;
using System.IO;
using OfficeNetwork.Windows;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Client;

public partial class MainWindow : Window
{
    private HubConnection? _connection;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly HttpClient _http = new();
    private OfficeFolder? _activeFolder;
    private readonly WindowsNetworkService _windowsNetwork = new();
    private Process? _serverProcess;

    public MainWindow() => InitializeComponent();

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string page)
            return;

        PageTitle.Text = page == "Dashboard" ? "Good day, Director" : page;
        DashboardContent.Visibility = page is "Dashboard" or "Office Chat" ? Visibility.Visible : Visibility.Collapsed;
        SectionContent.Visibility = page is "Working Files" or "Submitted Files" or "Final Files" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        UsersContent.Visibility = page == "Users" ? Visibility.Visible : Visibility.Collapsed;

        if (page is "Working Files" or "Submitted Files" or "Final Files")
        {
            _activeFolder = Enum.Parse<OfficeFolder>(page.Replace(" ", ""));
            SectionHeading.Text = page;
            WorkflowButton.Visibility = page == "Final Files" ? Visibility.Collapsed : Visibility.Visible;
            WorkflowButton.Content = page == "Submitted Files" ? "Approve to Final" : "Submit for Review";
            _ = LoadFilesAsync();
        }

        PageSubtitle.Text = page switch
        {
            "Dashboard" => "Manage your office files, staff and communication from one place.",
            "Working Files" => "Open and manage files currently being prepared by the team.",
            "Submitted Files" => "Review work submitted by Editors and News Sourcing staff.",
            "Final Files" => "Access approved final materials. Staff access is read-only by default.",
            "Office Chat" => "Message individuals, departments or everyone connected to the office network.",
            "Users" => "Manage Director, Editor and News Sourcing accounts and access levels.",
            "Settings" => "Configure the server, shared folders, network and application preferences.",
            _ => ""
        };
    }

    private async Task LoadFilesAsync()
    {
        if (_activeFolder is null) return;
        try
        {
            var baseUrl = ServerAddress.Text.TrimEnd('/');
            var files = await _http.GetFromJsonAsync<OfficeFileItem[]>($"{baseUrl}/api/files/{_activeFolder}");
            FileList.ItemsSource = files ?? [];
            SectionNotice.Text = $"{files?.Length ?? 0} file(s) available on the server.";
        }
        catch (Exception ex)
        {
            SectionNotice.Text = $"Could not load files: {ex.Message}";
        }
    }

    private async void RefreshFiles_Click(object sender, RoutedEventArgs e) => await LoadFilesAsync();

    private async void Workflow_Click(object sender, RoutedEventArgs e)
    {
        if (_activeFolder is null || FileList.SelectedItem is not OfficeFileItem file) return;
        try
        {
            var baseUrl = ServerAddress.Text.TrimEnd('/');
            var endpoint = _activeFolder == OfficeFolder.SubmittedFiles
                ? $"{baseUrl}/api/files/SubmittedFiles/approve?fileName={Uri.EscapeDataString(file.Name)}"
                : $"{baseUrl}/api/files/{_activeFolder}/submit?fileName={Uri.EscapeDataString(file.Name)}";
            var response = await _http.PostAsync(endpoint, null);
            SectionNotice.Text = response.IsSuccessStatusCode
                ? (_activeFolder == OfficeFolder.SubmittedFiles ? "File approved and moved to Final Files." : "File submitted for review.")
                : "The server could not complete that action.";
            await LoadFilesAsync();
        }
        catch (Exception ex)
        {
            SectionNotice.Text = $"Action failed: {ex.Message}";
        }
    }


    private async void SetupServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SettingsStatus.Text = "Configuring Windows network and office folders…";
            await _windowsNetwork.ConfigureServerAsync(ServerRootPath.Text.Trim());
            await _windowsNetwork.CreateShareAsync("ChoiceFlame", ServerRootPath.Text.Trim());
            StartBundledServer();
            ServerAddress.Text = "http://localhost:5077";
            SettingsStatus.Text = "Server configured. Office folders and the ChoiceFlame network share were created, firewall rules were enabled, and the local server was started.";
            ConnectionStatus.Text = "Server running";
        }
        catch (Exception ex)
        {
            SettingsStatus.Text = "Server setup failed: " + ex.Message;
        }
    }

    private void StartServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartBundledServer();
            ServerAddress.Text = "http://localhost:5077";
            SettingsStatus.Text = "Choice Flame server started on this computer.";
            ConnectionStatus.Text = "Server running";
        }
        catch (Exception ex)
        {
            SettingsStatus.Text = "Could not start server: " + ex.Message;
        }
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var server = new Uri(ServerAddress.Text).Host;
        var results = await _windowsNetwork.DiagnoseAsync(server);
        SettingsStatus.Text = string.Join(Environment.NewLine, results.Select(r => $"{(r.Passed ? "✓" : "✗")} {r.Name}: {r.Detail}"));
    }

    private void StartBundledServer()
    {
        if (_serverProcess is { HasExited: false }) return;
        var exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Server", "OfficeNetwork.Server.exe"));
        if (!File.Exists(exe))
            throw new FileNotFoundException("The bundled server component was not found.", exe);
        _serverProcess = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        if (_serverProcess is null) throw new InvalidOperationException("Windows could not start the Choice Flame server.");
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _connection = new HubConnectionBuilder()
            .WithUrl($"{ServerAddress.Text.TrimEnd('/')}/hubs/chat")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ChatMessage>("ReceiveMessage", message =>
            Dispatcher.Invoke(() =>
                Messages.Items.Add($"{message.SentAt.ToLocalTime():HH:mm}  {message.SenderName}: {message.Text}")));

        _connection.On<PresenceInfo[]>("PresenceChanged", users =>
            Dispatcher.Invoke(() =>
            {
                OnlineUsers.Items.Clear();
                foreach (var user in users)
                    OnlineUsers.Items.Add($"{user.DisplayName} — {user.Role}");
            }));

        _connection.Reconnecting += _ =>
        {
            Dispatcher.Invoke(() => ConnectionStatus.Text = "Reconnecting…");
            return Task.CompletedTask;
        };

        _connection.Reconnected += _ =>
        {
            Dispatcher.Invoke(() => ConnectionStatus.Text = "Connected");
            return RegisterAsync();
        };

        try
        {
            await _connection.StartAsync();
            await RegisterAsync();
            ConnectionStatus.Text = "Connected";
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = $"Connection failed: {ex.Message}";
        }
    }

    private async Task RegisterAsync()
    {
        if (_connection is null) return;
        var role = Enum.Parse<OfficeRole>(((System.Windows.Controls.ComboBoxItem)RoleBox.SelectedItem).Content.ToString()!);
        await _connection.InvokeAsync("Register", _userId, DisplayName.Text.Trim(), role);
    }

    private async void SendEveryone_Click(object sender, RoutedEventArgs e)
    {
        if (_connection?.State != HubConnectionState.Connected || string.IsNullOrWhiteSpace(MessageText.Text))
            return;

        var message = new ChatMessage(
            Guid.NewGuid(), _userId, DisplayName.Text.Trim(), null, null,
            MessageText.Text.Trim(), DateTimeOffset.UtcNow, true);

        await _connection.InvokeAsync("SendToEveryone", message);
        MessageText.Clear();
    }
}
