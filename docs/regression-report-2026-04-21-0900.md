# Regression Report — ZeroAlloc.Scheduling Dashboard

**Date:** 2026-04-21 09:00  
**Application URL:** http://localhost:5678/jobs/  
**Tester:** Claude Code (automated)

---

## Summary

| Metric                | Value |
|-----------------------|-------|
| Date                  | 2026-04-21 09:00 |
| Application URL       | http://localhost:5678/jobs/ |
| Pages Tested          | 1 (Dashboard) |
| Viewports Tested      | 3 (Desktop, Tablet, Mobile) |
| Existing Tests Passed | 45 |
| Existing Tests Failed | 0 |
| Console Errors        | 1 (favicon.ico 404) |
| Network Errors        | 0 |
| Visual Issues Found   | 4 (all Minor) |
| **Overall Status**    | **PASS** |

---

## Phase 2: Existing Test Results

**Framework:** xUnit (.NET)  
**Command:** `dotnet test`

| Project | Tests | Passed | Failed |
|---------|-------|--------|--------|
| ZeroAlloc.Scheduling.Tests | 28 | 28 | 0 |
| ZeroAlloc.Scheduling.EfCore.Tests | 5 | 5 | 0 |
| ZeroAlloc.Scheduling.Dashboard.Tests | 9 | 9 | 0 |
| ZeroAlloc.Scheduling.Generator.Tests | 3 | 3 | 0 |
| **Total** | **45** | **45** | **0** |

All tests pass. ✅

---

## Phase 3: Browser-Based Testing

### Authentication

No login form detected. Dashboard is publicly accessible at `/jobs/`.

### Functional Checks — `/jobs/`

| Check | Result |
|-------|--------|
| Page loads | ✅ 200 OK |
| Page title | ✅ "ZeroAlloc Scheduler" |
| Summary cards render | ✅ 5 cards (pending/running/succeeded/failed/deadLetter) |
| Jobs table renders | ✅ Headers visible (ID, Type, Status, Attempts, Scheduled At, Error, Actions) |
| `/api/summary` | ✅ 200 OK |
| `/api/pending` | ✅ 200 OK |
| `/api/running` | ✅ 200 OK |
| `/api/failed` | ✅ 200 OK |
| `/api/succeeded` | ✅ 200 OK |
| Auto-refresh (5s) | ✅ Confirmed (API calls repeat in network log) |
| Console errors | ⚠️ 1 — `favicon.ico` 404 (cosmetic) |
| Network failures | ✅ None |

---

### Visual Evaluation

#### Desktop (1920×1080) — PASS

The page renders correctly. Title, summary cards, and table header are all visible. At 1920px the layout feels narrow — the cards and table are constrained to a small portion of the viewport with no max-width centering. The page would benefit from a container with a max-width and horizontal centering at large viewports.

**Issues:**
- Cards occupy a very small fraction of the 1920px viewport (no responsive max-width container)
- `deadLetter` label uses camelCase — inconsistent with the other lowercase labels

#### Tablet (768×1024) — PASS

Cards wrap to a 3+2 grid. The 5th card (`deadLetter`) sits alone on the second row, which looks asymmetric. The table is readable. No horizontal overflow. Overall clean and functional.

**Issues:**
- 5th card (`deadLetter`) alone on row 2 — grid symmetry could be improved with 2+3 or a 5-col row

#### Mobile (375×812) — MINOR ISSUES

Cards stack single-column but only fill roughly half the screen width, leaving dead whitespace to the right. The table squeezes 7 columns into 375px — "Scheduled At" wraps in the header cell and real data rows would be very cramped. No horizontal scroll is applied to the table.

**Issues:**
- Cards don't stretch to full width on mobile (width is fixed or has a max-width not suited for mobile)
- Table is not responsive at mobile — 7 columns in 375px with no horizontal scroll
- "Scheduled At" header wraps to two lines

---

## Findings

### Critical
_None._

### Important
_None._

### Minor

1. **`deadLetter` label displays as camelCase**  
   File: `src/ZeroAlloc.Scheduling.Dashboard/wwwroot/index.html`  
   The summary card label reads `deadLetter` instead of `Dead Letter`. All other labels are lowercase but single-word (`pending`, `running`, etc.). This one is visually inconsistent.  
   Fix: Change the label text to `Dead Letter`.

2. **Table has no empty-state message**  
   File: `src/ZeroAlloc.Scheduling.Dashboard/wwwroot/index.html`  
   When there are no jobs, the table shows only headers with no content. A "No jobs found" row would improve clarity.  
   Fix: Add a conditional empty-state `<tr>` with a colspan message.

3. **Table is not responsive on mobile**  
   File: `src/ZeroAlloc.Scheduling.Dashboard/wwwroot/index.html`  
   7 columns at 375px results in cramped layout. The table lacks `overflow-x: auto` on its container, so it clips rather than scrolls.  
   Fix: Wrap the table in a `div` with `overflow-x: auto`.

4. **Summary cards don't fill full width on mobile**  
   File: `src/ZeroAlloc.Scheduling.Dashboard/wwwroot/index.html`  
   Cards are roughly half the screen width on mobile due to fixed or min-width constraints not tuned for small viewports.  
   Fix: Add `min-width: 0; flex: 1 1 100%` for the smallest breakpoint, or use `width: calc(50% - gap)` to fill the row.

### Suggestions

5. **Missing `favicon.ico`** — Add a favicon to eliminate the console 404.

6. **No last-refresh timestamp** — Display "Last updated: HH:MM:SS" near the summary cards so operators know data is current.

7. **No max-width container at desktop** — At 1920px the content floats in the top-left corner. A `max-width: 1200px; margin: auto` container would improve large-screen presentation.

---

## Recommendations (Prioritised)

| Priority | Item |
|----------|------|
| 1 | Wrap table in `overflow-x: auto` container (mobile usability) |
| 2 | Fix `deadLetter` → `Dead Letter` label |
| 3 | Add empty-state row to jobs table |
| 4 | Center content with `max-width` container for large screens |
| 5 | Add `favicon.ico` |
| 6 | Add last-refresh timestamp |

---

## Verdict

**PASS** — All 45 automated tests pass, all API endpoints return 200 OK, and auto-refresh works correctly. No critical or important issues. Four minor UI polish items identified, all in the embedded HTML/JS dashboard (`index.html`). The library core and API layer are production-ready.
