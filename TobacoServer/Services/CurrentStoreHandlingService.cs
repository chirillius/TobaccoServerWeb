using Newtonsoft.Json;
using TobaccoEntities.Models;

namespace TobacoServer.Services
{
    public class CurrentStoreHandlingService : IDisposable
    {
        private Store _thisStore;
        private object _lock = new object();
        private readonly string filePath = Path.Join(Directory.GetCurrentDirectory(), "Configuration", "thisStore.json");

        public CurrentStoreHandlingService()
        {
            lock (_lock)
            {
                if (!File.Exists(filePath))
                {
                    _thisStore = new Store("", "", new List<Camera>());
                    SaveChanges();
                }
                else
                {
                    var jsonContent = File.ReadAllText(filePath);
                    _thisStore = JsonConvert.DeserializeObject<Store>(jsonContent) ?? new Store("", "", new List<Camera>());
                    NormalizeStore(_thisStore);
                }
            }
        }

        public Store GetStore()
        {
            lock (_lock)
            {
                return _thisStore;
            }
        }

        public void SetStore(Store store)
        {
            lock (_lock)
            {
                NormalizeStore(store);
                _thisStore = store;
                SaveChanges();
            }
        }

        private static void NormalizeStore(Store store)
        {
            store.Cameras ??= [];
            store.Employees ??= [];
        }

        private void SaveChanges()
        {
            File.WriteAllText(filePath, JsonConvert.SerializeObject(_thisStore, Formatting.Indented));
        }

        public void Dispose()
        {
            SaveChanges();
        }
    }
}
