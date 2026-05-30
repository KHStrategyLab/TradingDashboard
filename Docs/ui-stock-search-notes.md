# Stock Search UI Notes

Date: 2026-05-31
Checkpoint: stock search master autocomplete

This note records the current stock search/autocomplete design so the next UI changes keep the same hand-flow rules.

## Stage Names

1. Header Search Dock
   - The stock search box lives in the top header center.
   - It is no longer hidden inside the `Recent Views` tab.
   - The visible `Find` button is hidden because this flow is keyboard-first.

2. Quiet Placeholder
   - Empty search text shows a magnifier icon and `[Alt+S]`.
   - The word `Search` is intentionally removed to reduce visual noise.
   - The magnifier is drawn in XAML, not as emoji text.

3. Keyboard Entry
   - `Alt+S` focuses the search box from anywhere.
   - The current text is selected so the next typing action replaces it immediately.
   - Future shortcut settings should move this key binding into configurable settings.

4. Idle Autocomplete
   - Autocomplete waits until typing is idle.
   - Current idle delay: `700ms`.
   - The delay is intentionally slower than the first version because fast typing/backspacing must not be interrupted.

5. Candidate Focus
   - When suggestions are ready, the first candidate is selected and focused.
   - Arrow keys move through the suggestion rows.
   - `Enter` opens the focused candidate.
   - `Esc` closes the suggestion list.

6. Selection Lock
   - After a candidate is opened, autocomplete is suppressed.
   - The suggestion list closes and does not reopen just because the search text changed to the selected stock name.
   - Autocomplete is re-enabled only when the user returns to the search box by mouse or keyboard.

7. Recent View Landing
   - After a stock is opened from autocomplete, focus moves to the selected item in `Recent Views`.
   - The user's keyboard point should not stay trapped in the search box.

8. Calm Highlight
   - The suggestion list itself must not show a bright focus border.
   - Only the selected row carries a blue point.
   - Keyboard movement should move that row point, not light up the whole panel.

## UI Principle

- Keyboard path: type, arrow, enter, escape.
- Mouse path: click, double-click, wheel, drag.
- Do not force the user to switch hands in the middle of a flow.
- If a UI makes the user say "응!???", treat the hand-flow as broken first.

## Current Files

- `MainWindow.xaml`
  - Header search box
  - Placeholder icon/text
  - Suggestion list style and placement
- `MainWindow.xaml.cs`
  - Autocomplete debounce
  - `Alt+S` focus shortcut
  - Suggestion keyboard handling
  - Selection suppression and focus landing
- `Services/KiwoomRestConditionService.cs`
  - Stock master autocomplete source
  - ETF/ETN/SPAC exclusion
- `Services/StockMasterCacheStore.cs`
  - Local stock master cache
- `Models/StockMasterCacheDocument.cs`
  - Cache document model

## Verification

- Build command: `dotnet build TradingDashboard.csproj`
- Expected result at this checkpoint: 0 warnings, 0 errors.
