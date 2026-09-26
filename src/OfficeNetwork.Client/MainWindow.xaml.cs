using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Brush = System.Windows.Media.Brush;
using System.Windows;
using System.Net.Http;
using System.Diagnostics;
using System.IO;
using OfficeNetwork.Windows;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using OfficeNetwork.Shared;
using Microsoft.Win32;
using NAudio.Wave;
using System.Media;
using System.Windows.Input;
using System.Windows.Media;

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
    private Guid? _callPeerId;
    private WaveInEvent? _microphone;
    private WaveOutEvent? _speaker;
    private BufferedWaveProvider? _audioBuffer;
    private bool _muted;
    private string? _assignmentFilePath;
    private readonly System.Windows.Threading.DispatcherTimer _sessionHeartbeat = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly System.Windows.Threading.DispatcherTimer _activityReminderTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private OfficeActivity[] _calendarActivities=[];
    private readonly HashSet<string> _shownActivityReminders=[];
    private readonly System.Windows.Forms.NotifyIcon _trayIcon = new();
    private bool _exitRequested;
    private static readonly string ClientSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Choice Flame Communications Network", "client-server.txt");

    public MainWindow()
    {
        InitializeComponent();
        _activityReminderTimer.Tick+=async (_,_)=>await CheckActivityRemindersAsync();
        _sessionHeartbeat.Tick += async (_, _) =>
        {
            if(_currentUser is null)return;
            try
            {
                var response=await _http.PostAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/session/heartbeat",null);
                if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized)
                {
                    _sessionHeartbeat.Stop();
                    Dispatcher.Invoke(()=>MessageBox.Show("Your login session has expired. Please sign in again.","Session expired",MessageBoxButton.OK,MessageBoxImage.Information));
                }
            }
            catch { }
        };
        var trayMenu=new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add("Open Choice Flame Network",null,(_,_)=>Dispatcher.Invoke(RestoreFromTray));
        trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        trayMenu.Items.Add("Exit App",null,(_,_)=>Dispatcher.Invoke(async ()=>await ExitApplicationAsync()));
        _trayIcon.Text="Choice Flame Communications Network";
        _trayIcon.Icon=new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory,"Assets","ChoiceFlame.ico"));
        _trayIcon.ContextMenuStrip=trayMenu;
        _trayIcon.DoubleClick+=(_,_)=>Dispatcher.Invoke(RestoreFromTray);
        _trayIcon.Visible=true;
        Closing += (sender,e) =>
        {
            if(_exitRequested)return;
            e.Cancel=true;
            Hide();
            _trayIcon.ShowBalloonTip(2000,"Choice Flame Network","The app is still running in the background. Right-click this icon and choose Exit App to close it.",System.Windows.Forms.ToolTipIcon.Info);
        };
        Loaded += async (_, _) =>
        {
            MaxWidth = SystemParameters.WorkArea.Width;
            MaxHeight = SystemParameters.WorkArea.Height;
            Width = Math.Min(1100, SystemParameters.WorkArea.Width * 0.92);
            Height = Math.Min(720, SystemParameters.WorkArea.Height * 0.92);
            LoadSavedServerAddress();
            var serverInstallation=HasBundledServer();
            ServerAdministratorSetupButton.Visibility=serverInstallation?Visibility.Visible:Visibility.Collapsed;
            FirstDirectorPanel.Visibility=Visibility.Collapsed;
            if(serverInstallation) await EnsureLocalServerAsync();
        };
    }

    private void ShowTrayNotification(string title,string message)
    {
        var safe=string.IsNullOrWhiteSpace(message)?"Open Choice Flame Network to view details.":message;
        if(safe.Length>220)safe=safe[..217]+"...";
        _trayIcon.ShowBalloonTip(5000,title,safe,System.Windows.Forms.ToolTipIcon.Info);
    }

    public void RestoreFromExternalLaunch()
    {
        RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState=WindowState.Normal;
        Activate();
        Topmost=true;
        Topmost=false;
        Focus();
    }

    private async Task ExitApplicationAsync()
    {
        if(_exitRequested)return;
        _exitRequested=true;
        _sessionHeartbeat.Stop();
        try
        {
            if(_currentUser is not null) await _http.PostAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/logout",null);
        }
        catch { }
        try { if(_connection is not null) await _connection.DisposeAsync(); } catch { }
        StopAudio();
        _trayIcon.Visible=false;
        _trayIcon.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string page)
            return;

        PageTitle.Text = page == "Dashboard" ? $"Good day, {_currentUser?.DisplayName ?? "Server Administrator"}" : page;
        DashboardContent.Visibility = page is "Dashboard" or "Office Chat" ? Visibility.Visible : Visibility.Collapsed;
        SectionContent.Visibility = page is "Working Files" or "Submitted Files" or "Final Files" ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        CalendarContent.Visibility = page == "Calendar of Activities" ? Visibility.Visible : Visibility.Collapsed;
        AssignmentsContent.Visibility = page == "Assigned Work" ? Visibility.Visible : Visibility.Collapsed;
        UsersContent.Visibility = page == "Users" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "Users") _ = LoadUsersAsync();
        if (page == "Assigned Work") _ = LoadAssignmentsAsync();
        if (page == "Dashboard") _ = LoadDashboardSummaryAsync();
        if (page == "Settings") _ = LoadStorageConfigurationAsync();
        if (page == "Calendar of Activities") _ = LoadCalendarActivitiesAsync();

        if (page is "Working Files" or "Submitted Files" or "Final Files")
        {
            _activeFolder = Enum.Parse<OfficeFolder>(page.Replace(" ", ""));
            SectionHeading.Text = page;
            WorkflowButton.Visibility = page switch
            {
                "Working Files" when _currentUser?.Role is OfficeRole.Editor or OfficeRole.NewsSourcing => Visibility.Visible,
                "Submitted Files" when _currentUser?.Role is OfficeRole.Director or OfficeRole.Admin => Visibility.Visible,
                _ => Visibility.Collapsed
            };
            WorkflowButton.Content = page == "Submitted Files" ? "Approve to Final" : "Submit for Review";
            AddWorkingFileButton.Visibility = page == "Working Files" ? Visibility.Visible : Visibility.Collapsed;
            BackupFinalFilesButton.Visibility = page == "Final Files" && _currentUser?.Role is OfficeRole.Director or OfficeRole.Admin ? Visibility.Visible : Visibility.Collapsed;
            OpenFileButton.Visibility = Visibility.Visible;
            RenameFileButton.Visibility = page == "Working Files" ? Visibility.Visible : Visibility.Collapsed;
            DeleteFileButton.Visibility = page == "Working Files" ? Visibility.Visible : Visibility.Collapsed;
            RecallButton.Visibility = page == "Submitted Files" && _currentUser?.Role is OfficeRole.Editor or OfficeRole.NewsSourcing ? Visibility.Visible : Visibility.Collapsed;
            ReturnCorrectionButton.Visibility = page == "Submitted Files" && _currentUser?.Role is OfficeRole.Director or OfficeRole.Admin ? Visibility.Visible : Visibility.Collapsed;
            DirectorMinutePanel.Visibility = page == "Submitted Files" && _currentUser?.Role is OfficeRole.Director or OfficeRole.Admin ? Visibility.Visible : Visibility.Collapsed;
            _ = LoadFilesAsync();
        }

        PageSubtitle.Text = page switch
        {
            "Dashboard" => "Manage your office files, staff and communication from one place.",
            "Assigned Work" => "Assign, track and complete daily work with files and instructions.",
            "Calendar of Activities" => "View scheduled office activities, event coverage and upcoming reminders.",
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


    private async void BackupFinalFiles_Click(object sender, RoutedEventArgs e)
    {
        if(_currentUser?.Role is not (OfficeRole.Director or OfficeRole.Admin) || _activeFolder!=OfficeFolder.FinalFiles)return;
        var dialog=new System.Windows.Forms.FolderBrowserDialog
        {
            Description="Choose the drive or folder where the Final Files backup should be copied.",
            UseDescriptionForTitle=true,
            ShowNewFolderButton=true
        };
        if(dialog.ShowDialog()!=System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))return;
        try
        {
            SectionNotice.Text="Preparing Final Files backup…";
            var response=await _http.GetAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/files/FinalFiles/backup");
            if(!response.IsSuccessStatusCode)
            {
                SectionNotice.Text="Backup failed: "+await response.Content.ReadAsStringAsync();
                return;
            }
            var disposition=response.Content.Headers.ContentDisposition;
            var fileName=disposition?.FileNameStar ?? disposition?.FileName?.Trim('"') ?? $"Choice-Flame-Final-Files-Backup-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip";
            fileName=Path.GetFileName(fileName);
            var destination=Path.Combine(dialog.SelectedPath,fileName);
            if(File.Exists(destination))
                destination=Path.Combine(dialog.SelectedPath,$"{Path.GetFileNameWithoutExtension(fileName)}-{DateTime.Now:HHmmss}{Path.GetExtension(fileName)}");
            await using(var output=File.Create(destination))
                await response.Content.CopyToAsync(output);
            SectionNotice.Text=$"Backup completed successfully: {destination}";
            SystemSounds.Asterisk.Play();
            MessageBox.Show($"Final Files were copied successfully.\n\nBackup: {destination}\n\nThe originals on the office server were not changed.","Backup complete",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex)
        {
            SectionNotice.Text="Backup failed: "+ex.Message;
            MessageBox.Show("The backup could not be completed. "+ex.Message,"Backup failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    private async void AddWorkingFile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null || _activeFolder != OfficeFolder.WorkingFiles) return;
        var dialog = new OpenFileDialog { Title = "Choose a file to add to Working Files" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SectionNotice.Text = "Uploading file…";
            await using var stream = File.OpenRead(dialog.FileName);
            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(stream);
            form.Add(fileContent, "file", Path.GetFileName(dialog.FileName));
            var response = await _http.PostAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/files/WorkingFiles/upload", form);
            SectionNotice.Text = response.IsSuccessStatusCode ? "File added to Working Files." : "Upload failed: " + await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                SystemSounds.Asterisk.Play();
                if (_connection?.State == HubConnectionState.Connected && _currentUser.Role != OfficeRole.Director)
                    await _connection.InvokeAsync("NotifyDirectorsOfFile", "New upload", Path.GetFileName(dialog.FileName), "A new file was uploaded to Working Files.");
                await LoadFilesAsync();
            }
        }
        catch (Exception ex) { SectionNotice.Text = "Upload failed: " + ex.Message; }
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_activeFolder is null || FileList.SelectedItem is not OfficeFileItem file) return;
        try
        {
            var url = $"{LoginServerAddress.Text.TrimEnd('/')}/api/files/{_activeFolder}/download?fileName={Uri.EscapeDataString(file.Name)}";
            if (!string.IsNullOrWhiteSpace(file.OwnerUserName))
                url += $"&ownerUserName={Uri.EscapeDataString(file.OwnerUserName)}";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) { SectionNotice.Text = "Could not download the selected file."; return; }
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Choice Flame");
            Directory.CreateDirectory(downloads);
            var localPath = Path.Combine(downloads, Path.GetFileName(file.Name));
            await using (var output = File.Create(localPath))
                await response.Content.CopyToAsync(output);
            Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
            SectionNotice.Text = $"Downloaded to {localPath}";
        }
        catch (Exception ex) { SectionNotice.Text = "Could not open file: " + ex.Message; }
    }


    private async void Recall_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not OfficeFileItem file) return;
        var response=await _http.PostAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/files/SubmittedFiles/recall?fileName={Uri.EscapeDataString(file.Name)}",null);
        SectionNotice.Text=response.IsSuccessStatusCode?"Submission recalled to Working Files.":"Could not recall the submission.";
        if(response.IsSuccessStatusCode) await LoadFilesAsync();
    }

    private async void ReturnCorrection_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not OfficeFileItem file || string.IsNullOrWhiteSpace(file.OwnerUserName)) { SectionNotice.Text="Select a submitted file first."; return; }
        if(string.IsNullOrWhiteSpace(DirectorMinuteText.Text)) { SectionNotice.Text="Enter the Director's minute/instruction before returning the file."; return; }
        var body=new ReturnFileRequest(file.OwnerUserName,file.Name,DirectorMinuteText.Text.Trim());
        var response=await _http.PostAsJsonAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/files/SubmittedFiles/return",body);
        SectionNotice.Text=response.IsSuccessStatusCode?"Returned for correction with the Director's minute.":"Could not return the file.";
        if(response.IsSuccessStatusCode)
        {
            SystemSounds.Asterisk.Play();
            if (_connection?.State == HubConnectionState.Connected)
            {
                var users = await _http.GetFromJsonAsync<OfficeUser[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/users");
                var owner = users?.FirstOrDefault(x => x.UserName.Equals(file.OwnerUserName, StringComparison.OrdinalIgnoreCase));
                if (owner is not null) await _connection.InvokeAsync("NotifyFileOwner", owner.Id, "Returned for correction", file.Name, "Director's Minute: " + DirectorMinuteText.Text.Trim());
            }
            DirectorMinuteText.Clear(); await LoadFilesAsync();
        }
    }

    private async void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if(_activeFolder!=OfficeFolder.WorkingFiles || FileList.SelectedItem is not OfficeFileItem file)return;
        if(MessageBox.Show($"Delete {file.Name}?","Confirm deletion",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        var response=await _http.DeleteAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/files/WorkingFiles?fileName={Uri.EscapeDataString(file.Name)}");
        SectionNotice.Text=response.IsSuccessStatusCode?"File deleted.":"Could not delete the file.";
        if(response.IsSuccessStatusCode) await LoadFilesAsync();
    }

    private async void RenameFile_Click(object sender, RoutedEventArgs e)
    {
        if(_activeFolder!=OfficeFolder.WorkingFiles || FileList.SelectedItem is not OfficeFileItem file)return;
        var ext=Path.GetExtension(file.Name); var stem=Path.GetFileNameWithoutExtension(file.Name);
        var dialog=new Window{Title="Rename File",Width=420,Height=170,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,ResizeMode=ResizeMode.NoResize};
        var panel=new System.Windows.Controls.StackPanel{Margin=new Thickness(18)};
        var box=new System.Windows.Controls.TextBox{Text=stem,Padding=new Thickness(8)};
        var button=new System.Windows.Controls.Button{Content="Rename",Margin=new Thickness(0,12,0,0),Padding=new Thickness(12,7,12,7),IsDefault=true};
        panel.Children.Add(new System.Windows.Controls.TextBlock{Text="New file name"}); panel.Children.Add(box); panel.Children.Add(button); dialog.Content=panel;
        button.Click+=(_,_)=>dialog.DialogResult=true;
        if(dialog.ShowDialog()!=true || string.IsNullOrWhiteSpace(box.Text))return;
        var response=await _http.PostAsJsonAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/files/WorkingFiles/rename",new RenameFileRequest(file.Name,box.Text.Trim()+ext));
        SectionNotice.Text=response.IsSuccessStatusCode?"File renamed.":"Could not rename the file.";
        if(response.IsSuccessStatusCode) await LoadFilesAsync();
    }

    private void FileList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(FileList.SelectedItem is not OfficeFileItem file)return;
        if(!string.IsNullOrWhiteSpace(file.DirectorMinute))
            SectionNotice.Text="Director's Minute: "+file.DirectorMinute;
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
            if (response.IsSuccessStatusCode)
            {
                SystemSounds.Asterisk.Play();
                if (_connection?.State == HubConnectionState.Connected)
                {
                    if (_activeFolder == OfficeFolder.WorkingFiles)
                        await _connection.InvokeAsync("NotifyDirectorsOfFile", "Submitted for review", file.Name, "A file is awaiting review.");
                    else if (_activeFolder == OfficeFolder.SubmittedFiles)
                    {
                        var owner = (await _http.GetFromJsonAsync<OfficeUser[]>($"{baseUrl}/api/users"))?.FirstOrDefault(x => x.UserName.Equals(file.OwnerUserName, StringComparison.OrdinalIgnoreCase));
                        if (owner is not null) await _connection.InvokeAsync("NotifyFileOwner", owner.Id, "Approved", file.Name, "Your file was approved and moved to Final Files.");
                    }
                }
            }
            await LoadFilesAsync();
        }
        catch (Exception ex)
        {
            SectionNotice.Text = $"Action failed: {ex.Message}";
        }
    }


    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        SignInButton.Content = "";
        SignInProgress.Visibility = Visibility.Visible;
        LoginStatus.Text = "Signing in…";
        try
        {
            var baseUrl = LoginServerAddress.Text.TrimEnd('/');
            var response = await _http.PostAsJsonAsync($"{baseUrl}/api/login", new LoginRequest(LoginUserName.Text.Trim(), LoginPassword.Password));
            if (!response.IsSuccessStatusCode)
            {
                LoginStatus.Text = response.StatusCode==System.Net.HttpStatusCode.Conflict
                    ? await response.Content.ReadAsStringAsync()
                    : "Incorrect username/password or the account is disabled.";
                return;
            }
            _currentUser = await response.Content.ReadFromJsonAsync<LoginResult>();
            if (_currentUser is null) { LoginStatus.Text = "The server returned an invalid login."; return; }
            LoginServerAddress.Text = baseUrl;
            SaveServerAddress(baseUrl);
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _currentUser.Token);
            _sessionHeartbeat.Start();
            _activityReminderTimer.Start();
            CurrentUserName.Text = _currentUser.DisplayName;
            CurrentUserRole.Text = _currentUser.Role.ToString();
            var serverInstallation = HasBundledServer();
            SettingsNavButton.Visibility = Visibility.Collapsed;
            UsersNavButton.Visibility = serverInstallation && _currentUser.Role is OfficeRole.Director or OfficeRole.Admin ? Visibility.Visible : Visibility.Collapsed;
            AssignWorkPanel.Visibility = serverInstallation && _currentUser.Role is OfficeRole.Director or OfficeRole.Admin ? Visibility.Visible : Visibility.Collapsed;
            CompleteAssignmentButton.Visibility = _currentUser.Role is OfficeRole.Editor or OfficeRole.NewsSourcing ? Visibility.Visible : Visibility.Collapsed;
            MessageEveryoneButton.Visibility = _currentUser.Role == OfficeRole.Director ? Visibility.Visible : Visibility.Collapsed;
            SendEveryoneButton.Visibility = _currentUser.Role == OfficeRole.Director ? Visibility.Visible : Visibility.Collapsed;
            LoginOverlay.Visibility = Visibility.Collapsed;
            PageTitle.Text = $"Good day, {_currentUser.DisplayName}";
            await ConnectToServerAsync();
            await RefreshPersistentAssignmentNoticeAsync();
            await LoadDashboardSummaryAsync();
            await CheckActivityRemindersAsync();
        }
        catch (Exception ex) { LoginStatus.Text = "Could not sign in: " + ex.Message; }
        finally
        {
            SignInProgress.Visibility = Visibility.Collapsed;
            SignInButton.Content = "Sign In";
            SignInButton.IsEnabled = true;
        }
    }

    private bool HasBundledServer()
    {
        try
        {
            using var key=Registry.LocalMachine.OpenSubKey(@"Software\Choice Flame Communications Network");
            var mode=key?.GetValue("InstallMode")?.ToString();
            if(string.Equals(mode,"Client",StringComparison.OrdinalIgnoreCase))return false;
            if(string.Equals(mode,"Server",StringComparison.OrdinalIgnoreCase))return true;
        }
        catch { }
        // Backward compatibility for older server installations that predate the install-mode marker.
        var exe=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Server","OfficeNetwork.Server.exe"));
        return File.Exists(exe);
    }

    private void ServerAdminMode_Click(object sender, RoutedEventArgs e)
    {
        if(!HasBundledServer())
        {
            ServerAdministratorSetupButton.Visibility=Visibility.Collapsed;
            FirstDirectorPanel.Visibility=Visibility.Collapsed;
            LoginStatus.Text="Server setup is available only on the Server installation.";
            return;
        }
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


    private async Task LoadDashboardSummaryAsync()
    {
        if(_currentUser is null)return;
        try
        {
            var baseUrl=LoginServerAddress.Text.TrimEnd('/');
            var workingTask=_http.GetFromJsonAsync<OfficeFileItem[]>($"{baseUrl}/api/files/WorkingFiles");
            var submittedTask=_http.GetFromJsonAsync<OfficeFileItem[]>($"{baseUrl}/api/files/SubmittedFiles");
            var finalTask=_http.GetFromJsonAsync<OfficeFileItem[]>($"{baseUrl}/api/files/FinalFiles");
            var assignmentTask=_http.GetFromJsonAsync<WorkAssignment[]>($"{baseUrl}/api/assignments");
            await Task.WhenAll(workingTask,submittedTask,finalTask,assignmentTask);
            var working=await workingTask??[]; var submitted=await submittedTask??[]; var final=await finalTask??[]; var assignments=await assignmentTask??[];
            DashboardWorkingCount.Text=working.Length.ToString();
            DashboardSubmittedCount.Text=submitted.Length.ToString();
            DashboardFinalCount.Text=final.Length.ToString();
            var pending=assignments.Count(x=>x.Status=="Pending");
            DashboardAssignedCount.Text=pending.ToString();
            if(_currentUser.Role is OfficeRole.Director or OfficeRole.Admin)
            {
                DashboardWorkflowHeadline.Text=pending==0?"Assignment desk is clear":$"{pending} work assignment(s) still pending";
                DashboardWorkflowDetail.Text=$"{submitted.Length} submitted file(s) currently await review. Use Assigned Work to assign and track staff work.";
            }
            else
            {
                var overdue=assignments.Count(x=>x.Status=="Pending" && x.DueAt.HasValue && x.DueAt.Value<DateTimeOffset.Now);
                DashboardWorkflowHeadline.Text=pending==0?"You have no unfinished assigned work":$"{pending} assigned task(s) waiting for you";
                DashboardWorkflowDetail.Text=overdue>0?$"{overdue} assignment(s) are overdue. Open Assigned Work to continue.":"Open Assigned Work to view instructions, source files and due dates.";
            }
        }
        catch { DashboardWorkflowDetail.Text="Dashboard summary will refresh when the server connection is available."; }
    }

    private async Task RefreshPersistentAssignmentNoticeAsync()
    {
        if (_currentUser?.Role is not (OfficeRole.Editor or OfficeRole.NewsSourcing)) return;
        try
        {
            var items = await _http.GetFromJsonAsync<WorkAssignment[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/assignments") ?? [];
            var pending = items.Where(x => x.Status == "Pending").OrderBy(x => x.DueAt ?? DateTimeOffset.MaxValue).ToArray();
            if (pending.Length == 0) return;
            var overdue = pending.Count(x => x.DueAt.HasValue && x.DueAt.Value < DateTimeOffset.Now);
            AssignmentStatus.Text = overdue > 0
                ? $"You have {pending.Length} unfinished assignment(s), including {overdue} overdue. Open Assigned Work to continue."
                : $"You have {pending.Length} unfinished assignment(s). Open Assigned Work to continue.";
            SystemSounds.Exclamation.Play();
        }
        catch { /* The Assigned Work page will show connection errors when opened. */ }
    }

    private async Task LoadAssignmentsAsync()
    {
        if(_currentUser is null)return;
        try
        {
            var items=await _http.GetFromJsonAsync<WorkAssignment[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/assignments") ?? [];
            AssignmentList.ItemsSource = _currentUser.Role is OfficeRole.Editor or OfficeRole.NewsSourcing ? items.Where(x=>x.Status=="Pending").ToArray() : items;
            if(HasBundledServer() && _currentUser.Role is OfficeRole.Director or OfficeRole.Admin)
            {
                var users=await _http.GetFromJsonAsync<OfficeUser[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/users/assignable") ?? [];
                AssignmentStaff.ItemsSource=users;
                AssignmentStaff.SelectedIndex=users.Length>0?0:-1;
                AssignmentHelp.Text=users.Length>0
                    ? $"Assign work to individual staff and track pending/completed work and completion time. {users.Length} staff member(s) available."
                    : "No Editor or News Sourcing account is available. Create a staff account under Users first.";
            }
            else if(_currentUser.Role is OfficeRole.Editor or OfficeRole.NewsSourcing)
                AssignmentHelp.Text="Work assigned specifically to you. Submit the finished file when your work is complete.";
            else
                AssignmentHelp.Text="Assignment management is available from the Server installation.";
        }
        catch(Exception ex){AssignmentStatus.Text="Could not load assigned work: "+ex.Message;}
    }

    private void ChooseAssignmentFile_Click(object sender,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Title="Choose video, audio, document or other work file"};
        if(d.ShowDialog()!=true)return; _assignmentFilePath=d.FileName; AssignmentFileName.Text=$"Selected: {Path.GetFileName(d.FileName)} (will upload when Assign Work is clicked)";
    }

    private async void AssignWork_Click(object sender,RoutedEventArgs e)
    {
        if(!HasBundledServer()){AssignmentStatus.Text="Work can only be assigned from the Server installation.";return;}
        if(_currentUser?.Role is not (OfficeRole.Director or OfficeRole.Admin))return;
        if(AssignmentStaff.SelectedItem is not OfficeUser target){AssignmentStatus.Text="No recipient selected. If the list is empty, create an Editor or News Sourcing account under Users, then return here.";return;}
        if(string.IsNullOrWhiteSpace(AssignmentTitle.Text)){AssignmentStatus.Text="Enter a title for the work.";return;}
        try
        {
            using var form=new MultipartFormDataContent();
            form.Add(new StringContent(target.Id.ToString()),"assignedToUserId"); form.Add(new StringContent(AssignmentTitle.Text.Trim()),"title"); form.Add(new StringContent(AssignmentInstructions.Text.Trim()),"instructions");
            if(AssignmentDueDate.SelectedDate is DateTime due)form.Add(new StringContent(due.ToString("O")),"dueAt");
            FileStream? stream=null;
            try
            {
                if(!string.IsNullOrWhiteSpace(_assignmentFilePath)){stream=File.OpenRead(_assignmentFilePath);form.Add(new StreamContent(stream),"file",Path.GetFileName(_assignmentFilePath));}
                var response=await _http.PostAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/assignments",form);
                AssignmentStatus.Text=response.IsSuccessStatusCode
                    ? $"Work assigned to {target.DisplayName}{(string.IsNullOrWhiteSpace(_assignmentFilePath) ? "." : " with the attached file saved on the server.")}"
                    : "Assignment failed: "+await response.Content.ReadAsStringAsync();
                if(response.IsSuccessStatusCode){SystemSounds.Asterisk.Play();AssignmentTitle.Clear();AssignmentInstructions.Clear();AssignmentDueDate.SelectedDate=null;_assignmentFilePath=null;AssignmentFileName.Text="No file selected";await LoadAssignmentsAsync();}
            } finally {stream?.Dispose();}
        }catch(Exception ex){AssignmentStatus.Text="Assignment failed: "+ex.Message;}
    }

    private async void CompleteAssignment_Click(object sender,RoutedEventArgs e)
    {
        if(AssignmentList.SelectedItem is not WorkAssignment item || item.Status!="Pending"){AssignmentStatus.Text="Select a pending assignment.";return;}
        var dialog=new OpenFileDialog{Title=$"Choose the finished file for: {item.Title}"};
        if(dialog.ShowDialog()!=true){AssignmentStatus.Text="Submission cancelled. The assignment remains pending.";return;}
        try
        {
            AssignmentStatus.Text="Submitting finished work for review…";
            await using var stream=File.OpenRead(dialog.FileName);
            using var form=new MultipartFormDataContent();
            form.Add(new StreamContent(stream),"file",Path.GetFileName(dialog.FileName));
            var response=await _http.PostAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/assignments/{item.Id}/complete",form);
            AssignmentStatus.Text=response.IsSuccessStatusCode
                ?"Finished work submitted. It is now in Submitted Files for Director/Admin review."
                :"Could not submit the finished work: "+await response.Content.ReadAsStringAsync();
            if(response.IsSuccessStatusCode){SystemSounds.Asterisk.Play();await LoadAssignmentsAsync();}
        }
        catch(Exception ex){AssignmentStatus.Text="Could not submit the finished work: "+ex.Message;}
    }

    private async void OpenAssignmentFile_Click(object sender,RoutedEventArgs e)
    {
        if(AssignmentList.SelectedItem is not WorkAssignment item){AssignmentStatus.Text="Select a saved assignment from the list first.";return;}
        if(string.IsNullOrWhiteSpace(item.FileName)){AssignmentStatus.Text="The selected assignment was saved without an attached source file.";return;}
        var response=await _http.GetAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/assignments/{item.Id}/attachment"); if(!response.IsSuccessStatusCode){AssignmentStatus.Text="Could not download the attached file.";return;}
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","Choice Flame","Assigned Work");Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,Path.GetFileName(item.FileName));await File.WriteAllBytesAsync(path,await response.Content.ReadAsByteArrayAsync());Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    }

    private async void RefreshAssignments_Click(object sender,RoutedEventArgs e)=>await LoadAssignmentsAsync();

    private async Task LoadCalendarActivitiesAsync()
    {
        if(_currentUser is null)return;
        try
        {
            var from=DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd");var to=DateTime.Today.AddMonths(6).ToString("yyyy-MM-dd");
            _calendarActivities=await _http.GetFromJsonAsync<OfficeActivity[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/activities?from={from}&to={to}")??[];
            CalendarManagePanel.Visibility=_currentUser.Role is OfficeRole.Director or OfficeRole.Admin?Visibility.Visible:Visibility.Collapsed;
            ActivityDatePicker.IsEnabled=_currentUser.Role is OfficeRole.Director or OfficeRole.Admin;ActivityTitle.IsReadOnly=_currentUser.Role is not (OfficeRole.Director or OfficeRole.Admin);ActivityDetails.IsReadOnly=_currentUser.Role is not (OfficeRole.Director or OfficeRole.Admin);
            ActivityCalendar.SelectedDate??=DateTime.Today;RefreshSelectedDayActivities();
        }
        catch(Exception ex){CalendarStatus.Text="Could not load office calendar: "+ex.Message;}
    }
    private void RefreshSelectedDayActivities()
    {
        var day=(ActivityCalendar.SelectedDate??DateTime.Today).Date;CalendarSelectedDateText.Text=day.ToString("dddd, d MMMM yyyy");
        DayActivitiesList.ItemsSource=_calendarActivities.Where(x=>x.ActivityDate.Date==day).ToArray();ActivityDatePicker.SelectedDate=day;
    }
    private void ActivityCalendar_SelectedDatesChanged(object sender,SelectionChangedEventArgs e){RefreshSelectedDayActivities();}
    private void DayActivitiesList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(DayActivitiesList.SelectedItem is not OfficeActivity item)return;ActivityDatePicker.SelectedDate=item.ActivityDate;ActivityTitle.Text=item.Title;ActivityDetails.Text=item.Details;
    }
    private async void SaveActivity_Click(object sender,RoutedEventArgs e)=>await SaveCalendarActivityAsync(null);
    private async void UpdateActivity_Click(object sender,RoutedEventArgs e)
    {
        if(DayActivitiesList.SelectedItem is not OfficeActivity item){CalendarStatus.Text="Select an activity to update.";return;}await SaveCalendarActivityAsync(item.Id);
    }
    private async Task SaveCalendarActivityAsync(Guid? id)
    {
        if(_currentUser?.Role is not (OfficeRole.Director or OfficeRole.Admin))return;if(ActivityDatePicker.SelectedDate is not DateTime date||string.IsNullOrWhiteSpace(ActivityTitle.Text)){CalendarStatus.Text="Select a date and enter the activity/event.";return;}
        try
        {
            var body=new SaveActivityRequest(date,ActivityTitle.Text.Trim(),ActivityDetails.Text.Trim());
            var response=id is null?await _http.PostAsJsonAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/activities",body):await _http.PutAsJsonAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/activities/{id}",body);
            if(!response.IsSuccessStatusCode){CalendarStatus.Text="Could not save activity: "+await response.Content.ReadAsStringAsync();return;}
            CalendarStatus.Text=id is null?"Activity added to the office calendar.":"Activity updated.";ActivityTitle.Clear();ActivityDetails.Clear();await LoadCalendarActivitiesAsync();
        }catch(Exception ex){CalendarStatus.Text="Could not save activity: "+ex.Message;}
    }
    private async void DeleteActivity_Click(object sender,RoutedEventArgs e)
    {
        if(DayActivitiesList.SelectedItem is not OfficeActivity item){CalendarStatus.Text="Select an activity to delete.";return;}
        if(MessageBox.Show($"Delete '{item.Title}' from the office calendar?","Delete activity",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        var response=await _http.DeleteAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/activities/{item.Id}");CalendarStatus.Text=response.IsSuccessStatusCode?"Activity deleted.":"Could not delete activity.";if(response.IsSuccessStatusCode)await LoadCalendarActivitiesAsync();
    }
    private async Task CheckActivityRemindersAsync()
    {
        if(_currentUser is null)return;
        try
        {
            var today=DateTime.Today;var tomorrow=today.AddDays(1);var to=tomorrow.ToString("yyyy-MM-dd");
            var items=await _http.GetFromJsonAsync<OfficeActivity[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/activities?from={today:yyyy-MM-dd}&to={to}")??[];
            var hour=DateTime.Now.Hour;string? slot=null;DateTime target=today;
            if(hour>=6&&hour<12){slot="morning";target=today;}
            var tomorrowReminder=hour>=6&&hour<12?"tomorrow-morning":hour>=17&&hour<22?"tomorrow-evening":null;
            if(tomorrowReminder is not null)
            {
                foreach(var item in items.Where(x=>x.ActivityDate.Date==tomorrow)){var key=$"{item.Id}:{today:yyyyMMdd}:{tomorrowReminder}";if(_shownActivityReminders.Add(key))ShowTrayNotification("Activity tomorrow",$"{item.Title} — {item.Details}");}
            }
            if(slot is not null)
            {
                foreach(var item in items.Where(x=>x.ActivityDate.Date==target)){var key=$"{item.Id}:{today:yyyyMMdd}:today-morning";if(_shownActivityReminders.Add(key))ShowTrayNotification("Activity today",$"{item.Title} — {item.Details}");}
            }
        }catch { }
    }

    private async void CreateUser_Click(object sender, RoutedEventArgs e)
    {
        if(_currentUser?.Role is not (OfficeRole.Director or OfficeRole.Admin))
        {
            UserStatus.Text="Only the Director or Admin can create staff accounts.";
            return;
        }
        if(string.IsNullOrWhiteSpace(NewDisplayName.Text)||string.IsNullOrWhiteSpace(NewUserName.Text)||string.IsNullOrWhiteSpace(NewPassword.Password))
        {
            UserStatus.Text="Enter the employee's full name, username and temporary password.";
            return;
        }
        if(NewRole.SelectedItem is not ComboBoxItem roleItem)
        {
            UserStatus.Text="Select an account role.";
            return;
        }
        try
        {
            var role=Enum.Parse<OfficeRole>(roleItem.Content!.ToString()!);
            if(_currentUser.Role==OfficeRole.Admin && role is OfficeRole.Director or OfficeRole.Admin)
            {
                UserStatus.Text="An Admin can create Editor or News Sourcing accounts only.";
                return;
            }
            UserStatus.Text="Creating account…";
            var request=new CreateUserRequest(NewUserName.Text.Trim(),NewDisplayName.Text.Trim(),role,NewPassword.Password);
            var response=await _http.PostAsJsonAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/users",request);
            var detail=await response.Content.ReadAsStringAsync();
            if(!response.IsSuccessStatusCode)
            {
                UserStatus.Text=response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Forbidden=>"Account creation was refused for this role. Sign in as Director, or create an Editor/News Sourcing account as Admin.",
                    System.Net.HttpStatusCode.Unauthorized=>"Your session has expired. Sign in again before creating an account.",
                    _=>"Could not create user: "+(string.IsNullOrWhiteSpace(detail)?response.ReasonPhrase:detail.Trim('"'))
                };
                return;
            }
            UserStatus.Text=$"User '{NewDisplayName.Text.Trim()}' created successfully.";
            SystemSounds.Asterisk.Play();
            NewUserName.Clear();NewDisplayName.Clear();NewPassword.Clear();
            await LoadUsersAsync();
        }
        catch(Exception ex){UserStatus.Text="Could not create user: "+ex.Message;}
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await _http.GetFromJsonAsync<OfficeUser[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/users");
            UserList.ItemsSource = users ?? [];
            EditUserPanel.Visibility=Visibility.Collapsed;
        }
        catch (Exception ex) { UserStatus.Text = "Could not load users: " + ex.Message; }
    }

    private void UserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if(UserList.SelectedItem is not OfficeUser user){EditUserPanel.Visibility=Visibility.Collapsed;return;}
        EditUserPanel.Visibility=Visibility.Visible; EditDisplayName.Text=user.DisplayName; EditUserName.Text=user.UserName; EditPassword.Clear(); EditEnabled.IsChecked=user.IsEnabled;
        var roleName=user.Role.ToString();
        foreach(ComboBoxItem option in EditRole.Items) if(string.Equals(option.Content?.ToString(),roleName,StringComparison.OrdinalIgnoreCase)){EditRole.SelectedItem=option;break;}
    }

    private async void SaveUserChanges_Click(object sender,RoutedEventArgs e)
    {
        if(UserList.SelectedItem is not OfficeUser user || EditRole.SelectedItem is not ComboBoxItem roleItem)return;
        try
        {
            var role=Enum.Parse<OfficeRole>(roleItem.Content!.ToString()!);
            var body=new UpdateUserRequest(EditUserName.Text.Trim(),EditDisplayName.Text.Trim(),role,EditEnabled.IsChecked==true,string.IsNullOrWhiteSpace(EditPassword.Password)?null:EditPassword.Password);
            var response=await _http.PutAsJsonAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/users/{user.Id}",body);
            UserStatus.Text=response.IsSuccessStatusCode?"Account updated successfully.":"Could not update account: "+await response.Content.ReadAsStringAsync();
            if(response.IsSuccessStatusCode){SystemSounds.Asterisk.Play();await LoadUsersAsync();}
        }catch(Exception ex){UserStatus.Text="Could not update account: "+ex.Message;}
    }

    private async void DeleteUser_Click(object sender,RoutedEventArgs e)
    {
        if(UserList.SelectedItem is not OfficeUser user)return;
        if(MessageBox.Show($"Delete the account for {user.DisplayName}? Existing work files and assignment history will be kept.","Delete account",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try
        {
            var response=await _http.DeleteAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/users/{user.Id}");
            UserStatus.Text=response.IsSuccessStatusCode?"Account deleted. Existing work files and assignment history were retained.":"Could not delete account: "+await response.Content.ReadAsStringAsync();
            if(response.IsSuccessStatusCode){SystemSounds.Asterisk.Play();await LoadUsersAsync();}
        }catch(Exception ex){UserStatus.Text="Could not delete account: "+ex.Message;}
    }

    private async Task LoadStorageConfigurationAsync()
    {
        if(!HasBundledServer() || _currentUser is null)return;
        try
        {
            var config=await _http.GetFromJsonAsync<ServerConfiguration>($"{LoginServerAddress.Text.TrimEnd('/')}/api/configuration");
            if(config is null)return;
            ServerRootPath.Text=config.RootPath;
            UpdateStorageDriveInfo(config.RootPath);
        }
        catch { StorageDriveInfo.Text="Storage information could not be loaded."; }
    }

    private void UpdateStorageDriveInfo(string path)
    {
        try
        {
            var root=Path.GetPathRoot(Path.GetFullPath(path));
            if(string.IsNullOrWhiteSpace(root)){StorageDriveInfo.Text="Choose a valid local drive or folder.";return;}
            var drive=new DriveInfo(root);
            StorageDriveInfo.Text=$"Selected: {path}   •   Drive {drive.Name}   •   Free space: {drive.AvailableFreeSpace/1024d/1024d/1024d:N1} GB";
        }
        catch { StorageDriveInfo.Text=$"Selected: {path}"; }
    }

    private void ChooseStorageLocation_Click(object sender,RoutedEventArgs e)
    {
        if(!HasBundledServer())return;
        using var dialog=new System.Windows.Forms.FolderBrowserDialog
        {
            Description="Choose the secondary drive or folder for Choice Flame office files.",
            UseDescriptionForTitle=true,
            ShowNewFolderButton=true,
            SelectedPath=Directory.Exists(ServerRootPath.Text)?ServerRootPath.Text:string.Empty
        };
        if(dialog.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
        var selected=dialog.SelectedPath;
        if(string.Equals(Path.GetPathRoot(selected),Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),StringComparison.OrdinalIgnoreCase))
        {
            if(MessageBox.Show("You selected the Windows operating-system drive. You said you prefer office files on a separate disk. Continue with this drive anyway?","Operating-system drive selected",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        }
        ServerRootPath.Text=Path.Combine(selected,"Choice Flame Communications");
        UpdateStorageDriveInfo(ServerRootPath.Text);
    }

    private async void ApplyStorageLocation_Click(object sender,RoutedEventArgs e)
    {
        if(!HasBundledServer() || _currentUser?.Role is not (OfficeRole.Director or OfficeRole.Admin))return;
        var target=ServerRootPath.Text.Trim();
        if(string.IsNullOrWhiteSpace(target))return;
        var copy=MessageBox.Show("Copy all existing Choice Flame office files to the new storage location before switching?\n\nChoose Yes to keep all existing Working, Submitted, Final, assignment attachments and workflow data together.","Move office storage",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
        if(copy==MessageBoxResult.Cancel)return;
        try
        {
            SettingsStatus.Text="Changing office storage location…";
            var response=await _http.PostAsJsonAsync($"{LoginServerAddress.Text.TrimEnd('/')}/api/configuration/storage",new ChangeStorageRequest(target,copy==MessageBoxResult.Yes));
            var detail=await response.Content.ReadAsStringAsync();
            if(!response.IsSuccessStatusCode){SettingsStatus.Text="Storage change failed: "+detail;return;}
            SettingsStatus.Text=$"Office storage is now: {target}. Existing files were {(copy==MessageBoxResult.Yes?"copied to the new location":"left in the previous location")}.";
            UpdateStorageDriveInfo(target);
            SystemSounds.Asterisk.Play();
            MessageBox.Show($"Office file storage is now:\n{target}\n\nNew office files will be saved on this drive.","Storage location changed",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){SettingsStatus.Text="Storage change failed: "+ex.Message;}
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

    private void LoadSavedServerAddress()
    {
        try
        {
            if (!File.Exists(ClientSettingsPath)) return;
            var saved = File.ReadAllText(ClientSettingsPath).Trim();
            if (Uri.TryCreate(saved, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                LoginServerAddress.Text = saved.TrimEnd('/');
        }
        catch { }
    }

    private void SaveServerAddress(string address)
    {
        try
        {
            var directory = Path.GetDirectoryName(ClientSettingsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(ClientSettingsPath, address.TrimEnd('/'));
        }
        catch { }
    }

    private async Task EnsureLocalServerAsync()
    {
        if (!string.Equals(LoginServerAddress.Text.TrimEnd('/'), "http://localhost:5077", StringComparison.OrdinalIgnoreCase))
            return;

        var bundledServer = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Server", "OfficeNetwork.Server.exe"));
        if (!File.Exists(bundledServer))
        {
            LoginStatus.Text = "Client computer: enter the Office Server address above, then sign in.";
            ConnectionStatus.Text = "Waiting for server address";
            return;
        }
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

    private void SetServerStatus(string text, string color)
    {
        ConnectionStatus.Text = text;
        ServerStatusDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private async void RestartServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetServerStatus("Restarting server…", "#D6A21E");
            foreach (var process in Process.GetProcessesByName("OfficeNetwork.Server"))
            {
                try { process.Kill(true); process.WaitForExit(3000); } catch { }
            }
            _serverProcess = null;
            StartBundledServer();
            await Task.Delay(1200);
            SettingsStatus.Text = "Office server restarted. Reconnecting…";
            await ConnectToServerAsync();
        }
        catch (Exception ex)
        {
            SetServerStatus("Server restart failed", "#DC2626");
            SettingsStatus.Text = "Could not restart OfficeNetwork.Server.exe: " + ex.Message;
        }
    }

    private async Task ConnectToServerAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        if (_currentUser is null) return;
        _connection = new HubConnectionBuilder()
            .WithUrl($"{LoginServerAddress.Text.TrimEnd('/')}/hubs/chat", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_currentUser.Token);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ChatMessage>("ReceiveMessage", message =>
            Dispatcher.Invoke(() =>
            {
                Messages.Items.Add($"{message.SentAt.ToLocalTime():HH:mm}  {message.SenderName}: {message.Text}");
                if (message.SenderId != _currentUser?.UserId)
                {
                    SystemSounds.Asterisk.Play();
                    ShowTrayNotification(message.IsBroadcast?"Office Broadcast":$"Message from {message.SenderName}",message.Text);
                }
            }));

        _connection.On<string, string, string, string>("FileNotification", (action, fileName, actor, detail) =>
            Dispatcher.Invoke(async () =>
            {
                SystemSounds.Exclamation.Play();
                SectionNotice.Text = $"{action}: {fileName} — {actor}. {detail}";
                ShowTrayNotification(action,$"{fileName} — {actor}. {detail}");
                if (_activeFolder is not null) await LoadFilesAsync();
            }));

        _connection.On<Guid, string, string, string>("AssignmentNotification", (id, title, actor, instructions) =>
            Dispatcher.Invoke(async () =>
            {
                SystemSounds.Exclamation.Play();
                AssignmentStatus.Text = $"New work assigned by {actor}: {title}. {instructions}";
                ShowTrayNotification("New Work Assigned",$"{actor}: {title}. {instructions}");
                await LoadAssignmentsAsync();
                await LoadDashboardSummaryAsync();
            }));
        _connection.On<Guid, string, string, DateTimeOffset?>("AssignmentCompleted", (id, title, staff, completedAt) =>
            Dispatcher.Invoke(async () =>
            {
                SystemSounds.Asterisk.Play();
                AssignmentStatus.Text = $"{staff} completed '{title}' at {completedAt?.ToLocalTime():g}.";
                ShowTrayNotification("Assigned Work Submitted",$"{staff} completed '{title}'.");
                await LoadAssignmentsAsync();
                await LoadDashboardSummaryAsync();
            }));

        _connection.On<PresenceInfo[]>("PresenceChanged", users =>
            Dispatcher.Invoke(() =>
            {
                var others = users.Where(u => u.UserId != _currentUser?.UserId).ToArray();
                OnlineUsers.ItemsSource = others;
                DashboardOnlineCount.Text = users.Length.ToString();
            }));

        _connection.On<Guid, string, OfficeRole>("IncomingCall", (callerId, callerName, role) =>
            Dispatcher.Invoke(() =>
            {
                _callPeerId = callerId;
                SystemSounds.Exclamation.Play();
                CallPanel.Visibility = Visibility.Visible;
                CallStatus.Text = $"Incoming voice call from {callerName} ({role})";
                ShowTrayNotification("Incoming Office Call",$"{callerName} ({role}) is calling.");
                AcceptCallButton.Visibility = Visibility.Visible;
                DeclineCallButton.Visibility = Visibility.Visible;
                MuteCallButton.Visibility = Visibility.Collapsed;
                EndCallButton.Visibility = Visibility.Collapsed;
            }));

        _connection.On<Guid, string>("CallAccepted", (userId, name) =>
            Dispatcher.Invoke(async () =>
            {
                _callPeerId = userId;
                CallStatus.Text = $"Voice call with {name}";
                ShowActiveCallControls();
                await StartAudioAsync();
            }));

        _connection.On<Guid, string>("CallDeclined", (userId, name) =>
            Dispatcher.Invoke(() => ResetCallUi($"{name} declined the call.")));

        _connection.On<Guid, string>("CallEnded", (userId, name) =>
            Dispatcher.Invoke(() => { StopAudio(); ResetCallUi($"Call with {name} ended."); }));

        _connection.On<Guid, byte[]>("ReceiveAudio", (senderId, audio) =>
        {
            if (_callPeerId == senderId) _audioBuffer?.AddSamples(audio, 0, audio.Length);
        });

        _connection.Reconnecting += _ =>
        {
            Dispatcher.Invoke(() => SetServerStatus("Reconnecting…", "#D6A21E"));
            return Task.CompletedTask;
        };

        _connection.Reconnected += _ =>
        {
            Dispatcher.Invoke(() => SetServerStatus("Connected", "#16A34A"));
            return RegisterAsync();
        };

        _connection.Closed += _ =>
        {
            Dispatcher.Invoke(() => SetServerStatus("Disconnected", "#DC2626"));
            return Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync();
            await RegisterAsync();
            SetServerStatus("Connected", "#16A34A");
        }
        catch (Exception ex)
        {
            SetServerStatus($"Connection failed: {ex.Message}", "#DC2626");
        }
    }

    private async Task RegisterAsync()
    {
        if (_connection is null) return;
        if (_currentUser is null) return;
        await _connection.InvokeAsync("Register");
    }


    private async void CallSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_connection?.State != HubConnectionState.Connected || OnlineUsers.SelectedItem is not PresenceInfo target) return;
        _callPeerId = target.UserId;
        CallPanel.Visibility = Visibility.Visible;
        CallStatus.Text = $"Calling {target.DisplayName}…";
        AcceptCallButton.Visibility = Visibility.Collapsed;
        DeclineCallButton.Visibility = Visibility.Collapsed;
        MuteCallButton.Visibility = Visibility.Collapsed;
        EndCallButton.Visibility = Visibility.Visible;
        await _connection.InvokeAsync("StartCall", target.UserId);
    }

    private async void AcceptCall_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is null || _callPeerId is null) return;
        await _connection.InvokeAsync("AcceptCall", _callPeerId.Value);
        ShowActiveCallControls();
        await StartAudioAsync();
    }

    private async void DeclineCall_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is null || _callPeerId is null) return;
        await _connection.InvokeAsync("DeclineCall", _callPeerId.Value);
        ResetCallUi("Call declined.");
    }

    private async void EndCall_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is not null && _callPeerId is Guid peer)
            await _connection.InvokeAsync("EndCall", peer);
        StopAudio();
        ResetCallUi("Call ended.");
    }

    private void MuteCall_Click(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        MuteCallButton.Content = _muted ? "Unmute" : "Mute";
    }

    private async Task StartAudioAsync()
    {
        StopAudio();
        _audioBuffer = new BufferedWaveProvider(new WaveFormat(16000, 16, 1)) { DiscardOnBufferOverflow = true };
        _speaker = new WaveOutEvent();
        _speaker.Init(_audioBuffer);
        _speaker.Play();
        _microphone = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 60 };
        _microphone.DataAvailable += async (_, e) =>
        {
            if (_muted || _connection?.State != HubConnectionState.Connected || _callPeerId is not Guid peer) return;
            try
            {
                var packet = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
                await _connection.InvokeAsync("SendAudio", peer, packet);
            }
            catch { }
        };
        _microphone.StartRecording();
        await Task.CompletedTask;
    }

    private void StopAudio()
    {
        try { _microphone?.StopRecording(); } catch { }
        _microphone?.Dispose(); _microphone = null;
        _speaker?.Stop(); _speaker?.Dispose(); _speaker = null;
        _audioBuffer = null;
        _muted = false;
    }

    private void ShowActiveCallControls()
    {
        CallPanel.Visibility = Visibility.Visible;
        AcceptCallButton.Visibility = Visibility.Collapsed;
        DeclineCallButton.Visibility = Visibility.Collapsed;
        MuteCallButton.Visibility = Visibility.Visible;
        EndCallButton.Visibility = Visibility.Visible;
        MuteCallButton.Content = "Mute";
    }

    private void ResetCallUi(string status)
    {
        _callPeerId = null;
        CallPanel.Visibility = Visibility.Visible;
        CallStatus.Text = status;
        AcceptCallButton.Visibility = Visibility.Collapsed;
        DeclineCallButton.Visibility = Visibility.Collapsed;
        MuteCallButton.Visibility = Visibility.Collapsed;
        EndCallButton.Visibility = Visibility.Collapsed;
    }


    private async void SendPrivate_Click(object sender, RoutedEventArgs e) => await SendCurrentMessageAsync();

    private async void MessageText_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        await SendCurrentMessageAsync();
    }

    private async Task SendCurrentMessageAsync()
    {
        if (_connection?.State != HubConnectionState.Connected || string.IsNullOrWhiteSpace(MessageText.Text)) return;
        if (OnlineUsers.SelectedItem is not PresenceInfo target)
        {
            ChatConnectionInfo.Text = "Select an online staff member to send a private message.";
            return;
        }
        var text = MessageText.Text.Trim();
        await _connection.InvokeAsync("SendPrivate", text, target.UserId);
        Messages.Items.Add($"{DateTime.Now:HH:mm}  You → {target.DisplayName}: {text}");
        MessageText.Clear();
        MessageText.Focus();
    }

    private async void SendEveryone_Click(object sender, RoutedEventArgs e)
    {
        if (_connection?.State != HubConnectionState.Connected || string.IsNullOrWhiteSpace(MessageText.Text))
            return;

        if (_currentUser?.Role != OfficeRole.Director)
        {
            ConnectionStatus.Text = "Only the Director can message everyone.";
            return;
        }
        await _connection.InvokeAsync("SendToEveryone", MessageText.Text.Trim());
        MessageText.Clear();
    }
}
