using Bloxstrap.AppData;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ModsViewModel : NotifyPropertyChangedViewModel
    {
        private const int CursorSize = 64;
        private const int ShiftlockCursorSize = 32;

        private static readonly string CursorDir =
            Path.Combine(Paths.Modifications, "Content", "textures", "Cursors", "KeyboardMouse");

        private static readonly string TextureDir =
            Path.Combine(Paths.Modifications, "Content", "textures");

        private static readonly string SoundsDir =
            Path.Combine(Paths.Modifications, "Content", "sounds");

        private static readonly string[] ArrowCursorFiles = ["ArrowCursor.png", "ArrowFarCursor.png", "IBeamCursor.png"];
        private static readonly string[] ShiftlockCursorFiles = ["MouseLockedCursor.png"];
        private static readonly string[] DeathSoundFiles = ["oof.ogg"];

        private static readonly Dictionary<IntPtr, (IntPtr Small, IntPtr Big)> _activeIconHandles = new();

        private readonly Dictionary<string, BitmapImage?> _presetPreviewCache = new();

        private readonly Dictionary<string, byte[]> _fontHeaders = new()
        {
            { "ttf", new byte[] { 0x00, 0x01, 0x00, 0x00 } },
            { "otf", new byte[] { 0x4F, 0x54, 0x54, 0x4F } },
            { "ttc", new byte[] { 0x74, 0x74, 0x63, 0x66 } }
        };

        private string? _selectedCursorSlot;

        public ObservableCollection<RobloxIconEntry> RobloxIcons { get; set; } = new();

        public RobloxIcon SelectedRobloxIcon
        {
            get => App.Settings.Prop.CustomRobloxIcon;
            set
            {
                App.Settings.Prop.CustomRobloxIcon = value;
                OnPropertyChanged(nameof(SelectedRobloxIcon));
                OnPropertyChanged(nameof(CustomRobloxIconLocationVisible));
            }
        }

        public bool CustomRobloxIconLocationVisible => SelectedRobloxIcon == RobloxIcon.Custom;

        public string CustomRobloxIconLocation
        {
            get => App.Settings.Prop.CustomRobloxIconLocation;
            set
            {
                App.Settings.Prop.CustomRobloxIconLocation = value;
                OnPropertyChanged(nameof(CustomRobloxIconLocation));

                var entry = RobloxIcons.FirstOrDefault(e => e.IconType == RobloxIcon.Custom);
                if (entry is not null)
                {
                    entry.RefreshPreview();
                    OnPropertyChanged(nameof(RobloxIcons));
                }
            }
        }

        public ModPresetTask OldAvatarBackgroundTask { get; } =
            new("OldAvatarBackground", @"ExtraContent\places\Mobile.rbxl", "OldAvatarBackground.rbxl");

        public ModPresetTask OldCharacterSoundsTask { get; } = new("OldCharacterSounds", new()
        {
            { @"content\sounds\action_footsteps_plastic.mp3", "Sounds.OldWalk.mp3"  },
            { @"content\sounds\action_jump.mp3",              "Sounds.OldJump.mp3"  },
            { @"content\sounds\action_get_up.mp3",            "Sounds.OldGetUp.mp3" },
            { @"content\sounds\action_falling.mp3",           "Sounds.Empty.mp3"    },
            { @"content\sounds\action_jump_land.mp3",         "Sounds.Empty.mp3"    },
            { @"content\sounds\action_swim.mp3",              "Sounds.Empty.mp3"    },
            { @"content\sounds\impact_water.mp3",             "Sounds.Empty.mp3"    }
        });

        public EmojiModPresetTask EmojiFontTask { get; } = new();

        public EnumModPresetTask<Enums.CursorType> CursorTypeTask { get; } = new("CursorType", new()
        {
            {
                Enums.CursorType.From2006, new()
                {
                    { @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",    "Cursor.From2006.ArrowCursor.png"    },
                    { @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Cursor.From2006.ArrowFarCursor.png" }
                }
            },
            {
                Enums.CursorType.From2013, new()
                {
                    { @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",    "Cursor.From2013.ArrowCursor.png"    },
                    { @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Cursor.From2013.ArrowFarCursor.png" }
                }
            }
        });

        public FontModPresetTask TextFontTask { get; } = new();

        public ICommand OpenModsFolderCommand { get; } = new RelayCommand(() => Process.Start(new ProcessStartInfo(Paths.Modifications) { UseShellExecute = true }));
        public ICommand ManageCustomFontCommand { get; }
        public ICommand OpenCompatSettingsCommand { get; }
        public ICommand AddCustomDeathSoundCommand { get; }
        public ICommand RemoveCustomDeathSoundCommand { get; }
        public ICommand AddCustomCursorCommand { get; }
        public ICommand AddCustomArrowFarCursorCommand { get; }
        public ICommand AddCustomIBeamCursorCommand { get; }
        public ICommand AddCustomShiftlockCursorCommand { get; }
        public ICommand RemoveCustomShiftlockCursorCommand { get; }
        public ICommand RemoveSelectedCursorCommand { get; }
        public ICommand RemoveAllCursorsCommand { get; }
        public ICommand ImportCursorSetCommand { get; }
        public ICommand ExportCursorSetCommand { get; }
        public ICommand SelectShiftlockSlotCommand { get; }
        public ICommand SelectArrowSlotCommand { get; }
        public ICommand SelectArrowFarSlotCommand { get; }
        public ICommand SelectIBeamSlotCommand { get; }
        public ICommand BrowseCustomRobloxIconCommand { get; }

        public ModsViewModel()
        {
            ManageCustomFontCommand = new RelayCommand(ManageCustomFont);
            OpenCompatSettingsCommand = new RelayCommand(OpenCompatSettings);
            AddCustomDeathSoundCommand = new AsyncRelayCommand(AddCustomDeathSound);
            RemoveCustomDeathSoundCommand = new AsyncRelayCommand(RemoveCustomDeathSound);
            AddCustomCursorCommand = new AsyncRelayCommand(AddCustomCursor);
            AddCustomArrowFarCursorCommand = new AsyncRelayCommand(AddCustomArrowFarCursor);
            AddCustomIBeamCursorCommand = new AsyncRelayCommand(AddCustomIBeamCursor);
            AddCustomShiftlockCursorCommand = new AsyncRelayCommand(AddCustomShiftlockCursor);
            RemoveCustomShiftlockCursorCommand = new AsyncRelayCommand(RemoveCustomShiftlockCursor);
            RemoveSelectedCursorCommand = new AsyncRelayCommand(RemoveSelectedCursor);
            RemoveAllCursorsCommand = new AsyncRelayCommand(RemoveAllCursors);
            ImportCursorSetCommand = new AsyncRelayCommand(ImportCursorSet);
            ExportCursorSetCommand = new AsyncRelayCommand(ExportCursorSet);
            BrowseCustomRobloxIconCommand = new RelayCommand(BrowseCustomRobloxIcon);

            SelectShiftlockSlotCommand = new RelayCommand(() => SelectedCursorSlot = SelectedCursorSlot == "Shiftlock" ? null : "Shiftlock");
            SelectArrowSlotCommand = new RelayCommand(() => SelectedCursorSlot = SelectedCursorSlot == "Arrow" ? null : "Arrow");
            SelectArrowFarSlotCommand = new RelayCommand(() => SelectedCursorSlot = SelectedCursorSlot == "ArrowFar" ? null : "ArrowFar");
            SelectIBeamSlotCommand = new RelayCommand(() => SelectedCursorSlot = SelectedCursorSlot == "IBeam" ? null : "IBeam");

            foreach (RobloxIcon icon in RobloxIconEx.Selections)
                RobloxIcons.Add(new RobloxIconEntry(icon));
        }

        public string? SelectedCursorSlot
        {
            get => _selectedCursorSlot;
            set
            {
                _selectedCursorSlot = value;
                OnPropertyChanged(nameof(SelectedCursorSlot));
                OnPropertyChanged(nameof(IsShiftlockSelected));
                OnPropertyChanged(nameof(IsArrowSelected));
                OnPropertyChanged(nameof(IsArrowFarSelected));
                OnPropertyChanged(nameof(IsIBeamSelected));
                OnPropertyChanged(nameof(CanRemoveSelected));
            }
        }

        public bool IsShiftlockSelected => SelectedCursorSlot == "Shiftlock";
        public bool IsArrowSelected => SelectedCursorSlot == "Arrow";
        public bool IsArrowFarSelected => SelectedCursorSlot == "ArrowFar";
        public bool IsIBeamSelected => SelectedCursorSlot == "IBeam";
        public bool CanRemoveSelected => SelectedCursorSlot is not null;

        public Enums.CursorType CursorTypeSelection
        {
            get => CursorTypeTask.NewState;
            set
            {
                CursorTypeTask.NewState = value;
                OnPropertyChanged(nameof(CursorTypeSelection));
                OnPropertyChanged(nameof(IsCursorPresetActive));
                OnPropertyChanged(nameof(CursorBrowseIsEnabled));
                _presetPreviewCache.Clear();
                RefreshAllCursorPreviews();

                if (IsCursorPresetActive && ArrowCursorFiles.Any(f => File.Exists(Path.Combine(CursorDir, f))))
                    Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomCursor_PresetConflictWarning, MessageBoxImage.Warning);
            }
        }

        public bool IsCursorPresetActive => !CursorTypeTask.NewState.Equals(default(Enums.CursorType));
        public bool CursorBrowseIsEnabled => !IsCursorPresetActive;

        public Visibility ChooseCustomFontVisibility =>
            string.IsNullOrEmpty(TextFontTask.NewState) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DeleteCustomFontVisibility =>
            string.IsNullOrEmpty(TextFontTask.NewState) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility ChooseCustomDeathSoundVisibility => GetFileVisibility(SoundsDir, DeathSoundFiles, checkExist: false);
        public Visibility DeleteCustomDeathSoundVisibility => GetFileVisibility(SoundsDir, DeathSoundFiles, checkExist: true);
        public Visibility ChooseCustomCursorVisibility => GetFileVisibility(CursorDir, ArrowCursorFiles, checkExist: false);
        public Visibility DeleteCustomCursorVisibility => GetFileVisibility(CursorDir, ArrowCursorFiles, checkExist: true);
        public Visibility ChooseCustomShiftlockCursorVisibility => GetFileVisibility(TextureDir, ShiftlockCursorFiles, checkExist: false);
        public Visibility DeleteCustomShiftlockCursorVisibility => GetFileVisibility(TextureDir, ShiftlockCursorFiles, checkExist: true);

        public object? ArrowCursorPreview =>
            IsCursorPresetActive ? GetPresetPreviewForSlot("ArrowCursor.png") : GetCustomCursorPreviewPath("ArrowCursor.png");

        public object? ArrowFarCursorPreview =>
            IsCursorPresetActive ? GetPresetPreviewForSlot("ArrowFarCursor.png") : GetCustomCursorPreviewPath("ArrowFarCursor.png");

        public object? IBeamCursorPreview => GetCustomCursorPreviewPath("IBeamCursor.png");
        public object? ShiftlockCursorPreview => GetShiftlockCursorPreviewPath();

        private static Visibility GetFileVisibility(string directory, string[] filenames, bool checkExist)
        {
            bool anyExist = filenames.Any(name => File.Exists(Path.Combine(directory, name)));
            return (checkExist ? anyExist : !anyExist) ? Visibility.Visible : Visibility.Collapsed;
        }

        private string? GetCustomCursorPreviewPath(string filename)
        {
            string path = Path.Combine(CursorDir, filename);
            if (!File.Exists(path)) return null;
            string relativeKey = @"content\textures\Cursors\KeyboardMouse\" + filename;
            return CursorTypeTask.IsFileOwnedByAnyPreset(relativeKey) ? null : path;
        }

        private static string? GetShiftlockCursorPreviewPath()
        {
            string path = Path.Combine(TextureDir, "MouseLockedCursor.png");
            return File.Exists(path) ? path : null;
        }

        private BitmapImage? GetPresetPreviewForSlot(string filename)
        {
            if (!IsCursorPresetActive || string.IsNullOrEmpty(filename))
                return null;

            string relativeKey = @"content\textures\Cursors\KeyboardMouse\" + filename;

            string? resourceName = CursorTypeTask.GetResourceNameForFile(relativeKey);
            if (resourceName is null)
                return null;

            if (_presetPreviewCache.TryGetValue(resourceName, out var cached))
                return cached;

            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream($"Bloxstrap.Assets.{resourceName}");

                if (stream is null)
                {
                    _presetPreviewCache[resourceName] = null;
                    return null;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                _presetPreviewCache[resourceName] = bitmap;
                return bitmap;
            }
            catch
            {
                _presetPreviewCache[resourceName] = null;
                return null;
            }
        }

        private void RefreshAllCursorPreviews()
        {
            OnPropertyChanged(nameof(ArrowCursorPreview));
            OnPropertyChanged(nameof(ArrowFarCursorPreview));
            OnPropertyChanged(nameof(IBeamCursorPreview));
            OnPropertyChanged(nameof(ShiftlockCursorPreview));
            OnPropertyChanged(nameof(ChooseCustomCursorVisibility));
            OnPropertyChanged(nameof(DeleteCustomCursorVisibility));
            OnPropertyChanged(nameof(ChooseCustomShiftlockCursorVisibility));
            OnPropertyChanged(nameof(DeleteCustomShiftlockCursorVisibility));
        }

        private static BitmapFrame ResizeImage(string sourcePath, int maxWidth, int maxHeight)
        {
            var src = new BitmapImage();
            src.BeginInit();
            src.UriSource = new Uri(sourcePath, UriKind.Absolute);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            src.EndInit();
            src.Freeze();

            if (src.PixelWidth <= maxWidth && src.PixelHeight <= maxHeight)
                return BitmapFrame.Create(src);

            double scale = Math.Min((double)maxWidth / src.PixelWidth, (double)maxHeight / src.PixelHeight);
            var scaled = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            var frame = BitmapFrame.Create(scaled);
            frame.Freeze();
            return frame;
        }

        private static void SavePng(BitmapSource bitmap, string destPath)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var fs = File.OpenWrite(destPath);
            encoder.Save(fs);
        }

        private async Task AddCustomImageAsync(string[] targetFiles, string targetDir, string dialogTitle, string failureText, int maxSize, Action postAction)
        {
            if (IsCursorPresetActive)
            {
                Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomCursor_PresetActiveWarning, MessageBoxImage.Warning);
                return;
            }

            var dialog = new OpenFileDialog { Filter = "PNG Image (*.png)|*.png", Title = dialogTitle };
            if (dialog.ShowDialog() != true) return;

            string sourcePath = dialog.FileName;
            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(targetDir);
                    var frame = ResizeImage(sourcePath, maxSize, maxSize);
                    foreach (var name in targetFiles)
                        SavePng(frame, Path.Combine(targetDir, name));
                });
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(
                    $"{string.Format(Strings.Menu_Mods_Misc_CustomCursor_AddFailed, failureText)}\n{ex.Message}",
                    MessageBoxImage.Error);
                return;
            }

            postAction();
        }

        private async Task RemoveCustomFileAsync(string[] targetFiles, string targetDir, string notFoundMessage, bool silent, Action? postAction)
        {
            bool anyDeleted = false;
            var errors = new List<string>();

            await Task.Run(() =>
            {
                foreach (var name in targetFiles)
                {
                    string filePath = Path.Combine(targetDir, name);
                    if (!File.Exists(filePath)) continue;
                    try { File.Delete(filePath); anyDeleted = true; }
                    catch (Exception ex) { errors.Add($"{name}: {ex.Message}"); }
                }
            });

            if (errors.Count > 0)
                Frontend.ShowMessageBox(
                    $"{Strings.Menu_Mods_Misc_CustomCursor_RemoveFailed}\n{string.Join("\n", errors)}",
                    MessageBoxImage.Error);
            else if (!anyDeleted && !silent)
                Frontend.ShowMessageBox(notFoundMessage, MessageBoxImage.Information);

            postAction?.Invoke();
        }

        private void ManageCustomFont()
        {
            if (!string.IsNullOrEmpty(TextFontTask.NewState))
            {
                TextFontTask.NewState = string.Empty;
            }
            else
            {
                var dialog = new OpenFileDialog { Filter = $"{Strings.Menu_FontFiles}|*.ttf;*.otf;*.ttc" };
                if (dialog.ShowDialog() != true) return;

                string extension = Path.GetExtension(dialog.FileName).TrimStart('.').ToLowerInvariant();
                byte[] buffer = new byte[4];
                try { using var fs = File.OpenRead(dialog.FileName); fs.ReadExactly(buffer, 0, 4); }
                catch { Array.Clear(buffer, 0, 4); }

                if (!_fontHeaders.TryGetValue(extension, out var expectedHeader) || !expectedHeader.SequenceEqual(buffer))
                {
                    Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomFont_Invalid, MessageBoxImage.Error);
                    return;
                }

                TextFontTask.NewState = dialog.FileName;
            }

            OnPropertyChanged(nameof(ChooseCustomFontVisibility));
            OnPropertyChanged(nameof(DeleteCustomFontVisibility));
        }

        private static void OpenCompatSettings()
        {
            try
            {
                string path = new RobloxPlayerData().ExecutablePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    PInvoke.SHObjectProperties(HWND.Null, SHOP_TYPE.SHOP_FILEPATH, path, "Compatibility");
                else
                    Frontend.ShowMessageBox(Strings.Common_RobloxNotInstalled, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"{Strings.Common_CompatSettings_OpenFailed}\n{ex.Message}", MessageBoxImage.Error);
            }
        }

        public async Task AddCustomDeathSound()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "OGG Audio (*.ogg)|*.ogg",
                Title = Strings.Menu_Mods_Misc_CustomDeathSound_Select
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(SoundsDir);
                    File.Copy(dialog.FileName, Path.Combine(SoundsDir, "oof.ogg"), overwrite: true);
                });
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"{Strings.Menu_Mods_Misc_CustomDeathSound_AddFailed}\n{ex.Message}", MessageBoxImage.Error);
                return;
            }

            OnPropertyChanged(nameof(ChooseCustomDeathSoundVisibility));
            OnPropertyChanged(nameof(DeleteCustomDeathSoundVisibility));
        }

        public async Task RemoveCustomDeathSound() =>
            await RemoveCustomFileAsync(DeathSoundFiles, SoundsDir,
                Strings.Menu_Mods_Misc_CustomDeathSound_NotFound, silent: false,
                () =>
                {
                    OnPropertyChanged(nameof(ChooseCustomDeathSoundVisibility));
                    OnPropertyChanged(nameof(DeleteCustomDeathSoundVisibility));
                });

        public async Task AddCustomCursor() =>
            await AddCustomImageAsync(["ArrowCursor.png"], CursorDir,
                Strings.Menu_Mods_Misc_CustomCursorFeatures_SelectCursor, "cursor", CursorSize,
                () =>
                {
                    OnPropertyChanged(nameof(ArrowCursorPreview));
                    OnPropertyChanged(nameof(ChooseCustomCursorVisibility));
                    OnPropertyChanged(nameof(DeleteCustomCursorVisibility));
                });

        public async Task AddCustomArrowFarCursor() =>
            await AddCustomImageAsync(["ArrowFarCursor.png"], CursorDir,
                Strings.Menu_Mods_Misc_CustomCursorFeatures_SelectArrowFar, "arrow far cursor", CursorSize,
                () =>
                {
                    OnPropertyChanged(nameof(ArrowFarCursorPreview));
                    OnPropertyChanged(nameof(ChooseCustomCursorVisibility));
                    OnPropertyChanged(nameof(DeleteCustomCursorVisibility));
                });

        public async Task AddCustomIBeamCursor() =>
            await AddCustomImageAsync(["IBeamCursor.png"], CursorDir,
                Strings.Menu_Mods_Misc_CustomCursorFeatures_SelectIBeam, "IBeam cursor", CursorSize,
                () =>
                {
                    OnPropertyChanged(nameof(IBeamCursorPreview));
                    OnPropertyChanged(nameof(ChooseCustomCursorVisibility));
                    OnPropertyChanged(nameof(DeleteCustomCursorVisibility));
                });

        public async Task AddCustomShiftlockCursor() =>
            await AddCustomImageAsync(ShiftlockCursorFiles, TextureDir,
                Strings.Menu_Mods_Misc_CustomCursorFeatures_SelectShiftlock, "Shiftlock cursor", ShiftlockCursorSize,
                () =>
                {
                    OnPropertyChanged(nameof(ShiftlockCursorPreview));
                    OnPropertyChanged(nameof(ChooseCustomShiftlockCursorVisibility));
                    OnPropertyChanged(nameof(DeleteCustomShiftlockCursorVisibility));
                });

        public async Task RemoveCustomShiftlockCursor() =>
            await RemoveCustomFileAsync(ShiftlockCursorFiles, TextureDir,
                Strings.Menu_Mods_Misc_CustomCursor_NotFound_Shiftlock, silent: false,
                () =>
                {
                    OnPropertyChanged(nameof(ShiftlockCursorPreview));
                    OnPropertyChanged(nameof(ChooseCustomShiftlockCursorVisibility));
                    OnPropertyChanged(nameof(DeleteCustomShiftlockCursorVisibility));
                });

        public async Task RemoveSelectedCursor()
        {
            if (SelectedCursorSlot is null) return;

            var (files, dir, notFound) = SelectedCursorSlot switch
            {
                "Shiftlock" => (ShiftlockCursorFiles, TextureDir, Strings.Menu_Mods_Misc_CustomCursor_NotFound_Shiftlock),
                "Arrow" => (new[] { "ArrowCursor.png" }, CursorDir, Strings.Menu_Mods_Misc_CustomCursor_NotFound_Arrow),
                "ArrowFar" => (new[] { "ArrowFarCursor.png" }, CursorDir, Strings.Menu_Mods_Misc_CustomCursor_NotFound_ArrowFar),
                "IBeam" => (new[] { "IBeamCursor.png" }, CursorDir, Strings.Menu_Mods_Misc_CustomCursor_NotFound_IBeam),
                _ => (Array.Empty<string>(), string.Empty, string.Empty)
            };

            if (files.Length == 0) return;

            await RemoveCustomFileAsync(files, dir, notFound, silent: false,
                () => { RefreshAllCursorPreviews(); SelectedCursorSlot = null; });
        }

        public async Task RemoveAllCursors()
        {
            if (Frontend.ShowMessageBox(
                    Strings.Menu_Mods_Misc_CustomCursor_RemoveAllConfirm,
                    MessageBoxImage.Warning,
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            await RemoveCustomFileAsync(ArrowCursorFiles, CursorDir, string.Empty, silent: true, null);
            await RemoveCustomFileAsync(ShiftlockCursorFiles, TextureDir, string.Empty, silent: true, null);

            SelectedCursorSlot = null;
            RefreshAllCursorPreviews();
        }

        public async Task ImportCursorSet()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ZIP Archive (*.zip)|*.zip",
                Title = Strings.Menu_Mods_Misc_CustomCursorFeatures_Import
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(dialog.FileName);
                    Directory.CreateDirectory(CursorDir);
                    Directory.CreateDirectory(TextureDir);

                    var knownFiles = new Dictionary<string, string>
                    {
                        { "ArrowCursor.png",       CursorDir  },
                        { "ArrowFarCursor.png",    CursorDir  },
                        { "IBeamCursor.png",       CursorDir  },
                        { "MouseLockedCursor.png", TextureDir }
                    };

                    foreach (var entry in archive.Entries)
                    {
                        string name = Path.GetFileName(entry.FullName);
                        if (knownFiles.TryGetValue(name, out string? destDir))
                            entry.ExtractToFile(Path.Combine(destDir, name), overwrite: true);
                    }
                });
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"{Strings.Menu_Mods_Misc_CustomCursor_ImportFailed}\n{ex.Message}", MessageBoxImage.Error);
                return;
            }

            RefreshAllCursorPreviews();
        }

        public async Task ExportCursorSet()
        {
            var filesToExport = new List<(string path, string name)>();

            foreach (var name in ArrowCursorFiles)
            {
                string path = Path.Combine(CursorDir, name);
                if (File.Exists(path)) filesToExport.Add((path, name));
            }

            string shiftlockPath = Path.Combine(TextureDir, "MouseLockedCursor.png");
            if (File.Exists(shiftlockPath)) filesToExport.Add((shiftlockPath, "MouseLockedCursor.png"));

            if (filesToExport.Count == 0)
            {
                Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomCursor_NoneToExport, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "ZIP Archive (*.zip)|*.zip",
                Title = Strings.Menu_Mods_Misc_CustomCursorFeatures_Export,
                FileName = "CursorSet.zip"
            };
            if (dialog.ShowDialog() != true) return;

            string zipPath = dialog.FileName;
            try
            {
                await Task.Run(() =>
                {
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                    using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                    foreach (var (path, name) in filesToExport)
                        archive.CreateEntryFromFile(path, name);
                });
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"{Strings.Menu_Mods_Misc_CustomCursor_ExportFailed}\n{ex.Message}", MessageBoxImage.Error);
                return;
            }

            Frontend.ShowMessageBox(
                string.Format(Strings.Menu_Mods_Misc_CustomCursor_ExportSuccess, zipPath),
                MessageBoxImage.Information);
        }

        private void BrowseCustomRobloxIcon()
        {
            var dialog = new OpenFileDialog
            {
                Filter = $"{Strings.Menu_IconFiles}|*.ico",
                Title = "Select a custom Roblox icon"
            };
            if (dialog.ShowDialog() != true) return;
            CustomRobloxIconLocation = dialog.FileName;
        }

        public static string? ResolveRobloxIconPath()
        {
            var setting = App.Settings.Prop.CustomRobloxIcon;

            if (setting == RobloxIcon.Default) return null;

            if (setting == RobloxIcon.Custom)
            {
                string loc = App.Settings.Prop.CustomRobloxIconLocation;
                return File.Exists(loc) ? loc : null;
            }

            BootstrapperIcon? mapped = MapRobloxIconToBootstrapper(setting);
            if (mapped is null) return null;

            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), $"BubblestrapRobloxIcon_{setting}.ico");
                if (!File.Exists(tempPath))
                {
                    using var icon = mapped.Value.GetIcon();
                    using var fs = File.OpenWrite(tempPath);
                    icon.Save(fs);
                }
                return tempPath;
            }
            catch { return null; }
        }

        internal static BootstrapperIcon? MapRobloxIconToBootstrapper(RobloxIcon setting) => setting switch
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

        public static void ApplyRobloxIcon()
        {
            string? iconPath = ResolveRobloxIconPath();

            string robloxExe = string.Empty;
            if (iconPath is null)
            {
                try { robloxExe = new RobloxPlayerData().ExecutablePath; }
                catch { }
            }

            string[] candidates =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),                "Roblox Player.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Roblox Player.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),    "Programs", "Roblox", "Roblox Player.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "Roblox", "Roblox Player.lnk"),
            ];

            bool anyChanged = false;
            foreach (string lnk in candidates)
            {
                if (!File.Exists(lnk)) continue;
                try { SetShortcutIcon(lnk, iconPath ?? robloxExe); anyChanged = true; }
                catch { }
            }

            if (anyChanged)
                SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        public static void ApplyRobloxWindowIcon(IntPtr robloxHwnd)
        {
            if (App.Settings.Prop.CustomRobloxIcon == RobloxIcon.Default) return;

            string? iconPath = ResolveRobloxIconPath();
            if (iconPath is null || !File.Exists(iconPath)) return;

            try
            {
                int smallSize = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
                int bigSize = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXICON);

                IntPtr hIconSmall = NativeMethods.LoadImage(
                    IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON, smallSize, smallSize, NativeMethods.LR_LOADFROMFILE);

                IntPtr hIconBig = NativeMethods.LoadImage(
                    IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON, bigSize, bigSize, NativeMethods.LR_LOADFROMFILE);

                if (hIconSmall == IntPtr.Zero && hIconBig == IntPtr.Zero) return;

                _activeIconHandles.TryGetValue(robloxHwnd, out var oldHandles);

                if (hIconSmall != IntPtr.Zero)
                {
                    NativeMethods.SendMessage(robloxHwnd, NativeMethods.WM_SETICON, (IntPtr)NativeMethods.ICON_SMALL, hIconSmall);
                    NativeMethods.SetClassLongPtr(robloxHwnd, NativeMethods.GCLP_HICONSM, hIconSmall);
                }

                if (hIconBig != IntPtr.Zero)
                {
                    NativeMethods.SendMessage(robloxHwnd, NativeMethods.WM_SETICON, (IntPtr)NativeMethods.ICON_BIG, hIconBig);
                    NativeMethods.SetClassLongPtr(robloxHwnd, NativeMethods.GCLP_HICON, hIconBig);
                }

                _activeIconHandles[robloxHwnd] = (hIconSmall, hIconBig);

                if (oldHandles.Small != IntPtr.Zero) NativeMethods.DestroyIcon(oldHandles.Small);
                if (oldHandles.Big != IntPtr.Zero) NativeMethods.DestroyIcon(oldHandles.Big);
            }
            catch { }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010B-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder ppszFileName);
        }

        private static void SetShortcutIcon(string lnkPath, string iconPath)
        {
            var link = (IShellLinkW)new ShellLink();
            var file = (IPersistFile)link;
            file.Load(lnkPath, 0);
            link.SetIconLocation(iconPath, 0);
            file.Save(lnkPath, true);
        }
    }

    internal static class NativeMethods
    {
        public const int SM_CXSMICON = 49;
        public const int SM_CXICON = 11;
        public const int GCLP_HICON = -14;
        public const int GCLP_HICONSM = -34;
        public const uint IMAGE_ICON = 1;
        public const uint LR_LOADFROMFILE = 0x10;
        public const int WM_SETICON = 0x0080;
        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;
        public const int SHCNE_ASSOCCHANGED = 0x08000000;
        public const int SHCNF_IDLIST = 0x0000;

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        public static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
    }

    public class RobloxIconEntry : NotifyPropertyChangedViewModel
    {
        private ImageSource? _previewImage;
        private bool _previewLoaded;

        public RobloxIcon IconType { get; }

        public RobloxIconEntry(RobloxIcon iconType)
        {
            IconType = iconType;
        }

        public ImageSource? PreviewImage
        {
            get
            {
                if (_previewLoaded) return _previewImage;
                var result = LoadPreview();
                if (result is not null) { _previewLoaded = true; _previewImage = result; }
                return result;
            }
        }

        public void RefreshPreview()
        {
            _previewLoaded = false;
            _previewImage = null;
            OnPropertyChanged(nameof(PreviewImage));
        }

        private ImageSource? LoadPreview()
        {
            try
            {
                if (IconType == RobloxIcon.Default) return LoadDefaultRobloxIcon();
                if (IconType == RobloxIcon.Custom) return LoadCustomIcon(App.Settings.Prop.CustomRobloxIconLocation);

                var mapped = ModsViewModel.MapRobloxIconToBootstrapper(IconType);
                return mapped is null ? null : IconToBitmapSource16(mapped.Value.GetIcon());
            }
            catch { return null; }
        }

        private static ImageSource? LoadDefaultRobloxIcon()
        {
            try
            {
                string exePath = new RobloxPlayerData().ExecutablePath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                return icon is null ? null : IconToBitmapSource16(icon);
            }
            catch { return null; }
        }

        private static ImageSource? LoadCustomIcon(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var fs = File.OpenRead(path);
                using var icon = new System.Drawing.Icon(fs, 16, 16);
                return IconToBitmapSource16(icon);
            }
            catch { return null; }
        }

        private static BitmapSource IconToBitmapSource16(System.Drawing.Icon icon)
        {
            using var ms = new System.IO.MemoryStream();
            icon.Save(ms);
            ms.Position = 0;
            using var icon16 = new System.Drawing.Icon(ms, 16, 16);
            var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon16.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));
            bmp.Freeze();
            return bmp;
        }
    }

    public class FilePathToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BitmapImage bmp) return bmp;
            if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}