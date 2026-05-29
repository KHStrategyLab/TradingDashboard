# UI and News Panel Notes

Date: 2026-05-29

## Core UI Language

- Visible dashboard labels were moved mostly to English to reduce Korean encoding risk.
- User-facing runtime logs were also moved mostly to English.
- Korean strings are intentionally kept only where they are data inputs or API matching tokens, such as Kiwoom response field aliases and Korean news search keywords.

## Market News Tab

- The right-side `News` tab is now a usable market-news board.
- It uses the existing Naver News Open API credentials.
- Default query:
  - `증권 | 증시 | 코스피 | 코스닥 | 주식 | 금융`
- The query remains Korean because it is a data-quality input, not a UI label.
- The top bar contains:
  - Keyword search box
  - `Search` button
  - `Refresh` button
- Pressing Enter in the search box also runs a search.
- Double-clicking a news item opens its link in the default browser.

## Thumbnail Loading

- Naver News Search API does not provide thumbnail URLs.
- The app first displays text-only news cards immediately.
- Thumbnail images are loaded lazily from each article page by reading `og:image`.
- `NewsThumbnailService` limits thumbnail fetches with `SemaphoreSlim(3)`.
- If thumbnail extraction fails, the `NEWS` badge remains visible.

## Operational Notes

- Stock/watchlist cache file is ignored and marked skip-worktree locally:
  - `Config/watchlist_stock_cache.json`
- Build verification:
  - `dotnet build TradingDashboard.csproj`
  - Current state builds with 0 warnings and 0 errors.
