using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net.Http.Json;

namespace Bloxstrap.RobloxInterfaces
{
    public class ApplicationSettings
    {
        private readonly string _applicationName;
        private readonly string _channelName;
        private readonly SemaphoreSlim _fetchLock = new(1, 1);
        private Dictionary<string, string>? _flags;
        private bool _initialised;

        private static readonly ConcurrentDictionary<(string, string), ApplicationSettings> _cache = new();

        private ApplicationSettings(string applicationName, string channelName)
        {
            _applicationName = applicationName;
            _channelName = channelName.ToLowerInvariant();
        }

        private async Task EnsureFetched()
        {
            if (_initialised) return;

            await _fetchLock.WaitAsync();
            try
            {
                if (_initialised) return;

                string path = $"/v2/settings/application/{_applicationName}";
                if (_channelName != Deployment.DefaultChannel.ToLowerInvariant())
                    path += $"/bucket/{_channelName}";

                _flags = await FetchInternal(path);
                _initialised = true;
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        private async Task<Dictionary<string, string>> FetchInternal(string path)
        {
            string[] hosts = { "https://clientsettingscdn.roblox.com", "https://clientsettings.roblox.com" };

            foreach (var host in hosts)
            {
                try
                {
                    var response = await App.HttpClient.GetAsync(host + path);
                    if (response.IsSuccessStatusCode)
                    {
                        var data = await response.Content.ReadFromJsonAsync<ClientFlagSettings>();
                        if (data?.ApplicationSettings != null) return data.ApplicationSettings;
                    }
                }
                catch { continue; }
            }
            throw new Exception("Failed to fetch settings from all endpoints.");
        }

        public async Task<T?> GetAsync<T>(string name)
        {
            await EnsureFetched();
            if (_flags == null || !_flags.TryGetValue(name, out var value)) return default;

            try
            {
                return (T?)TypeDescriptor.GetConverter(typeof(T)).ConvertFromString(value);
            }
            catch { return default; }
        }

        public T? Get<T>(string name) => GetAsync<T>(name).GetAwaiter().GetResult();

        public static ApplicationSettings PCDesktopClient => GetSettings("PCDesktopClient");
        public static ApplicationSettings PCClientBootstrapper => GetSettings("PCClientBootstrapper");

        public static ApplicationSettings GetSettings(string applicationName, string channelName = Deployment.DefaultChannel)
        {
            var key = (applicationName, channelName.ToLowerInvariant());
            return _cache.GetOrAdd(key, k => new ApplicationSettings(k.Item1, k.Item2));
        }
    }
}