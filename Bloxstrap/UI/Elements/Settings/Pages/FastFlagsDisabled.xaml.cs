using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class FastFlagsDisabled
    {
        public FastFlagsDisabled()
        {
            DataContext = new FastFlagsDisabledViewModel(this);
            InitializeComponent();
        }
    }
}