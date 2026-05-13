using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;

namespace Bloxstrap.Models
{
    public class RobloxIconEntry
    {
        public RobloxIcon IconType { get; set; }

        public ImageSource? PreviewImage
        {
            get
            {
                if (IconType == RobloxIcon.Default)
                    return TryGetRobloxExeIcon();

                if (IconType == RobloxIcon.Custom)
                {
                    string loc = App.Settings.Prop.CustomRobloxIconLocation;
                    if (string.IsNullOrEmpty(loc) || !File.Exists(loc))
                        return null;
                    try
                    {
                        using var icon = new Icon(loc, 16, 16);
                        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle, System.Windows.Int32Rect.Empty,
                            BitmapSizeOptions.FromWidthAndHeight(16, 16));
                        src.Freeze();
                        return src;
                    }
                    catch { return null; }
                }

                BootstrapperIcon? mapped = IconType switch
                {
                    RobloxIcon.Icon2008 => BootstrapperIcon.Icon2008,
                    RobloxIcon.Icon2011 => BootstrapperIcon.Icon2011,
                    RobloxIcon.IconEarly2015 => BootstrapperIcon.IconEarly2015,
                    RobloxIcon.IconLate2015 => BootstrapperIcon.IconLate2015,
                    RobloxIcon.Icon2017 => BootstrapperIcon.Icon2017,
                    RobloxIcon.Icon2019 => BootstrapperIcon.Icon2019,
                    RobloxIcon.Icon2022 => BootstrapperIcon.Icon2022,
                    RobloxIcon.Icon2025 => BootstrapperIcon.Icon2025,
                    RobloxIcon.Icon2025NoBg => BootstrapperIcon.Icon2025NoBg,
                    _ => null
                };

                if (mapped is null) return null;

                try
                {
                    using var icon = mapped.Value.GetIcon();
                    using var bmp = icon.ToBitmap();
                    using var ms = new System.IO.MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.StreamSource = ms;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
                catch { return null; }
            }
        }

        private static ImageSource? TryGetRobloxExeIcon()
        {
            try
            {
                string exePath = new AppData.RobloxPlayerData().ExecutablePath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    return null;

                using var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon is null) return null;

                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(16, 16));
                src.Freeze();
                return src;
            }
            catch { return null; }
        }
    }
}