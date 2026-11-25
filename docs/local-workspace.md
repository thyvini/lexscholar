# Your local LexScholar workspace

This page explains where LexScholar keeps your research projects and PDFs, how it handles them, and which controls are not available yet.

## Where your data is stored

LexScholar keeps its workspace in one folder on your computer, the **data directory**. Summarizing can also send document text and instructions to an external service, as explained below.

- By default: `$XDG_DATA_HOME/lexscholar`, or `~/.local/share/lexscholar` if `XDG_DATA_HOME` is not set (or is not an absolute path).
- To use another folder, set `LexScholar:DataDirectory` when starting LexScholar, either as an environment variable or as a command-line option:

  ```sh
  LexScholar__DataDirectory=/home/me/research/lexscholar dotnet LexScholar.Server.dll
  dotnet LexScholar.Server.dll --LexScholar:DataDirectory=/home/me/research/lexscholar
  ```

  The path must be absolute. It cannot be the filesystem root or an existing file. LexScholar refuses to start and says why if the path is not usable.

  Run these commands from the folder LexScholar was published to, or add `--contentRoot <that folder>`. Started anywhere else, LexScholar still runs, but the browser app does not load: its files are not found.

To see which folder a running LexScholar uses, look at its console output when it starts. One of the first messages names it:

```text
info: LexScholar.Server[0]
      Using data directory /home/me/.local/share/lexscholar.
```

The path is only written to this local log; it is never sent to the browser.

LexScholar creates the folder if it does not exist, readable only by your user account (mode `0700`). Inside it you will find:

| Item | What it holds |
|---|---|
| `workspace.sqlite` | Your projects, active project, library records (original filename, size, page count, import time), project associations and roles, and saved summaries and their generation details. |
| `originals/` | The PDFs you imported, exactly as imported. Each file is read-only and has a generated name; your original filename is never used as a file name. |
| `staging/` | Uploads in progress. Anything left here is deleted the next time LexScholar starts. |
| `quarantine/` | Stored PDFs that ended up without a library record because an import was interrupted (for example by a crash). LexScholar moves them here when it starts and logs a warning; it never deletes them. You can inspect or delete them yourself. |
| `exports/` | Temporary export archives. LexScholar cleans up abandoned archives on startup; cleanup can fail, leaving files for a later attempt. |
| `chatgpt-signin.json` | When signed in, account information, tokens and the send-confirmation record. The file is created with owner-only permissions (`0600`); treat it as a secret. |
| `chatgpt-welcome-shown` | A marker recording that the ChatGPT welcome message was dismissed. |
| `.lexscholar.lock` | Marks the folder as in use. Only one running LexScholar can use a data directory at a time; a second one refuses to start. Several browser tabs can use the same running LexScholar. |

## How your data is handled

- **Local workspace, optional external processing.** Projects, imported PDFs and saved summaries are stored locally. When you request an external summary, document text and any optional instruction leave your computer; see below.
- **Only your computer can connect.** LexScholar listens only on `127.0.0.1` (port 5000 by default; change it with `LexScholar:Port`) and rejects requests from other websites.
- **Originals are kept unchanged.** An imported PDF is stored byte for byte, made read-only and never overwritten.
- **One copy of identical files.** If you import a PDF whose bytes are identical to one already in the library, LexScholar reuses the existing record instead of storing a second copy, even under a different filename. A SHA-256 checksum is used only to find possible duplicates; files are then compared byte for byte.
- **Your filename is for display only.** The original filename is shown in the library but never used to locate the file.
- **Removing a document from a project keeps it in the library.** The PDF and its other project associations stay, but the removed association's saved summary is deleted.
- **Unavailable documents keep their records.** When you open a document, LexScholar checks that its stored file exists, can be read and has the recorded size. If not, you get a clear "document unavailable" error, and the library record and project associations are left as they are. LexScholar does not re-check file contents when opening, so a change made outside LexScholar that keeps the same file size is not detected.

## Summaries, sign-in and consent

In the app, generating a new summary uses your ChatGPT plan. Signing in opens the ChatGPT authorization flow; LexScholar stores the resulting credentials locally so you can stay signed in. Signing in alone does not send a document for summarization.

Before the first summary send for a login, LexScholar asks you to confirm sending the document's extracted text and any optional instruction to OpenAI through your ChatGPT account. Cancel if you do not want to send them. After confirmation, later ordinary summaries can be sent without asking again until you sign out. Regenerating an existing summary with feedback asks for confirmation each time. Stopping a request cannot recall data already sent.

An optional initial instruction or regeneration feedback is sent along with document text, including in multi-part summary requests. LexScholar does not separately save the instruction or feedback, but the generated summary may reflect it and is saved locally. Summaries are generated for your review and are not verified. How an external service uses or retains sent data is governed by that service and your account settings; local deletion cannot control it.

The server also supports a separately configured chat-completions service through its API. For a non-local configured service, the API requires confirmation identifying its host and model before sending text and optional instructions; the current app offers the ChatGPT plan rather than a service chooser. A locally configured service processes requests at that local destination.

To sign out, use **Conta → Sair**. LexScholar removes the local sign-in record and asks OpenAI to revoke it. Revocation is best effort: if OpenAI does not confirm it, the app says so and you can also disconnect LexScholar in your ChatGPT settings. Signing out does not delete saved summaries or data already sent.

The browser uses session storage for onboarding state and, when applicable, a pending library-deletion confirmation used for retry and reconciliation. These are separate from the workspace folder.

## Exporting and deleting

In the library, **Exportar biblioteca** downloads a ZIP containing the original PDFs, project and association metadata, roles, and saved summaries (including readable text versions). Credentials and settings are not included. Check the browser's download: LexScholar cannot confirm it was saved to disk. The archive is for use outside LexScholar; there is no in-app restore. An unavailable document can prevent export.

To delete library documents, use **Selecionar para exclusão**, review the impact, and confirm **Excluir permanentemente**. This removes selected library PDFs, their project associations and saved summaries. Check the results: deletion can be incomplete and offer a retry. Removing a document from one project is a different action and retains the library PDF.

Deleting a summary, library document, or the data directory does not remove original files you imported, downloads, exports, backups, other copies, or data already sent to external services. Deletion does not guarantee secure physical erasure. To back up, move or remove the entire local workspace, stop LexScholar and copy, move or delete its data directory yourself; protect any copies, especially sign-in credentials.

## Supported platform and browsers

LexScholar supports Linux with Firefox and Chromium. PDFs are shown in the browser's own PDF viewer, so page navigation, zoom and page labels look and behave as they do in your browser.

## Import limits

| Setting | Default | Meaning |
|---|---|---|
| `LexScholar:ImportLimits:MaxBytes` | 100,000,000 | Largest PDF you can import, in bytes. |
| `LexScholar:ImportLimits:MaxPages` | 2,000 | Most pages a PDF you import can have. |

Set them the same way as the data directory (for example `LexScholar__ImportLimits__MaxPages=500`). A value that is not a whole number, or is zero or negative, stops LexScholar from starting with an error naming the setting. Limits apply when importing: lowering a limit does not affect PDFs already in your library.

## Remaining limitations

There is no in-app project deletion, export restore, or recovery screen for documents reported as unavailable. Exporting or deleting in the app does not manage copies outside the workspace.
