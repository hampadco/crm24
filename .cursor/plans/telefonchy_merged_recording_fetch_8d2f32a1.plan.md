---
name: Telefonchy merged recording fetch
overview: Verification against the live Telefonchy API shows CRM is waiting for a `file_id` that Telefonchy assigns to only some calls, while an on-demand "merged quality recording" endpoint (and a webhook announcing it) already provides the audio for every answered call. Switch recording retrieval to that endpoint and drop the blind 45-second polling.
todos:
  - id: client-merged
    content: "TelefonchyClient: GetMergedRecordingAsync (200/202/404 handling), DTO RecordId/DownloadUrl, keep object_id when call_id is Asterisk id, parse recording.merged.ready"
    status: completed
  - id: recording-store
    content: ICallRecordingStore abstraction + Web implementation (wwwroot/uploads/records/{tenant}/calls/{id}/merged.wav) + Attachment row, register in Program.cs
    status: completed
  - id: service-ensure
    content: TelefonchyCallService.EnsureRecordingAsync + HasRecording helper; persist recordingFile/recordingStatus; keep file_id path as fallback
    status: completed
  - id: jobs-rewrite
    content: "Jobs: handle recording.merged.ready, FetchMergedRecordingAsync with backoff and dedupe, remove 45s x 40 polling chain and triple scheduling"
    status: completed
  - id: web-play
    content: TelephonyController.Recording/Report and RecordsController.Details use EnsureRecordingAsync/HasRecording; pending UI stays
    status: completed
  - id: backfill-verify
    content: Backfill today's answered calls without recording, build, verify 19640 plays and only one fetch chain per call
    status: completed
isProject: false
---

# Telefonchy: fetch recordings via merged-quality endpoint

## What the API check showed (call 19640, cuid `1788975011.1566322`, id `6aa197d9be7839b9910114d7`)

- The record **does** exist in CRM (row 19640, talk 14s, cuid + callId + requestId filled). Only the recording is missing.
- `GET /calls?call_dest=09913389706` for today: 8 answered calls, all `has_audio=true`, but only **3 of 8** ever got a `file_id`. 19640, 19479 and three others have `updated_at == created_at` and no `file_id` after hours. So `file_id` + `/calls/record` is not a reliable path; it is not something CRM can wait its way out of.
- Telefonchy sends a second webhook to our **CDR hook** that we ignore: `{"event":"recording.merged.ready","cuid":..,"record_id":<mongo id>,"download_url":"https://panel.telefonchy.com/webservice/v1/calls/record-quality/merged?cuid=..&record_id=.."}`. For 19640 it arrived at 17:32:46 UTC, ~2 min after hangup. 10 such events received today, all parsed as ordinary CDRs and discarded.
- `GET /calls/record-quality/merged?cuid=&record_id=` (header `webservice-token`) verified live:
  - 19640 → **HTTP 200, `audio/wav`, 697 KB, RIFF header**, `filename=1788975011_1566322-merged.wav`. The audio exists right now.
  - 14:21 call (never had a ready event) → first call **202** `{"status":"processing"}`; 25 s later → **200 WAV**. So requesting it triggers preparation on demand.
  - Wrong `record_id` → **404** `{"status":"failed","message":"... یافت نشد یا منقضی شده است"}` — files can expire, so cache locally as soon as they are ready.
  - `record_id` must be the Mongo id (`id`/`object_id`), not `cuid` and not CDR's `record_id:"1"` boolean.
- Everything else CRM uses is correct per docs: `/calls` (Jalali `Y/m/d H:i`, `call_dest`, `data.data` + `pagination`), `/call?call_id=`, `/smartcall` + `/smartcall/status/{request_id}`, `/calls/record?file_id&cuid`, events hook (ringing/hangup) and CDR hook.
- Waste: the deployed code now starts a 45 s x 40 polling chain **three times per call** (hangup event, CDR, and the ready event), each hitting `/calls` up to 6 times, for nothing. Rate limit is 60 req/min per token.

```mermaid
sequenceDiagram
    participant T as Telefonchy
    participant H as CdrHook
    participant J as HangfireJob
    participant S as RecordingStore
    participant U as CallDetailsPage
    T->>H: CDR (ANSWERED, object_id)
    H->>J: FetchMergedRecording(cuid, recordId) after 20s
    J->>T: GET record-quality/merged
    T-->>J: 202 processing
    J->>J: reschedule 30s / 60s / 2m / 5m / 15m
    T->>H: recording.merged.ready (download_url)
    H->>J: FetchMergedRecording now
    J->>T: GET record-quality/merged
    T-->>J: 200 audio/wav
    J->>S: save merged.wav, set recordingFile on record
    U->>S: play from cache; if missing, fetch on demand (202 -> pending UI)
```

## Changes

### 1. `TelefonchyClient` — [src/Crm.Infrastructure/Services/TelefonchyClient.cs](src/Crm.Infrastructure/Services/TelefonchyClient.cs)
- Add `GetMergedRecordingAsync(cuid, recordId, tenantId)` → result with `State` = `Ready(bytes, contentType, fileName)` / `Processing` / `Failed(message)`. Map 200+audio → Ready, 202 or JSON `status=processing` → Processing, 404/JSON `status=failed` → Failed. Reuse `TryReadJsonError` guard so a JSON body is never cached as audio.
- DTO: add `RecordId` (Mongo id) and `DownloadUrl`. In `ParseCall`, when `id`/`call_id` is the Asterisk unique id, still keep the Mongo id from `object_id`/`record_id` (currently `CallId` ends up null for CDRs, losing `object_id`). Ignore `record_id` when it is `"1"`/`"0"`/`false`.
- `ParseWebhookPayload`: read `event`, `download_url`, `record_id`, `generated_at` so `recording.merged.ready` is recognisable.

### 2. Recording storage usable from background jobs
- Add `ICallRecordingStore` (Crm.Core abstraction, same pattern as `ICallPopupNotifier`) with `SaveAsync(tenantId, callRecordId, bytes, ext)` → relative path, and `TryGetPath(...)`. Implement in Crm.Web using `IWebHostEnvironment.WebRootPath` (`uploads/records/{tenant}/calls/{id}/merged.wav`), register in [src/Crm.Web/Program.cs](src/Crm.Web/Program.cs). Add an `Attachment` row like the current controller does.

### 3. `TelefonchyCallService` — [src/Crm.Infrastructure/Services/TelefonchyCallService.cs](src/Crm.Infrastructure/Services/TelefonchyCallService.cs)
- New `EnsureRecordingAsync(callsModuleId, record)` → `Ready | Processing | None`: if `recordingFile` already set → Ready; else if `cuid` + Mongo id present → call merged endpoint; on Ready save via store and set `recordingFile`, `recordingSource=merged`; on Processing set `recordingStatus=processing`. Fallback: if merged fails and `file_id` is usable, use existing `/calls/record`.
- Replace `HasUsableRecordingId`-only checks with `HasRecording(data)` = `recordingFile` set OR usable `recordingFileId`. Keep `IsRecordingPending` but base it on `talkSeconds>0 || hasAudio || recordingFileName`.
- Persist `callId` (Mongo id) from CDR `object_id` so the merged request can always be built.

### 4. Jobs — [src/Crm.Infrastructure/Services/TelefonchyJobs.cs](src/Crm.Infrastructure/Services/TelefonchyJobs.cs)
- `ProcessCdrAsync`: if `event == "recording.merged.ready"` → do not upsert as a call; enqueue `FetchMergedRecordingAsync(tenantId, cuid, recordId)` immediately (validate `download_url` host is the configured Telefonchy base before using it; otherwise build the URL from cuid+recordId).
- After a normal ANSWERED CDR/hangup: schedule `FetchMergedRecordingAsync` once at +20 s.
- `FetchMergedRecordingAsync(tenantId, cuid, recordId, attempt)`: find the CRM call by cuid/callId; skip if already has `recordingFile` (dedupe across the three triggers); call merged endpoint; on Processing reschedule with backoff 30 s, 60 s, 2 m, 5 m, 15 m (max 6 attempts); on Failed try `/calls` once for a `file_id` fallback then stop.
- Remove the 45 s x 40 `RefreshRecordingAsync` chain and the triple scheduling; keep a single `[Obsolete]` overload so already-scheduled Hangfire jobs still deserialize.

### 5. Web — play and details
- [src/Crm.Web/Areas/App/Controllers/TelephonyController.cs](src/Crm.Web/Areas/App/Controllers/TelephonyController.cs) `Recording`: serve `recordingFile` from cache with range support; if absent call `EnsureRecordingAsync`; on Processing return 404 with reason «در حال آماده‌سازی» (existing `RecordingUnavailable`), on Ready stream. Drop the "only if file_id" gate. `Report` `HasRecording` → new helper.
- [src/Crm.Web/Areas/App/Controllers/RecordsController.cs](src/Crm.Web/Areas/App/Controllers/RecordsController.cs) `Details`: call `EnsureRecordingAsync` instead of `TryAttachRecordingAsync`; `HasCallRecording` via new helper; keep `HasPendingRecording` + 20 s auto-refresh in [Details.cshtml](src/Crm.Web/Areas/App/Views/Records/Details.cshtml).
- `RefreshCallIdentifiersAsync`: unchanged (still needed for stub rows with only `requestId`).

### 6. Backfill and verification
- One-off Hangfire job (or run `SyncTenantAsync`) that, for today's answered calls without `recordingFile`, enqueues `FetchMergedRecordingAsync` — this fixes 19640, 19479, 19475, 19466, 19455 immediately.
- Build; open `/App/m/calls/19640` and confirm the WAV plays; confirm Hangfire shows at most one fetch chain per call.

## Notes / open points
- Storage: merged files are WAV (~33 KB/s, ~120 MB per hour of talk). Acceptable for now; optional later: transcode to mp3 with ffmpeg if present.
- `record-quality/merged` is not in the public docs; it is what Telefonchy itself pushes in `download_url`, so treat it as first-class but keep the `file_id` path as fallback.