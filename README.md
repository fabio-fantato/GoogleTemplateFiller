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

## Project structure

```
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
│   └── TemplateInspectorService.cs              Placeholder discovery for InspectTemplate
└── models/                                      Plain DTOs for JSON (de)serialization, not OSStructures
    ├── GoogleFillRequest.cs
    ├── OutSystemsFillRequest.cs
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
