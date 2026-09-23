using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Client;

public partial class MainWindow : Window
{
    private HubConnection? _connection;
    private readonly Guid _userId = Guid.NewGuid();

    public MainWindow() => InitializeComponent();

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
