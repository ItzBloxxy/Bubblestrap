namespace Bloxstrap
{
    public class LocalDataManager : JsonManager<Models.APIs.Config.LocalDataBase>
    {
        public override string ClassName => nameof(LocalDataManager);

        public override string LOG_IDENT_CLASS => ClassName;

        public override string FileLocation => Path.Combine(Paths.Base, "Data.json");

        public bool Changed => !OriginalProp.Equals(Prop);

        public GenericTriState LoadedState = GenericTriState.Unknown;

        public event EventHandler DataLoaded = null!;

        public void Subscribe(EventHandler Handler)
        {
            switch (LoadedState)
            {
                case GenericTriState.Unknown:
                    DataLoaded += Handler;
                    break;
                case GenericTriState.Successful:
                    Handler(this, EventArgs.Empty);
                    break;
                default:
                    Handler(this, EventArgs.Empty);
                    break;
            }
        }

        public async Task WaitUntilDataFetched()
        {
            const int delay = 100;
            const int maxTries = 30; // 3 seconds
            int tries = 0;

            while (LoadedState == GenericTriState.Unknown)
            {
                await Task.Delay(delay);
                tries++;

                if (tries >= maxTries)
                    break;
            }
        }

        public Task LoadData()
        {
            const string LOG_IDENT = $"{nameof(LocalDataManager)}::LoadData";

            // load existing config
            this.Load(false);

            // sync url if file is outdated
            if (Prop.DeeplinkUrl != new Models.APIs.Config.LocalDataBase().DeeplinkUrl)
            {
                Prop.DeeplinkUrl = new Models.APIs.Config.LocalDataBase().DeeplinkUrl;
                this.Save();
            }

            LoadedState = GenericTriState.Successful;

            DataLoaded?.Invoke(this, EventArgs.Empty);

            return Task.CompletedTask;
        }
    }
}