using Bloxstrap.UI.ViewModels.Installer;

namespace Bloxstrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Interaction logic for LaunchMenuDialog.xaml
    /// </summary>
    public partial class LaunchMenuDialog
    {
        public NextAction CloseAction = NextAction.Terminate;

        public LaunchMenuDialog()
        {
            var viewModel = new LaunchMenuViewModel();
            viewModel.CloseWindowRequest += (_, closeAction) =>
            {
                CloseAction = closeAction;
                Close();
            };

            DataContext = viewModel;

            InitializeComponent();

            DateTime today = DateTime.Now;
            if (today.Month == 4 && today.Day == 1)
            {
                LaunchTitle.Text = "Bubble trap";
            }
        }
    }
}