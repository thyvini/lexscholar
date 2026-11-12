namespace LexScholar.Server

open System
open System.Globalization
open System.IO
open Microsoft.Extensions.Configuration
open LexScholar.Application

/// Optional connection to the researcher's chosen chat-completions service.
type SummaryConfiguration =
    { Endpoint: Uri
      Model: string
      ApiKey: string option
      MaxChunkCharacters: int
      Temperature: float option
      MaxOutputTokens: int option }

type WorkspaceConfiguration =
    { DataDirectory: string
      ImportLimits: ImportLimits
      Summary: SummaryConfiguration option }

type ConfigurationError =
    | InvalidLimit of name: string * value: string
    | NonPositiveLimit of name: string
    | InvalidDataDirectory of reason: string
    /// `reason` is fixed text; the offending value is never echoed (it may hold a secret).
    | InvalidSummarySetting of key: string * reason: string

module Configuration =
    [<Literal>]
    let DataDirectoryKey = "LexScholar:DataDirectory"
    [<Literal>]
    let MaxBytesKey = "LexScholar:ImportLimits:MaxBytes"
    [<Literal>]
    let MaxPagesKey = "LexScholar:ImportLimits:MaxPages"

    let private validatePath path =
        if String.IsNullOrWhiteSpace(path) then
            Error (InvalidDataDirectory "The path must not be empty.")
        elif not (Path.IsPathFullyQualified(path)) || path.Contains('\000') then
            Error (InvalidDataDirectory "Use an absolute local filesystem path without NUL characters.")
        else
            try
                let fullPath = Path.GetFullPath(path)
                if fullPath = Path.GetPathRoot(fullPath) then
                    Error (InvalidDataDirectory "The filesystem root cannot be used as the data directory.")
                elif File.Exists(fullPath) then
                    Error (InvalidDataDirectory "The configured path is an existing file, not a directory.")
                else Ok fullPath
            with
            | :? ArgumentException
            | :? NotSupportedException
            | :? PathTooLongException -> Error (InvalidDataDirectory "The path is not valid on this system.")

    let private limit name defaultValue zero parse (value: string option) =
        match value with
        | None -> Ok defaultValue
        | Some text ->
            match parse text with
            | true, number when number > zero -> Ok number
            | true, _ -> Error (NonPositiveLimit name)
            | false, _ -> Error (InvalidLimit (name, text))

    let private parseInt64 (text: string) = Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)
    let private parseInt (text: string) = Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)

    [<Literal>]
    let SummaryPrefix = "LexScholar:Summary:"

    let private invalid name reason = Error (InvalidSummarySetting (SummaryPrefix + name, reason))

    let private blankAsNone (value: string option) =
        value |> Option.filter (fun text -> not (String.IsNullOrWhiteSpace text))

    /// Optional section (research §1). Error reasons are fixed text: a value, and
    /// above all the API key, never reaches an error message.
    let private loadSummary (get: string -> string option) : Result<SummaryConfiguration option, ConfigurationError> =
        let key name = SummaryPrefix + name
        let optional name parse reason : Result<'a option, ConfigurationError> =
            match get (key name) with
            | None -> Ok None
            | Some text ->
                match parse text with
                | Some value -> Ok (Some value)
                | None -> invalid name reason
        let invariant = CultureInfo.InvariantCulture
        let endpoint (text: string) =
            match Uri.TryCreate(text.Trim(), UriKind.Absolute) with
            | true, uri
                when (uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps)
                     && String.IsNullOrEmpty uri.UserInfo
                     && String.IsNullOrEmpty uri.Query
                     && String.IsNullOrEmpty uri.Fragment
                     && not (text.Contains '?' || text.Contains '#') -> Some uri
            | _ -> None
        match get (key "Endpoint") |> blankAsNone with
        | None -> Ok None
        | Some text ->
            match endpoint text with
            | None -> invalid "Endpoint" "use an absolute http or https URL without user info, query or fragment."
            | Some uri ->
                match get (key "Model") |> blankAsNone with
                | None -> invalid "Model" "a model name is required when an endpoint is set."
                | Some model ->
                    optional "MaxChunkCharacters"
                        (fun t -> match Int32.TryParse(t, NumberStyles.Integer, invariant) with
                                  | true, n when n >= 4_000 -> Some n
                                  | _ -> None)
                        "use a whole number of at least 4000."
                    |> Result.bind (fun chunk ->
                        optional "Temperature"
                            (fun t -> match Double.TryParse(t, NumberStyles.Float, invariant) with
                                      | true, n when n >= 0.0 && n <= 2.0 -> Some n
                                      | _ -> None)
                            "use a number from 0 to 2."
                        |> Result.bind (fun temperature ->
                            optional "MaxOutputTokens"
                                (fun t -> match Int32.TryParse(t, NumberStyles.Integer, invariant) with
                                          | true, n when n > 0 -> Some n
                                          | _ -> None)
                                "use a positive whole number."
                            |> Result.map (fun tokens ->
                                Some { Endpoint = uri
                                       Model = model.Trim()
                                       ApiKey = get (key "ApiKey") |> blankAsNone
                                       MaxChunkCharacters = defaultArg chunk 12_000
                                       Temperature = temperature
                                       MaxOutputTokens = tokens })))

    /// Environment lookup and IConfiguration access remain at the composition boundary.
    let load (configuration: IConfiguration) : Result<WorkspaceConfiguration, ConfigurationError> =
        let get key = configuration.[key] |> Option.ofObj
        let directory =
            match get DataDirectoryKey with
            | Some path -> path
            | None ->
                let xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                let baseDirectory =
                    if not (String.IsNullOrWhiteSpace(xdg)) && Path.IsPathFullyQualified(xdg) then xdg
                    else Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                Path.Combine(baseDirectory, "lexscholar")
        validatePath directory
        |> Result.bind (fun path ->
            limit MaxBytesKey 100_000_000L 0L parseInt64 (get MaxBytesKey)
            |> Result.bind (fun bytes ->
                limit MaxPagesKey 2_000 0 parseInt (get MaxPagesKey)
                |> Result.bind (fun pages ->
                    loadSummary get
                    |> Result.map (fun summary ->
                        { DataDirectory = path
                          ImportLimits = { MaxBytes = bytes; MaxPages = pages }
                          Summary = summary }))))
