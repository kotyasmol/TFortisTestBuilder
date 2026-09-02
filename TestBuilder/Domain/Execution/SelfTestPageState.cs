using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TestBuilder.Domain.Execution
{
    public enum SelfTestPageLoadState
    {
        NotLoaded,
        Loading,
        Loaded,
        Error
    }

    public sealed record SelfTestPageSnapshot(
        SelfTestPageLoadState LoadState,
        string Url,
        string OutputPrefix,
        IReadOnlyDictionary<string, string> Fields,
        DateTimeOffset? LoadedAt,
        string ErrorMessage)
    {
        public static SelfTestPageSnapshot Empty { get; } = new(
            SelfTestPageLoadState.NotLoaded,
            string.Empty,
            string.Empty,
            EmptyFields(),
            null,
            string.Empty);

        private static IReadOnlyDictionary<string, string> EmptyFields()
        {
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Thread-safe shared state for the latest XML snapshot of the DUT test page.
    /// A successful publish replaces the complete field set, so removed XML fields
    /// cannot remain visible as stale values.
    /// </summary>
    public sealed class SelfTestPageState
    {
        private readonly object _sync = new();
        private SelfTestPageSnapshot _current = SelfTestPageSnapshot.Empty;

        public event Action<SelfTestPageSnapshot>? SnapshotChanged;

        public SelfTestPageSnapshot Current
        {
            get
            {
                lock (_sync)
                {
                    return _current;
                }
            }
        }

        public void Reset()
        {
            Publish(SelfTestPageSnapshot.Empty);
        }

        public void BeginLoading(string url, string outputPrefix)
        {
            var previous = Current;
            Publish(new SelfTestPageSnapshot(
                SelfTestPageLoadState.Loading,
                url ?? string.Empty,
                outputPrefix ?? string.Empty,
                previous.Fields,
                previous.LoadedAt,
                string.Empty));
        }

        public void SetLoaded(
            string url,
            string outputPrefix,
            IReadOnlyDictionary<string, string> fields)
        {
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                copy[field.Key] = field.Value;
            }

            Publish(new SelfTestPageSnapshot(
                SelfTestPageLoadState.Loaded,
                url ?? string.Empty,
                outputPrefix ?? string.Empty,
                new ReadOnlyDictionary<string, string>(copy),
                DateTimeOffset.Now,
                string.Empty));
        }

        public void SetError(string url, string outputPrefix, string errorMessage)
        {
            var previous = Current;
            Publish(new SelfTestPageSnapshot(
                SelfTestPageLoadState.Error,
                url ?? string.Empty,
                outputPrefix ?? string.Empty,
                previous.Fields,
                previous.LoadedAt,
                errorMessage ?? string.Empty));
        }

        private void Publish(SelfTestPageSnapshot snapshot)
        {
            lock (_sync)
            {
                _current = snapshot;
            }

            SnapshotChanged?.Invoke(snapshot);
        }
    }
}
