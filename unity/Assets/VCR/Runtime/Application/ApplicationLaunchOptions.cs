using System;
using System.Collections.Generic;

namespace VCR.Runtime.Application
{
    public sealed class ApplicationLaunchOptions
    {
        private readonly Dictionary<string, string> _values;

        private ApplicationLaunchOptions(
            Dictionary<string, string> values)
        {
            _values = values;
        }

        public static ApplicationLaunchOptions Parse(
            string[] args)
        {
            var values =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            if (args == null)
            {
                return new ApplicationLaunchOptions(values);
            }

            for (var i = 0; i < args.Length; i++)
            {
                var raw = args[i];
                if (string.IsNullOrWhiteSpace(raw) ||
                    !raw.StartsWith(
                        "--",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var body = raw.Substring(2);
                var equals = body.IndexOf('=');

                if (equals > 0)
                {
                    values[
                        body.Substring(0, equals)] =
                        body.Substring(equals + 1);
                    continue;
                }

                if (i + 1 < args.Length &&
                    !args[i + 1].StartsWith(
                        "--",
                        StringComparison.Ordinal))
                {
                    values[body] = args[++i];
                }
                else
                {
                    values[body] = string.Empty;
                }
            }

            return new ApplicationLaunchOptions(values);
        }

        public bool TryGet(
            string key,
            out string value)
        {
            return _values.TryGetValue(
                key,
                out value);
        }

        public string GetOrDefault(
            string key,
            string fallback = null)
        {
            return
                _values.TryGetValue(
                    key,
                    out var value) &&
                !string.IsNullOrWhiteSpace(value)
                    ? value
                    : fallback;
        }
    }
}
