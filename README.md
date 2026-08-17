# GoogleTemplateFiller

OutSystems ODC Custom Code (External Library) that copies a Google Docs template, fills its
placeholders with real data via the Google Docs and Drive APIs, and returns the resulting
document (and optionally a PDF export).

## Actions (`IGoogleTemplateFillerActions`)

- **`FillGoogleDocTemplate`** — copies `templateId` into `folderId` as `fileName`, fills it with
  a JSON payload (`fields`/`images`/`tables`, see below), and returns the filled Google **Doc's**
  ID and URL. Does not export a PDF — see `ExportFilledDocumentAsPdf` below.
- **`FillGoogleDocTemplateOutSystems`** — same as `FillGoogleDocTemplate`, but accepts the JSON
  shape produced by OutSystems JSON generators: tables as `table1`/`table2`/... keys (each a
  `columns` map + `rows` objects) or as a single `tables` array, and `images` as a flat map or
  an array of single-key objects. See "OutSystems JSON shape" below.
- **`InspectTemplate`** — scans a template and returns every placeholder found: plain fields,
  image placeholders, table definitions (id + field names, in template order), and conditional
  block names. Use this to validate a payload before filling.
- **`ExportFilledDocumentAsPdf`** — exports a filled Doc (from one of the two actions above)
  straight to PDF bytes and deletes the source Doc. Kept as a separate call from filling so a
  consumer that only needs the Doc — or that will export later, on demand — doesn't pay the
  export's latency on every fill. Doesn't persist the PDF to Drive; it returns the bytes directly.
- **`FillTemplateWithCallback`** — fills a template via a callback exchange instead of an
  inline JSON payload. See "Callback flow" below.
- **`DownloadFilledDocumentAsPdf`** — exports a Doc to PDF bytes **without** deleting the source
  Doc, so it can be called again later against the same `documentId`. Pairs with
  `FillTemplateWithCallback` when `uploadPDFWhenCompleted` is `false`: the Doc is left alive, and
  this is how the caller fetches the PDF afterwards, on demand, possibly more than once.
- **`ExportAndPreserveFilledDocumentAsPdf`** — exports a filled Doc to PDF, saves that PDF as a
  new file inside `targetFolderId`, and deletes the source Doc. Unlike `ExportFilledDocumentAsPdf`,
  the exported PDF is persisted in Drive (not only returned as bytes) and the source Doc is
  removed instead of kept.

### Deprecated

- **`DownloadPdfFromDrive`** and **`DownloadPdfFromDriveAndDelete`** — removed. They relied on
  the caller already having a PDF's Drive `fileId`, an old pattern from before PDFs were exported
  on demand. Use `ExportFilledDocumentAsPdf` (bytes only) or `ExportAndPreserveFilledDocumentAsPdf`
  (bytes + persisted in a target folder) instead.

All actions take a Google OAuth2 access `token` with the required Docs/Drive scopes and report
`success` + `errorMessage` as `out` parameters.

## Placeholder syntax

- **Plain field**: `{{fieldName}}` → replaced by `fields.fieldName`.
- **Image**: `{{img:name|w:200|h:150}}` → replaced by an inline image from `images.name`
  (a base64 data URI, e.g. `data:image/png;base64,...`). `w`/`h` (points) are optional. If the
  base64 value has no `data:` prefix, `data:image/png;base64,` is assumed.
- **Conditional block**: `{{if:name}}...{{endif:name}}` → the block is kept or removed based on
  whether `name` is present/truthy in the request (see `ConditionalReplacerService`).
- **Table cell**: `{{tableId_fieldName_row_N}}` (or the Portuguese variant
  `{{tableId_fieldName_linha_N}}`) → filled from the matching `TableDefinition`. `tableId` must
  not contain underscores; `fieldName` may. Only row 1 needs to exist in the template — extra
  rows are inserted and filled automatically when the table data has more than one row.

## JSON payload shape (`FillGoogleDocTemplate`)

```json
{
  "templateId": "...",
  "folderId": "...",
  "fileName": "...",
  "fields": { "fieldName": "value" },
  "images": { "name": "data:image/png;base64,..." },
  "tables": [
    {
      "id": "tableId",
      "fields": ["col1", "col2"],
      "rows": [["a", "b"], ["c", "d"]]
    }
  ]
}
```

## OutSystems JSON shape (`FillGoogleDocTemplateOutSystems`)

Same `fields`, plus:

- **Tables** — either `table1`, `table2`, ... top-level keys, or a single `tables` array:

  ```json
  "tables": [
    {
      "id": "tableId",
      "columns": { "column1": "col1", "column2": "col2" },
      "rows": [{ "column1": "a", "column2": "b" }]
    }
  ]
  ```

  Column order is derived by sorting the `columnN` keys numerically.
- **Images** — a flat map (`{ "name": "data:..." }`) or an array of single-key objects
  (`[{ "name": "data:..." }]`).

## Callback flow (`FillTemplateWithCallback`)

For callers that can't (or don't want to) embed large base64 images inline, `FillTemplateWithCallback`
takes just `token` + `requestGuid` + `requestUri` and pulls everything else via HTTP calls, all
authenticated with `token` sent as the `X-API-KEY` header (a convention of this callback contract, not
a Google API header):

1. **GET `requestUri`** — `?requestGuid={requestGuid}`, header `X-API-KEY: {token}`. Returns the same
   JSON shape as `FillGoogleDocTemplateOutSystems`'s payload (`templateId`/`folderId`/`fileName`/
   `fields`/`tables`), except each image value is a **file GUID** instead of base64, plus:

   ```json
   {
     "templateId": "...", "folderId": "...", "fileName": "...",
     "fields": { "fieldName": "value" },
     "images": { "companyLogo": "8f14e45f-...guid...", "signature": "3c59dc04-...guid..." },
     "tables": [ { "id": "tableId", "columns": { "column1": "col1" }, "rows": [{ "column1": "a" }] } ],
     "downloadUri": "https://.../download",
     "uploadUri": "https://.../upload",
     "uploadPDFWhenCompleted": true
   }
   ```

2. **GET `downloadUri`** — once per image GUID, `?fileGuid={guid}`, header `X-API-KEY: {token}`.
   Response body is the raw image bytes (any `Content-Type`) — the library sniffs the real format
   from magic bytes and base64-encodes it before splicing it back into the fill payload in place of
   the GUID.
3. Fills the template (same logic as `FillGoogleDocTemplateOutSystems`), then branches on
   `uploadPDFWhenCompleted`:
   - **`true`** — exports the filled Doc to PDF and deletes the source Doc (same as
     `ExportFilledDocumentAsPdf`).
   - **`false`** — leaves the filled Doc as-is, no export, no delete. Its `documentId` is reported
     via `uploadUri` so the caller can fetch the PDF later, any number of times, with
     `DownloadFilledDocumentAsPdf`.
4. Either way, **POSTs `uploadUri`** as `multipart/form-data` — header `X-API-KEY: {token}`, form
   fields `requestGuid`, `documentId`, `timestamp` (UTC ISO-8601, when this POST was built),
   `isSuccess`, `errorMessage`, and `hasFile`; when `hasFile` is `true`, also `fileName` and a file
   part named `fileContent` holding the raw PDF bytes (`Content-Type: application/pdf`). Sent as
   multipart rather than JSON+base64 so an OutSystems receiver can bind `fileContent` straight to a
   `BinaryData` input parameter — no manual base64 decode, and no ~33% size inflation on top of the
   PDF's real byte size (relevant since ODC's own REST API Gateway has its own request-size
   ceiling, typically ~10 MB). If any step before this point throws, `uploadUri` is still POSTed
   with `isSuccess=false`, `errorMessage` set, and `hasFile=false`, as long as `uploadUri` was
   already known by then (i.e. `requestUri` had already responded).

`FillTemplateWithCallback` returns the uploaded `fileName` (empty when `uploadPDFWhenCompleted` was
`false`) plus the usual `success`/`errorMessage` pair. Nothing is returned to the OutSystems caller
except that — the actual PDF only ever leaves this library via `uploadUri` or a later
`DownloadFilledDocumentAsPdf` call.

### Example `uploadUri` request bodies

Three concrete shapes of the `multipart/form-data` POST body `FillTemplateWithCallback` sends to
`uploadUri`, shown as raw multipart (boundary shortened for readability) so you can check your
receiver parses each field correctly:

**1. `uploadPDFWhenCompleted: true`, fill succeeded — file attached, `hasFile: true`:**

```text
POST /upload HTTP/1.1
X-API-KEY: ya29.a0AfH6...
Content-Type: multipart/form-data; boundary=----B1

------B1
Content-Disposition: form-data; name="requestGuid"

8b1e2f2a-4c3d-4e9a-9a1e-6f2b3c4d5e6f
------B1
Content-Disposition: form-data; name="documentId"

1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789
------B1
Content-Disposition: form-data; name="timestamp"

2026-08-17T14:32:05.1234567Z
------B1
Content-Disposition: form-data; name="isSuccess"

True
------B1
Content-Disposition: form-data; name="errorMessage"

------B1
Content-Disposition: form-data; name="hasFile"

True
------B1
Content-Disposition: form-data; name="fileName"

Invoice_2026.pdf
------B1
Content-Disposition: form-data; name="fileContent"; filename="Invoice_2026.pdf"
Content-Type: application/pdf

%PDF-1.7 ...(raw PDF bytes)...
------B1--
```

**2. `uploadPDFWhenCompleted: false`, fill succeeded — no file, `hasFile: false`, `documentId` kept
for a later `DownloadFilledDocumentAsPdf` call:**

```text
------B1
Content-Disposition: form-data; name="requestGuid"

8b1e2f2a-4c3d-4e9a-9a1e-6f2b3c4d5e6f
------B1
Content-Disposition: form-data; name="documentId"

1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789
------B1
Content-Disposition: form-data; name="timestamp"

2026-08-17T14:31:47.9876543Z
------B1
Content-Disposition: form-data; name="isSuccess"

True
------B1
Content-Disposition: form-data; name="errorMessage"

------B1
Content-Disposition: form-data; name="hasFile"

False
------B1--
```

**3. Failure partway through (e.g. `downloadUri` timed out, or the template had a bad
placeholder) — `isSuccess: false`, `errorMessage` set, no file, `documentId` empty because the
Doc was never created:**

```text
------B1
Content-Disposition: form-data; name="requestGuid"

8b1e2f2a-4c3d-4e9a-9a1e-6f2b3c4d5e6f
------B1
Content-Disposition: form-data; name="documentId"

------B1
Content-Disposition: form-data; name="timestamp"

2026-08-17T14:30:12.1112223Z
------B1
Content-Disposition: form-data; name="isSuccess"

False
------B1
Content-Disposition: form-data; name="errorMessage"

Response status code does not indicate success: 404 (Not Found).
------B1
Content-Disposition: form-data; name="hasFile"

False
------B1--
```

Note `documentId` is only empty when the failure happened before the fill step created the Doc
(e.g. a bad `downloadUri`/image GUID). A failure during export (case 1's flow, but the PDF export
itself fails) still reports the real `documentId` — the Doc exists, just wasn't exported.

### Example receiver APIs

These endpoints are *not* part of this library — they're what the caller (whoever invokes
`FillTemplateWithCallback`) must implement so the library has something to talk to.

**`downloadUri`** — minimal ASP.NET Core, for wherever image files actually live:

```csharp
// GET /download?fileGuid={guid}  -> raw image bytes
app.MapGet("/download", async (string fileGuid, HttpRequest req) =>
{
    if (req.Headers["X-API-KEY"] != ExpectedToken)
        return Results.Unauthorized();

    byte[] bytes = await imageStore.GetBytesAsync(fileGuid); // your own storage lookup
    string contentType = imageStore.GetContentType(fileGuid) ?? "application/octet-stream";
    return Results.Bytes(bytes, contentType);
});
```

**`uploadUri`** — an OutSystems REST API method, since that's where the PDF actually needs to
land. Define it in Service Studio (Integrations / Exposed REST API) with:

- Method: `POST`, consumes `multipart/form-data`.
- Input parameters: `requestGuid`, `documentId`, `timestamp`, `errorMessage` (all Text),
  `isSuccess` and `hasFile` (Boolean), `fileName` (Text, only present when `hasFile` is `true`) —
  bound to the matching form fields.
- Input parameter `fileContent` of type **`BinaryData`** (only present when `hasFile` is `true`) —
  OutSystems binds the multipart file part with that name to it automatically, no manual base64
  decode needed. Make it an optional/nullable input, since it's absent whenever `hasFile` is `false`.
- In the method's logic: check the `X-API-KEY` request header against the expected token
  (`GetHTTPRequestHeader` or the built-in HTTP Request handling). If `isSuccess` is `false`, log/
  alert on `errorMessage` instead of expecting a file. If `hasFile` is `true`, persist `fileContent`
  (e.g. `File System` extension, or upload to Drive/Blob storage) keyed by `requestGuid`; if
  `false`, just record `documentId` — that's what a later `DownloadFilledDocumentAsPdf` call needs.
- Respond `200 OK` (empty body is fine — `FillTemplateWithCallback` only checks the status code).

## Action examples

Every action follows the OutSystems External Library pattern: positional inputs, `out success` +
`out errorMessage`, and one main return value. C# call shape shown below (same as
`GoogleTemplateFillerTest/UnitTestGoogleTemplateFiller.cs`); from OutSystems it's the same
parameters/outputs via the generated block.

**`FillGoogleDocTemplate`** — inline JSON, `fields`/`images`/`tables` shape:

```csharp
var actions = new GoogleTemplateFillerActions();
string docId = actions.FillGoogleDocTemplate(
    token, templateId, folderId, "Invoice_2026",
    """{"fields":{"customerName":"Acme Corp"},"images":{},"tables":[]}""",
    out string docUrl, out bool success, out string errorMessage);
// success = true, docId = "1AbCdEfGh...", docUrl = "https://docs.google.com/document/d/.../edit"
```

**`FillGoogleDocTemplateOutSystems`** — same, OutSystems-generated `table1`/`table2`/`columns`+`rows` shape:

```csharp
string docId = actions.FillGoogleDocTemplateOutSystems(
    token, templateId, folderId, "Invoice_2026",
    """{"fields":{"customerName":"Acme Corp"},"table1":{"id":"items","columns":{"column1":"name"},"rows":[{"column1":"Widget"}]}}""",
    out string docUrl, out bool success, out string errorMessage);
```

**`InspectTemplate`** — discover placeholders before filling:

```csharp
string fieldsJson = actions.InspectTemplate(
    token, templateId,
    out string imagesJson, out string tablesJson, out string conditionsJson,
    out bool success, out string errorMessage);
// fieldsJson      = ["customerName","invoiceDate"]
// imagesJson      = [{"name":"companyLogo","width":200,"height":150,"placeholder":"{{img:companyLogo|w:200|h:150}}"}]
// tablesJson      = [{"id":"items","fields":["name","qty"]}]
// conditionsJson  = ["hasDiscount"]
```

**`ExportFilledDocumentAsPdf`** — export a filled Doc to PDF bytes, **deletes** the source Doc:

```csharp
byte[] pdfBytes = actions.ExportFilledDocumentAsPdf(
    token, docId, out bool success, out string errorMessage);
// success = true, pdfBytes[0..3] = { 0x25, 0x50, 0x44, 0x46 } ("%PDF")
// docId no longer exists in Drive after this call
```

**`DownloadFilledDocumentAsPdf`** — export a filled Doc to PDF bytes, **keeps** the source Doc, so
it can be called again against the same `documentId`:

```csharp
byte[] firstPdfBytes = actions.DownloadFilledDocumentAsPdf(
    token, docId, out bool success1, out string error1);
// success1 = true, docId still exists in Drive

byte[] secondPdfBytes = actions.DownloadFilledDocumentAsPdf(
    token, docId, out bool success2, out string error2);
// success2 = true — same docId, downloaded again, still not deleted
```

**`ExportAndPreserveFilledDocumentAsPdf`** — export to PDF, persist that PDF in a Drive folder,
deletes the source Doc:

```csharp
byte[] pdfBytes = actions.ExportAndPreserveFilledDocumentAsPdf(
    token, docId, targetFolderId,
    out string resultPdfFileId, out string resultPdfUrl,
    out bool success, out string errorMessage);
// resultPdfFileId = "1XyZ..." (new Drive file, lives in targetFolderId)
// resultPdfUrl    = "https://drive.google.com/file/d/1XyZ.../view"
// docId no longer exists in Drive after this call; resultPdfFileId does
```

**`FillTemplateWithCallback`** — no inline JSON, just the callback trio:

```csharp
string fileName = actions.FillTemplateWithCallback(
    token, requestGuid, requestUri,
    out bool success, out string errorMessage);
// success = true
// fileName = "Invoice_2026.pdf" if uploadPDFWhenCompleted was true in requestUri's response, else ""
// The actual result (PDF or just documentId) was already POSTed to uploadUri — see the
// "Example uploadUri request bodies" above for exactly what that POST looks like.
```

## Project structure

```text
GoogleTemplateFiller/
├── interfaces/IGoogleTemplateFillerActions.cs   OSInterface/OSAction - the ODC-exposed contract
├── actions/GoogleTemplateFillerActions.cs       Implementation (out resultDocumentUrl/success/errorMessage)
├── services/
│   ├── GoogleTemplateFillerService.cs           Orchestrates: copy template -> fields -> tables -> images
│   ├── GoogleDocsService.cs                     Google Docs API wrapper (get/batchUpdate)
│   ├── GoogleDriveService.cs                    Google Drive API wrapper (copy/upload/export-to-pdf/delete)
│   ├── PlaceholderReplacerService.cs            {{field}} and {{tableId_field_row_N}} replaceAllText requests
│   ├── TableExpanderService.cs                  Inserts extra table rows for multi-row data
│   ├── ImageReplacerService.cs                  Finds {{img:...}} placeholders, uploads + inserts inline images
│   ├── ConditionalReplacerService.cs            {{if:name}}...{{endif:name}} block removal
│   ├── TemplateInspectorService.cs              Placeholder discovery for InspectTemplate
│   ├── CallbackFillService.cs                    Orchestrates FillTemplateWithCallback (requestUri/downloadUri/uploadUri)
│   └── ImageMimeSniffer.cs                       Detects image format from magic bytes
└── models/                                      Plain DTOs for JSON (de)serialization, not OSStructures
    ├── GoogleFillRequest.cs
    ├── OutSystemsFillRequest.cs
    ├── CallbackFillPayload.cs
    ├── OutSystemsTableDefinition.cs
    ├── TableDefinition.cs
    ├── ImagePlaceholder.cs
    └── ConditionalPlaceholder.cs
```

## Testing

`dotnet test` runs unit tests (models/services logic) without any external dependency, plus a
set of `Integration_*` tests that call the real Google Docs/Drive APIs and are skipped unless
`GOOGLE_ACCESS_TOKEN` is set in the environment.

## Releasing

`GoogleTemplateFiller/CompileAndGenerateRelease.ps1` publishes (`linux-x64`, framework-dependent)
and zips the output into `GoogleTemplateFiller/dist/GoogleTemplateFiller.zip`, which is what gets
uploaded to the ODC Portal and attached to GitHub releases.
