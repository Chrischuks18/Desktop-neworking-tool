using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Client;

public partial class MainWindow : Window
{
    private HubConnection? _connection;
    private readonly Guid _userId = Guid.NewGuid();

    public MainWindow() => InitializeComponent();

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string page)
            return;

        PageTitle.Text = page == "Dashboard" ? "Good day, Director" : page;
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
