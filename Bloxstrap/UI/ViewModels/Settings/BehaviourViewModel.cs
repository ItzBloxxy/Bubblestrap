namespace Bloxstrap.UI.ViewModels.Settings
{
    public class BehaviourViewModel : NotifyPropertyChangedViewModel
    {

        public BehaviourViewModel()
        {
            App.Cookies.StateChanged += (object? _, CookieState state) => CookieLoadingFailed = state != CookieState.Success && state != CookieState.Unknown;
        }

        public bool IsRobloxInstallationMissing => String.IsNullOrEmpty(App.RobloxState.Prop.Player.VersionGuid) && String.IsNullOrEmpty(App.RobloxState.Prop.Studio.VersionGuid);

        public bool CookieAccess
        {
            get => App.Settings.Prop.AllowCookieAccess;
            set
            {
                App.Settings.Prop.AllowCookieAccess = value;
                if (value)
                    Task.Run(App.Cookies.LoadCookies);

                OnPropertyChanged(nameof(CookieAccess));
            }
        }

        private bool _cookieLoadingFailed;
        public bool CookieLoadingFailed
        {
            get => _cookieLoadingFailed;
            set
            {
                _cookieLoadingFailed = value;
                OnPropertyChanged(nameof(CookieLoadingFailed));
            }
        }

        public bool UpdateRoblox
        {
            get => App.Settings.Prop.UpdateRoblox && !IsRobloxInstallationMissing;
            set => App.Settings.Prop.UpdateRoblox = value;
        }

        public bool ConfirmLaunches
        {
            get => App.Settings.Prop.ConfirmLaunches;
            set => App.Settings.Prop.ConfirmLaunches = value;
        }

        public bool HideBootstrapperInfo
        {
            get => App.Settings.Prop.HideBootstrapperInfo;
            set => App.Settings.Prop.HideBootstrapperInfo = value;
        }

        public bool CloseCrashHandler
        {
            get => App.Settings.Prop.AutoCloseCrashHandler;
            set => App.Settings.Prop.AutoCloseCrashHandler = value;
        }

        public bool BackgroundUpdates
        {
            get => App.Settings.Prop.BackgroundUpdatesEnabled;
            set => App.Settings.Prop.BackgroundUpdatesEnabled = value;
        }

        public bool EnableMemoryTrimmer
        {
            get => App.Settings.Prop.EnableMemoryTrimmer;
            set
            {
                App.Settings.Prop.EnableMemoryTrimmer = value;
                OnPropertyChanged(nameof(EnableMemoryTrimmer));
            }
        }
        public string MemoryTrimInterval
        {
            get => App.Settings.Prop.MemoryTrimInterval.ToString();
            set
            {
                if (int.TryParse(value, out int result))
                {
                    App.Settings.Prop.MemoryTrimInterval = result;
                }
                OnPropertyChanged(nameof(MemoryTrimInterval));
            }
        }

        public bool EnableMemoryThreshold
        {
            get => App.Settings.Prop.EnableMemoryThreshold;
            set
            {
                if (App.Settings.Prop.EnableMemoryThreshold != value)
                {
                    App.Settings.Prop.EnableMemoryThreshold = value;
                    OnPropertyChanged(nameof(EnableMemoryThreshold));
                }
            }
        }
        public string MemoryTrimThreshold
        {
            get => App.Settings.Prop.MemoryTrimThreshold == 0 ? "" : App.Settings.Prop.MemoryTrimThreshold.ToString();
            set
            {
                if (int.TryParse(value, out int result))
                    App.Settings.Prop.MemoryTrimThreshold = result;
                else
                    App.Settings.Prop.MemoryTrimThreshold = 0;

                OnPropertyChanged(nameof(MemoryTrimThreshold));
            }
        }

        public CleanerOptions SelectedCleanUpMode
        {
            get => App.Settings.Prop.CleanerOptions;
            set => App.Settings.Prop.CleanerOptions = value;
        }

        public IEnumerable<CleanerOptions> CleanerOptions { get; } = CleanerOptionsEx.Selections;

        public CleanerOptions CleanerOption
        {
            get => App.Settings.Prop.CleanerOptions;
            set
            {
                App.Settings.Prop.CleanerOptions = value;
            }
        }

        private List<string> CleanerItems = App.Settings.Prop.CleanerDirectories;

        public bool CleanerLogs
        {
            get => CleanerItems.Contains("RobloxLogs");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxLogs");
                else
                    CleanerItems.Remove("RobloxLogs"); // should we try catch it?
            }
        }

        public bool CleanerCache
        {
            get => CleanerItems.Contains("RobloxCache");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxCache");
                else
                    CleanerItems.Remove("RobloxCache");
            }
        }

        public bool CleanerBubblestrap
        {
            get => CleanerItems.Contains("BubblestrapLogs");
            set
            {
                if (value)
                    CleanerItems.Add("BubblestrapLogs");
                else
                    CleanerItems.Remove("BubblestrapLogs");
            }
        }
    }
}
