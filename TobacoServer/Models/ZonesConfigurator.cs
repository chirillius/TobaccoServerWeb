using Newtonsoft.Json;
using System.Text;
using TobaccoEntities.Models;

namespace TobacoServer.Models
{
    internal class ZonesConfigurator
    {
        public static readonly string Customchoice = "Свой вариант";
        private static List<Zone> _zones = new List<Zone>();
        public ZonesConfigurator(string fileName = "zones.json")
        {
            if (_zones.Count == 0)
            {
                lock (_zones)
                {
                    if (_zones.Count == 0)
                    {
                        _zones = GetZonesFromFile(fileName);

                    }
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
            return _zones;
        }
        public async Task AddZone(Zone zone, string fileName = "zones.json")
        {
            _zones = GetZonesFromFile(fileName);
            File.Delete(fileName);
            WriteZoneToFile(zone, fileName);
        }

        private void WriteZoneToFile(Zone zone, string fileName)
        {
            if (zone.Rectangle.ToRect() != new OpenCvSharp.Rect())
            {
                if (_zones.FirstOrDefault(x => x.Name == zone.Name) != default(Zone))
                {
                    var i = _zones.IndexOf(_zones.First(x => x.Name == zone.Name));
                    _zones[i] = zone;
                }
                else
                {
                    _zones.Add(zone);

                }
                var json = JsonConvert.SerializeObject(_zones);
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Configuration", fileName), json);
            }
        }

        private List<Zone> GetZonesFromFile(string fileName = "zones.json")
        {
            List<Zone> zones = new List<Zone>();
            using (var file = File.Open(Path.Combine(Directory.GetCurrentDirectory(), "Configuration", fileName), FileMode.OpenOrCreate))
            {
                var buffer = new byte[file.Length];
                _ = file.Read(buffer);

                var a = Encoding.UTF8.GetString(buffer);
                var deserialized = JsonConvert.DeserializeObject<List<Zone>>(a);
                if (deserialized is not null)
                {
                    zones = deserialized;
                }
            }
            return zones;
        }
    }
}
