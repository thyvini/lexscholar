/// Stored finished summaries (F004, research §5): one row per (project, document) in `DocumentSummaries`.
module LexScholar.Infrastructure.SummaryRepository

open LexScholar.Domain

let save (dataDirectory: string) (key: SummaryKey) (summary: StoredSummary) : unit = ()

let find (dataDirectory: string) (key: SummaryKey) : StoredSummary option = None

let delete (dataDirectory: string) (key: SummaryKey) : unit = ()

let listKeys (dataDirectory: string) : SummaryKey list = []
