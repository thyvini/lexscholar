/// Reading the summary endpoint's JSON: error bodies and NDJSON stream events.
/// Pure parsing, no HTTP (the request itself is `App.Api.summarize`).
module LexScholar.Client.SummaryStream

open System.Text.Json

let private intList (element: JsonElement) =
    element.EnumerateArray() |> Seq.map (fun value -> value.GetInt32()) |> Seq.toList

let private failureOfCode (code: string) : Summary.Failure =
    match code with
    | "summary_service_not_configured" -> Summary.NotConfigured
    | "document_unavailable" -> Summary.Unavailable
    | "no_text" -> Summary.NoText
    | "not_enough_text" -> Summary.NotEnoughText
    | "too_many_pages" -> Summary.TooManyPages
    | "service_failed" -> Summary.ServiceFailed None
    | _ -> Summary.RequestFailed

/// An error response's failure. A 409 `confirmation_required` carries the host and
/// model to show; anything else maps by its `code`. An unreadable body is a plain failure.
let failureOfBody (body: string) : Summary.Failure =
    try
        use json = JsonDocument.Parse(body)
        let root = json.RootElement
        match root.GetProperty("code").GetString() with
        | "confirmation_required" ->
            Summary.ConfirmationRequired (root.GetProperty("serviceHost").GetString(), root.GetProperty("model").GetString())
        | code -> failureOfCode code
    with _ -> Summary.RequestFailed

let private generated (element: JsonElement) : Summary.Generated =
    { DocumentId = element.GetProperty("documentId").GetGuid()
      Points =
          element.GetProperty("points").EnumerateArray()
          |> Seq.map (fun point -> { Summary.Text = point.GetProperty("text").GetString(); Summary.Pages = intList (point.GetProperty "pages") })
          |> Seq.toList
      PagesCovered = intList (element.GetProperty "pagesCovered")
      UnreadablePages = intList (element.GetProperty "unreadablePages")
      OmittedPoints = element.GetProperty("omittedPoints").GetInt32()
      GeneratedAt = element.GetProperty("generatedAt").GetDateTimeOffset()
      ServiceHost = element.GetProperty("serviceHost").GetString()
      Model = element.GetProperty("model").GetString() }

/// The outcome an event ends the stream with: its one `summary` or `failed`; None for any other event.
let outcome (element: JsonElement) : Result<Summary.Generated, Summary.Failure> option =
    match element.GetProperty("type").GetString() with
    | "summary" -> Some (Ok (generated element))
    | "failed" ->
        match failureOfCode (element.GetProperty("code").GetString()) with
        | Summary.ServiceFailed _ ->
            let detail =
                match element.TryGetProperty "detail" with
                | true, value when value.ValueKind = JsonValueKind.String -> Some (value.GetString())
                | _ -> None
            Some (Error (Summary.ServiceFailed detail))
        | failure -> Some (Error failure)
    | _ -> None

/// The progress an event reports, if it is one.
let progress (element: JsonElement) : Summary.Progress option =
    match element.GetProperty("type").GetString() with
    | "progress" -> Some (Summary.PagesProcessed (element.GetProperty("pagesProcessed").GetInt32(), element.GetProperty("totalPages").GetInt32()))
    | "combining" -> Some Summary.Combining
    | _ -> None

/// `confirmSend` names the service the researcher confirmed, so the server can check it is still the configured one.
let requestBody (confirmed: Summary.Confirmed option) : string =
    match confirmed with
    | Some service -> JsonSerializer.Serialize({| confirmSend = true; confirmedHost = service.Host; confirmedModel = service.Model |})
    | None -> JsonSerializer.Serialize({| confirmSend = false |})
