using Bloxstrap.Enums.GBSPresets;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class GlobalSettingsViewModel : NotifyPropertyChangedViewModel
    {
        public bool ReadOnly
        {
            get => App.GlobalSettings.GetReadOnly();
            set => App.GlobalSettings.SetReadOnly(value);
        }

        public string FramerateCap
        {
            get => App.GlobalSettings.GetPreset("Rendering.FramerateCap")!;
            set => App.GlobalSettings.SetPreset("Rendering.FramerateCap", value);
        }

        public string UITransparency
        {
            get => App.GlobalSettings.GetPreset("UI.Transparency")!;
            set
            {
                App.GlobalSettings.SetPreset("UI.Transparency", value.Length >= 3 ? value[..3] : value); // guhh??

                OnPropertyChanged(nameof(UITransparency));
            }
        }

        public string GraphicsQuality
        {
            get => App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel")!;
            set
            {
                App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", value);

                OnPropertyChanged(nameof(GraphicsQuality));
            }
        }
        public bool VRVignette
        {
            get => App.GlobalSettings.GetPreset("User.VignetteEnabled")?.ToLower() == "true";
            set { App.GlobalSettings.SetPreset("User.VignetteEnabled", value.ToString().ToLower());
                OnPropertyChanged(nameof(VRVignette)); }
        }

        public bool ReducedMotion
        {
            get => App.GlobalSettings.GetPreset("UI.ReducedMotion")?.ToLower() == "true";
            set => App.GlobalSettings.SetPreset("UI.ReducedMotion", value);
        }
        public bool ChatTranslationEnabled
        {
            get => App.GlobalSettings.GetPreset("User.ChatTranslationEnabled")?.ToLower() == "true";
            set => App.GlobalSettings.SetPreset("User.ChatTranslationEnabled", value.ToString().ToLower());
        }
        public bool HapticFeedback
        {
            get => App.GlobalSettings.GetPreset("User.HapticStrength") != "0";
            set => App.GlobalSettings.SetPreset("User.HapticStrength", value ? "1" : "0");
        }

        public string StartScreenSizeX
        {
            get => App.GlobalSettings.GetPreset("User.StartScreenSize.X") ?? "1920";
            set
            {
                App.GlobalSettings.SetPreset("User.StartScreenSize.X", value);
                OnPropertyChanged(nameof(StartScreenSizeX));
            }
        }

        public string StartScreenSizeY
        {
            get => App.GlobalSettings.GetPreset("User.StartScreenSize.Y") ?? "1080";
            set
            {
                App.GlobalSettings.SetPreset("User.StartScreenSize.Y", value);
                OnPropertyChanged(nameof(StartScreenSizeY));
            }
        }

        public IReadOnlyDictionary<FontSize, string?> FontSizes => GlobalSettingsManager.FontSizes;
        public FontSize SelectedFontSize
        {
            get => FontSizes.FirstOrDefault(x => x.Value == App.GlobalSettings.GetPreset("UI.FontSize")).Key;
            set => App.GlobalSettings.SetPreset("UI.FontSize", FontSizes[value]);
        }

        public string MouseSensitivity
        {
            get => App.GlobalSettings.GetPreset("User.MouseSensitivity")!;
            set => App.GlobalSettings.SetPreset("User.MouseSensitivity", value);
        }

        public string VREnabled
        {
            get => App.GlobalSettings.GetPreset("User.VREnabled")!;
            set => App.GlobalSettings.SetPreset("User.VREnabled", value);
        }

        public double MasterVolume
        {
            get => double.TryParse(App.GlobalSettings.GetPreset("Audio.MasterVolume"), out var v) ? v : 1.0;
            set
            {
                App.GlobalSettings.SetPreset("Audio.MasterVolume", value.ToString("0.0"));
                OnPropertyChanged(nameof(MasterVolume));
            }
        }

        public double PartyVolume
        {
            get => double.TryParse(App.GlobalSettings.GetPreset("Audio.PartyVoiceVolume"), out var v) ? v : 1.0;
            set
            {
                App.GlobalSettings.SetPreset("Audio.PartyVoiceVolume", value.ToString("0.0"));
                OnPropertyChanged(nameof(PartyVolume));
            }
        }

        public bool Fullscreen
        {
            get => App.GlobalSettings.GetPreset("User.Fullscreen")?.ToLower() == "true";
            set
            {
                App.GlobalSettings.SetPreset("User.Fullscreen", value.ToString().ToLower());
                OnPropertyChanged(nameof(Fullscreen));
            }
        }
    }
}