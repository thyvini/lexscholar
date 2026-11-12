/// Types for summaries of a document (F003). Nothing here is stored; rules
/// (readability, chunking, parsing points) arrive with the slices that need them.
namespace LexScholar.Domain

/// Whether the configured service runs on this machine or elsewhere (FR-014).
type ServiceLocation =
    | Local
    | Elsewhere

module ServiceLocation =
    /// Local only for the name `localhost` or a loopback IP literal (127.0.0.0/8, ::1);
    /// everything else, including private network addresses and 0.0.0.0, is elsewhere
    /// (research.md section 2). A bracketed IPv6 host, as `Uri.Host` gives it, is accepted.
    let classify (host: string) : ServiceLocation =
        let name = host.Trim().Trim('[', ']')
        if name.Equals("localhost", System.StringComparison.OrdinalIgnoreCase) then Local
        else
            match System.Net.IPAddress.TryParse name with
            | true, address when System.Net.IPAddress.IsLoopback address -> Local
            | _ -> Elsewhere

/// Text of one page; `Number` is the page position, first page = 1.
type PageText = { Number: int; Text: string }

/// Why a document has too little text to summarize.
type NoTextReason =
    | NoReadableText
    | NotEnoughText

/// Pages split by whether they have readable text.
type ReadablePages =
    { Readable: PageText list
      Unreadable: int list }

/// Consecutive text from whole pages (or part of one long page), sent in one request.
type Chunk = { Content: string; PageNumbers: int list }

/// One generated point and the readable pages it cites.
type SummaryPoint = { Text: string; Pages: int list }

/// Points that passed citation validation, and how many were dropped.
type ValidatedPoints =
    { Points: SummaryPoint list
      Omitted: int }

/// A finished summary with the context shown next to it.
type DocumentSummary =
    { DocumentId: System.Guid
      Points: SummaryPoint list
      PagesCovered: int list
      UnreadablePages: int list
      OmittedPoints: int
      GeneratedAt: System.DateTimeOffset
      ServiceHost: string
      Model: string }

/// The service's answer could not be read as the requested JSON.
type Unparseable = Unparseable

module Summaries =
    /// Fewer readable characters than this in total is not enough to summarize.
    let minimumReadableCharacters = 200

    let private hasLetterOrDigit (text: string) = text |> Seq.exists System.Char.IsLetterOrDigit

    /// Splits pages into readable (some letter or digit) and unreadable, and
    /// refuses documents with no readable page or fewer than 200 readable characters.
    let readability (pages: PageText list) : Result<ReadablePages, NoTextReason> =
        let readable, unreadable = pages |> List.partition (fun page -> hasLetterOrDigit page.Text)
        if List.isEmpty readable then Error NoReadableText
        elif readable |> List.sumBy (fun page -> page.Text.Length) < minimumReadableCharacters then Error NotEnoughText
        else Ok { Readable = readable; Unreadable = unreadable |> List.map (fun page -> page.Number) }


    /// Splits readable pages into chunks of at most `maxCharacters` (marker included).
    let chunk (maxCharacters: int) (pages: PageText list) : Chunk list =
        let marker (number: int) = $"[Page {number}]\n"
        let separator = 2 // the blank line between pieces
        let pieces =
            pages
            |> List.collect (fun page ->
                let room = max 1 (maxCharacters - (marker page.Number).Length)
                let parts =
                    if page.Text.Length <= room then [ page.Text ]
                    else page.Text |> Seq.chunkBySize room |> Seq.map (fun characters -> System.String characters) |> Seq.toList
                parts |> List.map (fun part -> page.Number, marker page.Number + part))
        // Each open chunk is its pieces, newest first, and its length so far.
        let packed =
            pieces
            |> List.fold (fun chunks (number, piece: string) ->
                match chunks with
                | (items, length) :: rest when length + separator + piece.Length <= maxCharacters ->
                    ((number, piece) :: items, length + separator + piece.Length) :: rest
                | _ -> ([ number, piece ], piece.Length) :: chunks) []
        packed
        |> List.rev
        |> List.map (fun (items, _) ->
            let ordered = List.rev items
            { Content = ordered |> List.map snd |> String.concat "\n\n"
              PageNumbers = ordered |> List.map fst |> List.distinct |> List.sort })

    /// How a point is shown to the model when combining.
    let pointLine (point: SummaryPoint) : string =
        let pages = System.String.Join(", ", point.Pages)
        $"- (pages {pages}) {point.Text}"

    /// Groups points, in order, so each group's lines fit `maxCharacters`.
    let groupPoints (maxCharacters: int) (points: SummaryPoint list) : SummaryPoint list list =
        points
        |> List.fold (fun groups point ->
            let cost = (pointLine point).Length
            match groups with
            | (items, length) :: rest when length + 1 + cost <= maxCharacters -> (point :: items, length + 1 + cost) :: rest // 1 = newline
            | _ -> ([ point ], cost) :: groups) []
        |> List.rev
        |> List.map (fst >> List.rev)

    /// Longest accepted point text.
    let maximumPointCharacters = 1000

    /// Drops wrappers around the JSON only: leading think blocks, then a leading code
    /// fence line and a trailing fence. Text inside the JSON is never touched.
    let private unwrap (output: string) =
        let mutable text = output.Trim()
        let mutable changed = true
        while changed do
            changed <- false
            if text.StartsWith("<think>", System.StringComparison.OrdinalIgnoreCase) then
                let close = text.IndexOf("</think>", System.StringComparison.OrdinalIgnoreCase)
                if close >= 0 then
                    text <- text.Substring(close + "</think>".Length).Trim()
                    changed <- true
        if text.StartsWith "```" then
            let newline = text.IndexOf '\n'
            text <- if newline >= 0 then text.Substring(newline + 1) else ""
        if text.TrimEnd().EndsWith "```" then text <- text.TrimEnd().[.. text.TrimEnd().Length - 4]
        text

    let private pageOf (element: System.Text.Json.JsonElement) =
        match element.ValueKind with
        | System.Text.Json.JsonValueKind.Number ->
            match element.TryGetInt32() with
            | true, value when value >= 1 -> Some value
            | _ -> None
        | _ -> None

    /// A point is valid when its text is a non-empty string of at most 1,000
    /// characters and its pages are a non-empty array of integers, all allowed.
    let private point (allowed: Set<int>) (element: System.Text.Json.JsonElement) : SummaryPoint option =
        let kind = System.Text.Json.JsonValueKind.String
        if element.ValueKind <> System.Text.Json.JsonValueKind.Object then None
        else
            match element.TryGetProperty "text", element.TryGetProperty "pages" with
            | (true, text), (true, pages) when text.ValueKind = kind && pages.ValueKind = System.Text.Json.JsonValueKind.Array ->
                let trimmed = text.GetString().Trim()
                let cited = [ for item in pages.EnumerateArray() -> pageOf item ]
                if trimmed = "" || trimmed.Length > maximumPointCharacters then None
                elif List.isEmpty cited || cited |> List.exists (fun page -> page.IsNone || not (allowed.Contains page.Value)) then None
                else Some { Text = trimmed; Pages = cited |> List.choose id |> List.distinct |> List.sort }
            | _ -> None

    /// Reads the model's answer into validated points (research §5). The output is
    /// untrusted: think blocks and code fences are dropped, the outermost JSON
    /// object is parsed, and a point citing any page outside `allowedPages` is omitted.
    let parsePoints (allowedPages: Set<int>) (output: string) : Result<ValidatedPoints, Unparseable> =
        let cleaned = unwrap output
        let first = cleaned.IndexOf '{'
        let last = cleaned.LastIndexOf '}'
        if first < 0 || last <= first then Error Unparseable
        else
            try
                use document = System.Text.Json.JsonDocument.Parse(cleaned.Substring(first, last - first + 1))
                match document.RootElement.TryGetProperty "points" with
                | true, points when points.ValueKind = System.Text.Json.JsonValueKind.Array ->
                    let parsed = [ for item in points.EnumerateArray() -> point allowedPages item ]
                    Ok { Points = parsed |> List.choose id; Omitted = parsed |> List.filter Option.isNone |> List.length }
                | _ -> Error Unparseable
            with :? System.Text.Json.JsonException -> Error Unparseable
