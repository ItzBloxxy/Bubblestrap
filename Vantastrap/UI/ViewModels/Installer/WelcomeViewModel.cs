namespace vantastrap.UI.ViewModels.Installer
{
    public class WelcomeViewModel : NotifyPropertyChangedViewModel
    {
        // formatting is done here instead of in xaml, it's a bit easier
        public string MainText => String.Format(
            Strings.Installer_Welcome_MainText,
            "[github.com/ItzBloxxy/Vantastrap](https://github.com/ItzBloxxy/Vantastrap)"
        );

        public bool CanContinue { get; set; } = false;
    }
}
