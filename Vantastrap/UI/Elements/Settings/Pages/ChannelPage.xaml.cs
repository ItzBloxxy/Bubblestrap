using vantastrap.UI.ViewModels.Settings;
using System.Windows;

namespace vantastrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ChannelPage.xaml
    /// </summary>
    public partial class ChannelPage
    {
        public ChannelPage()
        {
            DataContext = new ChannelViewModel();
            InitializeComponent();
            App.BubbleRPC?.SetPage("Channel Page Settings");
        }

        private void ToggleSwitch_Checked(object sender, RoutedEventArgs e)
        {

        }
    }
}