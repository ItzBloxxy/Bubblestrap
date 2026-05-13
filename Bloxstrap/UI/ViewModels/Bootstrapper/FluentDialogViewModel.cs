using Bloxstrap.RobloxInterfaces;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Bloxstrap.UI.ViewModels.Bootstrapper
{
    public class FluentDialogViewModel : BootstrapperDialogViewModel
    {
        public BackgroundType WindowBackdropType { get; set; } = BackgroundType.Mica;
        public SolidColorBrush BackgroundColourBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        public string VersionText
        {
            get => _versionText;
            set
            {
                _versionText = value;
                OnPropertyChanged(nameof(VersionText));
            }
        }
        private string _versionText = string.Empty;
        public string ChannelText
        {
            get => _channelText;
            set
            {
                _channelText = value;
                OnPropertyChanged(nameof(ChannelText));
            }
        }
        private string _channelText = string.Empty;
        public FluentDialogViewModel(IBootstrapperDialog dialog, bool aero, string version) : base(dialog)
        {
            const int alpha = 128;

            WindowBackdropType = aero ? BackgroundType.Aero : BackgroundType.Mica;

            if (aero)
            {
                BackgroundColourBrush = App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Light ?
                    new SolidColorBrush(Color.FromArgb(alpha, 225, 225, 225)) :
                    new SolidColorBrush(Color.FromArgb(alpha, 30, 30, 30));
            }

            if (App.Settings.Prop.HideBootstrapperInfo)
            {
                VersionText = string.Empty;
                ChannelText = string.Empty;
            }
            else
            {
                VersionText = $"{Strings.Common_Version}: {version}";
                ChannelText = $"{Strings.Common_Channel}: {Deployment.Channel}";

                Deployment.ChannelChanged += (_, newChannel) =>
                {
                    ChannelText = $"{Strings.Common_Channel}: {newChannel}";
                };
            }
        }
    }
}
