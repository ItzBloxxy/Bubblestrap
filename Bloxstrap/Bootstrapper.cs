// To debug the automatic updater:
// - Uncomment the definition below
// - Publish the executable
// - Launch the executable (click no when it asks you to upgrade)
// - Launch Roblox (for testing web launches, run it from the command prompt)
// - To re-test the same executable, delete it from the installation folder

// #define DEBUG_UPDATER

#if DEBUG_UPDATER
#warning "Automatic updater debugging is enabled"
#endif

using Bloxstrap.AppData;
using Bloxstrap.RobloxInterfaces;
using Bloxstrap.UI.Elements.Bootstrapper.Base;
using Bloxstrap.UI.ViewModels.Settings;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Shell;
using System.Text.Json.Nodes;

namespace Bloxstrap
{
    public class Bootstrapper
    {
        #region Properties
        private const int ProgressBarMaximum = 10000;

        private const double TaskbarProgressMaximumWpf = 1; // this can not be changed. keep it at 1.
        private const int TaskbarProgressMaximumWinForms = WinFormsDialogBase.TaskbarProgressMaximum;

        private const string AppSettings =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
            "<Settings>\r\n" +
            "	<ContentFolder>content</ContentFolder>\r\n" +
            "	<BaseUrl>http://www.roblox.com</BaseUrl>\r\n" +
            "</Settings>\r\n";

        private readonly FastZipEvents _fastZipEvents = new();
        private readonly CancellationTokenSource _cancelTokenSource = new();

        private IAppData AppData = default!;
        private Dictionary<string, string> PackageDirectoryMap = null!;
        private LaunchMode _launchMode;
        public int AppPid => _appPid;
        public bool IsPlayerLaunch => _launchMode == LaunchMode.Player;

        private string _launchCommandLine = App.LaunchSettings.RobloxLaunchArgs;
        private Version? _latestVersion = null;
        private string _latestVersionGuid = null!;
        private string _latestVersionDirectory = null!;
        private PackageManifest _versionPackageManifest = null!;
        public static bool _staticDirectory => App.Settings.Prop.StaticDirectory;

        private bool _isInstalling = false;
        private double _progressIncrement;
        private double _taskbarProgressIncrement;
        private double _taskbarProgressMaximum;
        private long _totalDownloadedBytes = 0;
        private bool _packageExtractionSuccess = true;

        private bool _mustUpgrade => !App.Settings.Prop.SkipRobloxUpgrades && !App.Settings.Prop.UsePreviousVersion && (App.LaunchSettings.ForceFlag.Active || App.State.Prop.ForceReinstall || String.IsNullOrEmpty(AppData.State.VersionGuid) || !File.Exists(AppData.ExecutablePath));
        private bool _noConnection = false;

        private AsyncMutex? _mutex;

        private int _appPid = 0;

        public IBootstrapperDialog? Dialog = null;

        public bool IsStudioLaunch => _launchMode != LaunchMode.Player;

        public string MutexName { get; set; } = "Bloxstrap-Bootstrapper";
        public bool QuitIfMutexExists { get; set; } = false;
        #endregion

        #region Core
        public Bootstrapper(LaunchMode launchMode)
        {
            _launchMode = launchMode;

            // https://github.com/icsharpcode/SharpZipLib/blob/master/src/ICSharpCode.SharpZipLib/Zip/FastZip.cs/#L669-L680
            // exceptions don't get thrown if we define events without actually binding to the s2failure events. probably a bug. ¯\_(ツ)_/¯
            _fastZipEvents.FileFailure += (_, e) =>
            {
                // only give a pass to font files (no idea whats wrong with them)
                if (!e.Name.EndsWith(".ttf"))
                    throw e.Exception;

                App.Logger.WriteLine("FastZipEvents::OnFileFailure", $"Failed to extract {e.Name}");
                _packageExtractionSuccess = false;
            };
            _fastZipEvents.DirectoryFailure += (_, e) => throw e.Exception;
            _fastZipEvents.ProcessFile += (_, e) => e.ContinueRunning = !_cancelTokenSource.IsCancellationRequested;

            SetupAppData();
        }

        private void SetupAppData()
        {
            AppData = IsStudioLaunch ? new RobloxStudioData() : new RobloxPlayerData();
            Deployment.BinaryType = AppData.BinaryType;
        }

        // we will use this to ensure local data is loaded
        private async Task SetupPackageDictionaries()
        {
            await App.LocalData.WaitUntilDataFetched(); // does this even work?

            var localData = App.LocalData.Prop.PackageMaps[IsStudioLaunch ? "studio" : "player"];
            var commonData = App.LocalData.Prop.PackageMaps.CommonPackageMap;

            PackageDirectoryMap = new(commonData);

            foreach (var package in localData)
                PackageDirectoryMap[package.Key] = package.Value;
        }

        private void SetStatus(string message)
        {
            message = message.Replace("{product}", AppData.ProductName);

            if (Dialog is not null)
                Dialog.Message = message;
        }

        private void UpdateProgressBar()
        {
            if (Dialog is null) return;

            // calculate the current ratio (0.0 to 1.0)
            // we divide it by the Max to normalize it
            double ratio = (_progressIncrement * _totalDownloadedBytes) / ProgressBarMaximum;
            ratio = Math.Clamp(ratio, 0, 1.0);

            // update UI
            Dialog.ProgressValue = (int)Math.Floor(ratio * ProgressBarMaximum);

            // update Taskbar
            Dialog.TaskbarProgressValue = ratio * _taskbarProgressMaximum;
        }

        private void HandleConnectionError(Exception exception)
        {
            const string LOG_IDENT = "Bootstrapper::HandleConnectionError";

            _noConnection = true;

            App.Logger.WriteLine(LOG_IDENT, "Connectivity check failed");
            App.Logger.WriteException(LOG_IDENT, exception);

            string message = Strings.Dialog_Connectivity_BadConnection;

            if (exception is AggregateException)
                exception = exception.InnerException!;

            // https://gist.github.com/pizzaboxer/4b58303589ee5b14cc64397460a8f386
            if (exception is HttpRequestException && exception.InnerException is null)
                message = String.Format(Strings.Dialog_Connectivity_RobloxDown, "[status.roblox.com](https://status.roblox.com)");

            if (_mustUpgrade)
                message += $"\n\n{Strings.Dialog_Connectivity_RobloxUpgradeNeeded}\n\n{Strings.Dialog_Connectivity_TryAgainLater}";
            else
                message += $"\n\n{Strings.Dialog_Connectivity_RobloxUpgradeSkip}";

            Frontend.ShowConnectivityDialog(
                String.Format(Strings.Dialog_Connectivity_UnableToConnect, "Roblox"),
                message,
                _mustUpgrade ? MessageBoxImage.Error : MessageBoxImage.Warning,
                exception);

            if (_mustUpgrade)
                App.Terminate(ErrorCode.ERROR_CANCELLED);
        }

        public async Task Run()
        {
            const string LOG_IDENT = "Bootstrapper::Run";

            App.Logger.WriteLine(LOG_IDENT, "Running bootstrapper");

            // this is now always enabled as of v2.8.0 (bloxstrap)
            if (Dialog is not null)
                Dialog.CancelEnabled = true;

            SetStatus(Strings.Bootstrapper_Status_Connecting);

            // 1) Initialize connectivity first (must complete so Deployment.BaseUrl is set)
            var connectionResult = await Deployment.InitializeConnectivity();
            App.Logger.WriteLine(LOG_IDENT, "Connectivity check finished");

            if (connectionResult is not null)
                HandleConnectionError(connectionResult);

#if (!DEBUG || DEBUG_UPDATER)
            // save settings before spawning concurrent tasks to avoid file-sharing conflicts
            // (CheckForUpdates needs settings persisted for the updater process it may launch)
            App.Settings.Save();

            // prepare an update-check task but do not await it yet; wrap to absorb exceptions
            Task<bool>? checkUpdatesTask = null;
            if (App.Settings.Prop.CheckForUpdates && !App.LaunchSettings.UpgradeFlag.Active)
            {
                checkUpdatesTask = Task.Run(async () =>
                {
                    try
                    {
                        return await CheckForUpdates();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "CheckForUpdates threw an exception");
                        App.Logger.WriteException(LOG_IDENT, ex);
                        return false;
                    }
                });
            }
#endif

            // 2) If we have connectivity, fetch latest version info and run update check concurrently
            if (!_noConnection)
            {
                // fetch latest version info (capture exceptions so we can handle them)
                var getLatestInfoTask = Task.Run(async () =>
                {
                    try
                    {
                        await GetLatestVersionInfo();
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

#if (!DEBUG || DEBUG_UPDATER)
                // await both tasks (or CompletedTask if checkUpdatesTask is null)
                await Task.WhenAll(getLatestInfoTask, checkUpdatesTask ?? Task.CompletedTask);
#else
                await getLatestInfoTask;
#endif

                // handle error from GetLatestVersionInfo
                var latestInfoException = ((Task<Exception?>)getLatestInfoTask).Result;
                if (latestInfoException is not null)
                    HandleConnectionError(latestInfoException);

#if (!DEBUG || DEBUG_UPDATER)
                // if an update was found, the updater process will have been launched — exit
                if (checkUpdatesTask is not null && checkUpdatesTask.Result)
                    return;
#endif
            }

            // rest of original Run() flow follows...
            // ensure only one instance of the bootstrapper is running at the time
            // so that we don't have stuff like two updates happening simultaneously

            bool mutexExists = Utilities.DoesMutexExist(MutexName);

            if (mutexExists)
            {
                if (!QuitIfMutexExists)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{MutexName} mutex exists, waiting...");
                    SetStatus(Strings.Bootstrapper_Status_WaitingOtherInstances);
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{MutexName} mutex exists, exiting!");
                    return;
                }
            }

            // wait for mutex to be released if it's not yet
            await using var mutex = new AsyncMutex(false, MutexName);
            await mutex.AcquireAsync(_cancelTokenSource.Token);

            _mutex = mutex;

            // reload our configs since they've likely changed by now
            if (mutexExists)
            {
                App.Settings.Load();
                App.State.Load();
                App.RobloxState.Load();
            }

            CleanupVersionsFolder(); // cleanup after background updater

            bool allModificationsApplied = true;

            if (!_noConnection)
            {
                if (App.LocalData.LoadedState == GenericTriState.Unknown) // we dont want it to flicker
                    SetStatus(Strings.Bootstrapper_Status_WaitingForData);

                await SetupPackageDictionaries(); // mods also require it

                if (AppData.State.VersionGuid != _latestVersionGuid || _mustUpgrade)
                {
                    bool backgroundUpdaterMutexOpen = Utilities.DoesMutexExist("Bloxstrap-BackgroundUpdater");
                    if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
                        backgroundUpdaterMutexOpen = false; // we want to actually update lol

                    App.Logger.WriteLine(LOG_IDENT, $"Background updater running: {backgroundUpdaterMutexOpen}");

                    if (backgroundUpdaterMutexOpen && _mustUpgrade)
                    {
                        // I am Forced Upgrade, killer of Background Updates
                        Utilities.KillBackgroundUpdater();
                        backgroundUpdaterMutexOpen = false;
                    }

                    if (!backgroundUpdaterMutexOpen)
                    {
                        if (IsEligibleForBackgroundUpdate())
                            StartBackgroundUpdater();
                        else
                            await UpgradeRoblox();
                    }
                }

                if (_cancelTokenSource.IsCancellationRequested)
                    return;

                // we require deployment details for applying modifications for a worst case scenario,
                // where we'd need to restore files from a package that isn't present on disk and needs to be redownloaded
                allModificationsApplied = await ApplyModifications();
            }

            // check registry entries for every launch, just in case the stock bootstrapper changes it back

            if (IsStudioLaunch)
                WindowsRegistry.RegisterStudio();
            else
            {
                WindowsRegistry.RegisterPlayer();
                ModsViewModel.ApplyRobloxIcon();
            }

            WindowsRegistry.RegisterClientLocation(IsStudioLaunch, _latestVersionDirectory); // if it for some reason doesnt exist

            if (_launchMode != LaunchMode.Player)
                await mutex.ReleaseAsync();

            if (!App.LaunchSettings.NoLaunchFlag.Active && !_cancelTokenSource.IsCancellationRequested)
            {
                if (!App.LaunchSettings.QuietFlag.Active)
                {
                    // show some balloon tips
                    if (!_packageExtractionSuccess)
                        Frontend.ShowBalloonTip(Strings.Bootstrapper_ExtractionFailed_Title, Strings.Bootstrapper_ExtractionFailed_Message, ToolTipIcon.Warning);
                    else if (!allModificationsApplied)
                        Frontend.ShowBalloonTip(Strings.Bootstrapper_ModificationsFailed_Title, Strings.Bootstrapper_ModificationsFailed_Message, ToolTipIcon.Warning);
                }

                StartRoblox();
            }

            await mutex.ReleaseAsync();

            Dialog?.CloseBootstrapper();
        }

        private async Task GetLatestVersionInfo()
        {
            const string LOG_IDENT = "Bootstrapper::GetLatestVersionInfo";

            // before we do anything, we need to query our channel
            // if it's set in the launch uri, we need to use it and set the registry key for it
            // else, check if the registry key for it exists, and use it

            using var key = Registry.CurrentUser.CreateSubKey($"SOFTWARE\\ROBLOX Corporation\\Environments\\{AppData.RegistryName}\\Channel");

            var match = Regex.Match(
                App.LaunchSettings.RobloxLaunchArgs,
                "channel:([a-zA-Z0-9-_]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );

            bool ChannelFlag = App.LaunchSettings.ChannelFlag.Active && !string.IsNullOrEmpty(App.LaunchSettings.ChannelFlag.Data);

            // CHANNEL CHANGE MODE

            void EnrollChannel(string Channel = "production") => Deployment.Channel = Channel;
            void RevertChannel() => Deployment.Channel = Deployment.DefaultChannel;

            string EnrolledChannel = match.Groups.Count == 2 ? match.Groups[1].Value.ToLowerInvariant() : Deployment.DefaultChannel;
            bool behindProductionCheck = App.Settings.Prop.ChannelChangeMode == ChannelChangeMode.Prompt;

            // private channels
            if (App.Cookies.Loaded)
            {
                UserChannel? userChannel = await App.Cookies.GetUserChannel(Deployment.BinaryType);

                if (
                    userChannel?.Token is not null &&
                    userChannel.AssignmentType != 1 // might need a change in the future
                    )
                {
                    // prevent roblox from thinking its a different channel
                    // we have to do it to prevent issues with channel fflags
                    if (!string.IsNullOrEmpty(EnrolledChannel))
                        _launchCommandLine = _launchCommandLine.Replace(
                            $"channel:{EnrolledChannel}",
                            $"channel:{userChannel.Channel}",
                            StringComparison.OrdinalIgnoreCase);

                    Deployment.ChannelToken = userChannel.Token;
                    EnrolledChannel = userChannel.Channel;
                }
            }

            if (!ChannelFlag)
            {
                switch (App.Settings.Prop.ChannelChangeMode)
                {
                    case ChannelChangeMode.Automatic:
                        App.Logger.WriteLine(LOG_IDENT, "Enrolling into channel");

                        EnrollChannel(EnrolledChannel);
                        break;
                    case ChannelChangeMode.Prompt:
                        App.Logger.WriteLine(LOG_IDENT, "Prompting channel enrollment");

                        if
                        (
                        !match.Success ||
                        match.Groups.Count != 2 ||
                        match.Groups[1].Value.ToLowerInvariant() == Deployment.Channel
                        )
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Channel is either equal or incorrectly formatted");
                            break;
                        }

                        string DisplayChannel = !String.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : Deployment.DefaultChannel;

                        var Result = Frontend.ShowMessageBox(
                        String.Format(Strings.Bootstrapper_Bootstrapper_Dialog_PromptChannelChange,
                        DisplayChannel, App.Settings.Prop.Channel),
                        MessageBoxImage.Question,
                        MessageBoxButton.YesNo
                        );

                        if (Result == MessageBoxResult.Yes)
                            EnrollChannel(EnrolledChannel);
                        break;
                    case ChannelChangeMode.Ignore:
                        App.Logger.WriteLine(LOG_IDENT, "Ignoring channel enrollment");
                        break;
                }
            }
            else
            {
                string ChannelFlagData = App.LaunchSettings.ChannelFlag.Data!;

                if (!String.IsNullOrEmpty(ChannelFlagData))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Forcing channel {ChannelFlagData}");
                    EnrollChannel(ChannelFlagData);
                }
            }

            if (!App.LaunchSettings.VersionFlag.Active)
            {
                ClientVersion clientVersion;

                try
                {
                    clientVersion = await Deployment.GetInfo(Deployment.Channel, behindProductionCheck);
                }
                catch (InvalidChannelException ex)
                {
                    // copied from v2.5.4
                    // we are keeping similar logic just updated for newer apis

                    // If channel does not exist
                    if (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Reverting enrolled channel to {Deployment.DefaultChannel} because a WindowsPlayer build does not exist for {App.Settings.Prop.Channel}");
                    }
                    // If channel is not available to the user (private/internal release channel)
                    else if (ex.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Reverting enrolled channel to {Deployment.DefaultChannel} because {App.Settings.Prop.Channel} is restricted for public use.");

                        // Only prompt if user has channel switching mode set to something other than Automatic.
                        if (App.Settings.Prop.ChannelChangeMode != ChannelChangeMode.Automatic)
                        {
                            Frontend.ShowMessageBox(
                                String.Format(
                                    Strings.Boostrapper_Dialog_UnauthorizedChannel,
                                    Deployment.Channel,
                                    Deployment.DefaultChannel
                                ),
                                MessageBoxImage.Information
                            );
                        }
                    }
                    else
                    {
                        throw;
                    }

                    RevertChannel();
                    clientVersion = await Deployment.GetInfo(Deployment.DefaultChannel, behindProductionCheck);
                }

                if (clientVersion.IsBehindDefaultChannel && App.Settings.Prop.ChannelChangeMode == ChannelChangeMode.Prompt)
                {
                    MessageBoxResult action = Frontend.ShowMessageBox(
                            String.Format(Strings.Bootstrapper_Dialog_ChannelOutOfDate, Deployment.Channel, Deployment.DefaultChannel),
                            MessageBoxImage.Warning,
                            MessageBoxButton.YesNo
                        );

                    if (action == MessageBoxResult.Yes)
                    {
                        App.Logger.WriteLine("Bootstrapper::CheckLatestVersion", $"Changed Roblox channel from {App.Settings.Prop.Channel} to {Deployment.DefaultChannel}");

                        RevertChannel();
                        clientVersion = await Deployment.GetInfo(Deployment.DefaultChannel);
                    }
                }

                key.SetValueSafe("www.roblox.com", Deployment.IsDefaultChannel ? "" : Deployment.Channel);

                _latestVersionGuid = clientVersion.VersionGuid;
                _latestVersion = Utilities.ParseVersionSafe(clientVersion.Version);

                // if UsePreviousVersion is on, fetch and install the version before the current live one.
                if (App.Settings.Prop.UsePreviousVersion)
                {
                    string? prevGuid = await FetchPreviousVersionGuidAsync(clientVersion.VersionGuid);
                    if (!string.IsNullOrEmpty(prevGuid))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"UsePreviousVersion: overriding version guid from {_latestVersionGuid} to {prevGuid}");
                        _latestVersionGuid = prevGuid;
                    }
                    else if (prevGuid is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "UsePreviousVersion: could not fetch previous version guid; falling back to latest.");
                        Frontend.ShowMessageBox(Strings.Menu_Errors_RobloxVersionFetchFailed_Message, MessageBoxImage.Warning, MessageBoxButton.OK);
                    }
                }
                // if SkipRobloxUpgrades is on (and NOT UsePreviousVersion), keep whatever is already installed.
                else if (App.Settings.Prop.SkipRobloxUpgrades && !string.IsNullOrEmpty(AppData.State.VersionGuid))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"StopRobloxUpdates: keeping current version {AppData.State.VersionGuid}");
                    _latestVersionGuid = AppData.State.VersionGuid;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(App.LaunchSettings.VersionFlag.Data))
                    throw new InvalidOperationException(
                        "VersionFlag.Active is true but VersionFlag.Data is null or empty"
                    );

                App.Logger.WriteLine(
                    LOG_IDENT,
                    $"Version set to {App.LaunchSettings.VersionFlag.Data} from arguments"
                );

                _latestVersionGuid = App.LaunchSettings.VersionFlag.Data;
            }

            if (_staticDirectory)
            {
                _latestVersionDirectory = AppData.StaticDirectory;
            }
            else
            {
                if (string.IsNullOrEmpty(_latestVersionGuid))
                    throw new InvalidOperationException("_latestVersionGuid is null or empty when building version directory");

                _latestVersionDirectory = Path.Combine(Paths.Versions, _latestVersionGuid);
            }

            string pkgManifestUrl = Deployment.GetLocation($"/{_latestVersionGuid}-rbxPkgManifest.txt");
            var pkgManifestData = await App.HttpClient.GetStringAsync(pkgManifestUrl);

            _versionPackageManifest = new(pkgManifestData);

            // this can happen if version is set through arguments
            if (_launchMode == LaunchMode.Unknown)
            {
                App.Logger.WriteLine(LOG_IDENT, "Identifying launch mode from package manifest");

                bool isPlayer = _versionPackageManifest.Exists(x => x.Name == "RobloxApp.zip");
                App.Logger.WriteLine(LOG_IDENT, $"isPlayer: {isPlayer}");

                _launchMode = isPlayer ? LaunchMode.Player : LaunchMode.Studio;
                SetupAppData(); // we need to set it up again
            }
        }

        private bool IsEligibleForBackgroundUpdate()
        {
            const string LOG_IDENT = "Bootstrapper::IsEligibleForBackgroundUpdate";

            if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Is the background updater process");
                return false;
            }

            if (!App.Settings.Prop.BackgroundUpdatesEnabled)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Background updates disabled");
                return false;
            }

            if (IsStudioLaunch)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Studio launch");
                return false;
            }

            if (_mustUpgrade)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Must upgrade is true");
                return false;
            }

            // at least 5GB of free space
            const long minimumFreeSpace = 5_000_000_000;
            long space = Filesystem.GetFreeDiskSpace(Paths.Base);
            if (space < minimumFreeSpace)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Not eligible: User has {space} free space, at least {minimumFreeSpace} is required");
                return false;
            }

            if (_latestVersion == default)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Latest version is undefined");
                return false;
            }

            Version? currentVersion = Utilities.GetRobloxVersion(AppData);
            if (currentVersion == default)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Current version is undefined");
                return false;
            }

            // always normally upgrade for downgrades
            if (currentVersion.Minor > _latestVersion.Minor)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Downgrade");
                return false;
            }

            // only background update if we're:
            // - one major update behind
            // - the same major update
            int diff = _latestVersion.Minor - currentVersion.Minor;
            if (diff == 0 || diff == 1)
            {
                App.Logger.WriteLine(LOG_IDENT, "Eligible");
                return true;
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"Not eligible: Major version diff is {diff}");
                return false;
            }
        }

        private static void LaunchMultiInstanceWatcher()
        {
            const string LOG_IDENT = "Bootstrapper::LaunchMultiInstanceWatcher";

            if (Utilities.DoesMutexExist("ROBLOX_singletonMutex"))
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox singleton mutex already exists");
                return;
            }

            using EventWaitHandle initEventHandle = new EventWaitHandle(false, EventResetMode.AutoReset, "Bloxstrap-MultiInstanceWatcherInitialisationFinished");
            Process.Start(Paths.Process, "-multiinstancewatcher");

            bool initSuccess = initEventHandle.WaitOne(TimeSpan.FromSeconds(2));
            if (initSuccess)
                App.Logger.WriteLine(LOG_IDENT, "Initialisation finished signalled, continuing.");
            else
                App.Logger.WriteLine(LOG_IDENT, "Did not receive the initialisation finished signal, continuing.");
        }

        private void TerminateCrashHandler()
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName("RobloxCrashHandler");
                foreach (var process in processes)
                {
                    App.Logger.WriteLine("Bootstrapper::TerminateCrashHandler", $"Found Crash Handler (PID {process.Id}), terminating...");
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Bootstrapper::TerminateCrashHandler", "Failed to terminate crash handler.");
                App.Logger.WriteException("Bootstrapper::TerminateCrashHandler", ex);
            }
        }

        private async void StartRoblox()
        {
            const string LOG_IDENT = "Bootstrapper::StartRoblox";

            string appStoragePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Roblox\LocalStorage\appStorage.json");

            if (File.Exists(appStoragePath))
            {
                try
                {
                    string json = File.ReadAllText(appStoragePath);

                    var root = JsonNode.Parse(json)?.AsObject();
                    if (root is null)
                        throw new Exception("Failed to parse appStorage.json as a JSON object.");

                    string trayEnabledValue = App.Settings.Prop.MinimizeToTray ? "true" : "false";
                    string modalEnabledValue = App.Settings.Prop.EnableTrayModal ? "true" : "false";
                    string startupValue = App.Settings.Prop.LaunchOnStartup.ToString().ToLower();
                    string themeValue = App.Settings.Prop.RobloxTheme.ToLower(); // "light" or "dark"

                    root["SystemTrayModalShown"] = modalEnabledValue;
                    root["MinimizeToTray"] = trayEnabledValue;
                    root["LaunchAtStartup"] = startupValue;

                    if (root.ContainsKey("DeviceLevelTheme"))
                    {
                        string innerJson = root["DeviceLevelTheme"]!.GetValue<string>();
                        var innerObj = JsonNode.Parse(innerJson)?.AsObject();
                        if (innerObj is not null)
                        {
                            foreach (var key in innerObj.Select(k => k.Key).ToList())
                                innerObj[key] = themeValue;

                            root["DeviceLevelTheme"] = innerObj.ToJsonString();
                        }
                    }

                    string newJson = root.ToJsonString();
                    if (newJson != json)
                    {
                        File.WriteAllText(appStoragePath, newJson);
                        App.Logger.WriteLine(LOG_IDENT, $"Patched appStorage.json (Tray: {trayEnabledValue}, Modal: {modalEnabledValue}, Theme: {themeValue})");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Warning: Could not patch appStorage.json: {ex.Message}");
                }
            }

            SetStatus(Strings.Bootstrapper_Status_Starting);

            if (_launchMode == LaunchMode.Player)
            {
                // this needs to be done before roblox launches
                if (App.Settings.Prop.MultiInstanceLaunching)
                    LaunchMultiInstanceWatcher();

            }

            if (!File.Exists(AppData.ExecutablePath))
            {
                await UpgradeRoblox();
                return;
            }

            var startInfo = new ProcessStartInfo()
            {
                FileName = AppData.ExecutablePath,
                Arguments = _launchCommandLine,
                WorkingDirectory = AppData.Directory
            };

            if (_launchMode == LaunchMode.Player && ShouldRunAsAdmin())
            {
                startInfo.Verb = "runas";
                startInfo.UseShellExecute = true;
            }
            else if (_launchMode == LaunchMode.StudioAuth)
            {
                Process.Start(startInfo);
                return;
            }

            string? logFileName = null;

            string rbxDir = Path.Combine(Paths.LocalAppData, "Roblox");
            if (!Directory.Exists(rbxDir))
                Directory.CreateDirectory(rbxDir);

            string rbxLogDir = Path.Combine(rbxDir, "logs");
            if (!Directory.Exists(rbxLogDir))
                Directory.CreateDirectory(rbxLogDir);

            var logWatcher = new FileSystemWatcher()
            {
                Path = rbxLogDir,
                Filter = "*.log",
                EnableRaisingEvents = true
            };

            var logCreatedEvent = new AutoResetEvent(false);

            logWatcher.Created += (_, e) =>
            {
                logWatcher.EnableRaisingEvents = false;
                logFileName = e.FullPath;
                logCreatedEvent.Set();
            };

            var autoclosePids = new List<int>();

            // the code you're gonna read ahead is horrible. sorry for the hack, but it works ¯\_(ツ)_/¯
            // check if prelaunch is checked
            foreach (var integration in App.Settings.Prop.CustomIntegrations)
            {
                if (integration?.PreLaunch == true)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Pre-Launching custom integration '{integration.Name}' ({integration.Location} {integration.LaunchArgs} - autoclose is {integration.AutoClose})");
                    int pid = 0;
                    try
                    {
                        var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = integration.Location,
                            Arguments = integration.LaunchArgs.Replace("\r\n", " "),
                            WorkingDirectory = Path.GetDirectoryName(integration.Location),
                            UseShellExecute = true
                        })!;
                        pid = process.Id;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to pre-launch integration '{integration.Name}'!");
                        App.Logger.WriteLine(LOG_IDENT, ex.Message);
                    }

                    if (integration?.AutoClose == true && pid != 0)
                        autoclosePids.Add(pid);

                    if (integration?.Delay != null)
                        Thread.Sleep(integration.Delay);
                }
            }

            // v2.2.0 - byfron will trip if we keep a process handle open for over a minute, so we're doing this now
            try
            {
                using var process = Process.Start(startInfo)!;
                _appPid = process.Id;

                if (App.Settings.Prop.AutoCloseCrashHandler)
                {
                    _ = Task.Run(async () =>
                    {
                        while (!_cancelTokenSource.IsCancellationRequested)
                        {
                            foreach (var p in Process.GetProcessesByName("RobloxCrashHandler"))
                            {
                                try { p.Kill(); }
                                catch { }
                                finally { p.Dispose(); }
                            }
                            await Task.Delay(1000);
                        }
                    });
                }
            }


            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // 1223 = ERROR_CANCELLED, gets thrown if a UAC prompt is cancelled
                return;
            }
            catch (Exception)
            {
                // attempt a reinstall on next launch
                File.Delete(AppData.ExecutablePath);
                throw;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Started Roblox (PID {_appPid}), waiting for log file");

            logCreatedEvent.WaitOne(TimeSpan.FromSeconds(15));

            if (String.IsNullOrEmpty(logFileName))
            {
                App.Logger.WriteLine(LOG_IDENT, "Unable to identify log file");
                // Frontend.ShowPlayerErrorDialog();
                return;
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"Got log file as {logFileName}");
            }

            _mutex?.ReleaseAsync();

            if (IsStudioLaunch)
                return;

            // lord.... forgive me for this hack.....
            // launch custom integrations now
            foreach (var integration in App.Settings.Prop.CustomIntegrations)
            {
                if (integration == null)
                    continue;

                if (integration?.PreLaunch == true)
                    continue; // skip pre-launch integrations

                App.Logger.WriteLine(LOG_IDENT, $"Launching custom integration '{integration!.Name}' ({integration.Location} {integration?.LaunchArgs} - autoclose is {integration!.AutoClose})");

                int pid = 0;

                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = integration.Location,
                        Arguments = integration.LaunchArgs.Replace("\r\n", " "),
                        WorkingDirectory = Path.GetDirectoryName(integration.Location),
                        UseShellExecute = true
                    })!;

                    pid = process.Id;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to launch integration '{integration.Name}'!");
                    App.Logger.WriteLine(LOG_IDENT, ex.Message);
                }

                if (integration.AutoClose && pid != 0)
                    autoclosePids.Add(pid);
            }

            if (App.Settings.Prop.EnableActivityTracking || App.LaunchSettings.TestModeFlag.Active || autoclosePids.Any())
            {
                using var ipl = new InterProcessLock("Watcher", TimeSpan.FromSeconds(5));

                var watcherData = new WatcherData
                {
                    ProcessId = _appPid,
                    LogFile = logFileName,
                    AutoclosePids = autoclosePids
                };

                string watcherDataArg = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(watcherData)));

                string args = $"-watcher \"{watcherDataArg}\"";

                if (App.LaunchSettings.TestModeFlag.Active)
                    args += " -testmode";

                if (ipl.IsAcquired)
                    Process.Start(Paths.Process, args);
            }

            // allow for window to show, since the log is created pretty far beforehand
        }

        private bool ShouldRunAsAdmin()
        {
            foreach (var root in WindowsRegistry.Roots)
            {
                using var key = root.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");

                if (key is null)
                    continue;

                string? flags = (string?)key.GetValue(AppData.ExecutablePath);

                if (flags is not null && flags.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public void Cancel()
        {
            const string LOG_IDENT = "Bootstrapper::Cancel";

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            App.Logger.WriteLine(LOG_IDENT, "Cancelling launch...");

            _cancelTokenSource.Cancel();

            if (Dialog is not null)
                Dialog.CancelEnabled = false;

            if (_isInstalling)
            {
                try
                {
                    // clean up registry keys
                    WindowsRegistry.RegisterClientLocation(IsStudioLaunch, null);

                    // clean up install
                    if (Directory.Exists(_latestVersionDirectory))
                        Directory.Delete(_latestVersionDirectory, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not fully clean up installation!");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
            else if (_appPid != 0)
            {
                try
                {
                    using var process = Process.GetProcessById(_appPid);
                    process.Kill();
                }
                catch (Exception) { }
            }

            Dialog?.CloseBootstrapper();

            App.SoftTerminate(ErrorCode.ERROR_CANCELLED);
        }
        #endregion

        #region App Install
        private async Task<bool> CheckForUpdates()
        {
            const string LOG_IDENT = "Bootstrapper::CheckForUpdates";

            // don't update if there's another instance running (likely running in the background)
            // i don't like this, but there isn't much better way of doing it /shrug
            if (Process.GetProcessesByName(App.ProjectName).Length > 1)
            {
                App.Logger.WriteLine(LOG_IDENT, $"More than one Bubblestrap instance running, aborting update check");
                return false;
            }

            App.Logger.WriteLine(LOG_IDENT, "Checking for updates...");

#if !DEBUG_UPDATER
            var releaseInfo = await App.GetLatestRelease();

            if (releaseInfo is null)
                return false;

            var versionComparison = Utilities.CompareVersions(App.Version, releaseInfo.TagName);

            // check if we aren't using a deployed build, so we can update to one if a new version comes out
            if (App.IsProductionBuild && versionComparison == VersionComparison.Equal || versionComparison == VersionComparison.GreaterThan)
            {
                App.Logger.WriteLine(LOG_IDENT, "No updates found");
                return false;
            }

            if (Dialog is not null)
                Dialog.CancelEnabled = false;

            string version = releaseInfo.TagName;
#else
            string version = App.Version;
#endif

            SetStatus(Strings.Bootstrapper_Status_UpgradingBloxstrap);

            try
            {
#if DEBUG_UPDATER
                string downloadLocation = Path.Combine(Paths.TempUpdates, "Bubblestrap.exe");

                Directory.CreateDirectory(Paths.TempUpdates);

                File.Copy(Paths.Process, downloadLocation, true);
#else
                var asset = releaseInfo.Assets!.FirstOrDefault(a => a.Name.EndsWith(".exe"));

                if (asset is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "No .exe asset found in release");
                    return false;
                }

                string downloadLocation = Path.Combine(Paths.TempUpdates, asset.Name);

                Directory.CreateDirectory(Paths.TempUpdates);

                App.Logger.WriteLine(LOG_IDENT, $"Downloading {releaseInfo.TagName}...");

                if (!File.Exists(downloadLocation))
                {
                    var response = await App.HttpClient.GetAsync(asset.BrowserDownloadUrl);

                    await using var fileStream = new FileStream(downloadLocation, FileMode.OpenOrCreate, FileAccess.Write);
                    await response.Content.CopyToAsync(fileStream);
                }
#endif

                App.Logger.WriteLine(LOG_IDENT, $"Starting {version}...");

                ProcessStartInfo startInfo = new()
                {
                    FileName = downloadLocation,
                };

                startInfo.ArgumentList.Add("-upgrade");

                foreach (string arg in App.LaunchSettings.Args)
                    startInfo.ArgumentList.Add(arg);

                if (_launchMode == LaunchMode.Player && !startInfo.ArgumentList.Contains("-player"))
                    startInfo.ArgumentList.Add("-player");
                else if (_launchMode == LaunchMode.Studio && !startInfo.ArgumentList.Contains("-studio"))
                    startInfo.ArgumentList.Add("-studio");

                // settings already saved before concurrent tasks in Run() — no save needed here

                using var ipl = new InterProcessLock("AutoUpdater");

                Process.Start(startInfo);

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the auto-updater");
                App.Logger.WriteException(LOG_IDENT, ex);

                Frontend.ShowMessageBox(
                    string.Format(Strings.Bootstrapper_AutoUpdateFailed, version),
                    MessageBoxImage.Information
                );

                Utilities.ShellExecute(App.ProjectDownloadLink);
            }

            return false;
        }
        #endregion

        #region Roblox Install

        private static bool TryDeleteRobloxInDirectory(string dir)
        {
            string clientPath = Path.Combine(dir, "RobloxPlayerBeta.exe");
            if (!File.Exists(clientPath))
            {
                clientPath = Path.Combine(dir, "RobloxStudioBeta.exe");
                if (!File.Exists(clientPath))
                    return true;
            }

            try
            {
                File.Delete(clientPath);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Bootstrapper::TryDeleteRoblox", $"Failed to delete {clientPath}: {ex.Message}");
                return false;
            }
        }

        public static void CleanupVersionsFolder()
        {
            const string LOG_IDENT = "Bootstrapper::CleanupVersionsFolder";

            if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Background updater tried to cleanup, stopping!");
                return;
            }

            if (!Directory.Exists(Paths.Versions))
            {
                App.Logger.WriteLine(LOG_IDENT, "Versions directory does not exist, skipping cleanup.");
                return;
            }

            string playerVersion = App.RobloxState.Prop.Player.VersionGuid;
            string studioVersion = App.RobloxState.Prop.Studio.VersionGuid;

            foreach (string dir in Directory.EnumerateDirectories(Paths.Versions))
            {
                string dirName = Path.GetFileName(dir);
                bool shouldDelete;

                if (!_staticDirectory)
                {
                    shouldDelete = dirName != playerVersion && dirName != studioVersion;
                }
                else
                {
                    shouldDelete = dirName != "WindowsPlayer" && dirName != "WindowsStudio64";
                }

                if (!shouldDelete)
                    continue;

                // check if it's still being used first
                if (!TryDeleteRobloxInDirectory(dir))
                    continue;

                SafeDeleteDirectory(dir, LOG_IDENT);
            }
        }

        private static void SafeDeleteDirectory(string path, string logIdent)
        {
            try
            {
                Directory.Delete(path, true);
                App.Logger.WriteLine(logIdent, $"Deleted directory: {path}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(logIdent, $"Failed to delete {path}");
                App.Logger.WriteException(logIdent, ex);
            }
        }

        private void MigrateCompatibilityFlags()
        {
            const string LOG_IDENT = "Bootstrapper::MigrateCompatibilityFlags";

            string oldClientLocation = Path.Combine(Paths.Versions, AppData.State.VersionGuid, AppData.ExecutableName);
            string newClientLocation = Path.Combine(_latestVersionDirectory, AppData.ExecutableName);

            // move old compatibility flags for the old location
            using RegistryKey appFlagsKey = Registry.CurrentUser.CreateSubKey($"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");
            string? appFlags = appFlagsKey.GetValue(oldClientLocation) as string;

            if (appFlags is not null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Migrating app compatibility flags from {oldClientLocation} to {newClientLocation}...");
                appFlagsKey.SetValueSafe(newClientLocation, appFlags);
                appFlagsKey.DeleteValueSafe(oldClientLocation);
            }
        }
        private static void KillRobloxPlayers()
        {
            const string LOG_IDENT = "Bootstrapper::KillRobloxPlayers";

            List<Process> processes = new List<Process>();
            processes.AddRange(Process.GetProcessesByName("RobloxPlayerBeta"));
            processes.AddRange(Process.GetProcessesByName("RobloxCrashHandler")); // roblox studio doesnt depend on crash handler being open, so this should be fine

            foreach (Process process in processes)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to close process {process.Id}");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
        }

        private async Task WaitForStudioProcesses(CancellationToken cancellationToken = default)
        {
            const string LOG_IDENT = "Bootstrapper::WaitForStudioProcesses";

            var studioProcessNames = new[] { "RobloxStudioBeta", "RobloxStudioLauncherBeta" };

            var processes = studioProcessNames
                .SelectMany(Process.GetProcessesByName)
                .ToList();

            if (processes.Count == 0)
                return;

            App.Logger.WriteLine(LOG_IDENT, $"Waiting for {processes.Count} studio process(es) to close before updating...");
            SetStatus(Strings.Bootstrapper_Status_WaitingOtherInstances);

            var waitTasks = processes.Select(p => p.WaitForExitAsync(cancellationToken));

            try
            {
                await Task.WhenAll(waitTasks);
                App.Logger.WriteLine(LOG_IDENT, "All studio processes have closed, proceeding with update.");
            }
            catch (OperationCanceledException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Wait cancelled.");
            }
        }


        private static async Task<string?> FetchPreviousVersionGuidAsync(string currentVersionGuid)
        {
            const string LOG_IDENT = "Bootstrapper::FetchPreviousVersionGuidAsync";
            const string VersionHistoryUrl = "https://setup-rbxcdn.github.io/version-history/Windows/WindowsPlayer.json";

            try
            {
                string json = await App.HttpClient.GetStringAsync(VersionHistoryUrl);

                // json is an ordered object: { "version_string": "version-guid"}
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? prevGuid = null;
                foreach (var prop in root.EnumerateObject())
                {
                    string guid = prop.Value.GetString() ?? string.Empty;
                    if (guid == currentVersionGuid)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Found current version '{currentVersionGuid}' in history; previous is '{prevGuid}'");
                        break;
                    }
                    prevGuid = guid;
                }

                if (prevGuid == null)
                {
                    var last = root.EnumerateObject().LastOrDefault();
                    prevGuid = last.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? last.Value.GetString()
                        : null;
                    App.Logger.WriteLine(LOG_IDENT, $"Current version '{currentVersionGuid}' not found in history; using last known: '{prevGuid}'");
                }

                // refuse versions older than 7 days.
                if (!string.IsNullOrEmpty(prevGuid))
                {
                    try
                    {
                        string manifestUrl = Deployment.GetLocation($"/{prevGuid}-rbxPkgManifest.txt");
                        using var req = new HttpRequestMessage(HttpMethod.Head, manifestUrl);
                        using var resp = await App.HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                        if (resp.Content.Headers.LastModified is DateTimeOffset lastModified)
                        {
                            TimeSpan age = DateTimeOffset.UtcNow - lastModified;
                            App.Logger.WriteLine(LOG_IDENT, $"Previous version '{prevGuid}' age: {age.TotalDays:F1} days");
                            if (age.TotalDays > 14)
                            {
                                Frontend.ShowMessageBox(string.Format(Strings.Menu_Errors_RobloxVersionTooOld_Message, prevGuid), MessageBoxImage.Warning, MessageBoxButton.OK);
                                App.Settings.Prop.UsePreviousVersion = false;
                                App.Settings.Save();
                                return string.Empty;
                            }
                        }
                    }
                    catch (Exception ageEx)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Could not determine age of previous version: {ageEx.Message}");
                        // non-fatal: proceed with the version
                    }
                }

                return prevGuid;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to fetch version history: {ex.Message}");
                return null;
            }
        }


        private async Task UpgradeRoblox()
        {
            const string LOG_IDENT = "Bootstrapper::UpgradeRoblox";

            bool CancelUpgrade = !App.Settings.Prop.UpdateRoblox;

            if (CancelUpgrade)
            {
                SetStatus(Strings.Bootstrapper_Status_CancelUpgrade);
                App.Logger.WriteLine(LOG_IDENT, "Upgrading disabled, cancelling the upgrade.");
                Thread.Sleep(2000);
            }

            if (CancelUpgrade && !Directory.Exists(_latestVersionDirectory))
            {
                Frontend.ShowMessageBox(Strings.Bootstrapper_Dialog_NoUpgradeWithoutClient, MessageBoxImage.Warning, MessageBoxButton.OK);
            }
            else if (CancelUpgrade)
            {
                return;
            }

            if (String.IsNullOrEmpty(AppData.State.VersionGuid))
                SetStatus(Strings.Bootstrapper_Status_Installing);
            else
                SetStatus(Strings.Bootstrapper_Status_Upgrading);

            Directory.CreateDirectory(Paths.Base);
            Directory.CreateDirectory(Paths.Downloads);
            Directory.CreateDirectory(Paths.Versions);

            _isInstalling = true;

            // make sure nothing is running before continuing upgrade   
            if (!App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                if (IsStudioLaunch)
                    await WaitForStudioProcesses(_cancelTokenSource.Token);
                else
                    KillRobloxPlayers();
            }

            // get a fully clean install
            if (!App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                if (App.State.Prop.ForceReinstall && !App.Settings.Prop.UsePreviousVersion && Directory.Exists(Paths.Versions))
                {
                    App.Logger.WriteLine(LOG_IDENT, "ForceReinstall: removing all version directories for a fresh install");
                    foreach (string vdir in Directory.EnumerateDirectories(Paths.Versions))
                    {
                        try { Directory.Delete(vdir, true); }
                        catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
                    }
                }
                else if (Directory.Exists(_latestVersionDirectory))
                {
                    try
                    {
                        Directory.Delete(_latestVersionDirectory, true);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to delete the latest version directory");
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }
            }

            Directory.CreateDirectory(_latestVersionDirectory);

            var cachedPackageHashes = Directory.GetFiles(Paths.Downloads).Select(x => Path.GetFileName(x)).ToHashSet();

            // package manifest states packed size and uncompressed size in exact bytes
            int totalSizeRequired = 0;

            // packed size only matters if we don't already have the package cached on disk
            totalSizeRequired += _versionPackageManifest.Where(x => !cachedPackageHashes.Contains(x.Signature)).Sum(x => x.PackedSize);
            totalSizeRequired += _versionPackageManifest.Sum(x => x.Size);

            if (Filesystem.GetFreeDiskSpace(Paths.Base) < totalSizeRequired)
            {
                Frontend.ShowMessageBox(Strings.Bootstrapper_NotEnoughSpace, MessageBoxImage.Error);
                App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
                return;
            }

            if (Dialog is not null)
            {
                Dialog.ProgressStyle = ProgressBarStyle.Continuous;
                Dialog.TaskbarProgressState = TaskbarItemProgressState.Normal;

                Dialog.ProgressMaximum = ProgressBarMaximum;

                // compute total bytes to download
                int totalPackedSize = _versionPackageManifest
                    .Where(x => !cachedPackageHashes.Contains(x.Signature))
                    .Sum(package => package.PackedSize);

                if (totalPackedSize > 0)
                {
                    _progressIncrement = (double)ProgressBarMaximum / totalPackedSize;
                    _taskbarProgressIncrement = _taskbarProgressMaximum / (double)totalPackedSize;
                }

                if (Dialog is WinFormsDialogBase)
                    _taskbarProgressMaximum = (double)TaskbarProgressMaximumWinForms;
                else
                    _taskbarProgressMaximum = (double)TaskbarProgressMaximumWpf;

            }

            using var downloadSemaphore = new SemaphoreSlim(3);
            var packageTasks = new List<Task>();

            foreach (var package in _versionPackageManifest)
            {
                if (_cancelTokenSource.IsCancellationRequested)
                    return;

                // check if the package should be ignored
                if (App.LocalData.Prop.IgnoredPackages.Contains(package.Name))
                    continue;

                var pkg = package; // capture for lambda
                packageTasks.Add(Task.Run(async () =>
                {
                    await downloadSemaphore.WaitAsync(_cancelTokenSource.Token);
                    try
                    {
                        await DownloadPackage(pkg);
                    }
                    finally
                    {
                        downloadSemaphore.Release();
                    }

                    // we'll extract the runtime installer later if we need to
                    if (pkg.Name == "WebView2RuntimeInstaller.zip")
                        return;

                    // extract immediately after this package finishes downloading
                    ExtractPackage(pkg);
                }, _cancelTokenSource.Token));
            }

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            if (Dialog is not null)
            {
                Dialog.ProgressStyle = ProgressBarStyle.Marquee;
                Dialog.TaskbarProgressState = TaskbarItemProgressState.Indeterminate;
                SetStatus(Strings.Bootstrapper_Status_Configuring);
            }

            await Task.WhenAll(packageTasks);

            App.Logger.WriteLine(LOG_IDENT, "Writing AppSettings.xml...");
            await File.WriteAllTextAsync(Path.Combine(_latestVersionDirectory, "AppSettings.xml"), AppSettings);

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            if (App.State.Prop.PromptWebView2Install)
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                using var hkcuKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");

                if (hklmKey is not null || hkcuKey is not null)
                {
                    // reset prompt state if the user has it installed
                    App.State.Prop.PromptWebView2Install = false;
                }
                else
                {
                    var result = Frontend.ShowMessageBox(Strings.Bootstrapper_WebView2NotFound, MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.Yes);

                    if (result != MessageBoxResult.Yes)
                    {
                        App.State.Prop.PromptWebView2Install = false;
                    }
                    else
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Installing WebView2 runtime...");

                        var package = _versionPackageManifest.Find(x => x.Name == "WebView2RuntimeInstaller.zip");

                        if (package is null)
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Aborted runtime install because package does not exist, has WebView2 been added in this Roblox version yet?");
                            return;
                        }

                        string baseDirectory = Path.Combine(_latestVersionDirectory, PackageDirectoryMap[package.Name]);

                        ExtractPackage(package);

                        SetStatus(Strings.Bootstrapper_Status_InstallingWebView2);

                        var startInfo = new ProcessStartInfo()
                        {
                            WorkingDirectory = baseDirectory,
                            FileName = Path.Combine(baseDirectory, "MicrosoftEdgeWebview2Setup.exe"),
                            Arguments = "/silent /install"
                        };

                        await Process.Start(startInfo)!.WaitForExitAsync();

                        App.Logger.WriteLine(LOG_IDENT, "Finished installing runtime");

                        Directory.Delete(baseDirectory, true);
                    }
                }
            }

            // finishing and cleanup

            MigrateCompatibilityFlags();

            AppData.State.VersionGuid = _latestVersionGuid;

            AppData.State.PackageHashes.Clear();

            foreach (var package in _versionPackageManifest)
                AppData.State.PackageHashes.Add(package.Name, package.Signature);

            CleanupVersionsFolder();

            var allPackageHashes = new HashSet<string>(App.RobloxState.Prop.Player.PackageHashes.Values);
            allPackageHashes.UnionWith(App.RobloxState.Prop.Studio.PackageHashes.Values);

            if (!App.Settings.Prop.DebugDisableVersionPackageCleanup)
            {
                foreach (string hash in cachedPackageHashes)
                {
                    if (!allPackageHashes.Contains(hash))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Deleting unused package {hash}");

                        try
                        {
                            File.Delete(Path.Combine(Paths.Downloads, hash));
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Failed to delete {hash}!");
                            App.Logger.WriteException(LOG_IDENT, ex);
                        }
                    }
                }
            }

            App.Logger.WriteLine(LOG_IDENT, "Registering approximate program size...");

            int distributionSize = _versionPackageManifest.Sum(x => x.Size + x.PackedSize) / 1024;

            AppData.State.Size = distributionSize;

            int totalSize = App.RobloxState.Prop.Player.Size + App.RobloxState.Prop.Studio.Size;

            using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                uninstallKey.SetValueSafe("EstimatedSize", totalSize);
            }

            WindowsRegistry.RegisterClientLocation(IsStudioLaunch, _latestVersionDirectory);

            App.Logger.WriteLine(LOG_IDENT, $"Registered as {totalSize} KB");

            App.State.Prop.ForceReinstall = false;

            App.State.Save();
            App.RobloxState.Save();

            _isInstalling = false;
        }

        private static void StartBackgroundUpdater()
        {
            const string LOG_IDENT = "Bootstrapper::StartBackgroundUpdater";

            if (Utilities.DoesMutexExist("Bloxstrap-BackgroundUpdater"))
            {
                App.Logger.WriteLine(LOG_IDENT, "Background updater already running");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, "Starting background updater");

            Process.Start(Paths.Process, "-backgroundupdater");
        }

        private async Task<bool> ApplyModifications()
        {
            const string LOG_IDENT = "Bootstrapper::ApplyModifications";

            bool success = true;

            SetStatus(Strings.Bootstrapper_Status_ApplyingModifications);

            // handle file mods
            App.Logger.WriteLine(LOG_IDENT, "Checking file mods...");

            List<string> modFolderFiles = new();

            Directory.CreateDirectory(Paths.Modifications);

            // check custom font mod
            // instead of replacing the fonts themselves, we'll just alter the font family manifests

            string modFontFamiliesFolder = Path.Combine(Paths.Modifications, "content\\fonts\\families");

            if (File.Exists(Paths.CustomFont))
            {
                App.Logger.WriteLine(LOG_IDENT, "Begin font check");

                Directory.CreateDirectory(modFontFamiliesFolder);

                const string path = "rbxasset://fonts/CustomFont.ttf";

                // lets make sure the content/fonts/families path exists in the version directory
                string contentFolder = Path.Combine(_latestVersionDirectory, "content");
                Directory.CreateDirectory(contentFolder);

                string fontsFolder = Path.Combine(contentFolder, "fonts");
                Directory.CreateDirectory(fontsFolder);

                string familiesFolder = Path.Combine(fontsFolder, "families");
                Directory.CreateDirectory(familiesFolder);

                foreach (string jsonFilePath in Directory.GetFiles(familiesFolder))
                {
                    string jsonFilename = Path.GetFileName(jsonFilePath);
                    string modFilepath = Path.Combine(modFontFamiliesFolder, jsonFilename);

                    if (File.Exists(modFilepath))
                        continue;

                    App.Logger.WriteLine(LOG_IDENT, $"Setting font for {jsonFilename}");

                    var fontFamilyData = JsonSerializer.Deserialize<FontFamily>(File.ReadAllText(jsonFilePath));

                    if (fontFamilyData is null)
                        continue;

                    bool shouldWrite = false;

                    foreach (var fontFace in fontFamilyData.Faces)
                    {
                        if (fontFace.AssetId != path)
                        {
                            fontFace.AssetId = path;
                            shouldWrite = true;
                        }
                    }

                    if (shouldWrite)
                        File.WriteAllText(modFilepath, JsonSerializer.Serialize(fontFamilyData, new JsonSerializerOptions { WriteIndented = true }));
                }

                App.Logger.WriteLine(LOG_IDENT, "End font check");
            }
            else if (Directory.Exists(modFontFamiliesFolder))
            {
                Directory.Delete(modFontFamiliesFolder, true);
            }

            foreach (string file in Directory.GetFiles(Paths.Modifications, "*.*", SearchOption.AllDirectories))
            {
                if (_cancelTokenSource.IsCancellationRequested)
                    return true;

                string relativeFile = file.Substring(Paths.Modifications.Length + 1);

                if (!App.Settings.Prop.UseFastFlagManager && String.Equals(relativeFile, "ClientSettings\\ClientAppSettings.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (relativeFile.EndsWith(".lock"))
                    continue;

                string fileModFolder = Path.Combine(Paths.Modifications, relativeFile);
                string fileVersionFolder = Path.Combine(_latestVersionDirectory, relativeFile);

                if (File.Exists(fileVersionFolder))
                {
                    var modInfo = new FileInfo(fileModFolder);
                    var versionInfo = new FileInfo(fileVersionFolder);

                    if (modInfo.Length == versionInfo.Length && modInfo.LastWriteTimeUtc == versionInfo.LastWriteTimeUtc)
                    {
                        modFolderFiles.Add(relativeFile);
                        continue;
                    }

                    if (MD5Hash.FromFile(fileModFolder) == MD5Hash.FromFile(fileVersionFolder))
                    {
                        File.SetLastWriteTimeUtc(fileVersionFolder, modInfo.LastWriteTimeUtc);
                        modFolderFiles.Add(relativeFile);
                        continue;
                    }
                }

                modFolderFiles.Add(relativeFile);
                Directory.CreateDirectory(Path.GetDirectoryName(fileVersionFolder)!);
                Filesystem.AssertReadOnly(fileVersionFolder);

                try
                {
                    File.Copy(fileModFolder, fileVersionFolder, true);
                    File.SetLastWriteTimeUtc(fileVersionFolder, File.GetLastWriteTimeUtc(fileModFolder));
                    Filesystem.AssertReadOnly(fileVersionFolder);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to apply modification ({relativeFile})");
                    App.Logger.WriteException(LOG_IDENT, ex);
                    success = false;
                }
            }

            // the manifest is primarily here to keep track of what files have been
            // deleted from the modifications folder, so that we know when to restore the original files from the downloaded packages
            // now check for files that have been deleted from the mod folder according to the manifest

            var fileRestoreMap = new Dictionary<string, List<string>>();

            foreach (string fileLocation in App.RobloxState.Prop.ModManifest)
            {
                if (modFolderFiles.Contains(fileLocation))
                    continue;

                var packageMapEntry = PackageDirectoryMap.SingleOrDefault(x => !String.IsNullOrEmpty(x.Value) && fileLocation.StartsWith(x.Value));
                string packageName = packageMapEntry.Key;

                // package doesn't exist, likely mistakenly placed file
                if (String.IsNullOrEmpty(packageName))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{fileLocation} was removed as a mod but does not belong to a package");

                    string versionFileLocation = Path.Combine(_latestVersionDirectory, fileLocation);

                    if (File.Exists(versionFileLocation))
                        File.Delete(versionFileLocation);

                    continue;
                }

                string fileName = fileLocation.Substring(packageMapEntry.Value.Length);

                if (!fileRestoreMap.ContainsKey(packageName))
                    fileRestoreMap[packageName] = new();

                fileRestoreMap[packageName].Add(fileName);

                App.Logger.WriteLine(LOG_IDENT, $"{fileLocation} was removed as a mod, restoring from {packageName}");
            }

            foreach (var entry in fileRestoreMap)
            {
                var package = _versionPackageManifest.Find(x => x.Name == entry.Key);

                if (package is not null)
                {
                    if (_cancelTokenSource.IsCancellationRequested)
                        return true;

                    await DownloadPackage(package);
                    ExtractPackage(package, entry.Value);
                }
            }

            // make sure we're not overwriting a new update
            // if we're the background update process, always overwrite
            if (App.LaunchSettings.BackgroundUpdaterFlag.Active || !App.RobloxState.HasFileOnDiskChanged())
            {
                App.RobloxState.Prop.ModManifest = modFolderFiles;
                App.RobloxState.Save();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "RobloxState disk mismatch, not saving ModManifest");
            }

            App.Logger.WriteLine(LOG_IDENT, $"Finished checking file mods");

            if (!success)
                App.Logger.WriteLine(LOG_IDENT, "Failed to apply all modifications");

            return success;
        }

        private async Task DownloadPackage(Package package)
        {
            string LOG_IDENT = $"Bootstrapper::DownloadPackage.{package.Name}";

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            Directory.CreateDirectory(Paths.Downloads);

            string packageUrl = Deployment.GetLocation($"/{_latestVersionGuid}-{package.Name}");
            string robloxPackageLocation = Path.Combine(Paths.LocalAppData, "Roblox", "Downloads", package.Signature);

            if (File.Exists(package.DownloadPath))
            {
                var file = new FileInfo(package.DownloadPath);

                string calculatedMD5 = MD5Hash.FromFile(package.DownloadPath);

                if (calculatedMD5 != package.Signature)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Package is corrupted ({calculatedMD5} != {package.Signature})! Deleting and re-downloading...");
                    file.Delete();
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Package is already downloaded, skipping...");

                    Interlocked.Add(ref _totalDownloadedBytes, package.PackedSize);
                    UpdateProgressBar();

                    return;
                }
            }
            else if (File.Exists(robloxPackageLocation))
            {
                // let's cheat! if the stock bootstrapper already previously downloaded the file,
                // then we can just copy the one from there

                App.Logger.WriteLine(LOG_IDENT, $"Found existing copy at '{robloxPackageLocation}'! Copying to Downloads folder...");
                File.Copy(robloxPackageLocation, package.DownloadPath);

                Interlocked.Add(ref _totalDownloadedBytes, package.PackedSize);
                UpdateProgressBar();

                return;
            }

            if (File.Exists(package.DownloadPath))
                return;

            const int maxTries = 5;

            App.Logger.WriteLine(LOG_IDENT, "Downloading...");

            var buffer = new byte[65536];

            for (int i = 1; i <= maxTries; i++)
            {
                if (_cancelTokenSource.IsCancellationRequested)
                    return;

                int totalBytesRead = 0;

                try
                {
                    var response = await App.HttpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, _cancelTokenSource.Token);
                    await using var stream = await response.Content.ReadAsStreamAsync(_cancelTokenSource.Token);
                    await using var fileStream = new FileStream(package.DownloadPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);

                    while (true)
                    {
                        if (_cancelTokenSource.IsCancellationRequested)
                        {
                            stream.Close();
                            fileStream.Close();
                            return;
                        }

                        int bytesRead = await stream.ReadAsync(buffer, _cancelTokenSource.Token);

                        if (bytesRead == 0)
                            break;

                        totalBytesRead += bytesRead;

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), _cancelTokenSource.Token);

                        Interlocked.Add(ref _totalDownloadedBytes, bytesRead);
                        SetStatus(
                            String.Format(App.Settings.Prop.DownloadingStringFormat,
                            package.Name,
                            totalBytesRead / 1048576,
                            package.Size / 1048576
                            ));
                        UpdateProgressBar();
                    }

                    string hash = MD5Hash.FromStream(fileStream);

                    if (hash != package.Signature)
                        throw new ChecksumFailedException($"Failed to verify download of {packageUrl}\n\nExpected hash: {package.Signature}\nGot hash: {hash}");

                    App.Logger.WriteLine(LOG_IDENT, $"Finished downloading! ({totalBytesRead} bytes total)");
                    break;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"An exception occurred after downloading {totalBytesRead} bytes. ({i}/{maxTries})");
                    App.Logger.WriteException(LOG_IDENT, ex);

                    if (ex.GetType() == typeof(ChecksumFailedException))
                    {
                        Frontend.ShowConnectivityDialog(
                            Strings.Dialog_Connectivity_UnableToDownload,
                            String.Format(Strings.Dialog_Connectivity_UnableToDownloadReason, "https://bloxstraplabs.com/wiki/help/bloxstrap-cannot-download-roblox/)"),
                            MessageBoxImage.Error,
                            ex
                        );

                        App.Terminate(ErrorCode.ERROR_CANCELLED);
                    }
                    else if (i >= maxTries)
                        throw;

                    if (File.Exists(package.DownloadPath))
                        File.Delete(package.DownloadPath);

                    Interlocked.Add(ref _totalDownloadedBytes, -totalBytesRead);
                    UpdateProgressBar();
                }
            }
        }
        private void ExtractPackage(Package package, List<string>? files = null)
        {
            const string LOG_IDENT = "Bootstrapper::ExtractPackage";

            string? packageDir = PackageDirectoryMap.GetValueOrDefault(package.Name);

            if (packageDir is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"WARNING: {package.Name} was not found in the package map!");
                return;
            }

            string packageFolder = Path.Combine(_latestVersionDirectory, packageDir);
            string? fileFilter = null;

            // for sharpziplib, each file in the filter needs to be a regex
            if (files is not null)
            {
                var regexList = new List<string>();

                foreach (string file in files)
                    regexList.Add("^" + file.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + "$");

                fileFilter = String.Join(';', regexList);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Extracting {package.Name}...");

            var fastZip = new FastZip(_fastZipEvents);

            fastZip.ExtractZip(package.DownloadPath, packageFolder, fileFilter);

            App.Logger.WriteLine(LOG_IDENT, $"Finished extracting {package.Name}");
        }
        #endregion
    }
}