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
    private LoginResult? _currentUser;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            MaxWidth = SystemParameters.WorkArea.Width;
            MaxHeight = SystemParameters.WorkArea.Height;
            Width = Math.Min(1100, SystemParameters.WorkArea.Width * 0.92);
            Height = Math.Min(720, SystemParameters.WorkArea.Height * 0.92);
            await EnsureLocalServerAsync();
        };
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string page)
            return;

        PageTitle.Text = page == "Dashboard" ? $"Good day, {_currentUser?.DisplayName ?? "Server Administrator"}" : page;
        DashboardContent.Visibility = page is "Dashboard" or "Office Chat" ? Visibility.Visible : Visibility.Collapsed;
        SectionContent.Visibility = page is "Working Files" or "Submitted Files" or "Final Files" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        UsersContent.Visibility = page == "Users" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "Users") _ = LoadUsersAsync();

        if (page is "Working Files" or "Submitted Files" or "Final Files")
        {
            _activeFolder = Enum.Parse<OfficeFolder>(page.Replace(" ", ""));
            SectionHeading.Text = page;
            WorkflowButton.Visibility = page switch
            {
                "Working Files" when _currentUser?.Role is OfficeRole.Editor or OfficeRole.NewsSourcing => Visibility.Visible,
                "Submitted Files" when _currentUser?.Role == OfficeRole.Director => Visibility.Visible,
                _ => Visibility.Collapsed
            };
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
            var baseUrl = LoginServerAddress.Text.TrimEnd('/');
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
            var baseUrl = LoginServerAddress.Text.TrimEnd('/');
            var endpoint = _activeFolder == OfficeFolder.SubmittedFiles
                ? $"{baseUrl}/api/files/SubmittedFiles/approve?ownerUserName={Uri.EscapeDataString(file.OwnerUserName ?? "")}&fileName={Uri.EscapeDataString(file.Name)}"
                : $"{baseUrl}/api/files/WorkingFiles/submit?fileName={Uri.EscapeDataString(file.Name)}";
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


    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseUrl = LoginServerAddress.Text.TrimEnd('/');
            var response = await _http.PostAsJsonAsync($"{baseUrl}/api/login", new LoginRequest(LoginUserName.Text.Trim(), LoginPassword.Password));
            if (!response.IsSuccessStatusCode) { LoginStatus.Text = "Incorrect username/password or the account is disabled."; return; }
            _currentUser = await response.Content.ReadFromJsonAsync<LoginResult>();
            if (_currentUser is null) { LoginStatus.Text = "The server returned an invalid login."; return; }
            LoginServerAddress.Text = baseUrl;
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _currentUser.Token);
            CurrentUserName.Text = _currentUser.DisplayName;
            CurrentUserRole.Text = _currentUser.Role.ToString();
            SettingsNavButton.Visibility = Visibility.Collapsed;
            UsersNavButton.Visibility = _currentUser.Role == OfficeRole.Director ? Visibility.Visible : Visibility.Collapsed;
            LoginOverlay.Visibility = Visibility.Collapsed;
            PageTitle.Text = $"Good day, {_currentUser.DisplayName}";
            await ConnectToServerAsync();
        }
        catch (Exception ex) { LoginStatus.Text = "Could not sign in: " + ex.Message; }
    }

    private void ServerAdminMode_Click(object sender, RoutedEventArgs e)
    {
        LoginServerAddress.Text = "http://localhost:5077";
        try
        {
            StartBundledServer();
            ConnectionStatus.Text = "Starting server…";
        }
        catch (Exception ex)
        {
            LoginStatus.Text = "Could not start the local server: " + ex.Message;
        }
        FirstDirectorPanel.Visibility = Visibility.Visible;
        LoginStatus.Text = "Create the first Director below. The local server will be used.";
    }

    private async void CreateFirstDirector_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FirstDirectorName.Text) || string.IsNullOrWhiteSpace(FirstDirectorUserName.Text) || string.IsNullOrWhiteSpace(FirstDirectorPassword.Password))
            {
                FirstDirectorStatus.Text = "Please enter the Director's full name, username and password.";
                return;
            }
            FirstDirectorStatus.Text = "Creating Director account…";
            StartBundledServer();
            await Task.Delay(1200);
            var baseUrl = "http://localhost:5077";
            LoginServerAddress.Text = baseUrl;
            var request = new CreateUserRequest(FirstDirectorUserName.Text.Trim(), FirstDirectorName.Text.Trim(), OfficeRole.Director, FirstDirectorPassword.Password);
            var response = await _http.PostAsJsonAsync($"{baseUrl}/api/users", request);
            if (!response.IsSuccessStatusCode)
            {
                FirstDirectorStatus.Text = "Could not create Director: " + await response.Content.ReadAsStringAsync();
                return;
            }
            FirstDirectorStatus.Text = "Director account created. You can now sign in.";
            LoginUserName.Text = FirstDirectorUserName.Text.Trim();
            LoginPassword.Password = FirstDirectorPassword.Password;
            FirstDirectorPanel.Visibility = Visibility.Collapsed;
            LoginStatus.Text = "First Director created successfully. Click Sign In.";
        }
        catch (Exception ex) { FirstDirectorStatus.Text = "Could not reach the server: " + ex.Message; }
    }

    private void EnterServerAdministration()
    {
        LoginOverlay.Visibility = Visibility.Collapsed;
        CurrentUserName.Text = "Server";
        CurrentUserRole.Text = "Server Administrator";
        UsersNavButton.Visibility = Visibility.Visible;
        SettingsNavButton.Visibility = Visibility.Visible;
        PageTitle.Text = "Server Administration";
    }

    private async void CreateUser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var role = Enum.Parse<OfficeRole>(((System.Windows.Controls.ComboBoxItem)NewRole.SelectedItem).Content.ToString()!);
            var request = new CreateUserRequest(NewUserName.Text.Trim(), _currentUser?.DisplayName ?? "Server Administrator", role, NewPassword.Password);
            var response = await _http.PostAsJsonAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/users", request);
            UserStatus.Text = response.IsSuccessStatusCode ? "User created successfully." : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) { NewUserName.Clear(); NewDisplayName.Clear(); NewPassword.Clear(); await LoadUsersAsync(); }
        }
        catch (Exception ex) { UserStatus.Text = "Could not create user: " + ex.Message; }
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await _http.GetFromJsonAsync<OfficeUser[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/users");
            UserList.ItemsSource = users?.Select(u => $"{u.DisplayName} — {u.Role} — {u.UserName}").ToArray() ?? [];
        }
        catch (Exception ex) { UserStatus.Text = "Could not load users: " + ex.Message; }
    }

    private async void SetupServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SettingsStatus.Text = "Configuring Windows network and office folders…";
            await _windowsNetwork.ConfigureServerAsync(ServerRootPath.Text.Trim());
            await _windowsNetwork.CreateShareAsync("ChoiceFlame", ServerRootPath.Text.Trim());
            StartBundledServer();
            LoginServerAddress.Text = "http://localhost:5077";
            SettingsStatus.Text = "Server configured. Office folders and the ChoiceFlame network share were created, firewall rules were enabled, and the local server was started.";
            ConnectionStatus.Text = "Server running";
            await Task.Delay(800);
            await ConnectToServerAsync();
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
            LoginServerAddress.Text = "http://localhost:5077";
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
        var server = new Uri(LoginServerAddress.Text).Host;
        var results = await _windowsNetwork.DiagnoseAsync(server);
        SettingsStatus.Text = string.Join(Environment.NewLine, results.Select(r => $"{(r.Passed ? "✓" : "✗")} {r.Name}: {r.Detail}"));
    }

    private async Task EnsureLocalServerAsync()
    {
        if (!string.Equals(LoginServerAddress.Text.TrimEnd('/'), "http://localhost:5077", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            var response = await probe.GetAsync("http://localhost:5077/api/status");
            if (response.IsSuccessStatusCode) return;
        }
        catch { }

        try
        {
            StartBundledServer();
            LoginStatus.Text = "Starting the Choice Flame server…";
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(500);
                try
                {
                    using var probe = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                    var response = await probe.GetAsync("http://localhost:5077/api/status");
                    if (response.IsSuccessStatusCode)
                    {
                        LoginStatus.Text = "Server ready. You can sign in.";
                        ConnectionStatus.Text = "Server running";
                        return;
                    }
                }
                catch { }
            }
            LoginStatus.Text = "The local server could not be started. Use Server Administrator Setup.";
        }
        catch (Exception ex) { LoginStatus.Text = "Could not start local server: " + ex.Message; }
    }

    private void StartBundledServer()
    {
        if (_serverProcess is { HasExited: false }) return;
        var exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Server", "OfficeNetwork.Server.exe"));
        if (!File.Exists(exe))
            throw new FileNotFoundException("The bundled server component was not found.", exe);
        _serverProcess = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        if (_serverProcess is null) throw new InvalidOperationException("Windows could not start the Choice Flame server.");
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        await ConnectToServerAsync();
    }

    private async Task ConnectToServerAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _connection = new HubConnectionBuilder()
            .WithUrl($"{LoginServerAddress.Text.TrimEnd('/')}/hubs/chat")
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
        var role = _currentUser?.Role ?? OfficeRole.ServerAdministrator;
        await _connection.InvokeAsync("Register", _userId, _currentUser?.DisplayName ?? "Server Administrator", role);
    }

    private async void SendEveryone_Click(object sender, RoutedEventArgs e)
    {
        if (_connection?.State != HubConnectionState.Connected || string.IsNullOrWhiteSpace(MessageText.Text))
            return;

        var message = new ChatMessage(
            Guid.NewGuid(), _currentUser?.UserId ?? _userId, _currentUser?.DisplayName ?? _currentUser?.DisplayName ?? "Server Administrator", null, null,
            MessageText.Text.Trim(), DateTimeOffset.UtcNow, true);

        await _connection.InvokeAsync("SendToEveryone", message);
        MessageText.Clear();
    }
}
