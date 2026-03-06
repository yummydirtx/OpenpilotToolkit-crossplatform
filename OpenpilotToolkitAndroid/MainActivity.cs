using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.Runtime;
using Android.Text;
using Android.Views;
using AndroidX.AppCompat.App;
using AndroidX.Core.View;
using AndroidX.DrawerLayout.Widget;
using Google.Android.Material.Navigation;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;
using Java.Interop;
using OpenpilotSdk.Git;
using OpenpilotSdk.Hardware;
using OpenpilotSdk.OpenPilot.Fork;
using Renci.SshNet.Common;
using Serilog;
using Serilog.Core;
using System.Linq;
using System.Net;
using Xamarin.Essentials;
using Environment = System.Environment;
using Toolbar = AndroidX.AppCompat.Widget.Toolbar;

namespace OpenpilotToolkitAndroid
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true,
        LaunchMode = LaunchMode.SingleInstance, ConfigurationChanges = ConfigChanges.Orientation, ScreenOrientation = ScreenOrientation.Portrait)]
    public class MainActivity : AppCompatActivity, NavigationView.IOnNavigationItemSelectedListener
    {
        private readonly Dictionary<string,OpenpilotDevice> _devices = new();
        private AutoCompleteTextView? _cmbOpenpilotDevice;
        private bool _settingsEnabled;



        public bool OnNavigationItemSelected(IMenuItem item)
        {
            var id = item.ItemId;

            if (id == Resource.Id.ssh_wizard)
            {
                var sshActivity = new Intent(this, typeof(SshActivity));
                StartActivity(sshActivity);
                OverridePendingTransition(Android.Resource.Animation.SlideInLeft, Android.Resource.Animation.FadeOut);
            }
            else if (id == Resource.Id.log_file)
            {
                var logActivity = new Intent(this, typeof(LogActivity));
                StartActivity(logActivity);
                OverridePendingTransition(Android.Resource.Animation.SlideInLeft, Android.Resource.Animation.FadeOut);
            }
            else if (id == Resource.Id.fork_installer)
            {
            }



            var drawer = FindViewById<DrawerLayout>(Resource.Id.drawer_layout);
            drawer?.CloseDrawer(GravityCompat.Start);
            return true;
        }

        private async void CmbOpenpilotDeviceOnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (e.Text != null && !string.IsNullOrWhiteSpace(e.Text.ToString()))
            {
                if (_devices.TryGetValue(e.Text.ToString(), out var device))
                {
                    await device.ConnectAsync();

                }
            }
        }

        protected override async void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            var personalFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            if (!Directory.Exists(personalFolder))
            {
                Directory.CreateDirectory(personalFolder);
            }
            
            var logPath = Path.Combine(personalFolder, "log.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.AndroidLog()
                .WriteTo.File(logPath)
                .Enrich.WithProperty(Constants.SourceContextPropertyName, "OpenpilotToolkit")
                .CreateLogger();
            AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightYes;


            Platform.Init(this, savedInstanceState);

            SetContentView(Resource.Layout.activity_main);

            var usernames = new[] { "spektor56",
                "sunnypilot",
                "commaai",
                "FrogAi" };

            var autoComplete = FindViewById<AutoCompleteTextView>(Resource.Id.tietForkUsername);

            var adapter2 = new ArrayAdapter<string>(
                this,
                Resource.Layout.support_simple_spinner_dropdown_item, // Material-friendly dropdown
                usernames);

            autoComplete.Adapter = adapter2;


            _cmbOpenpilotDevice = FindViewById<AutoCompleteTextView>(Resource.Id.autocomplete_comma);
            var adapter = new ArrayAdapter(this, Resource.Layout.list_item);
            if (_cmbOpenpilotDevice != null)
            {
                _cmbOpenpilotDevice.Adapter = adapter;
                _cmbOpenpilotDevice.TextChanged += CmbOpenpilotDeviceOnTextChanged;
            }

            var toolbar = FindViewById<Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            var drawer = FindViewById<DrawerLayout>(Resource.Id.drawer_layout);
            var toggle = new ActionBarDrawerToggle(this, drawer, toolbar, Resource.String.navigation_drawer_open,
                Resource.String.navigation_drawer_close);
            drawer.AddDrawerListener(toggle);
            toggle.SyncState();

            var navigationView = FindViewById<NavigationView>(Resource.Id.nav_view);
            navigationView.SetNavigationItemSelectedListener(this);

            //var status = await Permissions.RequestAsync<Permissions.NetworkState>();
            //var status2 = await Permissions.RequestAsync<Permissions.StorageRead>();
            //var status3 = await Permissions.RequestAsync<Permissions.StorageWrite>();

            var sshKeyPath = Path.Combine(personalFolder, "opensshkey");
            if (!File.Exists(sshKeyPath))
            {
                await using (var fs = File.Create(sshKeyPath))
                {
                    await using (var stream = Assets.Open("opensshkey"))
                    {
                        await stream.CopyToAsync(fs);
                    }
                }
            }
            
            ShowProgress("Scanning");
            await Task.Run(async () =>
            {
                try
                {
                    await GetDevicesAsync();
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error scanning devices");
                }
                finally
                {
                    await MainThread.InvokeOnMainThreadAsync(HideProgress);
                }
            });
        }

        private async Task GetDevicesAsync()
        {
            WifiManager.MulticastLock? multicastLock = null;
            if (ApplicationContext != null)
            {
                var wifi = ApplicationContext.GetSystemService(Context.WifiService) as WifiManager;
                if (wifi != null)
                {
                    multicastLock = wifi.CreateMulticastLock("Zeroconf lock");
                }
            }

            _devices.Clear();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_cmbOpenpilotDevice != null)
                {
                    ((ArrayAdapter)_cmbOpenpilotDevice.Adapter)?.Clear();
                    _cmbOpenpilotDevice.SetText("", false);
                }
            });

            try
            {
                var discoveredDevices = new HashSet<OpenpilotDevice>();

                try
                {
                    if (multicastLock != null)
                    {
                        multicastLock.Acquire();
                    }
                    
                    await Task.Run(async () =>
                    {
                        var connectionTasks = new List<Task>();
                        try
                        {
                            await foreach (var device in OpenpilotDevice.DiscoverAsync().ConfigureAwait(false))
                            {
                                if (discoveredDevices.Add(device))
                                {
                                    connectionTasks.Add(Task.Run(async () =>
                                    {
                                        if (device is not UnknownDevice)
                                        {
                                            if (!device.IsAuthenticated)
                                            {
                                                try
                                                {
                                                    await device.ConnectSftpAsync().ConfigureAwait(false);
                                                }
                                                catch (SshAuthenticationException ex)
                                                {
                                                    Log.Information(ex, "Authentication failed for {Device}", device);
                                                }
                                            }
                                        }

                                        if (device.IsAuthenticated)
                                        {
                                            var deviceDescription = device.ToString();
                                            if (!_devices.ContainsKey(deviceDescription))
                                            {
                                                _devices.Add(deviceDescription, device);

                                                await MainThread.InvokeOnMainThreadAsync(() =>
                                                {
                                                    ((ArrayAdapter)_cmbOpenpilotDevice.Adapter).Add(deviceDescription);
                                                    _cmbOpenpilotDevice.SetText(
                                                        ((ArrayAdapter)_cmbOpenpilotDevice.Adapter).GetItem(0).ToString(), false);

                                                });
                                            }
                                        }
                                    }));
                                }
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            Log.Error("Discovery Timed Out.");
                        }

                        await Task.WhenAll(connectionTasks).ConfigureAwait(false);
                    });
                }
                finally
                {
                    if (multicastLock != null)
                    {
                        multicastLock.Release();
                    }
                }

                if (discoveredDevices.Count < 1)
                {
                    await ShowToastOnMainThreadAsync(
                        "No devices were found, please check that SSH is enabled on your device and the device is connected to the network.");
                }
                else if (_devices.Count < 1 && discoveredDevices.Count > 0)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        Android.Views.View view = FindViewById(Android.Resource.Id.Content);
                        var snack = Snackbar.Make(view, $"{discoveredDevices.Count} device(s) found but authentication failed, click to start the SSH wizard.", BaseTransientBottomBar.LengthIndefinite);
                        snack.SetAction("SSH Wizard", (view) =>
                        {
                            var sshActivity = new Intent(this, typeof(SshActivity));
                            StartActivity(sshActivity);
                            OverridePendingTransition(Android.Resource.Animation.SlideInLeft, Android.Resource.Animation.FadeOut);
                        });
                        snack.Show();
                    });

                    //await ShowToastOnMainThreadAsync(
                    //$"{foundDevices} device(s) found but authentication failed, do you want to start the SSH wizard?");
                }
                /*
                if (_devices.Count > 0)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        
                        
                        
                        
                    });
                }
                */
            }
            catch (Exception e)
            {
                Log.Error(e, "Error scanning devices");
            }
        }

        private async Task ShowToastOnMainThreadAsync(string message)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Toast.MakeText(this,
                    message,
                    ToastLength.Long).Show();
            });
        }

        private void HideProgress()
        {
            var progressSection = (LinearLayout)FindViewById(Resource.Id.progressSection);
            progressSection.Visibility = ViewStates.Invisible;
            _settingsEnabled = true;
            InvalidateOptionsMenu();
        }

        private void ShowProgress(string text)
        {
            var progressSection = (LinearLayout)FindViewById(Resource.Id.progressSection);
            var progressText = (TextView)FindViewById(Resource.Id.progressText);
            var progressBar = (CircularProgressIndicator)FindViewById(Resource.Id.progressBar);
            
            progressBar.Indeterminate = true;
            progressText.Text = text;
            progressSection.Visibility = ViewStates.Visible;
            _settingsEnabled = false;
            InvalidateOptionsMenu();
        }

        [Export("InstallFork")]
        public async void InstallFork(View view)
        {
            var btnInstall = FindViewById<Button>(Resource.Id.btnInstall);
            btnInstall.Enabled = false;
            try
            {
                var tietForkUsername = (EditText)FindViewById(Resource.Id.tietForkUsername);
                var tietBranchName = (EditText)FindViewById(Resource.Id.tietForkBranch);

                var forkUser = tietForkUsername.Text;
                var forkBranch = tietBranchName.Text;

                if (string.IsNullOrWhiteSpace(forkUser))
                {
                    Snackbar.Make(view, "Fork Username is Required", BaseTransientBottomBar.LengthLong).Show();

                    //Toast.MakeText(this, "Fork Username is Required",
                    //ToastLength.Long).Show();

                    return;
                }

                if (string.IsNullOrWhiteSpace(forkBranch))
                {
                    Snackbar.Make(view, "Fork Branch is Required", BaseTransientBottomBar.LengthLong).Show();
                    /*
                    Toast.MakeText(this, "Fork Branch is Required",
                        ToastLength.Long).Show();*/
                    return;
                }

                OpenpilotDevice device = null;
                if (!string.IsNullOrWhiteSpace(_cmbOpenpilotDevice.Text))
                {
                    _devices.TryGetValue(_cmbOpenpilotDevice.Text, out device);
                }
                
                if (device is not null && device is not UnknownDevice)
                {
                    ForkResult result = null;

                    ShowProgress("Installing Fork");
                    var progress = new Progress<InstallProgress>();
                    progress.ProgressChanged += ProgressOnProgressChanged;
                    
                    await Task.Run(async () =>
                    {
                        result = await device.InstallForkAsync(forkUser, forkBranch, progress).ConfigureAwait(false);
                    });
                
                    Toast.MakeText(this, result.Success
                            ? "Install Successful"
                            : $"There was an error during installation: {result.Message}",
                        ToastLength.Long).Show();
                }
                else
                {
                    Snackbar.Make(view, @"No comma device selected", BaseTransientBottomBar.LengthLong).Show();
                }
            }
            catch (Exception exception)
            {
                Log.Error(exception, "error in fork installer");
                Toast.MakeText(this, exception.Message,
                    ToastLength.Long).Show();
            }
            finally
            {
                btnInstall.Enabled = true;
                HideProgress();
            }
        }

        private void ProgressOnProgressChanged(object sender, InstallProgress e)
        {
            var progressBar = (CircularProgressIndicator)FindViewById(Resource.Id.progressBar);
            var progressText = (TextView)FindViewById(Resource.Id.progressText);
            progressBar.Indeterminate = false;

            progressText.Text = e.ProgressText;
            if (e.Progress != progressBar.Progress)
            {
                progressBar.SetProgressCompat(e.Progress, e.Progress > progressBar.Progress);
            }
        }


        public override void OnBackPressed()
        {
            var drawer = FindViewById<DrawerLayout>(Resource.Id.drawer_layout);
            if (drawer.IsDrawerOpen(GravityCompat.Start))
            {
                drawer.CloseDrawer(GravityCompat.Start);
            }
            else
            {
                base.OnBackPressed();
            }
        }

        public override bool OnCreateOptionsMenu(IMenu? menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnPrepareOptionsMenu(IMenu? menu)
        {
            var item = menu.FindItem(Resource.Id.action_settings);
            if (item != null)
            {
                item.SetEnabled(_settingsEnabled);
            }

            return base.OnPrepareOptionsMenu(menu);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            var id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                _settingsEnabled = false;
                item.SetEnabled(false);
                ShowProgress("Scanning");
                Task.Run(async () =>
                {
                    try
                    {
                        await GetDevicesAsync();
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Error scanning devices");
                    }
                    finally
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            HideProgress();
                            item.SetEnabled(true);
                        });
                    }
                });
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        private void FabOnClick(object sender, EventArgs eventArgs)
        {
            var view = (View)sender;
            Snackbar.Make(view, "Replace with your own action", BaseTransientBottomBar.LengthLong)
                .SetAction("Action", (View.IOnClickListener)null).Show();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
            [GeneratedEnum] Permission[] grantResults)
        {
            Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}