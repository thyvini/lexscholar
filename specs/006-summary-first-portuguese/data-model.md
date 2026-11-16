# Data model: Summary first, in Portuguese (F006)

No storage change: no migration, no new table, column or file. Stored summaries (F004), sign-in (F005) and documents (F001) are unchanged.

## Client state changes

### Library (`src/LexScholar.Client/Library.fs`)

| Field | Type | Rule |
|---|---|---|
| `Opened` | `Guid option` | The document of the active project shown in the main area. Set by opening a row. Cleared when the active project changes or the document leaves the project's associations (the rules of `Reader.followLibrary`). |
| `ChangingRole` | `Guid option` | Moved from `Reader`: one role change at a time; other changes are ignored while one runs. |
| `RoleStale` | `bool` | Moved from `Reader`: a role change answered after the project changed doesn't show its error on the new visit. |
| `RoleError` | failure option | Moved from `Reader.Error` (role cases only). |

Messages added: `OpenDocument of Guid`, `ChangeRole of Guid * DocumentRole`, `RoleChanged of projectId: Guid * Result<unit, Failure>`. Effect added: `SendRole`; after success it reloads the project (`ReloadProject`), as `Reader.RefreshProject` did.

Removed with `Reader.fs`: `Opening`, `Open`, `Page`, `Navigation`, `CheckRequest`, `ContentChecked`, `ShowPage`, `Close`, `CheckContent`.

### Summary (`src/LexScholar.Client/Summary.fs`)

- `followReader` becomes `followOpened (library: Library.Model)`, with the same effects on a view change (cancel, refresh).
- Destination: the client always asks with `Destination.Plan`; a configured service is not offered (R7). `NotConfigured` stays as a case the client can receive, shown as the generic failure, and not described.
- Delete confirmation, finished summary only: `Delete` (was `Dismiss`) on a shown summary moves to `ConfirmingDelete`. `ConfirmDelete` then emits `DeleteSummary`, as `Dismiss` did. `CancelDelete` returns to the shown summary with nothing sent.
- A failed summary keeps an immediate action, "Fechar": it removes the failure at once, as `Dismiss` does today (emits `DeleteSummary`), with no confirmation. `Stop` while a summary is generating stays immediate (F004 Stop, not a delete).

### Texts (`src/LexScholar.Client/Texts.fs`, new)

Values and functions only, no state. Their contract is in [contracts/interface-texts.md](contracts/interface-texts.md).

## Citation link

| Part | Value |
|---|---|
| Document | the opened document's `contentUrl` from the API (never built from another document) |
| Page | the first page of the citation's validated pages |
| `href` | `{contentUrl}#page={first}` |
| Text | `p. {n}` for one page; `pp. {first}–{last}` (en dash) for consecutive pages. A non-consecutive set is shown as separate links joined by ", " (screen 06: `p. 3, p. 4`) |
| Target | new tab (`_blank`, `noopener`) |
