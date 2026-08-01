using vantastrap.UI.ViewModels.Settings;

namespace vantastrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ShortcutsPage.xaml
    /// </summary>
    public partial class ShortcutsPage
    {
        public ShortcutsPage()
        {
            DataContext = new ShortcutsViewModel();
            InitializeComponent();
            App.BubbleRPC?.SetPage("Shortcuts");
        }
    }
}
