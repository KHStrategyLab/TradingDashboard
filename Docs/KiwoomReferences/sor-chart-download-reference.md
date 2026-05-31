# SOR ON Chart Download Reference

Date: 2026-05-31

이 문서는 SOR ON 상태에서 차트 데이터를 다운로드할 때 참고할 레퍼런스다.

현재 TradingDashboard의 화면 차트 기본 정책은 KRX/NXT 분리다.

- KRX: 6자리 종목코드
- NXT: `_NX`
- SOR 통합: `_AL`

따라서 이 문서는 현재 화면 차트 정책을 대체하지 않는다.
나중에 SOR 통합 차트가 필요할 때 비교/검증용으로 사용한다.

## 사용 TR

Endpoint:

- `POST /api/dostk/chart`

TR:

- 일봉: `ka10081`
- 주봉: `ka10082`
- 월봉: `ka10083`
- 연봉: `ka10094`

공통 Header:

- `authorization: Bearer {accessToken}`
- `api-id: ka10081` 등 TR별 값
- `cont-yn: N` 또는 `Y`
- `next-key: ""` 또는 응답 header의 다음 키
- `Content-Type: application/json;charset=UTF-8`

공통 Body:

```json
{
  "stk_cd": "039490_AL",
  "base_dt": "20260501",
  "upd_stkpc_tp": "1"
}
```

## 거래소 코드 규칙

KRX:

```text
039490
```

NXT:

```text
039490_NX
```

SOR 통합:

```text
039490_AL
```

주의:

- 기준가는 항상 KRX 전일종가를 사용한다.
- SOR/NXT 응답의 기준가가 화면 기준가를 덮으면 안 된다.
- SOR ON 차트는 통합 흐름 확인용이다.
- 전략 기준봉은 별도 정의가 없는 한 KRX 정규장 일봉 기준봉을 우선한다.

## 연속조회

첫 요청:

```text
cont-yn: N
next-key:
```

응답 header에서 다음 값 확인:

```text
cont-yn: Y
next-key: {server_next_key}
```

다음 요청:

```text
cont-yn: Y
next-key: {server_next_key}
```

연속조회는 깊은 과거 데이터나 별도 전략 캐시가 필요할 때 사용한다.
현재 화면용 일/주/月 파일 캐시는 고정 범위 조회 후 날짜 기준 증분 병합을 사용한다.

## C# Reference Snippet

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

public sealed class KiwoomSorChartDownloader
{
    private readonly HttpClient _http;
    private readonly string _host = "https://api.kiwoom.com";
    private readonly string _accessToken;

    public KiwoomSorChartDownloader(string accessToken)
    {
        _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private async Task<string> PostAsync(
        string endpoint,
        string apiId,
        string jsonBody,
        string contYn = "N",
        string nextKey = "")
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _host + endpoint);
        req.Headers.Add("authorization", $"Bearer {_accessToken}");
        req.Headers.Add("api-id", apiId);
        req.Headers.Add("cont-yn", contYn);
        req.Headers.Add("next-key", nextKey);
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using HttpResponseMessage resp = await _http.SendAsync(req).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public Task<string> GetDailyAsync(
        string stockCode,
        string baseDate,
        string updStkpcTp = "1",
        string contYn = "N",
        string nextKey = "")
    {
        string body = $$"""
        {
          "stk_cd": "{{stockCode}}",
          "base_dt": "{{baseDate}}",
          "upd_stkpc_tp": "{{updStkpcTp}}"
        }
        """;

        return PostAsync("/api/dostk/chart", "ka10081", body, contYn, nextKey);
    }

    public Task<string> GetWeeklyAsync(string stockCode, string baseDate, string updStkpcTp = "1")
    {
        string body = $$"""
        {
          "stk_cd": "{{stockCode}}",
          "base_dt": "{{baseDate}}",
          "upd_stkpc_tp": "{{updStkpcTp}}"
        }
        """;

        return PostAsync("/api/dostk/chart", "ka10082", body);
    }

    public Task<string> GetMonthlyAsync(string stockCode, string baseDate, string updStkpcTp = "1")
    {
        string body = $$"""
        {
          "stk_cd": "{{stockCode}}",
          "base_dt": "{{baseDate}}",
          "upd_stkpc_tp": "{{updStkpcTp}}"
        }
        """;

        return PostAsync("/api/dostk/chart", "ka10083", body);
    }
}
```

## 운영 메모

- WebSocket 실시간과 historical REST 다운로드는 분리한다.
- 요청 제한은 키움 REST limiter를 거쳐야 한다.
- 대량 다운로드는 장중 실시간 처리와 경쟁하지 않게 백그라운드에서 낮은 우선순위로 실행한다.
- SOR 통합 차트를 전략에 쓰기 전에는 MTS와 OHLC/거래량/거래대금 차이를 먼저 검증한다.
