using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class ChartCandleSqliteCacheStore
    {
        private const string RelativePath = "Config/chart_candle_cache.db";
        private const string TableName = "chart_candles";
        private const string LegacyMinuteTableName = "chart_minute_candles";

        private readonly object _sync = new();
        private readonly string _path;
        private bool _initialized;

        public ChartCandleSqliteCacheStore(string? path = null)
        {
            _path = string.IsNullOrWhiteSpace(path) ? ResolveDefaultPath() : path;
        }

        public bool TryGet(string code, bool useNxtMarket, string period, int count, out List<DailyCandle> candles) =>
            TryGet(code, useNxtMarket ? "NXT" : "KRX", period, count, out candles);

        public bool TryGet(string code, string market, string period, int count, out List<DailyCandle> candles)
        {
            candles = [];
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(period) || count <= 0)
                return false;

            lock (_sync)
            {
                EnsureInitialized();
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT candle_key, open_price, high_price, low_price, close_price, volume, trading_value
                    FROM {TableName}
                    WHERE code = $code
                      AND market = $market
                      AND period = $period
                    ORDER BY candle_key DESC
                    LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$code", NormalizeCode(code));
                command.Parameters.AddWithValue("$market", NormalizeMarket(market));
                command.Parameters.AddWithValue("$period", period);
                command.Parameters.AddWithValue("$limit", Math.Max(1, count));

                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    candles.Add(new DailyCandle
                    {
                        Date = reader.GetString(0),
                        Open = reader.GetDouble(1),
                        High = reader.GetDouble(2),
                        Low = reader.GetDouble(3),
                        Close = reader.GetDouble(4),
                        Volume = reader.GetInt64(5),
                        TradingValue = reader.GetInt64(6)
                    });
                }
            }

            candles.Reverse();
            return candles.Count > 0;
        }

        public void Upsert(
            string code,
            bool useNxtMarket,
            string period,
            IEnumerable<DailyCandle> candles,
            int maxCount = 0,
            string source = "REST") =>
            Upsert(code, useNxtMarket ? "NXT" : "KRX", period, candles, maxCount, source);

        public void Upsert(
            string code,
            string market,
            string period,
            IEnumerable<DailyCandle> candles,
            int maxCount = 0,
            string source = "REST")
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(period))
                return;

            List<DailyCandle> snapshot = [.. (candles ?? [])
                .Where(candle => candle != null && !string.IsNullOrWhiteSpace(candle.Date) && candle.Close > 0)
                .OrderBy(candle => candle.Date, StringComparer.Ordinal)];

            if (maxCount > 0)
                snapshot = [.. snapshot.TakeLast(maxCount)];

            if (snapshot.Count == 0)
                return;

            string normalizedCode = NormalizeCode(code);
            string normalizedMarket = NormalizeMarket(market);
            string updatedAt = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string normalizedSource = string.IsNullOrWhiteSpace(source) ? "REST" : source.Trim();

            lock (_sync)
            {
                EnsureInitialized();
                using SqliteConnection connection = OpenConnection();
                using SqliteTransaction transaction = connection.BeginTransaction();

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = $"""
                        INSERT INTO {TableName}
                            (code, market, period, candle_key, open_price, high_price, low_price, close_price, volume, trading_value, source, updated_at)
                        VALUES
                            ($code, $market, $period, $candle_key, $open_price, $high_price, $low_price, $close_price, $volume, $trading_value, $source, $updated_at)
                        ON CONFLICT(code, market, period, candle_key) DO UPDATE SET
                            open_price = excluded.open_price,
                            high_price = excluded.high_price,
                            low_price = excluded.low_price,
                            close_price = excluded.close_price,
                            volume = excluded.volume,
                            trading_value = excluded.trading_value,
                            source = excluded.source,
                            updated_at = excluded.updated_at;
                        """;

                    SqliteParameter codeParameter = command.Parameters.Add("$code", SqliteType.Text);
                    SqliteParameter marketParameter = command.Parameters.Add("$market", SqliteType.Text);
                    SqliteParameter periodParameter = command.Parameters.Add("$period", SqliteType.Text);
                    SqliteParameter candleKeyParameter = command.Parameters.Add("$candle_key", SqliteType.Text);
                    SqliteParameter openParameter = command.Parameters.Add("$open_price", SqliteType.Real);
                    SqliteParameter highParameter = command.Parameters.Add("$high_price", SqliteType.Real);
                    SqliteParameter lowParameter = command.Parameters.Add("$low_price", SqliteType.Real);
                    SqliteParameter closeParameter = command.Parameters.Add("$close_price", SqliteType.Real);
                    SqliteParameter volumeParameter = command.Parameters.Add("$volume", SqliteType.Integer);
                    SqliteParameter tradingValueParameter = command.Parameters.Add("$trading_value", SqliteType.Integer);
                    SqliteParameter sourceParameter = command.Parameters.Add("$source", SqliteType.Text);
                    SqliteParameter updatedAtParameter = command.Parameters.Add("$updated_at", SqliteType.Text);

                    codeParameter.Value = normalizedCode;
                    marketParameter.Value = normalizedMarket;
                    periodParameter.Value = period;
                    sourceParameter.Value = normalizedSource;
                    updatedAtParameter.Value = updatedAt;

                    foreach (DailyCandle candle in snapshot)
                    {
                        candleKeyParameter.Value = candle.Date.Trim();
                        openParameter.Value = candle.Open;
                        highParameter.Value = candle.High;
                        lowParameter.Value = candle.Low;
                        closeParameter.Value = candle.Close;
                        volumeParameter.Value = Math.Max(0, candle.Volume);
                        tradingValueParameter.Value = Math.Max(0, candle.TradingValue);
                        command.ExecuteNonQuery();
                    }
                }

                if (maxCount > 0)
                    Prune(connection, transaction, normalizedCode, normalizedMarket, period, maxCount);

                transaction.Commit();
            }
        }

        private static void Prune(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string code,
            string market,
            string period,
            int maxCount)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                DELETE FROM {TableName}
                WHERE code = $code
                  AND market = $market
                  AND period = $period
                  AND candle_key NOT IN (
                      SELECT candle_key
                      FROM {TableName}
                      WHERE code = $code
                        AND market = $market
                        AND period = $period
                      ORDER BY candle_key DESC
                      LIMIT $limit
                  );
                """;
            command.Parameters.AddWithValue("$code", code);
            command.Parameters.AddWithValue("$market", market);
            command.Parameters.AddWithValue("$period", period);
            command.Parameters.AddWithValue("$limit", Math.Max(1, maxCount));
            command.ExecuteNonQuery();
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using SqliteConnection connection = OpenConnection();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL;";
                command.ExecuteNonQuery();
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA busy_timeout=5000;";
                command.ExecuteNonQuery();
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS {TableName} (
                        code TEXT NOT NULL,
                        market TEXT NOT NULL,
                        period TEXT NOT NULL,
                        candle_key TEXT NOT NULL,
                        open_price REAL NOT NULL,
                        high_price REAL NOT NULL,
                        low_price REAL NOT NULL,
                        close_price REAL NOT NULL,
                        volume INTEGER NOT NULL,
                        trading_value INTEGER NOT NULL DEFAULT 0,
                        source TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        PRIMARY KEY (code, market, period, candle_key)
                    );
                    """;
                command.ExecuteNonQuery();
            }

            MigrateLegacyMinuteTable(connection);

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = $"""
                    CREATE INDEX IF NOT EXISTS idx_chart_candles_lookup
                    ON {TableName} (code, market, period, candle_key DESC);
                    """;
                command.ExecuteNonQuery();
            }

            _initialized = true;
        }

        private static void MigrateLegacyMinuteTable(SqliteConnection connection)
        {
            using (SqliteCommand existsCommand = connection.CreateCommand())
            {
                existsCommand.CommandText = """
                    SELECT name
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name = $table_name;
                    """;
                existsCommand.Parameters.AddWithValue("$table_name", LegacyMinuteTableName);
                if (existsCommand.ExecuteScalar() == null)
                    return;
            }

            using SqliteCommand migrateCommand = connection.CreateCommand();
            migrateCommand.CommandText = $"""
                INSERT OR IGNORE INTO {TableName}
                    (code, market, period, candle_key, open_price, high_price, low_price, close_price, volume, trading_value, source, updated_at)
                SELECT code, market, period, candle_key, open_price, high_price, low_price, close_price, volume, trading_value, source, updated_at
                FROM {LegacyMinuteTableName};
                """;
            migrateCommand.ExecuteNonQuery();
        }

        private SqliteConnection OpenConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                Mode = SqliteOpenMode.ReadWriteCreate
            };

            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return connection;
        }

        private static string NormalizeMarket(string market)
        {
            string text = (market ?? string.Empty).Trim().ToUpperInvariant();
            if (text.Contains("AL", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("SOR", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("UNIFIED", StringComparison.OrdinalIgnoreCase))
            {
                return "AL";
            }

            if (text.Contains("NXT", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("NX", StringComparison.OrdinalIgnoreCase))
            {
                return "NXT";
            }

            return "KRX";
        }

        private static string NormalizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            string text = code.Trim().ToUpperInvariant();
            if (text.StartsWith("A", StringComparison.OrdinalIgnoreCase) && text.Length >= 7)
                text = text[1..];
            if (text.EndsWith("_NX", StringComparison.OrdinalIgnoreCase))
                text = text[..^3];
            if (text.EndsWith("_AL", StringComparison.OrdinalIgnoreCase))
                text = text[..^3];
            return text;
        }

        private static string ResolveDefaultPath()
        {
            string? foundFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(foundFromCurrent))
                return Path.Combine(foundFromCurrent, "chart_candle_cache.db");

            string? foundFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(foundFromBase))
                return Path.Combine(foundFromBase, "chart_candle_cache.db");

            return Path.Combine(Directory.GetCurrentDirectory(), RelativePath);
        }

        private static string? SearchUpwards(string startDirectory, string childDirectory)
        {
            var current = new DirectoryInfo(startDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, childDirectory);
                if (Directory.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }

            return null;
        }
    }
}
