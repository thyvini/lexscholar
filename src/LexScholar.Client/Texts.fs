module LexScholar.Client.Texts

open System
open System.Globalization

let projects = "Projetos"
let library = "Biblioteca"
let focusedSource = "Fonte principal"
let focusedSources = "Fontes principais"
let referenceSource = "Fonte de referência"
let referenceSources = "Fontes de referência"
let addDocuments = "Adicionar documentos"
let summary = "Resumo"
let requestSummary = "Resumir este documento"
let summarize = "Resumir"
let summaryReady = "Resumo pronto"
let stop = "Parar"
let delete = "Excluir"
let confirmDelete = "Excluir este resumo?"
let close = "Fechar"
let openPdf = "Abrir PDF"
let cancel = "Cancelar"
let generated = "Gerado para sua revisão. Não verificado."
let signedIn = "Conectado ao ChatGPT"
let usingPlan = "Usando seu plano ChatGPT"
let manageUsage = "Gerenciar uso"
let confirmationTitle = "Enviar documentos ao ChatGPT?"
let sendAndSummarize = "Enviar e resumir"
let signIn = "Continuar com o ChatGPT"
let makeFocused = "Tornar fonte principal"
let makeReference = "Tornar fonte de referência"
let genericFailure = "Algo deu errado. Tente novamente."
let account = "Conta"
let signOut = "Sair"
let createProject = "Criar projeto"
let projectName = "Nome do projeto"
let active = "Ativo"
let select = "Selecionar"
let refresh = "Atualizar"
let noProjects = "Nenhum projeto ainda. Crie um para começar."
let chooseDocument = "Selecione um documento para começar."
let noProject = "Selecione um projeto para ver seus documentos."
let noDocuments = "Nenhum documento neste projeto."
let summaryRunning = "Gerando resumo"
let summaryFailed = "Falha no resumo"
let inProject = "em"

let pages values =
    let format group =
        match group with
        | [single] -> $"p. {single}"
        | first :: _ -> $"pp. {first}–{List.last group}"
        | [] -> ""
    let rec groups remaining current result =
        match remaining, current with
        | [], [] -> List.rev result
        | [], _ -> List.rev (List.rev current :: result)
        | next :: rest, last :: _ when next = last + 1 -> groups rest (next :: current) result
        | next :: rest, [] -> groups rest [next] result
        | next :: rest, _ -> groups rest [next] (List.rev current :: result)
    groups values [] [] |> List.map format |> String.concat ", "

let pageCount n = if n = 1 then "1 página" else $"{n} páginas"
let documentCount n = if n = 1 then "1 documento" else $"{n} documentos"
let dateTime (value: DateTimeOffset) = value.LocalDateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
