using System.Diagnostics;
using System.Text;

namespace TobacoServer.Services
{
    public class StreamingHandlerService : IDisposable
    {
        private static readonly Dictionary<int, Process> _processes = new();
        private static readonly Dictionary<int, string> _keys = new();
        private static readonly Dictionary<int, HashSet<string>> _consumersByUser = new();
        private static readonly Dictionary<int, DateTime> _lastActivityUtc = new();
        private static readonly object _lock = new();
        private static readonly TimeSpan _streamInactivityTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(5);

        private readonly string _playlistsPath = Path.Combine(Directory.GetCurrentDirectory(), "Playlists");
        private readonly Timer _cleanupTimer;

        public StreamingHandlerService()
        {
            _cleanupTimer = new Timer(_ => CleanupInactiveSlots(), null, _cleanupInterval, _cleanupInterval);
        }

        private string GenerateRandomKey()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var sb = new StringBuilder(10);

            for (int i = 0; i < 10; i++)
            {
                sb.Append(chars[random.Next(chars.Length)]);
            }

            return sb.ToString();
        }

        public string CreateEmptySlot(int id, CurrentStoreHandlingService currentStoreHandlingService)
        {
            lock (_lock)
            {
                var dir = Path.Combine(_playlistsPath, id.ToString());

                if (_processes.TryGetValue(id, out var existingProcess) && _keys.TryGetValue(id, out var existingToken))
                {
                    var existingPlaylistPath = Path.Combine(dir, $"{existingToken}.m3u8");
                    if (!existingProcess.HasExited && File.Exists(existingPlaylistPath))
                    {
                        _lastActivityUtc[id] = DateTime.UtcNow;
                        return existingToken;
                    }

                    StopSlotCore(id);
                }

                Directory.CreateDirectory(dir);

                var camera = currentStoreHandlingService
                    .GetStore()
                    .Cameras
                    .FirstOrDefault(x => x.Id == id);

                if (camera is null)
                {
                    throw new Exception("Camera not found");
                }

                var token = GenerateRandomKey();
                var playlistPath = Path.Combine(dir, $"{token}.m3u8");

                var args =
                    $"-i {camera.Address} " +
                    "-c:v copy -c:a aac -g 25 " +
                    "-hls_time 2 -hls_list_size 100 " +
                    $"-hls_segment_filename \"{dir}/segment_%03d.ts\" " +
                    $"\"{playlistPath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                var process = new Process { StartInfo = startInfo };
                process.Start();
                process.BeginErrorReadLine();

                _processes[id] = process;
                _keys[id] = token;
                _consumersByUser[id] = new HashSet<string>(StringComparer.Ordinal);
                _lastActivityUtc[id] = DateTime.UtcNow;

                return token;
            }
        }

        public void TakeSlot(int id, string userId)
        {
            lock (_lock)
            {
                if (!_processes.ContainsKey(id))
                {
                    return;
                }

                if (!_consumersByUser.TryGetValue(id, out var users))
                {
                    users = new HashSet<string>(StringComparer.Ordinal);
                    _consumersByUser[id] = users;
                }

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    users.Add(userId);
                }

                _lastActivityUtc[id] = DateTime.UtcNow;
            }
        }

        public void TouchSlotActivity(int id)
        {
            lock (_lock)
            {
                if (_processes.ContainsKey(id))
                {
                    _lastActivityUtc[id] = DateTime.UtcNow;
                }
            }
        }

        public void ReleaseSlot(int id, string userId)
        {
            lock (_lock)
            {
                if (!_processes.ContainsKey(id))
                {
                    return;
                }

                if (_consumersByUser.TryGetValue(id, out var users) && !string.IsNullOrWhiteSpace(userId))
                {
                    users.Remove(userId);
                    if (users.Count > 0)
                    {
                        _lastActivityUtc[id] = DateTime.UtcNow;
                        return;
                    }
                }

                StopSlotCore(id);
            }
        }

        private void CleanupInactiveSlots()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var staleIds = _lastActivityUtc
                    .Where(x => now - x.Value >= _streamInactivityTimeout)
                    .Select(x => x.Key)
                    .ToList();

                foreach (var id in staleIds)
                {
                    StopSlotCore(id);
                }
            }
        }

        private void StopSlotCore(int id)
        {
            if (!_processes.TryGetValue(id, out var process))
            {
                _keys.Remove(id);
                _consumersByUser.Remove(id);
                _lastActivityUtc.Remove(id);
                CleanupPlaylist(id);
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.WriteLine("q");
                    process.StandardInput.Flush();

                    if (!process.WaitForExit(3000))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                    }
                }
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                process.Dispose();
            }

            _processes.Remove(id);
            _keys.Remove(id);
            _consumersByUser.Remove(id);
            _lastActivityUtc.Remove(id);

            CleanupPlaylist(id);
        }

        private void CleanupPlaylist(int id)
        {
            var dir = Path.Combine(_playlistsPath, id.ToString());

            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch
            {
                // Ignore cleanup failures for files still being released by the OS.
            }
        }

        public string GetKey(int id) => _keys[id];

        public void Dispose()
        {
            _cleanupTimer.Dispose();
        }
    }
}
