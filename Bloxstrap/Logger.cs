namespace Bloxstrap
{
    public class Logger
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private FileStream? _filestream;

        public readonly List<string> History = new();
        public bool Initialized = false;
        public bool NoWriteMode = false;
        public string? FileLocation;

        public string AsDocument => string.Join('\n', History);

        public void Initialize(string basePath, bool useTempDir = false)
        {
            const string LOG_IDENT = "Logger::Initialize";

            string directory = useTempDir ? Path.Combine(basePath, "TempLogs") : Path.Combine(basePath, "Logs");
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
            string filename = $"AppLog_{timestamp}.log";
            string location = Path.Combine(directory, filename);

            WriteLine(LOG_IDENT, $"Initializing at {location}");

            if (Initialized)
            {
                WriteLine(LOG_IDENT, "Logger is already initialized");
                return;
            }

            Directory.CreateDirectory(directory);

            if (File.Exists(location))
            {
                WriteLine(LOG_IDENT, "Log file already exists");
                return;
            }

            try
            {
                _filestream = File.Open(location, FileMode.Create, FileAccess.Write, FileShare.Read);
            }
            catch (IOException)
            {
                WriteLine(LOG_IDENT, "Failed to create log file");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                NoWriteMode = true;
                WriteLine(LOG_IDENT, $"Cannot write to {directory}");
                return;
            }

            Initialized = true;

            if (History.Count > 0)
                WriteToLog(string.Join("\r\n", History));

            WriteLine(LOG_IDENT, "Finished initializing!");
            FileLocation = location;

            // clean up old logs older than a week
            foreach (FileInfo log in new DirectoryInfo(directory).GetFiles())
            {
                if (log.LastWriteTimeUtc.AddDays(7) <= DateTime.UtcNow)
                {
                    WriteLine(LOG_IDENT, $"Deleting old log '{log.Name}'");
                    try { log.Delete(); } catch { }
                }
            }
        }

        private void WriteLine(string message)
        {
            string timestamp = DateTime.UtcNow.ToString("s") + "Z";
            string outlog = $"{timestamp} {message}";

            Debug.WriteLine(outlog);
            WriteToLog(outlog);

            History.Add(outlog);
        }

        public void WriteLine(string identifier, string message) => WriteLine($"[{identifier}] {message}");

        public void WriteException(string identifier, Exception ex)
        {
            string hresult = "0x" + ex.HResult.ToString("X8");
            WriteLine($"[{identifier}] ({hresult}) {ex}");
        }

        private async void WriteToLog(string message)
        {
            if (!Initialized || _filestream == null) return;

            try
            {
                await _semaphore.WaitAsync();
                await _filestream.WriteAsync(Encoding.UTF8.GetBytes($"{message}\r\n"));
                await _filestream.FlushAsync();
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
