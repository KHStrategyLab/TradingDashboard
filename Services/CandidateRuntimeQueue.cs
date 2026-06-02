using System;
using System.Collections.Generic;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class CandidateRuntimeQueue
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, CandidateRuntimeEntry> _entriesByKey = new(StringComparer.Ordinal);

        public bool AddOrUpdate(CandidateRuntimeEntry entry, out CandidateRuntimeEntry stored)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Code))
                throw new ArgumentException("Candidate runtime entry must have a code.", nameof(entry));

            entry.CandidateDate = NormalizeDate(entry.CandidateDate);
            entry.Market = NormalizeMarket(entry.Market);
            entry.Key = BuildKey(entry.Code, entry.Market, entry.CandidateDate);
            entry.UpdatedAt = string.IsNullOrWhiteSpace(entry.UpdatedAt)
                ? DateTime.Now.ToString("yyyyMMddHHmmss")
                : entry.UpdatedAt;

            lock (_sync)
            {
                bool added = !_entriesByKey.ContainsKey(entry.Key);
                _entriesByKey[entry.Key] = entry;
                stored = entry;
                return added;
            }
        }

        public bool TryGet(string code, out CandidateRuntimeEntry? entry)
        {
            string normalizedCode = NormalizeCode(code);
            string today = DateTime.Now.ToString("yyyyMMdd");
            lock (_sync)
            {
                entry = _entriesByKey.Values
                    .Where(item => string.Equals(NormalizeCode(item.Code), normalizedCode, StringComparison.Ordinal) &&
                        string.Equals(item.CandidateDate, today, StringComparison.Ordinal))
                    .OrderByDescending(item => item.CandidateTime)
                    .FirstOrDefault();
                return entry != null;
            }
        }

        public IReadOnlyList<CandidateRuntimeEntry> Snapshot()
        {
            lock (_sync)
                return [.. _entriesByKey.Values
                    .OrderByDescending(item => item.CandidateTime)
                    .ThenBy(item => item.Code)];
        }

        public IReadOnlyList<CandidateRuntimeEntry> SnapshotToday()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            lock (_sync)
                return [.. _entriesByKey.Values
                    .Where(item => string.Equals(item.CandidateDate, today, StringComparison.Ordinal))
                    .OrderByDescending(item => item.CandidateTime)
                    .ThenBy(item => item.Code)];
        }

        public void ClearExpired(DateTime today)
        {
            string todayText = today.ToString("yyyyMMdd");
            lock (_sync)
            {
                foreach (string key in _entriesByKey
                    .Where(pair => !string.Equals(pair.Value.CandidateDate, todayText, StringComparison.Ordinal))
                    .Select(pair => pair.Key)
                    .ToList())
                {
                    _entriesByKey.Remove(key);
                }
            }
        }

        private static string BuildKey(string code, string market, string candidateDate) =>
            $"{NormalizeCode(code)}|{NormalizeMarket(market)}|{NormalizeDate(candidateDate)}";

        private static string NormalizeCode(string code)
        {
            string digits = new([.. (code ?? string.Empty).Where(char.IsDigit)]);
            return digits.Length >= 6 ? digits[^6..] : digits;
        }

        private static string NormalizeMarket(string market)
        {
            string text = (market ?? string.Empty).Trim().ToUpperInvariant();
            if (text.Contains("NXT", StringComparison.OrdinalIgnoreCase))
                return "NXT";
            return "KRX";
        }

        private static string NormalizeDate(string date)
        {
            string digits = new([.. (date ?? string.Empty).Where(char.IsDigit)]);
            return digits.Length >= 8 ? digits[..8] : DateTime.Now.ToString("yyyyMMdd");
        }
    }
}
