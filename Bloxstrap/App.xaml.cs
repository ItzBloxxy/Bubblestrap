using Bloxstrap.Integrations;
using Microsoft.Win32;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Bloxstrap
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
#if QA_BUILD
        public const string ProjectName = "Bubblestrap-QA";
#else
        public const string ProjectName = "Bubblestrap";
#endif
        public const string ProjectOwner = "ItzBloxxy";
        public const string ProjectRepository = "ItzBloxxy/Bubblestrap";
        public const string ProjectDownloadLink = "https://github.com/ItzBloxxy/Bubblestrap/releases";
        public const string ProjectHelpLink = "https://github.com/bloxstraplabs/bloxstrap/wiki";
        public const string ProjectSupportLink = "https://github.com/ItzBloxxy/Bubblestrap/issues/new";

        public const string RobloxPlayerAppName = "RobloxPlayerBeta.exe";
        public const string RobloxStudioAppName = "RobloxStudioBeta.exe";

        public const string UninstallKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{ProjectName}";
        public const string ApisKey = $"Software\\{ProjectName}";

        public static LaunchSettings LaunchSettings { get; private set; } = null!;

        public static BuildMetadataAttribute BuildMetadata = Assembly.GetExecutingAssembly().GetCustomAttribute<BuildMetadataAttribute>()!;

        public static string Version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        public static Bootstrapper? Bootstrapper { get; set; } = null!;

        public BubblestrapRichPresence RichPresence { get; private set; } = null!;

        public static bool IsActionBuild => !string.IsNullOrEmpty(BuildMetadata.CommitRef);

        public static bool IsProductionBuild => IsActionBuild && BuildMetadata.CommitRef.StartsWith("tag", StringComparison.Ordinal);

        public static bool IsStudioVisible => !string.IsNullOrEmpty(App.RobloxState.Prop.Studio.VersionGuid);

        public static readonly MD5 MD5Provider = MD5.Create();

        public static readonly Logger Logger = new();

        public static readonly Dictionary<string, BaseTask> PendingSettingTasks = new();

        public static readonly JsonManager<Settings> Settings = new();

        public static readonly JsonManager<State> State = new();

        public static readonly JsonManager<RobloxState> RobloxState = new();

        public static readonly LocalDataManager LocalData = new();

        public static readonly FastFlagManager FastFlags = new();

        public static readonly GlobalSettingsManager GlobalSettings = new();

        public static readonly CookiesManager Cookies = new();

        public static readonly HttpClient HttpClient = new(
            new HttpClientLoggingHandler(
                new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }
            )
        );

        private static bool _showingExceptionDialog = false;

        public static void Terminate(ErrorCode exitCode = ErrorCode.ERROR_SUCCESS)
        {
            int exitCodeNum = (int)exitCode;
            Logger.WriteLine("App::Terminate", $"Terminating with exit code {exitCodeNum} ({exitCode})");
            Environment.Exit(exitCodeNum);
        }

        public static void SoftTerminate(ErrorCode exitCode = ErrorCode.ERROR_SUCCESS)
        {
            int exitCodeNum = (int)exitCode;
            Logger.WriteLine("App::SoftTerminate", $"Terminating with exit code {exitCodeNum} ({exitCode})");
            Current.Dispatcher.Invoke(() => Current.Shutdown(exitCodeNum));
        }

        void GlobalExceptionHandler(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Logger.WriteLine("App::GlobalExceptionHandler", "An exception occurred");
            FinalizeExceptionHandling(e.Exception);
        }

        public static void FinalizeExceptionHandling(AggregateException ex)
        {
            foreach (var innerEx in ex.InnerExceptions)
                Logger.WriteException("App::FinalizeExceptionHandling", innerEx);

            FinalizeExceptionHandling(ex.GetBaseException(), false);
        }

        public static void FinalizeExceptionHandling(Exception ex, bool log = true)
        {
            if (log)
                Logger.WriteException("App::FinalizeExceptionHandling", ex);

            if (_showingExceptionDialog)
                return;

            _showingExceptionDialog = true;
            SendLog();

            if (Bootstrapper?.Dialog != null)
            {
                if (Bootstrapper.Dialog.TaskbarProgressValue == 0)
                    Bootstrapper.Dialog.TaskbarProgressValue = 1;

                Bootstrapper.Dialog.TaskbarProgressState = TaskbarItemProgressState.Error;
            }

            Frontend.ShowExceptionDialog(ex);
            Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
        }

        public static BubblestrapRichPresence? BubbleRPC
        {
            get => (Current as App)?.RichPresence;
            set
            {
                if (Current is App app)
                    app.RichPresence = value!;
            }
        }

        public static async Task<GithubRelease?> GetLatestRelease()
        {
            const string LOG_IDENT = "App::GetLatestRelease";

            try
            {
                var releaseInfo = await Http.GetJson<GithubRelease>($"https://api.github.com/repos/{ProjectRepository}/releases/latest");

                if (releaseInfo is null || releaseInfo.Assets is null)
                {
                    Logger.WriteLine(LOG_IDENT, "Encountered invalid data");
                    return null;
                }

                return releaseInfo;
            }
            catch (Exception ex)
            {
                Logger.WriteException(LOG_IDENT, ex);
            }

            return null;
        }

        public static void SendLog()
        {
            // Implement logging logic here
        }

        public static void AssertWindowsOSVersion()
        {
            const string LOG_IDENT = "App::AssertWindowsOSVersion";

            int major = Environment.OSVersion.Version.Major;
            if (major < 10) // Windows 10+
            {
                Logger.WriteLine(LOG_IDENT, $"Detected unsupported Windows version ({Environment.OSVersion.Version}).");

                if (!LaunchSettings.QuietFlag.Active)
                    Frontend.ShowMessageBox(Strings.App_OSDeprecation_Win7_81, MessageBoxImage.Error);

                Terminate(ErrorCode.ERROR_INVALID_FUNCTION);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            const string LOG_IDENT = "App::OnStartup";

            Locale.Initialize();
            base.OnStartup(e);

            Logger.WriteLine(LOG_IDENT, $"Starting {ProjectName} v{Version}");

            string userAgent = $"{ProjectName}/{Version}";

            if (IsActionBuild)
            {
                Logger.WriteLine(LOG_IDENT, $"Compiled {BuildMetadata.Timestamp.ToFriendlyString()} from commit {BuildMetadata.CommitHash} ({BuildMetadata.CommitRef})");

                userAgent += IsProductionBuild ? " (Production)" : $" (Artifact {BuildMetadata.CommitHash}, {BuildMetadata.CommitRef})";
            }
            else
            {
                Logger.WriteLine(LOG_IDENT, $"Compiled {BuildMetadata.Timestamp.ToFriendlyString()} from {BuildMetadata.Machine}");

#if QA_BUILD
                userAgent += " (QA)";
#else
                userAgent += $" (Build {Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildMetadata.Machine))})";
#endif
            }

            Logger.WriteLine(LOG_IDENT, $"OSVersion: {Environment.OSVersion}");
            Logger.WriteLine(LOG_IDENT, $"Loaded from {Paths.Process}");
            Logger.WriteLine(LOG_IDENT, $"Temp path is {Paths.Temp}");
            Logger.WriteLine(LOG_IDENT, $"WindowsStartMenu path is {Paths.WindowsStartMenu}");

            ApplicationConfiguration.Initialize();

            HttpClient.Timeout = TimeSpan.FromSeconds(30);
            HttpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);

            LaunchSettings = new LaunchSettings(e.Args);

            using var uninstallKey = Registry.CurrentUser.OpenSubKey(UninstallKey);
            string? installLocation = null;
            bool fixInstallLocation = false;

            if (uninstallKey?.GetValue("InstallLocation") is string value)
            {
                if (Directory.Exists(value))
                    installLocation = value;
                else
                {
                    var match = Regex.Match(value, @"^[a-zA-Z]:\\Users\\([^\\]+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string newLocation = value.Replace(match.Groups[0].Value, Paths.UserProfile, StringComparison.InvariantCultureIgnoreCase);
                        if (Directory.Exists(newLocation))
                        {
                            installLocation = newLocation;
                            fixInstallLocation = true;
                        }
                    }
                }
            }

            if (installLocation is null && Directory.GetParent(Paths.Process)?.FullName is string processDir)
            {
                var files = Directory.GetFiles(processDir).Select(Path.GetFileName).ToArray();
                if (files.Length <= 3 && files.Contains("Settings.json") && files.Contains("State.json"))
                {
                    installLocation = processDir;
                    fixInstallLocation = true;
                }
            }

            if (fixInstallLocation && installLocation is not null)
            {
                var installer = new Installer
                {
                    InstallLocation = installLocation,
                    IsImplicitInstall = true
                };

                if (installer.CheckInstallLocation())
                {
                    Logger.WriteLine(LOG_IDENT, $"Changing install location to '{installLocation}'");
                    installer.DoInstall();
                }
                else
                {
                    installLocation = null;
                }
            }

            if (installLocation is null)
            {
                Logger.Initialize(Paths.Temp);
                AssertWindowsOSVersion();
                Logger.WriteLine(LOG_IDENT, "Not installed, launching the installer");
                LaunchHandler.LaunchInstaller();
            }
            else
            {
                Paths.Initialize(installLocation);

                if (Paths.Process != Paths.Application && !File.Exists(Paths.Application))
                    File.Copy(Paths.Process, Paths.Application);

                // Instead of Logger.Initialize(true);
                Logger.Initialize(Paths.Base, LaunchSettings.UninstallFlag.Active);

                if (!Logger.Initialized && !Logger.NoWriteMode)
                {
                    Logger.WriteLine(LOG_IDENT, "Possible duplicate launch detected, terminating.");
                    Terminate();
                }

                Settings.Load();
                State.Load();
                RobloxState.Load();
                FastFlags.Load();
                GlobalSettings.Load();

                if (Settings.Prop.AllowCookieAccess)
                    Task.Run(Cookies.LoadCookies);

                if (!Locale.SupportedLocales.ContainsKey(Settings.Prop.Locale))
                {
                    Settings.Prop.Locale = "nil";
                    Settings.Save();
                }

                Locale.Set(Settings.Prop.Locale);

                if (!LaunchSettings.BypassUpdateCheck)
                    Installer.HandleUpgrade();

                Task.Run(App.LocalData.LoadData);

                WindowsRegistry.RegisterApis();

                LaunchHandler.ProcessLaunchArgs();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            BubbleRPC?.Dispose();
            base.OnExit(e);
        }
    }
}