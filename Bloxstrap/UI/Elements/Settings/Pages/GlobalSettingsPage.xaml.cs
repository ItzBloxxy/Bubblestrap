using Bloxstrap.UI.ViewModels.Settings;
using System.Windows.Input;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for FastFlagsPage.xaml
    /// </summary>
    public partial class GlobalSettingsPage
    {
        private GlobalSettingsViewModel _viewModel = null!;

        public GlobalSettingsPage()
        {
            SetupViewModel();
            InitializeComponent();
            App.BubbleRPC?.SetPage("Global Settings");
        }

        private void SetupViewModel()
        {
            _viewModel = new GlobalSettingsViewModel();

            DataContext = _viewModel;
        }

        private void ValidateUInt32(object sender, TextCompositionEventArgs e) => e.Handled = !UInt32.TryParse(e.Text, out uint _);
        private void ValidateFloat(object sender, TextCompositionEventArgs e) => e.Handled = !Regex.IsMatch(e.Text, @"^\d*\.?\d*$");
    }
}
