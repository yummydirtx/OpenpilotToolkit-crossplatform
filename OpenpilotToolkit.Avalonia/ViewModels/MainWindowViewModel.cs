using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using CameraProgress = OpenpilotSdk.OpenPilot.Camera.Progress;
using OpenpilotSdk.Exceptions;
using OpenpilotSdk.Git;
using OpenpilotSdk.Hardware;
using OpenpilotSdk.OpenPilot.Fork;
using OpenpilotSdk.Runtime;
using OpenpilotToolkit.Avalonia.Services;

namespace OpenpilotToolkit.Avalonia.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly StringBuilder _logBuilder = new();

    private string _activityLog = string.Empty;
    private string _exportFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Openpilot");
    private string _forkBranch = "master";
    private string _forkOwner = string.Empty;
    private string _forkRepository = "openpilot";
    private bool _isBusy;
    private bool _isLivePlaybackMuted;
    private bool _isLivePlaybackRunning;
    private bool _isNavigationCollapsed;
    private CancellationTokenSource? _livePlaybackCancellation;
    private MediaPlayer? _livePlaybackMediaPlayer;
    private Task? _livePlaybackPrefetchTask;
    private VlcLivePlaybackSession? _livePlaybackSession;
    private string _livePlaybackStatusMessage = "Select a route to start live playback.";
    private string _manualHost = string.Empty;
    private string _playbackStatusMessage = "Select a route to prepare a playback file.";
    private string _resolvedSshKeySummary = string.Empty;
    private int _selectedPageIndex;
    private CameraType _selectedPlaybackCamera = CameraType.Front;
    private RouteViewModel? _selectedRoute;
    private int _selectedRouteLimit = 20;
    private DeviceViewModel? _selectedDevice;
    private string _sshKeyPassphrase = string.Empty;
    private string _sshKeyPath = string.Empty;
    private string _statusMessage = "Ready";

    public MainWindowViewModel()
    {
        Devices.CollectionChanged += OnDevicesChanged;
        Routes.CollectionChanged += OnRoutesChanged;

        RouteLimitOptions = [10, 20, 50, 100];

        DiscoverDevicesCommand = new AsyncCommand(DiscoverDevicesAsync, () => !IsBusy);
        ConnectHostCommand = new AsyncCommand(ConnectHostAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ManualHost));
        ConnectSelectedDeviceCommand = new AsyncCommand(ConnectSelectedDeviceAsync, () => !IsBusy && SelectedDevice is not null);
        LoadRoutesCommand = new AsyncCommand(LoadRoutesAsync, () => !IsBusy && SelectedDevice is not null);
        StartLivePlaybackCommand = new AsyncCommand(StartLivePlaybackAsync, CanStartLivePlayback);
        StopLivePlaybackCommand = new AsyncCommand(StopLivePlaybackAsync, CanStopLivePlayback);
        ToggleLivePlaybackMuteCommand = new AsyncCommand(ToggleLivePlaybackMuteAsync, CanToggleLivePlaybackMute);
        PlaySelectedRouteCommand = new AsyncCommand(PlaySelectedRouteAsync, CanPlaySelectedRoute);
        InstallForkCommand = new AsyncCommand(InstallForkAsync, CanInstallFork);
        RebootCommand = new AsyncCommand(() => ExecuteDeviceActionAsync("reboot", device => device.RebootAsync(), "Reboot command sent."), () => !IsBusy && SelectedDevice is not null);
        ShutdownCommand = new AsyncCommand(() => ExecuteDeviceActionAsync("shut down", device => device.ShutdownAsync(), "Shutdown command sent."), () => !IsBusy && SelectedDevice is not null);
        ToggleNavigationCommand = new RelayCommand(ToggleNavigation);
        ResetSshKeyCommand = new RelayCommand(ResetSshKey, () => !IsBusy);
        ClearLogCommand = new RelayCommand(ClearLog);

        UpdateResolvedSshKeySummary();
        LogStartupState();
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    public ObservableCollection<RouteViewModel> Routes { get; } = [];

    public ObservableCollection<CameraType> AvailablePlaybackCameras { get; } = [];

    public IReadOnlyList<int> RouteLimitOptions { get; }

    public AsyncCommand DiscoverDevicesCommand { get; }

    public AsyncCommand ConnectHostCommand { get; }

    public AsyncCommand ConnectSelectedDeviceCommand { get; }

    public AsyncCommand LoadRoutesCommand { get; }

    public AsyncCommand StartLivePlaybackCommand { get; }

    public AsyncCommand StopLivePlaybackCommand { get; }

    public AsyncCommand ToggleLivePlaybackMuteCommand { get; }

    public AsyncCommand PlaySelectedRouteCommand { get; }

    public AsyncCommand InstallForkCommand { get; }

    public AsyncCommand RebootCommand { get; }

    public AsyncCommand ShutdownCommand { get; }

    public RelayCommand ToggleNavigationCommand { get; }

    public RelayCommand ResetSshKeyCommand { get; }

    public RelayCommand ClearLogCommand { get; }

    public Func<string, Task<string?>>? RequestSshKeyPassphraseAsync { get; set; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ActivityLog
    {
        get => _activityLog;
        private set => SetProperty(ref _activityLog, value);
    }

    public string ExportFolder
    {
        get => _exportFolder;
        set
        {
            if (SetProperty(ref _exportFolder, value?.Trim() ?? string.Empty))
            {
                UpdatePlaybackStatusSummary();
            }
        }
    }

    public string SshKeyPath
    {
        get => _sshKeyPath;
        set
        {
            if (SetProperty(ref _sshKeyPath, value?.Trim() ?? string.Empty))
            {
                OpenpilotHost.SetSshKeyPath(string.IsNullOrWhiteSpace(_sshKeyPath) ? null : _sshKeyPath);
                UpdateResolvedSshKeySummary();
            }
        }
    }

    public string ResolvedSshKeySummary
    {
        get => _resolvedSshKeySummary;
        private set => SetProperty(ref _resolvedSshKeySummary, value);
    }

    public string SshKeyPassphrase
    {
        get => _sshKeyPassphrase;
        set
        {
            if (SetProperty(ref _sshKeyPassphrase, value ?? string.Empty))
            {
                OpenpilotHost.SetSshKeyPassphrase(string.IsNullOrEmpty(_sshKeyPassphrase) ? null : _sshKeyPassphrase);
                OnPropertyChanged(nameof(SshKeyPassphraseSummary));
            }
        }
    }

    public string SshKeyPassphraseSummary
    {
        get
        {
            return string.IsNullOrEmpty(OpenpilotHost.ResolvePrivateSshKeyPassphrase())
                ? "No SSH key passphrase is configured for this session."
                : "An SSH key passphrase is available for the current session.";
        }
    }

    public string ManualHost
    {
        get => _manualHost;
        set
        {
            if (SetProperty(ref _manualHost, value?.Trim() ?? string.Empty))
            {
                RefreshCommandStates();
            }
        }
    }

    public string ForkOwner
    {
        get => _forkOwner;
        set
        {
            if (SetProperty(ref _forkOwner, value?.Trim() ?? string.Empty))
            {
                RefreshCommandStates();
            }
        }
    }

    public string ForkBranch
    {
        get => _forkBranch;
        set
        {
            if (SetProperty(ref _forkBranch, value?.Trim() ?? string.Empty))
            {
                RefreshCommandStates();
            }
        }
    }

    public string ForkRepository
    {
        get => _forkRepository;
        set => SetProperty(ref _forkRepository, value?.Trim() ?? string.Empty);
    }

    public int SelectedRouteLimit
    {
        get => _selectedRouteLimit;
        set
        {
            if (SetProperty(ref _selectedRouteLimit, value))
            {
                OnPropertyChanged(nameof(RoutesSummary));
            }
        }
    }

    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        set => SetProperty(ref _selectedPageIndex, value);
    }

    public bool IsNavigationCollapsed
    {
        get => _isNavigationCollapsed;
        set
        {
            if (SetProperty(ref _isNavigationCollapsed, value))
            {
                OnPropertyChanged(nameof(IsNavigationExpanded));
                OnPropertyChanged(nameof(NavigationPaneWidth));
                OnPropertyChanged(nameof(NavigationToggleLabel));
            }
        }
    }

    public bool IsNavigationExpanded => !IsNavigationCollapsed;

    public double NavigationPaneWidth => IsNavigationCollapsed ? 88 : 252;

    public string NavigationToggleLabel => IsNavigationCollapsed ? ">" : "<";

    public DeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                Routes.Clear();
                SelectedRoute = null;
                _ = StopLivePlaybackCoreAsync(resetStatus: false);
                NotifySelectedDeviceStateChanged();
                RefreshCommandStates();
            }
        }
    }

    public RouteViewModel? SelectedRoute
    {
        get => _selectedRoute;
        set
        {
            if (SetProperty(ref _selectedRoute, value))
            {
                UpdateAvailablePlaybackCameras();
                NotifySelectedRouteStateChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public int DeviceCount => Devices.Count;

    public string DevicesSummary => Devices.Count switch
    {
        0 => "No devices loaded yet.",
        1 => "1 device available.",
        _ => $"{Devices.Count} devices available."
    };

    public string RoutesSummary => SelectedDevice is null
        ? "Select a device to inspect its recent routes."
        : Routes.Count == 0
            ? $"Load up to {SelectedRouteLimit} recent routes from the selected device."
            : $"{Routes.Count} recent routes loaded from {SelectedDevice.Model.IpAddress}.";

    public string ActiveDeviceDisplay => SelectedDevice?.DisplayName ?? "No device selected";

    public string WifiStatusText => Devices.Count > 0 ? "Wi-Fi online" : "Wi-Fi offline";

    public string WifiStatusBackground => Devices.Count > 0 ? "#0D5E4A" : "#5A3030";

    public string SshStatusText => SelectedDevice?.Model.IsAuthenticated == true ? "SSH ready" : "SSH locked";

    public string SshStatusBackground => SelectedDevice?.Model.IsAuthenticated == true ? "#0B6AAE" : "#4A3A1E";

    public string SelectedDeviceTitle => SelectedDevice?.DisplayName ?? "No device selected";

    public string SelectedDeviceSummary => SelectedDevice?.SummaryLine
        ?? "Pick a device from the list or connect directly to a host.";

    public string SelectedDeviceHostLine => SelectedDevice?.HostLine ?? "Host: -";

    public string SelectedDeviceStatusLine => SelectedDevice?.StatusLine ?? "SSH authentication not established.";

    public string SelectedRouteTitle => SelectedRoute?.Identifier ?? "No route selected";

    public string SelectedRouteSummary => SelectedRoute is null
        ? "Load routes from the selected device to inspect recent drive history."
        : $"{SelectedRoute.DateLine}. {SelectedRoute.SegmentLine}.";

    public string SelectedRouteDateLine => SelectedRoute?.DateLine ?? "-";

    public string SelectedRouteSegmentLine => SelectedRoute?.SegmentLine ?? "-";

    public CameraType SelectedPlaybackCamera
    {
        get => _selectedPlaybackCamera;
        set
        {
            if (SetProperty(ref _selectedPlaybackCamera, value))
            {
                OnPropertyChanged(nameof(SelectedPlaybackCameraLabel));
                UpdatePlaybackStatusSummary();
                UpdateLivePlaybackStatusSummary();
                RefreshCommandStates();
            }
        }
    }

    public bool HasPlaybackCameras => AvailablePlaybackCameras.Count > 0;

    public string SelectedPlaybackCameraLabel => HasPlaybackCameras
        ? GetCameraLabel(SelectedPlaybackCamera)
        : "No camera selected";

    public string PlaybackStatusMessage
    {
        get => _playbackStatusMessage;
        private set => SetProperty(ref _playbackStatusMessage, value);
    }

    public MediaPlayer? LivePlaybackMediaPlayer
    {
        get => _livePlaybackMediaPlayer;
        private set
        {
            if (SetProperty(ref _livePlaybackMediaPlayer, value))
            {
                OnPropertyChanged(nameof(HasLivePlaybackMediaPlayer));
                OnPropertyChanged(nameof(ShowLivePlaybackPlaceholder));
            }
        }
    }

    public bool HasLivePlaybackMediaPlayer => LivePlaybackMediaPlayer is not null;

    public bool ShowLivePlaybackPlaceholder => !HasLivePlaybackMediaPlayer;

    public bool IsLivePlaybackRunning
    {
        get => _isLivePlaybackRunning;
        private set
        {
            if (SetProperty(ref _isLivePlaybackRunning, value))
            {
                OnPropertyChanged(nameof(LiveMuteButtonLabel));
                RefreshCommandStates();
            }
        }
    }

    public bool IsLivePlaybackMuted
    {
        get => _isLivePlaybackMuted;
        private set
        {
            if (SetProperty(ref _isLivePlaybackMuted, value))
            {
                OnPropertyChanged(nameof(LiveMuteButtonLabel));
            }
        }
    }

    public string LiveMuteButtonLabel => IsLivePlaybackMuted ? "Unmute Audio" : "Mute Audio";

    public string LivePlaybackStatusMessage
    {
        get => _livePlaybackStatusMessage;
        private set => SetProperty(ref _livePlaybackStatusMessage, value);
    }

    public void SetSshKeyPath(string path)
    {
        SshKeyPath = path;
        Log($"SSH key override set to {path}.");
    }

    public void SetSshKeyPassphrase(string passphrase)
    {
        SshKeyPassphrase = passphrase;
        Log("Updated the SSH key passphrase for the current session.");
    }

    public void SetExportFolder(string path)
    {
        ExportFolder = path;
        Log($"Export folder set to {path}.");
    }

    private async Task DiscoverDevicesAsync()
    {
        await RunBusyOperationAsync("Discovering devices...", async () =>
        {
            Devices.Clear();
            Routes.Clear();
            SelectedRoute = null;

            var discoveredCount = 0;
            await foreach (var device in OpenpilotDevice.DiscoverAsync())
            {
                var (deviceViewModel, isNewDevice) = UpsertDevice(device);
                if (!isNewDevice)
                {
                    continue;
                }

                discoveredCount++;
                StatusMessage = discoveredCount == 1 ? "1 device discovered" : $"{discoveredCount} devices discovered";
                Log($"Discovered {deviceViewModel.DisplayName}. {deviceViewModel.StatusLine}.");
            }

            if (discoveredCount == 0)
            {
                StatusMessage = "No devices discovered";
                Log("Discovery finished without finding any openpilot devices.");
                return;
            }

            SelectedDevice = Devices.FirstOrDefault(device => device.Model.IsAuthenticated) ?? Devices[0];
        });
    }

    private async Task ConnectHostAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualHost))
        {
            return;
        }

        await RunBusyOperationAsync($"Connecting to {ManualHost}...", async () =>
        {
            var device = await OpenpilotDevice.GetOpenpilotDeviceAsync(ManualHost);
            if (device is null)
            {
                StatusMessage = "Connection failed";
                Log($"Failed to resolve an openpilot device at {ManualHost}.");
                return;
            }

            var (deviceViewModel, _) = UpsertDevice(device);
            SelectedDevice = deviceViewModel;
            Log($"Resolved {deviceViewModel.DisplayName}. {deviceViewModel.StatusLine}.");

            await device.ConnectAsync();
            deviceViewModel.Refresh();
            NotifySelectedDeviceStateChanged();

            StatusMessage = $"Connected to {device.IpAddress}";
            Log($"SSH connection established to {device.IpAddress}.");
        });
    }

    private async Task ConnectSelectedDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await RunBusyOperationAsync($"Connecting to {SelectedDevice.Model.IpAddress}...", async () =>
        {
            await SelectedDevice.Model.ConnectAsync();
            SelectedDevice.Refresh();
            NotifySelectedDeviceStateChanged();

            StatusMessage = $"Connected to {SelectedDevice.Model.IpAddress}";
            Log($"SSH connection established to {SelectedDevice.Model.IpAddress}.");
        });
    }

    private async Task LoadRoutesAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await RunBusyOperationAsync($"Loading {SelectedRouteLimit} recent routes...", async () =>
        {
            Routes.Clear();
            SelectedRoute = null;

            var routeCount = 0;
            await foreach (var route in SelectedDevice.Model.GetRoutesAsync())
            {
                Routes.Add(new RouteViewModel(route));
                routeCount++;

                if (routeCount >= SelectedRouteLimit)
                {
                    break;
                }
            }

            SelectedDevice.Refresh();
            NotifySelectedDeviceStateChanged();

            if (routeCount == 0)
            {
                StatusMessage = "No routes found";
                Log($"No routes were found on {SelectedDevice.Model.IpAddress}.");
                return;
            }

            SelectedRoute = Routes[0];
            StatusMessage = routeCount == 1 ? "1 route loaded" : $"{routeCount} routes loaded";
            Log($"Loaded {routeCount} recent routes from {SelectedDevice.Model.IpAddress}.");
        });
    }

    private async Task InstallForkAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var owner = ForkOwner.Trim();
        var branch = ForkBranch.Trim();
        var repository = string.IsNullOrWhiteSpace(ForkRepository) ? "openpilot" : ForkRepository.Trim();

        await RunBusyOperationAsync("Installing fork...", async () =>
        {
            var progress = new Progress<InstallProgress>(item =>
            {
                StatusMessage = $"{item.Progress}% {item.ProgressText}";
            });

            ForkResult result = await SelectedDevice.Model.InstallForkAsync(owner, branch, progress, repository);
            SelectedDevice.Refresh();
            NotifySelectedDeviceStateChanged();

            StatusMessage = result.Success ? "Fork install completed" : "Fork install failed";
            Log($"Fork install result: {result.Message}");
        });
    }

    private async Task StartLivePlaybackAsync()
    {
        if (SelectedDevice is null || SelectedRoute is null || !HasPlaybackCameras)
        {
            return;
        }

        await StopLivePlaybackCoreAsync(resetStatus: false).ConfigureAwait(true);

        var device = SelectedDevice.Model;
        var route = SelectedRoute.Model;
        var cameraType = SelectedPlaybackCamera;
        var cameraLabel = GetCameraLabel(cameraType);
        var routeSegments = route.Segments
            .Where(segment => segment.RawVideoSegments.ContainsKey(cameraType))
            .OrderBy(segment => segment.Index)
            .ToArray();

        if (routeSegments.Length == 0)
        {
            LivePlaybackStatusMessage = $"No {cameraLabel.ToLowerInvariant()} segments are available for live playback.";
            return;
        }

        var liveCacheDirectory = GetLivePlaybackCacheDirectory(route, cameraType);
        var cancellation = new CancellationTokenSource();
        _livePlaybackCancellation = cancellation;
        VlcLivePlaybackSession? livePlaybackSession = null;

        await RunBusyOperationAsync($"Preparing {cameraLabel.ToLowerInvariant()} live playback...", async () =>
        {
            Directory.CreateDirectory(liveCacheDirectory);
            LivePlaybackStatusMessage = $"Exporting segment 1 of {routeSegments.Length} for live playback...";

            var firstFileName = await device.ExportSegmentAsync(
                liveCacheDirectory,
                routeSegments[0],
                new Camera(cameraType),
                VideoExportContainer.MpegTs).ConfigureAwait(true);

            cancellation.Token.ThrowIfCancellationRequested();

            var firstFilePath = Path.Combine(liveCacheDirectory, firstFileName);
            if (!IsUsablePlaybackFile(firstFilePath))
            {
                throw new FileNotFoundException(
                    "Live playback could not prepare the first route segment.",
                    firstFilePath);
            }

            livePlaybackSession = VlcLivePlaybackSession.Start(firstFilePath, IsLivePlaybackMuted);

            if (_livePlaybackCancellation != cancellation)
            {
                await livePlaybackSession.DisposeAsync().ConfigureAwait(true);
                return;
            }

            livePlaybackSession.PlaybackEnded += OnLivePlaybackEnded;
            livePlaybackSession.PlaybackFailed += OnLivePlaybackFailed;
            _livePlaybackSession = livePlaybackSession;
            LivePlaybackMediaPlayer = livePlaybackSession.MediaPlayer;
            IsLivePlaybackRunning = true;
            StatusMessage = "Live playback started";
            LivePlaybackStatusMessage = $"{cameraLabel} live playback started in the embedded player with segment 1 of {routeSegments.Length}.";
            Log($"Started live playback for {route} ({cameraLabel}).");

            _livePlaybackPrefetchTask = ContinueLivePlaybackAsync(
                device,
                route,
                cameraType,
                routeSegments.Skip(1).ToArray(),
                liveCacheDirectory,
                livePlaybackSession,
                cameraLabel,
                routeSegments.Length,
                cancellation.Token);
        }).ConfigureAwait(true);

        if (_livePlaybackCancellation == cancellation && !IsLivePlaybackRunning)
        {
            _livePlaybackCancellation = null;
            cancellation.Cancel();
            cancellation.Dispose();

            if (livePlaybackSession is not null)
            {
                await livePlaybackSession.DisposeAsync().ConfigureAwait(true);
            }

            UpdateLivePlaybackStatusSummary();
        }
    }

    private async Task StopLivePlaybackAsync()
    {
        await StopLivePlaybackCoreAsync(resetStatus: true).ConfigureAwait(true);
    }

    private Task ToggleLivePlaybackMuteAsync()
    {
        var nextMutedState = !IsLivePlaybackMuted;
        IsLivePlaybackMuted = nextMutedState;

        if (_livePlaybackSession is not null)
        {
            _livePlaybackSession.SetMute(nextMutedState);
        }

        LivePlaybackStatusMessage = nextMutedState
            ? "Live playback audio is muted."
            : "Live playback audio is enabled.";

        return Task.CompletedTask;
    }

    private async Task PlaySelectedRouteAsync()
    {
        if (SelectedDevice is null || SelectedRoute is null || !HasPlaybackCameras)
        {
            return;
        }

        var route = SelectedRoute.Model;
        var cameraType = SelectedPlaybackCamera;
        var cameraLabel = GetCameraLabel(cameraType);
        var playbackFilePath = GetPlaybackFilePath(route, cameraType);

        await RunBusyOperationAsync($"Preparing {cameraLabel.ToLowerInvariant()} playback...", async () =>
        {
            Directory.CreateDirectory(GetPlaybackCacheDirectory());

            if (!IsUsablePlaybackFile(playbackFilePath))
            {
                Log($"Exporting {route} ({cameraLabel}) to {playbackFilePath} for playback.");
                PlaybackStatusMessage = $"Exporting the {cameraLabel.ToLowerInvariant()} camera to MP4...";

                var progress = new Progress<CameraProgress>(item =>
                {
                    StatusMessage = $"{item.Percent}% preparing {cameraLabel.ToLowerInvariant()} playback";
                    PlaybackStatusMessage = $"{item.Percent}% prepared for {cameraLabel.ToLowerInvariant()} playback.";
                });

                await SelectedDevice.Model.ExportRouteAsync(
                    GetPlaybackCacheDirectory(),
                    route,
                    new Camera(cameraType),
                    combineSegments: true,
                    progress).ConfigureAwait(true);
            }
            else
            {
                Log($"Using cached playback file {playbackFilePath}.");
            }

            if (!IsUsablePlaybackFile(playbackFilePath))
            {
                throw new FileNotFoundException(
                    "Playback export completed without producing a usable MP4 file.",
                    playbackFilePath);
            }

            StartPlaybackFile(playbackFilePath);
            StatusMessage = "Playback launched";
            PlaybackStatusMessage = $"{cameraLabel} playback opened in the system video player.";
            Log($"Opened playback file {playbackFilePath}.");
        });
    }

    private async Task ExecuteDeviceActionAsync(
        string actionName,
        Func<OpenpilotDevice, Task<bool>> action,
        string successMessage)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await RunBusyOperationAsync($"{actionName} device...", async () =>
        {
            var succeeded = await action(SelectedDevice.Model);
            SelectedDevice.Refresh();
            NotifySelectedDeviceStateChanged();

            StatusMessage = succeeded ? successMessage : $"{actionName} failed";
            Log(succeeded
                ? $"{actionName} succeeded for {SelectedDevice.Model.IpAddress}."
                : $"{actionName} failed for {SelectedDevice.Model.IpAddress}.");
        });
    }

    private async Task RunBusyOperationAsync(string startingStatus, Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = startingStatus;
            await ExecuteWithSshKeyRecoveryAsync(operation);
        }
        catch (Exception exception)
        {
            StatusMessage = "Operation failed";
            Log($"{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteWithSshKeyRecoveryAsync(Func<Task> operation)
    {
        while (true)
        {
            try
            {
                await operation();
                return;
            }
            catch (OpenpilotSshKeyException exception)
            {
                if (!await TryResolveSshKeyExceptionAsync(exception))
                {
                    throw;
                }
            }
        }
    }

    private async Task<bool> TryResolveSshKeyExceptionAsync(OpenpilotSshKeyException exception)
    {
        if (!exception.CanRetryWithPassphrase || RequestSshKeyPassphraseAsync is null)
        {
            return false;
        }

        StatusMessage = "Waiting for SSH key passphrase";
        Log(exception.Message);

        var passphrase = await RequestSshKeyPassphraseAsync(exception.Message);
        if (passphrase is null)
        {
            StatusMessage = "SSH connection cancelled";
            Log("SSH key passphrase prompt was cancelled.");
            return false;
        }

        SetSshKeyPassphrase(passphrase);
        return true;
    }

    private bool CanInstallFork()
    {
        return !IsBusy
               && SelectedDevice is not null
               && !string.IsNullOrWhiteSpace(ForkOwner)
               && !string.IsNullOrWhiteSpace(ForkBranch);
    }

    private bool CanStartLivePlayback()
    {
        return !IsBusy
               && SelectedDevice is not null
               && SelectedRoute is not null
               && HasPlaybackCameras;
    }

    private bool CanStopLivePlayback()
    {
        return _livePlaybackSession is not null || _livePlaybackCancellation is not null;
    }

    private bool CanToggleLivePlaybackMute()
    {
        return _livePlaybackSession is not null && IsLivePlaybackRunning;
    }

    private bool CanPlaySelectedRoute()
    {
        return !IsBusy
               && SelectedDevice is not null
               && SelectedRoute is not null
               && HasPlaybackCameras;
    }

    private void ToggleNavigation()
    {
        IsNavigationCollapsed = !IsNavigationCollapsed;
    }

    private void ResetSshKey()
    {
        SshKeyPath = string.Empty;
        Log("Cleared the explicit SSH key override. Default key lookup order is active again.");
    }

    private void ClearLog()
    {
        _logBuilder.Clear();
        ActivityLog = string.Empty;
        StatusMessage = "Log cleared";
    }

    private void LogStartupState()
    {
        Log("Openpilot Toolkit cross-platform preview initialized.");
        Log(ResolvedSshKeySummary);
    }

    private void Log(string message)
    {
        if (_logBuilder.Length > 0)
        {
            _logBuilder.AppendLine();
        }

        _logBuilder.Append($"[{DateTime.Now:HH:mm:ss}] ");
        _logBuilder.Append(message);

        ActivityLog = _logBuilder.ToString();
    }

    private void UpdateResolvedSshKeySummary()
    {
        try
        {
            var resolvedPath = OpenpilotHost.ResolvePrivateSshKeyPath();
            ResolvedSshKeySummary = $"Resolved SSH key: {resolvedPath}";
            OnPropertyChanged(nameof(SshKeyPassphraseSummary));
        }
        catch (Exception exception)
        {
            ResolvedSshKeySummary = exception.Message;
            OnPropertyChanged(nameof(SshKeyPassphraseSummary));
        }
    }

    private void RefreshCommandStates()
    {
        DiscoverDevicesCommand.RaiseCanExecuteChanged();
        ConnectHostCommand.RaiseCanExecuteChanged();
        ConnectSelectedDeviceCommand.RaiseCanExecuteChanged();
        LoadRoutesCommand.RaiseCanExecuteChanged();
        StartLivePlaybackCommand.RaiseCanExecuteChanged();
        StopLivePlaybackCommand.RaiseCanExecuteChanged();
        ToggleLivePlaybackMuteCommand.RaiseCanExecuteChanged();
        PlaySelectedRouteCommand.RaiseCanExecuteChanged();
        InstallForkCommand.RaiseCanExecuteChanged();
        RebootCommand.RaiseCanExecuteChanged();
        ShutdownCommand.RaiseCanExecuteChanged();
        ResetSshKeyCommand.RaiseCanExecuteChanged();
        ClearLogCommand.RaiseCanExecuteChanged();
    }

    private void NotifySelectedDeviceStateChanged()
    {
        OnPropertyChanged(nameof(ActiveDeviceDisplay));
        OnPropertyChanged(nameof(SelectedDeviceTitle));
        OnPropertyChanged(nameof(SelectedDeviceSummary));
        OnPropertyChanged(nameof(SelectedDeviceHostLine));
        OnPropertyChanged(nameof(SelectedDeviceStatusLine));
        OnPropertyChanged(nameof(SshStatusText));
        OnPropertyChanged(nameof(SshStatusBackground));
        OnPropertyChanged(nameof(RoutesSummary));
    }

    private void NotifySelectedRouteStateChanged()
    {
        OnPropertyChanged(nameof(SelectedRouteTitle));
        OnPropertyChanged(nameof(SelectedRouteSummary));
        OnPropertyChanged(nameof(SelectedRouteDateLine));
        OnPropertyChanged(nameof(SelectedRouteSegmentLine));
    }

    private void UpdateAvailablePlaybackCameras()
    {
        CameraType[] cameras = SelectedRoute?.Model.Segments
            .SelectMany(segment => segment.RawVideoSegments.Keys)
            .Distinct()
            .OrderBy(GetCameraSortKey)
            .ToArray() ?? [];

        AvailablePlaybackCameras.Clear();
        foreach (var camera in cameras)
        {
            AvailablePlaybackCameras.Add(camera);
        }

        if (cameras.Length > 0 && !cameras.Contains(SelectedPlaybackCamera))
        {
            _selectedPlaybackCamera = cameras[0];
            OnPropertyChanged(nameof(SelectedPlaybackCamera));
        }

        OnPropertyChanged(nameof(HasPlaybackCameras));
        OnPropertyChanged(nameof(SelectedPlaybackCameraLabel));
        UpdatePlaybackStatusSummary();
        UpdateLivePlaybackStatusSummary();
        RefreshCommandStates();
    }

    private void UpdateLivePlaybackStatusSummary()
    {
        if (IsLivePlaybackRunning)
        {
            return;
        }

        if (SelectedRoute is null)
        {
            LivePlaybackStatusMessage = "Select a route to start live playback.";
            return;
        }

        if (!HasPlaybackCameras)
        {
            LivePlaybackStatusMessage = "No raw camera footage is available for live playback on the selected route.";
            return;
        }

        var cameraLabel = GetCameraLabel(SelectedPlaybackCamera).ToLowerInvariant();
        LivePlaybackStatusMessage = $"Start live playback to stream the {cameraLabel} camera into the embedded player as each segment finishes exporting.";
    }

    private void UpdatePlaybackStatusSummary()
    {
        if (SelectedRoute is null)
        {
            PlaybackStatusMessage = "Select a route to prepare a playback file.";
            return;
        }

        if (!HasPlaybackCameras)
        {
            PlaybackStatusMessage = "No raw camera footage is available on the selected route.";
            return;
        }

        var cameraLabel = GetCameraLabel(SelectedPlaybackCamera).ToLowerInvariant();
        var playbackFilePath = GetPlaybackFilePath(SelectedRoute.Model, SelectedPlaybackCamera);
        PlaybackStatusMessage = IsUsablePlaybackFile(playbackFilePath)
            ? $"Cached {cameraLabel} playback is ready to open in your system video player."
            : $"The selected {cameraLabel} camera will be exported to MP4 before playback starts.";
    }

    private async Task StopLivePlaybackCoreAsync(bool resetStatus)
    {
        var cancellation = _livePlaybackCancellation;
        _livePlaybackCancellation = null;

        var livePlaybackSession = _livePlaybackSession;
        _livePlaybackSession = null;
        LivePlaybackMediaPlayer = null;

        cancellation?.Cancel();
        if (livePlaybackSession is not null)
        {
            livePlaybackSession.PlaybackEnded -= OnLivePlaybackEnded;
            livePlaybackSession.PlaybackFailed -= OnLivePlaybackFailed;
            await livePlaybackSession.DisposeAsync().ConfigureAwait(true);
        }

        cancellation?.Dispose();
        _livePlaybackPrefetchTask = null;
        IsLivePlaybackRunning = false;

        if (resetStatus)
        {
            StatusMessage = "Live playback stopped";
            LivePlaybackStatusMessage = "Live playback stopped.";
            Log("Stopped live playback.");
        }
        else
        {
            UpdateLivePlaybackStatusSummary();
        }
    }

    private async Task ContinueLivePlaybackAsync(
        OpenpilotDevice device,
        OpenpilotSdk.OpenPilot.Route route,
        CameraType cameraType,
        IReadOnlyList<OpenpilotSdk.OpenPilot.RouteSegment> remainingSegments,
        string liveCacheDirectory,
        VlcLivePlaybackSession livePlaybackSession,
        string cameraLabel,
        int totalSegments,
        CancellationToken cancellationToken)
    {
        try
        {
            var camera = new Camera(cameraType);
            for (var segmentIndex = 0; segmentIndex < remainingSegments.Count; segmentIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var routeSegment = remainingSegments[segmentIndex];
                LivePlaybackStatusMessage =
                    $"Buffering segment {segmentIndex + 2} of {totalSegments} for {cameraLabel.ToLowerInvariant()} live playback...";

                var fileName = await device.ExportSegmentAsync(
                    liveCacheDirectory,
                    routeSegment,
                    camera,
                    VideoExportContainer.MpegTs).ConfigureAwait(true);

                cancellationToken.ThrowIfCancellationRequested();

                var filePath = Path.Combine(liveCacheDirectory, fileName);
                if (!IsUsablePlaybackFile(filePath))
                {
                    throw new FileNotFoundException(
                        $"Live playback did not produce a usable file for segment {routeSegment.Index}.",
                        filePath);
                }

                livePlaybackSession.AppendSegment(filePath);
                LivePlaybackStatusMessage =
                    $"Buffered segment {segmentIndex + 2} of {totalSegments} for {cameraLabel.ToLowerInvariant()} live playback.";
            }

            if (_livePlaybackCancellation?.Token == cancellationToken)
            {
                livePlaybackSession.Complete();
                LivePlaybackStatusMessage =
                    $"{cameraLabel} live playback buffered all {totalSegments} segments for {route}.";
            }
        }
        catch (OperationCanceledException)
        {
            // A stopped/restarted session cancels the export loop.
        }
        catch (Exception exception)
        {
            if (_livePlaybackCancellation?.Token != cancellationToken)
            {
                return;
            }

            livePlaybackSession.Complete();
            StatusMessage = "Live playback buffering failed";
            LivePlaybackStatusMessage = "Live playback failed while buffering the remaining route segments.";
            Log($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OnLivePlaybackEnded()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_livePlaybackSession is null)
            {
                return;
            }

            _livePlaybackCancellation?.Dispose();
            _livePlaybackCancellation = null;
            _livePlaybackPrefetchTask = null;
            IsLivePlaybackRunning = false;
            StatusMessage = "Live playback finished";
            LivePlaybackStatusMessage = "Live playback reached the end of the buffered route.";
            Log("Live playback reached the end of the buffered route.");
            RefreshCommandStates();
        });
    }

    private void OnLivePlaybackFailed()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_livePlaybackSession is null)
            {
                return;
            }

            _livePlaybackCancellation?.Dispose();
            _livePlaybackCancellation = null;
            _livePlaybackPrefetchTask = null;
            IsLivePlaybackRunning = false;
            StatusMessage = "Live playback failed";
            LivePlaybackStatusMessage = "Embedded live playback hit a decoding or renderer error.";
            Log("Embedded live playback failed.");
            RefreshCommandStates();
        });
    }

    private string GetPlaybackCacheDirectory()
    {
        var rootDirectory = string.IsNullOrWhiteSpace(ExportFolder)
            ? OpenpilotPaths.TempDirectory
            : Path.GetFullPath(ExportFolder);

        return Path.Combine(rootDirectory, "PlaybackCache");
    }

    private string GetLivePlaybackCacheDirectory(OpenpilotSdk.OpenPilot.Route route, CameraType cameraType)
    {
        return Path.Combine(GetPlaybackCacheDirectory(), "Live", $"{route}{(char)cameraType}");
    }

    private string GetPlaybackFilePath(OpenpilotSdk.OpenPilot.Route route, CameraType cameraType)
    {
        return Path.Combine(GetPlaybackCacheDirectory(), $"{route}{(char)cameraType}.mp4");
    }

    private static bool IsUsablePlaybackFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return new FileInfo(path).Length > 0;
    }

    private static void StartPlaybackFile(string filePath)
    {
        if (TryOpenWithShell(filePath))
        {
            return;
        }

        if (OperatingSystem.IsLinux() && TryStartProcess("xdg-open", filePath))
        {
            return;
        }

        if (OperatingSystem.IsMacOS() && TryStartProcess("open", filePath))
        {
            return;
        }

        if (TryStartProcess("ffplay", filePath))
        {
            return;
        }

        if (TryStartProcess("mpv", filePath))
        {
            return;
        }

        if (TryStartProcess("vlc", filePath))
        {
            return;
        }

        throw new InvalidOperationException(
            "No video player could be launched for the exported playback file. Install a default media handler, ffplay, mpv, or VLC.");
    }

    private static bool TryOpenWithShell(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath)
            {
                UseShellExecute = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStartProcess(string command, string argument)
    {
        try
        {
            var startInfo = new ProcessStartInfo(command)
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(argument);

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCameraLabel(CameraType cameraType)
    {
        return cameraType switch
        {
            CameraType.Front => "Front",
            CameraType.Wide => "Wide",
            CameraType.Driver => "Driver",
            _ => cameraType.ToString()
        };
    }

    private static int GetCameraSortKey(CameraType cameraType)
    {
        return cameraType switch
        {
            CameraType.Front => 0,
            CameraType.Wide => 1,
            CameraType.Driver => 2,
            _ => int.MaxValue
        };
    }

    private (DeviceViewModel DeviceViewModel, bool IsNewDevice) UpsertDevice(OpenpilotDevice device)
    {
        var deviceViewModel = new DeviceViewModel(device);
        var existing = Devices.FirstOrDefault(item => item.Model == device);
        if (existing is not null)
        {
            var index = Devices.IndexOf(existing);
            Devices[index] = deviceViewModel;
            return (deviceViewModel, false);
        }

        Devices.Insert(0, deviceViewModel);
        return (deviceViewModel, true);
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(DeviceCount));
        OnPropertyChanged(nameof(DevicesSummary));
        OnPropertyChanged(nameof(WifiStatusText));
        OnPropertyChanged(nameof(WifiStatusBackground));
    }

    private void OnRoutesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RoutesSummary));
    }
}
