# Your local LexScholar workspace

This page explains where LexScholar keeps your research projects and PDFs, how it handles them, and which controls are not available yet.

## Where your data is stored

LexScholar keeps everything in one folder on your computer, the **data directory**:

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
| `workspace.sqlite` | Your projects, the active project, library records (original filename, size, page count, import time), which documents belong to which project, and each document's role in each project. |
| `originals/` | The PDFs you imported, exactly as imported. Each file is read-only and has a generated name; your original filename is never used as a file name. |
| `staging/` | Uploads in progress. Anything left here is deleted the next time LexScholar starts. |
| `quarantine/` | Stored PDFs that ended up without a library record because an import was interrupted (for example by a crash). LexScholar moves them here when it starts and logs a warning; it never deletes them. You can inspect or delete them yourself. |
| `.lexscholar.lock` | Marks the folder as in use. Only one running LexScholar can use a data directory at a time; a second one refuses to start. Several browser tabs can use the same running LexScholar. |

## How your data is handled

- **Local only.** Your projects and PDFs are stored and processed on your computer. LexScholar uses no cloud services and sends nothing elsewhere.
- **Only your computer can connect.** LexScholar listens only on `127.0.0.1` (port 5000 by default; change it with `LexScholar:Port`) and rejects requests from other websites.
- **Originals are kept unchanged.** An imported PDF is stored byte for byte, made read-only and never overwritten.
- **One copy of identical files.** If you import a PDF whose bytes are identical to one already in the library, LexScholar reuses the existing record instead of storing a second copy, even under a different filename. A SHA-256 checksum is used only to find possible duplicates; files are then compared byte for byte.
- **Your filename is for display only.** The original filename is shown in the library but never used to locate the file.
- **Removing a document from a project keeps it in the library.** The PDF and its other project associations stay.
- **Unavailable documents keep their records.** When you open a document, LexScholar checks that its stored file exists, can be read and has the recorded size. If not, you get a clear "document unavailable" error, and the library record and project associations are left as they are. LexScholar does not re-check file contents when opening, so a change made outside LexScholar that keeps the same file size is not detected.

## Supported platform and browsers

LexScholar supports Linux with Firefox and Chromium. PDFs are shown in the browser's own PDF viewer, so page navigation, zoom and page labels look and behave as they do in your browser.

## Import limits

| Setting | Default | Meaning |
|---|---|---|
| `LexScholar:ImportLimits:MaxBytes` | 100,000,000 | Largest PDF you can import, in bytes. |
| `LexScholar:ImportLimits:MaxPages` | 2,000 | Most pages a PDF you import can have. |

Set them the same way as the data directory (for example `LexScholar__ImportLimits__MaxPages=500`). A value that is not a whole number, or is zero or negative, stops LexScholar from starting with an error naming the setting. Limits apply when importing: lowering a limit does not affect PDFs already in your library.

## What you cannot do yet

LexScholar does not yet give you full control over your data from within the app:

- There is **no export** of your whole library.
- You **cannot delete** projects or library documents in the app. You can only remove a document from a project, which keeps it in the library.
- There is **no recovery screen** for documents reported as unavailable.

These controls are planned for a later feature (F002, tracked as Codecks card [$12y](https://thyago-cunha-dev.codecks.io/card/12y)). Until then, to back up, move or remove your data, stop LexScholar and copy, move or delete the data directory yourself.

Because these controls are missing, LexScholar does not yet fully meet its own privacy and user-control principle (Constitution Principle II, "Privacy and User Control"). That obligation remains open until they are delivered.
