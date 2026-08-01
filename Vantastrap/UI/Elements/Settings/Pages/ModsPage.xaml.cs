using vantastrap.UI.ViewModels.Settings;

namespace vantastrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ModsPage.xaml
    /// </summary>
    public partial class ModsPage
    {
        public ModsPage()
        {
            DataContext = new ModsViewModel();
            InitializeComponent();
            App.BubbleRPC?.SetPage("Mods Settings");
        }
    }
}