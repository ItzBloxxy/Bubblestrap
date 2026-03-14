namespace Bloxstrap
{
    public class JsonManager<T> where T : class, new()
    {
        public T OriginalProp { get; set; } = new();

        public T Prop { get; set; } = new();

        /// <summary>
        /// The file hash when last retrieved from disk
        /// </summary>
        public string? LastFileHash { get; private set; }

        public bool Loaded { get; set; } = false;

        public virtual string ClassName => typeof(T).Name;

        public virtual string ProfilesLocation => Path.Combine(Paths.Base, $"Profiles.json");

        public virtual string FileLocation => Path.Combine(Paths.Base, $"{ClassName}.json");

        public virtual string LOG_IDENT_CLASS => $"JsonManager<{ClassName}>";

        public virtual void Load(bool alertFailure = true)
        {
            string LOG_IDENT = $"{LOG_IDENT_CLASS}::Load";
            App.Logger.WriteLine(LOG_IDENT, $"Loading from {FileLocation}...");

            try
            {
                if (!File.Exists(FileLocation))
                {
                    App.Logger.WriteLine(LOG_IDENT, "File does not exist, skipping load.");
                    return;
                }

                string contents = File.ReadAllText(FileLocation);
                T? settings = JsonSerializer.Deserialize<T>(contents);

                if (settings is null)
                    throw new InvalidDataException("Deserialization returned null.");

                Prop = settings;
                OriginalProp = JsonSerializer.Deserialize<T>(contents) ?? new T();

                Loaded = true;
                LastFileHash = MD5Hash.FromString(contents);

                App.Logger.WriteLine(LOG_IDENT, "Loaded successfully!");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to load!");
                App.Logger.WriteException(LOG_IDENT, ex);

                if (alertFailure && File.Exists(FileLocation))
                {
                    string message = ClassName switch
                    {
                        nameof(Settings) => Strings.JsonManager_SettingsLoadFailed,
                        nameof(FastFlagManager) => Strings.JsonManager_FastFlagsLoadFailed,
                        _ => ""
                    };

                    if (!string.IsNullOrEmpty(message))
                        Frontend.ShowMessageBox($"{message}\n\n{ex.Message}", System.Windows.MessageBoxImage.Warning);

                    try
                    {
                        File.Copy(FileLocation, FileLocation + ".bak", true);
                    }
                    catch (Exception copyEx)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to create backup: {copyEx.Message}");
                    }
                }

                Save(); // Reset to defaults
            }
        }

        public virtual void Save()
        {
            string LOG_IDENT = $"{LOG_IDENT_CLASS}::Save";
            string tempPath = FileLocation + ".tmp";

            App.Logger.WriteLine(LOG_IDENT, $"Saving to {FileLocation}...");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FileLocation)!);

                string contents = JsonSerializer.Serialize(Prop, new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(tempPath, contents);

                if (File.Exists(FileLocation))
                    File.Replace(tempPath, FileLocation, null);
                else
                    File.Move(tempPath, FileLocation);

                LastFileHash = MD5Hash.FromString(contents);
                App.Logger.WriteLine(LOG_IDENT, "Save complete!");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to save");
                App.Logger.WriteException(LOG_IDENT, ex);

                if (File.Exists(tempPath)) File.Delete(tempPath);

                string errorMessage = string.Format(Resources.Strings.Bootstrapper_JsonManagerSaveFailed, ClassName, ex.Message);
                Frontend.ShowMessageBox(errorMessage, System.Windows.MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Is the file on disk different to the one deserialised during this session?
        /// </summary>
        public bool HasFileOnDiskChanged()
        {
            if (!File.Exists(FileLocation))
                return Loaded;

            try
            {
                return LastFileHash != MD5Hash.FromFile(FileLocation);
            }
            catch
            {
                return false;
            }
        }
    }
}
