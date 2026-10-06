using Newtonsoft.Json;
using System.Text;
using TobaccoEntities.Models;

namespace TobacoServer.Models
{
    internal class ZonesConfigurator
    {
        public static readonly string Customchoice = "Свой вариант";
        private static readonly object SyncRoot = new();
        private static List<Zone> _zones = new();
        private static string? _loadedPath;

        public ZonesConfigurator(string fileName = "zones.json")
        {
            lock (SyncRoot)
            {
                var path = ConfigurationPath(fileName);
                if (_loadedPath != path)
                {
                    _zones = ReadZones(path);
                    _loadedPath = path;
                }
            }
        }
        public List<string> GetZoneNames()
        {
            var zones = new List<string>();
            using (var file = File.Open(Path.Combine(Directory.GetCurrentDirectory(), "Configuration", "zone_names.json"), FileMode.OpenOrCreate))
            {
                var buffer = new byte[file.Length];
                _ = file.Read(buffer);

                var zoneNames = JsonConvert.DeserializeObject<List<string>>(Encoding.UTF8.GetString(buffer), new JsonSerializerSettings() { Culture = System.Globalization.CultureInfo.CurrentCulture });
                if (zoneNames is not null)
                {
                    zones = zoneNames;
                }
            }
            zones.Add(Customchoice);
            return zones;
        }

        public List<Zone> GetZones()
        {
            lock (SyncRoot) return _zones.ToList();
        }

        public Task AddZone(Zone zone, string fileName = "zones.json")
        {
            Synchronize(zone, fileName);
            return Task.CompletedTask;
        }

        internal static void Synchronize(Zone zone, string fileName = "zones.json")
        {
            if (!ZoneGeometry.TryNormalize(zone, out var error)) throw new ArgumentException(error);
            lock (SyncRoot)
            {
                var path = ConfigurationPath(fileName);
                var zones = ReadZones(path);
                zones.RemoveAll(item => string.Equals(item.Name, zone.Name, StringComparison.OrdinalIgnoreCase)
                    && item.CameraAddress == zone.CameraAddress);
                zones.Add(zone);
                Persist(path, zones);
            }
        }

        internal static bool RemoveZone(string name, string cameraAddress)
        {
            lock (SyncRoot)
            {
                var path = ConfigurationPath("zones.json");
                var zones = ReadZones(path);
                if (zones.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
                    && item.CameraAddress == cameraAddress) == 0) return false;
                Persist(path, zones);
                return true;
            }
        }

        private static string ConfigurationPath(string fileName) =>
            Path.Combine(Directory.GetCurrentDirectory(), "Configuration", fileName);

        private static void Persist(string path, List<Zone> zones)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(zones, Formatting.Indented));
                File.Move(temporaryPath, path, overwrite: true);
                _zones = zones;
                _loadedPath = path;
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }

        private static List<Zone> ReadZones(string path)
        {
            if (!File.Exists(path)) return new();
            var zones = JsonConvert.DeserializeObject<List<Zone>>(File.ReadAllText(path)) ?? new();
            foreach (var zone in zones)
                if (!ZoneGeometry.TryNormalize(zone, out var error))
                    throw new InvalidOperationException($"Некорректная геометрия зоны '{zone?.Name}': {error}");
            return zones;
        }
    }
}
