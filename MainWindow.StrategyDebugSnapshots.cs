using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TradingDashboard.Models;
using TradingDashboard.Services.Strategies;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private static readonly JsonSerializerOptions StrategyDebugSnapshotJsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private const string StrategyChartSnapshotModeCurrent = "CURRENT_VIEW";
        private const string StrategyChartSnapshotModeContext = "CONTEXT_VIEW";
        private const string StrategyChartSnapshotModeStrategy15Minute = "STRATEGY_15M_VIEW";
        private const int StrategyDebugChartMinute = 15;
        private const int StrategyDebugChartCandleCount = 120;
        private static readonly bool StrategyDebugSellTestEnabled = false;

        private void SaveSelectedStrategyMinuteSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            WatchStockItem? stock = ResolveSelectedProgressStock();
            if (stock == null || string.IsNullOrWhiteSpace(stock.Code))
            {
                AppendLog("strategy minute debug snapshot skipped: no selected stock");
                return;
            }

            try
            {
                string directory = ResolveDebugSnapshotDirectory();
                Directory.CreateDirectory(directory);
                DateTime createdAt = DateTime.Now;
                string normalizedCode = NormalizeStockCode(stock.Code);
                string market = ShouldUseNxtDataForStock(stock) ? "NXT" : "KRX";
                string prefix = $"{createdAt:yyyyMMdd-HHmmss}-{normalizedCode}-{market}";
                StrategyChartDebugSnapshot? chart = SaveCurrentVisibleChartDebugSnapshot(directory, prefix);
                StrategyMinuteDebugSnapshot snapshot = BuildStrategyMinuteDebugSnapshot(stock, chart);

                string fileName = $"{prefix}-strategy-minute.json";
                string path = Path.Combine(directory, fileName);
                File.WriteAllText(path, JsonSerializer.Serialize(snapshot, StrategyDebugSnapshotJsonOptions));
                string requestPath = SaveAiReviewRequest(
                    directory,
                    prefix,
                    "manual selected",
                    path,
                    chart,
                    [snapshot],
                    IsStrategyMinuteDebugSnapshotReady(snapshot) ? 1 : 0,
                    1);

                StrategyMinuteDataLoadStatusText.Text = $"전략 장부+Progress 스냅샷 저장: {stock.Name} / {snapshot.Market}";
                AppendReadyLog($"strategy minute debug snapshot saved: {stock.Code} {stock.Name} / {snapshot.Market} / {path}{FormatChartSnapshotLogSuffix(chart)}{FormatAiReviewRequestLogSuffix(requestPath)}");
            }
            catch (Exception ex)
            {
                AppendLog($"strategy minute debug snapshot error: {ex.Message}");
            }
        }

        private Task SaveAllStrategyMinuteDebugSnapshotsAsync(string reason)
        {
            _isStrategyDebugSnapshotRunning = true;
            try
            {
                IReadOnlyList<WatchStockItem> targets = BuildStrategyDebugSnapshotTargets();
                if (targets.Count == 0)
                    return Task.CompletedTask;

                string directory = ResolveDebugSnapshotDirectory();
                Directory.CreateDirectory(directory);
                DateTime createdAt = DateTime.Now;
                string prefix = $"{createdAt:yyyyMMdd-HHmmss}-ALL";
                StrategyChartDebugSnapshot? chart = SaveCurrentVisibleChartDebugSnapshot(directory, prefix);

                List<StrategyMinuteDebugSnapshot> snapshots = [.. targets
                    .Select(stock => BuildStrategyMinuteDebugSnapshot(stock, null))];

                int readyCount = snapshots.Count(IsStrategyMinuteDebugSnapshotReady);
                int sellTestCandidateCount = snapshots.Count(snapshot => snapshot.SellTest.ShouldSellCandidate);
                StrategyMinuteDebugSnapshotBatch batch = new(
                    CreatedAt: createdAt,
                    Reason: reason,
                    MarketStatusCode: _lastMarketStatusCode,
                    MarketStatusText: _lastMarketStatusText,
                    IsNxtMarketMode: _isNxtMarketMode,
                    EngineStart: AutoTradingEnabledToggle?.IsChecked == true,
                    LiveOrders: LiveBuyEnabledToggle?.IsChecked == true,
                    TotalCount: snapshots.Count,
                    ReadyCount: readyCount,
                    VisibleChart: chart,
                    Snapshots: snapshots);

                string fileName = $"{prefix}-strategy-minute.json";
                string path = Path.Combine(directory, fileName);
                File.WriteAllText(path, JsonSerializer.Serialize(batch, StrategyDebugSnapshotJsonOptions));
                string requestPath = SaveAiReviewRequest(
                    directory,
                    prefix,
                    reason,
                    path,
                    chart,
                    snapshots,
                    readyCount,
                    snapshots.Count);

                AppendReadyLog($"strategy minute debug snapshot auto saved: {readyCount}/{snapshots.Count}stocks / selltest {sellTestCandidateCount}candidates / {path}{FormatChartSnapshotLogSuffix(chart)}{FormatAiReviewRequestLogSuffix(requestPath)}");
            }
            catch (Exception ex)
            {
                AppendLog($"strategy minute debug snapshot auto error: {ex.Message}");
            }
            finally
            {
                _isStrategyDebugSnapshotRunning = false;
            }

            return Task.CompletedTask;
        }

        private IReadOnlyList<WatchStockItem> BuildStrategyDebugSnapshotTargets()
        {
            List<WatchStockItem> source = [.. _holdingWatchStocks, .. _watchStocks, .. _recentViewedStocks, .. _watchStockByIdentity.Values];
            return [.. source
                .Where(stock => stock != null && !string.IsNullOrWhiteSpace(stock.Code))
                .GroupBy(stock => BuildWatchStockIdentityKey(stock), StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group => group.First())
                .OrderBy(stock => IsStockOwned(stock) ? 0 : 1)
                .ThenBy(stock => NormalizeStockCode(stock.Code), StringComparer.Ordinal)];
        }

        private StrategyMinuteDebugSnapshot BuildStrategyMinuteDebugSnapshot(WatchStockItem stock, StrategyChartDebugSnapshot? visibleChart)
        {
            StrategyMinuteSnapshotSet? snapshots = BuildStrategyMinuteSnapshotSet(stock);
            StrategyMinuteDataStatus status = BuildStrategyMinuteDataStatus(stock);
            IReadOnlyList<StrategyEvaluationResult> progressResults = EvaluateEnabledStrategySlots(stock);
            string market = ShouldUseNxtDataForStock(stock) ? "NXT" : "KRX";
            long signalPrice = ResolveStrategySignalPrice(stock);
            StrategyRealtimeFlowSnapshot realtimeFlow = BuildStrategyRealtimeFlowSnapshot(stock);
            StrategyFiveMinuteOneMinuteSellTestEvaluation sellTest = BuildFiveMinuteOneMinuteSellTest(stock, market, realtimeFlow);

            return new StrategyMinuteDebugSnapshot(
                CreatedAt: DateTime.Now,
                Code: NormalizeStockCode(stock.Code),
                Name: stock.Name,
                Market: market,
                SupportsNxt: stock.SupportsNxt,
                DisplayPriceMarket: stock.DisplayPriceMarket,
                CurrentPrice: signalPrice,
                KrxPreviousCloseBasePrice: ResolveStrategyDebugKrxBasePrice(stock),
                GateBaseCandleFound: stock.GateBaseCandleFound,
                GateBaseCandleOffset: stock.GateBaseCandleOffset,
                GateBaseCandleDate: stock.GateBaseCandleDate,
                GateBaseCandleMarket: stock.GateBaseCandleMarket,
                GateBaseCandleChangeRate: stock.GateBaseCandleChangeRate,
                GateBaseCandleTradeValue: stock.GateBaseCandleTradeValue,
                IsIntradayPreCandidate: stock.IsIntradayPreCandidate,
                RealtimeFlow: StrategyRealtimeFlowDebugSnapshot.FromSnapshot(realtimeFlow),
                OrderBookProbe: BuildStrategyOrderBookProbeDebugSnapshot(stock, realtimeFlow),
                MinuteStatus: BuildMinuteStatusMap(status),
                Frames: snapshots?.Frames.Values
                    .OrderBy(frame => frame.Minute)
                    .Select(StrategyMinuteDebugFrame.FromSnapshot)
                    .ToList() ?? [],
                Progress: progressResults
                    .Select(BuildProgressSnapshot)
                    .ToList(),
                SellTest: sellTest,
                VisibleChart: visibleChart,
                Evidence: StrategyDebugEvidence.Empty());
        }

        private StrategyFiveMinuteOneMinuteSellTestEvaluation BuildFiveMinuteOneMinuteSellTest(
            WatchStockItem stock,
            string market,
            StrategyRealtimeFlowSnapshot realtimeFlow)
        {
            string code = NormalizeStockCode(stock.Code);
            string normalizedMarket = NormalizeIdentityMarket(market);
            if (!StrategyDebugSellTestEnabled)
                return StrategyFiveMinuteOneMinuteSellTestEvaluation.NotReady(normalizedMarket, "sell test disabled");

            if (string.IsNullOrWhiteSpace(code))
                return StrategyFiveMinuteOneMinuteSellTestEvaluation.NotReady(normalizedMarket, "stock code missing");

            IReadOnlyList<StrategyMinuteBar> oneMinuteBars = _strategyMinuteCacheService.GetBars(
                code,
                normalizedMarket,
                1,
                80);
            IReadOnlyList<StrategyMinuteBar> fiveMinuteBars = _strategyMinuteCacheService.GetBars(
                code,
                normalizedMarket,
                5,
                80);

            return StrategyFiveMinuteOneMinuteSellTestEvaluator.Evaluate(
                normalizedMarket,
                oneMinuteBars,
                fiveMinuteBars,
                realtimeFlow);
        }

        private string SaveAiReviewRequest(
            string debugSnapshotDirectory,
            string prefix,
            string reason,
            string snapshotPath,
            StrategyChartDebugSnapshot? chart,
            IReadOnlyList<StrategyMinuteDebugSnapshot> snapshots,
            int readyCount,
            int totalCount)
        {
            try
            {
                string pendingDirectory = ResolveAiReviewPendingDirectory(debugSnapshotDirectory);
                Directory.CreateDirectory(pendingDirectory);

                AiChartReviewRequest request = new(
                    RequestId: $"{prefix}-chart-review",
                    CreatedAt: DateTime.Now,
                    Type: "ChartReview",
                    Reason: reason,
                    SnapshotPath: snapshotPath,
                    ChartImagePath: chart?.ImagePath ?? string.Empty,
                    RequestOrderIntent: true,
                    EngineStart: AutoTradingEnabledToggle?.IsChecked == true,
                    LiveOrders: LiveBuyEnabledToggle?.IsChecked == true,
                    MarketStatusCode: _lastMarketStatusCode,
                    MarketStatusText: _lastMarketStatusText,
                    IsNxtMarketMode: _isNxtMarketMode,
                    TotalCount: totalCount,
                    ReadyCount: readyCount,
                    Targets: snapshots
                        .Select(snapshot => new AiChartReviewTarget(
                            snapshot.Code,
                            snapshot.Name,
                            snapshot.Market,
                            snapshot.CurrentPrice,
                            snapshot.KrxPreviousCloseBasePrice,
                            snapshot.GateBaseCandleFound,
                            snapshot.GateBaseCandleOffset,
                            snapshot.IsIntradayPreCandidate,
                            snapshot.Progress
                                .Select(progress => new AiChartReviewProgressTarget(
                                    progress.SlotId,
                                    progress.Name,
                                    progress.HasSignal,
                                    progress.StateText,
                                    progress.ProgressPercent,
                                    progress.LevelText,
                                    progress.StrengthPercent,
                                    progress.OrderIntentAction,
                                    progress.OrderIntentAllowsHandoff,
                                    progress.OrderIntentReason))
                                .ToList()))
                        .ToList(),
                    ExpectedResultPath: Path.Combine(
                        ResolveAiReviewDoneDirectory(debugSnapshotDirectory),
                        $"{prefix}-chart-review-result.json"));

                string path = Path.Combine(pendingDirectory, $"{prefix}-chart-review-request.json");
                File.WriteAllText(path, JsonSerializer.Serialize(request, StrategyDebugSnapshotJsonOptions));
                return path;
            }
            catch (Exception ex)
            {
                AppendLog($"AI review request skipped: {ex.Message}");
                return string.Empty;
            }
        }

        private StrategyChartDebugSnapshot? SaveCurrentVisibleChartDebugSnapshot(string directory, string prefix)
        {
            StrategyChartSnapshotRenderContext renderContext = PrepareStrategyChartSnapshotRenderContext();
            string imagePath = TrySaveCurrentVisibleChartImage(directory, prefix, renderContext);
            if (string.IsNullOrWhiteSpace(imagePath) && _currentChartCandles.Count == 0)
                return null;

            ChartCandle? last = renderContext.Candles.Count > 0
                ? renderContext.Candles[^1]
                : _currentChartCandles.Count > 0
                    ? _currentChartCandles[^1]
                    : null;
            int visibleCount = ResolveCurrentVisibleChartCandleCount();

            return new StrategyChartDebugSnapshot(
                CapturedAt: DateTime.Now,
                ImagePath: imagePath,
                Code: renderContext.Code,
                Market: renderContext.Market,
                Period: renderContext.PeriodLabel,
                TotalBars: _currentChartCandles.Count,
                ViewStartIndex: ResolveCurrentChartVisibleStartIndex(),
                ViewCount: visibleCount,
                SnapshotMode: renderContext.Mode,
                SnapshotStartIndex: renderContext.StartIndex,
                SnapshotCount: renderContext.Count,
                MinimumContextCount: renderContext.MinimumContextCount,
                LastBarDate: last?.Date ?? string.Empty,
                LastClose: last?.Close ?? 0,
                RenderedTotalBars: renderContext.Candles.Count > 0 ? renderContext.Candles.Count : _currentChartCandles.Count);
        }

        private string TrySaveCurrentVisibleChartImage(string directory, string prefix, StrategyChartSnapshotRenderContext renderContext)
        {
            try
            {
                if (ChartSnapshotHost == null ||
                    ChartSnapshotHost.ActualWidth <= 1 ||
                    ChartSnapshotHost.ActualHeight <= 1)
                {
                    return string.Empty;
                }

                int originalStart = _chartViewStartIndex;
                int originalCount = _chartViewCount;
                List<ChartCandle> originalCandles = CloneChartCandles(_currentChartCandles);
                string originalCode = _currentChartCode;
                string originalMarket = _currentChartMarket;
                ChartPeriod originalDataPeriod = _currentChartDataPeriod;
                bool needsTemporaryRender = renderContext.Mode is StrategyChartSnapshotModeContext or StrategyChartSnapshotModeStrategy15Minute;
                try
                {
                    if (needsTemporaryRender)
                    {
                        ApplyTemporaryStrategyChartSnapshotContext(renderContext);
                        DrawPriceChart(GetVisibleChartCandles());
                        DrawVolumeChart(GetVisibleChartCandles());
                    }

                    ChartSnapshotHost.UpdateLayout();
                    int width = Math.Max(1, (int)Math.Ceiling(ChartSnapshotHost.ActualWidth));
                    int height = Math.Max(1, (int)Math.Ceiling(ChartSnapshotHost.ActualHeight));
                    RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(ChartSnapshotHost);

                    string path = Path.Combine(directory, $"{prefix}-chart.png");
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using FileStream stream = File.Create(path);
                    encoder.Save(stream);
                    return path;
                }
                finally
                {
                    if (needsTemporaryRender)
                    {
                        RestoreStrategyChartSnapshotContext(
                            originalCandles,
                            originalCode,
                            originalMarket,
                            originalDataPeriod,
                            originalStart,
                            originalCount);
                        DrawPriceChart(GetVisibleChartCandles());
                        DrawVolumeChart(GetVisibleChartCandles());
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"strategy chart snapshot skipped: {ex.Message}");
                return string.Empty;
            }
        }

        private StrategyChartSnapshotRenderContext PrepareStrategyChartSnapshotRenderContext()
        {
            StrategyChartSnapshotRenderContext? strategyFifteenMinuteContext = TryPrepareStrategyFifteenMinuteChartSnapshotContext();
            if (strategyFifteenMinuteContext != null)
                return strategyFifteenMinuteContext;

            int visibleStart = ResolveCurrentChartVisibleStartIndex();
            int visibleCount = ResolveCurrentVisibleChartCandleCount();
            int minimumContextCount = ResolveStrategyChartSnapshotMinimumContextCount(_currentChartDataPeriod);
            if (_currentChartCandles.Count <= 0 ||
                minimumContextCount <= 0 ||
                visibleCount >= minimumContextCount)
            {
                return new StrategyChartSnapshotRenderContext(
                    StrategyChartSnapshotModeCurrent,
                    NormalizeStockCode(_currentChartCode),
                    NormalizeIdentityMarket(_currentChartMarket),
                    FormatChartPeriodLabel(_currentChartDataPeriod),
                    _currentChartDataPeriod,
                    [],
                    visibleStart,
                    visibleCount,
                    minimumContextCount);
            }

            int contextCount = Math.Min(_currentChartCandles.Count, minimumContextCount);
            int contextStart = Math.Max(0, _currentChartCandles.Count - contextCount);
            return new StrategyChartSnapshotRenderContext(
                StrategyChartSnapshotModeContext,
                NormalizeStockCode(_currentChartCode),
                NormalizeIdentityMarket(_currentChartMarket),
                FormatChartPeriodLabel(_currentChartDataPeriod),
                _currentChartDataPeriod,
                [],
                contextStart,
                contextCount,
                minimumContextCount);
        }

        private StrategyChartSnapshotRenderContext? TryPrepareStrategyFifteenMinuteChartSnapshotContext()
        {
            string code = NormalizeStockCode(_currentChartCode);
            string market = NormalizeIdentityMarket(_currentChartMarket);
            if (string.IsNullOrWhiteSpace(code))
            {
                WatchStockItem? stock = ResolveSelectedProgressStock();
                if (stock == null)
                    return null;

                code = NormalizeStockCode(stock.Code);
                market = ShouldUseNxtDataForStock(stock) ? "NXT" : "KRX";
            }

            if (string.IsNullOrWhiteSpace(code))
                return null;

            IReadOnlyList<StrategyMinuteBar> bars = _strategyMinuteCacheService.GetBars(
                code,
                market,
                StrategyDebugChartMinute,
                StrategyDebugChartCandleCount);
            if (bars.Count < 20)
                return null;

            List<ChartCandle> candles = [.. bars.Select(ToChartCandle)];
            int count = Math.Min(candles.Count, StrategyDebugChartCandleCount);
            int start = Math.Max(0, candles.Count - count);
            return new StrategyChartSnapshotRenderContext(
                StrategyChartSnapshotModeStrategy15Minute,
                code,
                market,
                "15min",
                ChartPeriod.Minute15,
                candles,
                start,
                count,
                StrategyDebugChartCandleCount);
        }

        private static ChartCandle ToChartCandle(StrategyMinuteBar bar)
        {
            return new ChartCandle
            {
                Date = bar.BucketTime.ToString("yyyyMMddHHmmss"),
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume
            };
        }

        private void ApplyTemporaryStrategyChartSnapshotContext(StrategyChartSnapshotRenderContext renderContext)
        {
            if (renderContext.Mode == StrategyChartSnapshotModeStrategy15Minute)
            {
                _currentChartCandles.Clear();
                _currentChartCandles.AddRange(CloneChartCandles(renderContext.Candles));
                RecalculateChartMovingAverages(_currentChartCandles);
                _currentChartCode = renderContext.Code;
                _currentChartMarket = renderContext.Market;
                _currentChartDataPeriod = renderContext.Period;
            }

            _chartViewStartIndex = renderContext.StartIndex;
            _chartViewCount = renderContext.Count;
        }

        private void RestoreStrategyChartSnapshotContext(
            IReadOnlyList<ChartCandle> originalCandles,
            string originalCode,
            string originalMarket,
            ChartPeriod originalDataPeriod,
            int originalStart,
            int originalCount)
        {
            _currentChartCandles.Clear();
            _currentChartCandles.AddRange(CloneChartCandles(originalCandles));
            _currentChartCode = originalCode;
            _currentChartMarket = originalMarket;
            _currentChartDataPeriod = originalDataPeriod;
            _chartViewStartIndex = originalStart;
            _chartViewCount = originalCount;
        }

        private static int ResolveStrategyChartSnapshotMinimumContextCount(ChartPeriod period)
        {
            return period switch
            {
                ChartPeriod.Daily => 120,
                ChartPeriod.Weekly => 100,
                ChartPeriod.Monthly => 60,
                _ when IsMinuteChartPeriod(period) => 120,
                _ => 0
            };
        }

        private int ResolveCurrentVisibleChartCandleCount()
        {
            if (_currentChartCandles.Count == 0)
                return 0;

            if (_chartViewCount <= 0)
                return _currentChartCandles.Count;

            int start = Math.Clamp(_chartViewStartIndex, 0, Math.Max(0, _currentChartCandles.Count - 1));
            return Math.Clamp(_chartViewCount, 1, _currentChartCandles.Count - start);
        }

        private int ResolveCurrentChartVisibleStartIndex()
        {
            if (_currentChartCandles.Count == 0)
                return 0;

            return _chartViewCount > 0
                ? Math.Clamp(_chartViewStartIndex, 0, Math.Max(0, _currentChartCandles.Count - 1))
                : 0;
        }

        private static string FormatChartSnapshotLogSuffix(StrategyChartDebugSnapshot? chart)
        {
            return chart == null || string.IsNullOrWhiteSpace(chart.ImagePath)
                ? string.Empty
                : $" / chart {chart.ImagePath}";
        }

        private static string FormatAiReviewRequestLogSuffix(string requestPath)
        {
            return string.IsNullOrWhiteSpace(requestPath)
                ? string.Empty
                : $" / AI queue {requestPath}";
        }

        private long ResolveStrategyDebugKrxBasePrice(WatchStockItem stock)
        {
            if (stock != null &&
                string.Equals(NormalizeStockCode(stock.Code), _selectedStockCode, StringComparison.Ordinal) &&
                _krxPrevClosePrice > 0)
            {
                return _krxPrevClosePrice;
            }

            if (stock != null &&
                _watchlistMemoryCache.TryGetValue(BuildWatchStockIdentityKey(stock), out WatchlistStockCacheEntry? entry) &&
                IsTrustedKrxBasePrice(entry, DateTime.Now.ToString("yyyyMMdd")))
            {
                return entry.BasePrice;
            }

            return 0;
        }

        private static bool IsStrategyMinuteDebugSnapshotReady(StrategyMinuteDebugSnapshot snapshot)
        {
            return snapshot.MinuteStatus.Values.All(status =>
                status.TargetCount <= 0 || status.Count >= status.TargetCount);
        }

        private static Dictionary<int, StrategyMinuteDebugStatus> BuildMinuteStatusMap(StrategyMinuteDataStatus status)
        {
            Dictionary<int, StrategyMinuteDebugStatus> result = [];
            foreach (int minute in new[] { 1, 3, 5, 10, 15, 30 })
            {
                result[minute] = new StrategyMinuteDebugStatus(
                    Count: status.GetCount(minute),
                    TargetCount: status.GetTargetCount(minute));
            }

            return result;
        }

        private StrategyProgressDebugSnapshot BuildProgressSnapshot(StrategyEvaluationResult result)
        {
            StrategyProgressSnapshot progress = result.Progress ?? StrategyProgressSnapshot.Empty(result.SlotId);
            StrategySlotDescriptor? descriptor = _strategySlotRegistry.GetDescriptor(result.SlotId);
            return new StrategyProgressDebugSnapshot(
                SlotId: result.SlotId.ToString(),
                Name: result.Name,
                MarketScope: descriptor?.MarketScope.ToString() ?? string.Empty,
                MarketBadgeText: descriptor?.MarketBadgeText ?? string.Empty,
                HasSignal: result.HasSignal,
                StateText: result.StateText,
                Summary: result.Summary,
                OrderIntentAction: result.ResolvedOrderIntent.Action.ToString(),
                OrderIntentAllowsHandoff: result.ResolvedOrderIntent.AllowsOrderHandoff,
                OrderIntentReason: result.ResolvedOrderIntent.Reason,
                EntryPrice: result.ResolvedOrderIntent.EntryPrice,
                StopPrice: result.ResolvedOrderIntent.StopPrice,
                TargetPrice: result.ResolvedOrderIntent.TargetPrice,
                RewardRiskRatio: result.ResolvedOrderIntent.RewardRiskRatio,
                NoBuyReasons: result.ResolvedOrderIntent.NoBuyReasons ?? [],
                StateKey: progress.StateKey,
                CurrentStep: progress.CurrentStep,
                TotalSteps: progress.TotalSteps,
                ProgressPercent: progress.ProgressPercent,
                LevelText: progress.LevelText,
                StrengthPercent: progress.StrengthPercent,
                StrengthLabel: progress.StrengthLabel,
                Steps: progress.Steps
                    .Select(step => new StrategyProgressStepDebugSnapshot(
                        step.Key,
                        step.Label,
                        step.IsCompleted,
                        step.IsCurrent))
                    .ToList());
        }

        private StrategyOrderBookProbeDebugSnapshot BuildStrategyOrderBookProbeDebugSnapshot(
            WatchStockItem stock,
            StrategyRealtimeFlowSnapshot realtimeFlow)
        {
            string code = NormalizeStockCode(stock.Code);
            string market = ShouldUseNxtDataForStock(stock) ? "NXT" : "KRX";
            string key = BuildMarketIdentityKey(code, market);
            string requestCode = BuildStrategyRealtimeRequestCode(code, market);
            bool isRegistered;
            DateTime retryAfter;
            DateTime lastProbeLogAt;

            lock (_strategyOrderBookProbeLock)
            {
                isRegistered = _strategyOrderBookProbeRequestCodes.Contains(requestCode);
                _strategyOrderBookProbeRetryAfterByKey.TryGetValue(key, out retryAfter);
                _strategyOrderBookProbeLastReceiveLogAtByKey.TryGetValue(key, out lastProbeLogAt);
            }

            return new StrategyOrderBookProbeDebugSnapshot(
                RequestCode: requestCode,
                IsRegistered: isRegistered,
                RetryAfter: retryAfter,
                LastReceiveLogAt: lastProbeLogAt,
                LastOrderBookAt: realtimeFlow.LastOrderBookAt,
                IsFresh: realtimeFlow.HasFreshOrderBook(DateTime.Now, maxAgeSeconds: 15),
                BestAskPrice: realtimeFlow.BestAskPrice,
                BestBidPrice: realtimeFlow.BestBidPrice,
                TotalAskQuantity: realtimeFlow.TotalAskQuantity,
                TotalBidQuantity: realtimeFlow.TotalBidQuantity,
                BidRatio: realtimeFlow.BidQuantityRatio);
        }

        private static string ResolveDebugSnapshotDirectory()
        {
            string? projectFromBase = SearchUpwards(AppContext.BaseDirectory, "TradingDashboard.csproj");
            string? configFromBase = projectFromBase == null
                ? SearchUpwards(AppContext.BaseDirectory, "Config")
                : null;
            string root = projectFromBase != null
                ? Directory.GetParent(projectFromBase)?.FullName ?? AppContext.BaseDirectory
                : configFromBase == null
                ? AppContext.BaseDirectory
                : Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory;
            return Path.Combine(root, "Storage", "DebugSnapshots");
        }

        private static string ResolveAiReviewPendingDirectory(string debugSnapshotDirectory)
        {
            string storageRoot = Directory.GetParent(debugSnapshotDirectory)?.FullName
                ?? Path.Combine(AppContext.BaseDirectory, "Storage");
            return Path.Combine(storageRoot, "AiReviewQueue", "pending");
        }

        private static string ResolveAiReviewDoneDirectory(string debugSnapshotDirectory)
        {
            string storageRoot = Directory.GetParent(debugSnapshotDirectory)?.FullName
                ?? Path.Combine(AppContext.BaseDirectory, "Storage");
            return Path.Combine(storageRoot, "AiReviewQueue", "done");
        }

        private static string? SearchUpwards(string startDirectory, string childName)
        {
            DirectoryInfo? current = Directory.Exists(startDirectory)
                ? new DirectoryInfo(startDirectory)
                : Directory.GetParent(startDirectory);

            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, childName);
                if (Directory.Exists(candidate) || File.Exists(candidate))
                    return candidate;
                current = current.Parent;
            }

            return null;
        }

        private sealed record StrategyMinuteDebugSnapshot(
            DateTime CreatedAt,
            string Code,
            string Name,
            string Market,
            bool SupportsNxt,
            string DisplayPriceMarket,
            long CurrentPrice,
            long KrxPreviousCloseBasePrice,
            bool GateBaseCandleFound,
            int GateBaseCandleOffset,
            string GateBaseCandleDate,
            string GateBaseCandleMarket,
            double GateBaseCandleChangeRate,
            long GateBaseCandleTradeValue,
            bool IsIntradayPreCandidate,
            StrategyRealtimeFlowDebugSnapshot RealtimeFlow,
            StrategyOrderBookProbeDebugSnapshot OrderBookProbe,
            IReadOnlyDictionary<int, StrategyMinuteDebugStatus> MinuteStatus,
            IReadOnlyList<StrategyMinuteDebugFrame> Frames,
            IReadOnlyList<StrategyProgressDebugSnapshot> Progress,
            StrategyFiveMinuteOneMinuteSellTestEvaluation SellTest,
            StrategyChartDebugSnapshot? VisibleChart,
            StrategyDebugEvidence Evidence);

        private sealed record StrategyRealtimeFlowDebugSnapshot(
            DateTime LastTickAt,
            DateTime LastOrderBookAt,
            long LastPrice,
            long LastTradeQuantity,
            bool LastTradeIsBuy,
            long BuyTradeVolume60s,
            long SellTradeVolume60s,
            long BuyTradeValue60s,
            long SellTradeValue60s,
            double BuyRatio60s,
            long BestAskPrice,
            long BestBidPrice,
            long TotalAskQuantity,
            long TotalBidQuantity,
            double BidRatio)
        {
            public static StrategyRealtimeFlowDebugSnapshot FromSnapshot(StrategyRealtimeFlowSnapshot snapshot) =>
                new(
                    snapshot.LastTickAt,
                    snapshot.LastOrderBookAt,
                    snapshot.LastPrice,
                    snapshot.LastTradeQuantity,
                    snapshot.LastTradeIsBuy,
                    snapshot.BuyTradeVolume60s,
                    snapshot.SellTradeVolume60s,
                    snapshot.BuyTradeValue60s,
                    snapshot.SellTradeValue60s,
                    snapshot.BuyTradeVolumeRatio60s,
                    snapshot.BestAskPrice,
                    snapshot.BestBidPrice,
                    snapshot.TotalAskQuantity,
                    snapshot.TotalBidQuantity,
                    snapshot.BidQuantityRatio);
        }

        private sealed record StrategyOrderBookProbeDebugSnapshot(
            string RequestCode,
            bool IsRegistered,
            DateTime RetryAfter,
            DateTime LastReceiveLogAt,
            DateTime LastOrderBookAt,
            bool IsFresh,
            long BestAskPrice,
            long BestBidPrice,
            long TotalAskQuantity,
            long TotalBidQuantity,
            double BidRatio);

        private sealed record StrategyMinuteDebugSnapshotBatch(
            DateTime CreatedAt,
            string Reason,
            string MarketStatusCode,
            string MarketStatusText,
            bool IsNxtMarketMode,
            bool EngineStart,
            bool LiveOrders,
            int TotalCount,
            int ReadyCount,
            StrategyChartDebugSnapshot? VisibleChart,
            IReadOnlyList<StrategyMinuteDebugSnapshot> Snapshots);

        private sealed record StrategyChartDebugSnapshot(
            DateTime CapturedAt,
            string ImagePath,
            string Code,
            string Market,
            string Period,
            int TotalBars,
            int ViewStartIndex,
            int ViewCount,
            string SnapshotMode,
            int SnapshotStartIndex,
            int SnapshotCount,
            int MinimumContextCount,
            string LastBarDate,
            double LastClose,
            int RenderedTotalBars);

        private sealed record StrategyChartSnapshotRenderContext(
            string Mode,
            string Code,
            string Market,
            string PeriodLabel,
            ChartPeriod Period,
            IReadOnlyList<ChartCandle> Candles,
            int StartIndex,
            int Count,
            int MinimumContextCount);

        private sealed record StrategyMinuteDebugStatus(
            int Count,
            int TargetCount);

        private sealed record StrategyProgressDebugSnapshot(
            string SlotId,
            string Name,
            string MarketScope,
            string MarketBadgeText,
            bool HasSignal,
            string StateText,
            string Summary,
            string OrderIntentAction,
            bool OrderIntentAllowsHandoff,
            string OrderIntentReason,
            long EntryPrice,
            long StopPrice,
            long TargetPrice,
            double RewardRiskRatio,
            IReadOnlyList<string> NoBuyReasons,
            string StateKey,
            int CurrentStep,
            int TotalSteps,
            double ProgressPercent,
            string LevelText,
            double StrengthPercent,
            string StrengthLabel,
            IReadOnlyList<StrategyProgressStepDebugSnapshot> Steps);

        private sealed record StrategyProgressStepDebugSnapshot(
            string Key,
            string Label,
            bool IsCompleted,
            bool IsCurrent);

        private sealed record StrategyDebugEvidence(
            string Version,
            IReadOnlyList<string> Notes,
            IReadOnlyDictionary<string, object> Values)
        {
            public static StrategyDebugEvidence Empty() =>
                new(
                    "EVIDENCE_PLACEHOLDER_V1",
                    ["Madi/waist/reward-risk evidence will be added after the numeric trigger is implemented."],
                    new Dictionary<string, object>());
        }

        private sealed record StrategyMinuteDebugFrame(
            int Minute,
            bool IsReady,
            int CompletedCount,
            int TargetCount,
            DateTime LoadedAt,
            DateTime LastRealtimeAt,
            DateTime CurrentBarTime,
            long CurrentOpen,
            long CurrentHigh,
            long CurrentLow,
            long CurrentClose,
            long CurrentVolume,
            long CurrentTradingValue,
            DateTime LastCompletedBarTime,
            long LastCompletedOpen,
            long LastCompletedHigh,
            long LastCompletedLow,
            long LastCompletedClose,
            long LastCompletedVolume,
            long LastCompletedTradingValue,
            double Ma5,
            double Ma10,
            double Ma20,
            double Ma60,
            double Ma200,
            double Ma240,
            double Ma480,
            double LastCompletedMa5,
            double LastCompletedMa10,
            double LastCompletedMa20,
            double LastCompletedMa60,
            double LastCompletedMa200,
            double LastCompletedMa240,
            double LastCompletedMa480,
            long High20,
            long Low20,
            long HighestClose20,
            long LowestClose20,
            long Volume20,
            long TradingValue20)
        {
            public static StrategyMinuteDebugFrame FromSnapshot(StrategyMinuteFrameSnapshot frame) =>
                new(
                    frame.Minute,
                    frame.IsReady,
                    frame.CompletedCount,
                    frame.TargetCount,
                    frame.LoadedAt,
                    frame.LastRealtimeAt,
                    frame.CurrentBarTime,
                    frame.CurrentOpen,
                    frame.CurrentHigh,
                    frame.CurrentLow,
                    frame.CurrentClose,
                    frame.CurrentVolume,
                    frame.CurrentTradingValue,
                    frame.LastCompletedBarTime,
                    frame.LastCompletedOpen,
                    frame.LastCompletedHigh,
                    frame.LastCompletedLow,
                    frame.LastCompletedClose,
                    frame.LastCompletedVolume,
                    frame.LastCompletedTradingValue,
                    frame.Ma5,
                    frame.Ma10,
                    frame.Ma20,
                    frame.Ma60,
                    frame.Ma200,
                    frame.Ma240,
                    frame.Ma480,
                    frame.LastCompletedMa5,
                    frame.LastCompletedMa10,
                    frame.LastCompletedMa20,
                    frame.LastCompletedMa60,
                    frame.LastCompletedMa200,
                    frame.LastCompletedMa240,
                    frame.LastCompletedMa480,
                    frame.High20,
                    frame.Low20,
                    frame.HighestClose20,
                    frame.LowestClose20,
                    frame.Volume20,
                    frame.TradingValue20);
        }

        private sealed record AiChartReviewRequest(
            string RequestId,
            DateTime CreatedAt,
            string Type,
            string Reason,
            string SnapshotPath,
            string ChartImagePath,
            bool RequestOrderIntent,
            bool EngineStart,
            bool LiveOrders,
            string MarketStatusCode,
            string MarketStatusText,
            bool IsNxtMarketMode,
            int TotalCount,
            int ReadyCount,
            IReadOnlyList<AiChartReviewTarget> Targets,
            string ExpectedResultPath);

        private sealed record AiChartReviewTarget(
            string Code,
            string Name,
            string Market,
            long CurrentPrice,
            long KrxPreviousCloseBasePrice,
            bool GateBaseCandleFound,
            int GateBaseCandleOffset,
            bool IsIntradayPreCandidate,
            IReadOnlyList<AiChartReviewProgressTarget> Progress);

        private sealed record AiChartReviewProgressTarget(
            string SlotId,
            string Name,
            bool HasSignal,
            string StateText,
            double ProgressPercent,
            string LevelText,
            double StrengthPercent,
            string OrderIntentAction,
            bool OrderIntentAllowsHandoff,
            string OrderIntentReason);
    }
}
