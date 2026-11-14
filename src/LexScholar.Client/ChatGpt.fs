/// The researcher's ChatGPT sign-in: pure state, no HTTP and no rendering (see App.fs).
module LexScholar.Client.ChatGpt

type Account = { Name: string; Confirmed: bool }

type Status =
    | Unknown
    | SignedOut
    /// The authorization address was requested; the tab navigates to it next.
    | Starting
    | SignedIn of Account

/// What the destinations answer says about the sign-in.
type Destinations = { Account: Account option; WelcomePending: bool }

/// A message shown once above the account line, then dismissed.
type Notice =
    | Cancelled
    | NoPlanAccess
    | Failed
    | StartFailed
    | NotRevoked
    | SignOutFailed

type StartProblem =
    | AlreadySignedIn
    | RequestFailed

type Model =
    { Status: Status
      WelcomePending: bool
      Notice: Notice option
      /// A sign-out is in flight; sign-in and sign-out are ignored meanwhile.
      SigningOut: bool
      /// Only the response to the latest destinations request is used.
      Request: int }

type Message =
    | Load
    | Loaded of request: int * Result<Destinations, unit>
    /// The `chatgpt` query value of the page address after returning from ChatGPT.
    | Returned of string option
    | SignIn
    | AuthorizationReady of Result<string, StartProblem>
    | SignOut
    | SignedOutResult of Result<bool, unit>
    | DismissWelcome
    | DismissNotice

type Effect =
    | FetchDestinations of request: int
    | StartSignIn
    /// Navigate the tab to the authorization address.
    | Navigate of url: string
    | SendSignOut
    | SendDismissWelcome

let initial = { Status = Unknown; WelcomePending = false; Notice = None; SigningOut = false; Request = 0 }

let update (message: Message) (model: Model) : Model * Effect option =
    match message with
    | Load ->
        let request = model.Request + 1
        { model with Request = request }, Some (FetchDestinations request)
    // Starting owns the state until the tab navigates away or the start fails.
    | Loaded (request, result) when request = model.Request && model.Status <> Starting ->
        match result with
        | Ok destinations ->
            match destinations.Account with
            | Some account -> { model with Status = SignedIn account; WelcomePending = destinations.WelcomePending }, None
            | None -> { model with Status = SignedOut; WelcomePending = false }, None
        | Error () -> model, None
    | Loaded _ -> model, None
    | Returned value ->
        let notice =
            match value with
            | Some "cancelled" -> Some Cancelled
            | Some "no_plan_access" -> Some NoPlanAccess
            | Some "failed" -> Some Failed
            | _ -> None
        { model with Notice = notice }, None
    | SignIn when model.Status = SignedOut && not model.SigningOut ->
        { model with Status = Starting; Notice = None }, Some StartSignIn
    | SignIn -> model, None
    | AuthorizationReady result when model.Status = Starting ->
        match result with
        | Ok url -> model, Some (Navigate url)
        | Error AlreadySignedIn ->
            let request = model.Request + 1
            { model with Status = SignedOut; Request = request }, Some (FetchDestinations request)
        | Error RequestFailed -> { model with Status = SignedOut; Notice = Some StartFailed }, None
    | AuthorizationReady _ -> model, None
    | SignOut when not model.SigningOut ->
        match model.Status with
        | SignedIn _ -> { model with SigningOut = true; Notice = None }, Some SendSignOut
        | _ -> model, None
    | SignOut -> model, None
    | SignedOutResult result when model.SigningOut ->
        match result with
        | Ok revoked ->
            { model with
                Status = SignedOut
                WelcomePending = false
                SigningOut = false
                Notice = if revoked then None else Some NotRevoked },
            None
        | Error () -> { model with SigningOut = false; Notice = Some SignOutFailed }, None
    | SignedOutResult _ -> model, None
    | DismissWelcome when model.WelcomePending -> { model with WelcomePending = false }, Some SendDismissWelcome
    | DismissWelcome -> model, None
    | DismissNotice -> { model with Notice = None }, None
