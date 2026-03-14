using Bloxstrap.UI.ViewModels.Dialogs;

namespace Bloxstrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Interaction logic for UninstallerDialog.xaml
    /// </summary>
    public partial class UninstallerDialog
    {
        public bool Confirmed { get; private set; } = false;

        public bool KeepData { get; private set; } = true;

        public UninstallerDialog()
        {
            var viewModel = new UninstallerViewModel();
            viewModel.ConfirmUninstallRequest += (_, _) =>
            {
                Confirmed = true;
                KeepData = viewModel.KeepData;
                Close();
            };

            DataContext = viewModel;

            InitializeComponent();
            App.BubbleRPC?.SetDialog("Uninstaller");
        }
    }
}
