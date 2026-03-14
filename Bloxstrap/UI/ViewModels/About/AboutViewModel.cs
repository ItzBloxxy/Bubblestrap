using System.Windows;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Bloxstrap.UI.ViewModels.About
{
    public class AboutViewModel : NotifyPropertyChangedViewModel
    {
        public string Version => string.Format(Strings.Menu_About_Version, string.Join(".", App.Version.Split('.').Take(3)));

        public BuildMetadataAttribute BuildMetadata => App.BuildMetadata;

        public string BuildTimestamp => BuildMetadata.Timestamp.ToFriendlyString();

        public string OperatingSystem
        {
            get
            {
                try
                {
                    string productName = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "Windows")?.ToString() ?? "Windows";
                    string displayVersion = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", "")?.ToString() ?? "";

                    if (Environment.OSVersion.Version.Build >= 22000)
                        productName = productName.Replace("Windows 10", "Windows 11");

                    return $"{productName.Replace("Microsoft ", "")} {displayVersion} ({RuntimeInformation.OSArchitecture.ToString().ToLower()})";
                }
                catch
                {
                    return $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
                }
            }
        }

        public string BuildCommitHashUrl => $"https://github.com/{App.ProjectRepository}/commit/{BuildMetadata.CommitHash}";

        public Visibility BuildInformationVisibility => App.IsProductionBuild ? Visibility.Collapsed : Visibility.Visible;

        public Visibility BuildCommitVisibility => App.IsActionBuild ? Visibility.Visible : Visibility.Collapsed;
    }
}