using Bloxstrap.AppData;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO.Compression;
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

        private readonly Dictionary<string, BitmapImage?> _presetPreviewCache = new();

        private readonly Dictionary<string, byte[]> _fontHeaders = new()
        {
            { "ttf", new byte[4] { 0x00, 0x01, 0x00, 0x00 } },
            { "otf", new byte[4] { 0x4F, 0x54, 0x54, 0x4F } },
            { "ttc", new byte[4] { 0x74, 0x74, 0x63, 0x66 } }
        };

        private static readonly Dictionary<Enums.CursorType, Dictionary<string, string>> PresetResourceNames = new()
        {
            {
                Enums.CursorType.From2006, new()
                {
                    { "ArrowCursor.png",    "Cursor.From2006.ArrowCursor.png"    },
                    { "ArrowFarCursor.png", "Cursor.From2006.ArrowFarCursor.png" }
                }
            },
            {
                Enums.CursorType.From2013, new()
                {
                    { "ArrowCursor.png",    "Cursor.From2013.ArrowCursor.png"    },
                    { "ArrowFarCursor.png", "Cursor.From2013.ArrowFarCursor.png" }
                }
            }
        };

        private string? _selectedCursorSlot;
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

            SelectShiftlockSlotCommand = new RelayCommand(() => SelectedCursorSlot = SelectedCursorSlot == "Shiftlock" ? null : "Shiftlock");
            SelectArrowSlotCommand = new RelayCommand(() => SelectedCursorSlot = SelectedCursorSlot == "Arrow" ? null : "Arrow");
            SelectArrowFarSlotCommand = new RelayCommand(() => SelectedCursorSlot = SelectedCursorSlot == "ArrowFar" ? null : "ArrowFar");
            SelectIBeamSlotCommand = new RelayCommand(() => SelectedCursorSlot = SelectedCursorSlot == "IBeam" ? null : "IBeam");
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

                if (IsCursorPresetActive)
                {
                    bool hasCustom = ArrowCursorFiles
                        .Any(f => File.Exists(Path.Combine(CursorDir, f)));

                    if (hasCustom)
                        Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomCursor_PresetConflictWarning, MessageBoxImage.Warning);
                }
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
            if (!File.Exists(path))
                return null;

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
            if (!IsCursorPresetActive)
                return null;

            if (!PresetResourceNames.TryGetValue(CursorTypeTask.NewState, out var resourceMap))
                return null;

            if (!resourceMap.TryGetValue(filename, out string? resourceName))
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

            var dialog = new OpenFileDialog
            {
                Filter = "PNG Image (*.png)|*.png",
                Title = dialogTitle
            };

            if (dialog.ShowDialog() != true)
                return;

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
                    if (!File.Exists(filePath))
                        continue;
                    try
                    {
                        File.Delete(filePath);
                        anyDeleted = true;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{name}: {ex.Message}");
                    }
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
                var dialog = new OpenFileDialog
                {
                    Filter = $"{Strings.Menu_FontFiles}|*.ttf;*.otf;*.ttc"
                };

                if (dialog.ShowDialog() != true)
                    return;

                string extension = Path.GetExtension(dialog.FileName).TrimStart('.').ToLowerInvariant();
                byte[] buffer = new byte[4];

                try
                {
                    using var fs = File.OpenRead(dialog.FileName);
                    fs.ReadExactly(buffer, 0, 4);
                }
                catch (Exception)
                {
                    Array.Clear(buffer, 0, 4);
                }

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

            if (dialog.ShowDialog() != true)
                return;

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
            await RemoveCustomFileAsync(
                DeathSoundFiles, SoundsDir,
                Strings.Menu_Mods_Misc_CustomDeathSound_NotFound,
                silent: false,
                () =>
                {
                    OnPropertyChanged(nameof(ChooseCustomDeathSoundVisibility));
                    OnPropertyChanged(nameof(DeleteCustomDeathSoundVisibility));
                });

        public async Task AddCustomCursor() =>
            await AddCustomImageAsync(
                ["ArrowCursor.png"], CursorDir,
                Strings.Menu_Mods_Misc_CustomCursorFeatures_SelectCursor,
                "cursor", CursorSize,
                () =>
                {
                    OnPropertyChanged(nameof(ArrowCursorPreview));
                    OnPropertyChanged(nameof(ChooseCustomCursorVisibility));
                    OnPropertyChanged(nameof(DeleteCustomCursorVisibility));
                });

        public async Task AddCustomArrowFarCursor() =>
            await AddCustomImageAsync(
                ["ArrowFarCursor.png"], CursorDir,
                Strings.Menu_Mods_Misc_CustomCursorFeatures_SelectArrowFar,
                "arrow far cursor", CursorSize,
                () =>
                {
                    OnPropertyChanged(nameof(ArrowFarCursorPreview));
                    OnPropertyChanged(nameof(ChooseCustomCursorVisibility));
                    OnPropertyChanged(nameof(DeleteCustomCursorVisibility));
                });

        public async Task AddCustomIBeamCursor() =>
            await AddCustomImageAsync(
                ["IBeamCursor.png"], CursorDir,
                Strings.Menu_Mods_Misc_CustomCursorFeatures_SelectIBeam,
                "IBeam cursor", CursorSize,
                () =>
                {
                    OnPropertyChanged(nameof(IBeamCursorPreview));
                    OnPropertyChanged(nameof(ChooseCustomCursorVisibility));
                    OnPropertyChanged(nameof(DeleteCustomCursorVisibility));
                });

        public async Task AddCustomShiftlockCursor() =>
            await AddCustomImageAsync(
                ShiftlockCursorFiles, TextureDir,
                Strings.Menu_Mods_Misc_CustomCursorFeatures_SelectShiftlock,
                "Shiftlock cursor", ShiftlockCursorSize,
                () =>
                {
                    OnPropertyChanged(nameof(ShiftlockCursorPreview));
                    OnPropertyChanged(nameof(ChooseCustomShiftlockCursorVisibility));
                    OnPropertyChanged(nameof(DeleteCustomShiftlockCursorVisibility));
                });

        public async Task RemoveCustomShiftlockCursor() =>
            await RemoveCustomFileAsync(
                ShiftlockCursorFiles, TextureDir,
                Strings.Menu_Mods_Misc_CustomCursor_NotFound_Shiftlock,
                silent: false,
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

            if (dialog.ShowDialog() != true)
                return;

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

            if (dialog.ShowDialog() != true)
                return;

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
    }

    public class FilePathToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BitmapImage bmp)
                return bmp;

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
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}