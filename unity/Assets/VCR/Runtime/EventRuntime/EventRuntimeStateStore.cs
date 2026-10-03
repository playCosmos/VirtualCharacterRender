using System;
using System.Collections.Generic;

namespace VCR.Runtime.EventRuntime
{
    public sealed class EventRuntimeStateStore
    {
        private readonly Dictionary<string, double> _numbers =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts =
            new(StringComparer.Ordinal);

        public bool Contains(string key) =>
            !string.IsNullOrWhiteSpace(key) &&
            (_numbers.ContainsKey(key) || _texts.ContainsKey(key));

        public double GetNumber(string key, double fallback = 0.0) =>
            !string.IsNullOrWhiteSpace(key) &&
            _numbers.TryGetValue(key, out var value)
                ? value
                : fallback;

        public string GetText(string key, string fallback = null) =>
            !string.IsNullOrWhiteSpace(key) &&
            _texts.TryGetValue(key, out var value)
                ? value
                : fallback;

        public void SetNumber(string key, double value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _texts.Remove(key);
            _numbers[key] = value;
        }

        public void AddNumber(string key, double value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            SetNumber(key, GetNumber(key) + value);
        }

        public void SetText(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _numbers.Remove(key);
            _texts[key] = value;
        }

        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _numbers.Remove(key);
            _texts.Remove(key);
        }

        public void Clear()
        {
            _numbers.Clear();
            _texts.Clear();
        }
    }
}
