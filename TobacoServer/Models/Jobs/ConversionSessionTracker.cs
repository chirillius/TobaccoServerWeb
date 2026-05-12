using TobaccoEntities.Models.Neuro;

namespace TobacoServer.Models.Jobs
{
    public sealed class ConversionSessionTracker
    {
        private readonly TimeSpan _finalizeAfterNoEntries;

        private DateTime _startedAt;
        private DateTime _lastEntryAt;
        private int _baselineClientCount;
        private int _peakClientCount;
        private int _lineCrossingSum;
        private int _maxObservedNewEntries;
        private bool _clientCountDroppedFromPeak;

        public ConversionSessionTracker(TimeSpan finalizeAfterNoEntries)
        {
            _finalizeAfterNoEntries = finalizeAfterNoEntries;
        }

        public bool HasActiveSession { get; private set; }
        public DateTime StartedAt => _startedAt;

        public bool ApplyObservation(DateTime observedAt, DirectionalEntryCountResponse observation)
        {
            var previousPeak = _peakClientCount;
            var stableClientCount = Math.Max(0, observation.StableClientCount);
            var shouldCapturePhoto = false;

            if (!HasActiveSession)
            {
                if (observation.NewEntries <= 0)
                {
                    return false;
                }

                HasActiveSession = true;
                _startedAt = observedAt;
                _lastEntryAt = observedAt;
                _baselineClientCount = Math.Max(0, observation.BaselineClientCount);
                _peakClientCount = Math.Max(_baselineClientCount, GetObservedPeak(observation));
                _clientCountDroppedFromPeak = false;
                _lineCrossingSum = observation.NewEntries;
                _maxObservedNewEntries = observation.NewEntries;
                return true;
            }

            _peakClientCount = Math.Max(_peakClientCount, GetObservedPeak(observation));

            if (observation.NewEntries > 0)
            {
                _lineCrossingSum += observation.NewEntries;
                _maxObservedNewEntries = Math.Max(_maxObservedNewEntries, observation.NewEntries);
                _lastEntryAt = observedAt;
                shouldCapturePhoto = true;
            }
            else if (_peakClientCount > _baselineClientCount && stableClientCount < _peakClientCount)
            {
                _clientCountDroppedFromPeak = true;
            }

            return shouldCapturePhoto || _peakClientCount > previousPeak;
        }

        public bool ShouldFinalize(DateTime now)
        {
            return HasActiveSession
                && (_clientCountDroppedFromPeak || now - _lastEntryAt >= _finalizeAfterNoEntries);
        }

        public ConversionSessionSnapshot Finalize()
        {
            if (!HasActiveSession)
            {
                throw new InvalidOperationException("Cannot finalize inactive conversion session.");
            }

            var zoneGrowth = Math.Max(0, _peakClientCount - _baselineClientCount);
            var peopleNumber = zoneGrowth > 0 ? zoneGrowth : _maxObservedNewEntries;
            var snapshot = new ConversionSessionSnapshot(
                _startedAt,
                _lastEntryAt,
                peopleNumber,
                _baselineClientCount,
                _peakClientCount,
                _lineCrossingSum);

            HasActiveSession = false;
            _startedAt = DateTime.MinValue;
            _lastEntryAt = DateTime.MinValue;
            _baselineClientCount = 0;
            _peakClientCount = 0;
            _lineCrossingSum = 0;
            _maxObservedNewEntries = 0;
            _clientCountDroppedFromPeak = false;

            return snapshot;
        }

        private static int GetObservedPeak(DirectionalEntryCountResponse observation)
        {
            return Math.Max(observation.PeakClientCount, observation.StableClientCount);
        }
    }

    public sealed record ConversionSessionSnapshot(
        DateTime StartedAt,
        DateTime LastEntryAt,
        int PeopleNumber,
        int BaselineClientCount,
        int PeakClientCount,
        int LineCrossingSum);
}
