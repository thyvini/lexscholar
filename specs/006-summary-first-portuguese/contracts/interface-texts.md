# Contract: interface texts (F006)

Every visible text comes from `src/LexScholar.Client/Texts.fs`, in Brazilian Portuguese. No language choice. `index.html` is `lang="pt-BR"`. Tests select by role and accessible name taken from `Texts`, and check the exact literals below (R5).

## Glossary (FR-012, exact)

| Concept | Text |
|---|---|
| Projects | Projetos |
| Library | Biblioteca |
| Focused source | Fonte principal / Fontes principais |
| Reference source | Fonte de referência / Fontes de referência |
| Add documents | Adicionar documentos |
| Summary | Resumo |
| Request | Resumir este documento; button Resumir |
| Ready marker | Resumo pronto |
| Stop | Parar |
| Delete | Excluir, on a finished summary (confirmation: "Excluir este resumo?", buttons Excluir / Cancelar) |
| Close a failed summary | Fechar (immediate, no confirmation) |
| Open PDF | Abrir PDF |
| Cancel | Cancelar |
| Generated label | Gerado para sua revisão. Não verificado. |
| Signed in | Conectado ao ChatGPT |
| Using the plan | Usando seu plano ChatGPT |
| Manage usage | Gerenciar uso |
| Confirmation title / action | Enviar documentos ao ChatGPT? / Enviar e resumir |
| Sign-in button | Continuar com o ChatGPT |
| Role change actions | Tornar fonte principal / Tornar fonte de referência |
| Generic failure | Algo deu errado. Tente novamente. |
| Service of an older summary made through the settings file (in its context) | Recorded `generated.ServiceHost` unchanged, alongside model, date and existing context; never ChatGPT-plan attribution |

The other texts follow the mockups (screens 04, 05, 06, 08) where they show them, for example the confirmation body and its info note, the first-run steps, "8 páginas · Fonte principal em Dissertação", and "2 sem texto".

## Formats

| What | Format | Example |
|---|---|---|
| One cited page | `p. N` | p. 3 |
| Consecutive pages | `pp. N–M` (en dash U+2013) | pp. 4–6 |
| Date and time | `dd/MM/yyyy HH:mm`, local time, 24 h | 14/11/2026 18:54 |
| Page count | `1 página`, `N páginas` | 8 páginas |
| Document count | `1 documento`, `N documentos` | 4 documentos |

## Links

- Confirmation help link: "Como usar seu plano do ChatGPT em outros apps e sites" → https://help.openai.com/pt-br/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites
- "Gerenciar uso" → her ChatGPT usage settings (unchanged target, F005).

## Server failures

Each problem `code` the client receives maps to a Portuguese message via the existing failure unions. An unknown code, or a body without a code, shows the generic failure. The server's `title` and `detail` are never shown.

## Not built (F007)

"Gerar novamente", "Sua instrução", the field "O que você quer desta fonte?" and its "Recentes" suggestions, flags on points and "1 ponto marcado", "Este resumo foi útil?", and screen 07.
