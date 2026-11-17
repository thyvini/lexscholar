# F006 mockups

Reference screens for the redesigned workspace, made with the owner on 2026-11-16. They show the layout, look and Portuguese texts. Where a screen and `spec.md` disagree, the spec wins.

| Screen | What it shows | In F006 |
|--------|---------------|---------|
| `04-confirm-before-sending.png` | Confirmation before her text goes to her ChatGPT plan (F005 FR-010, F006 FR-013) | Yes |
| `05-first-run.png` | First run: create a project, import PDFs, sign in with ChatGPT | Yes. Only the layout and texts; the steps themselves are unchanged |
| `06-summary-first.png` | A document opened: summary in the main area, "Abrir PDF", citations as links, "Excluir" | Yes, without the F007 parts below |
| `07-regenerate-with-feedback.png` | Regenerating with feedback and an instruction | No, F007 |
| `08-ask-with-instruction.png` | A document with no summary: the request to summarize it | Yes, without the instruction field and its shortcuts (F007) |

These parts of the screens belong to F007, not F006: "Gerar novamente", the instruction ("Sua instrução" and the "O que você quer desta fonte?" field), flags on points, "Este resumo foi útil?", and the whole of screen 7.

Not shown, but in the spec:
- the Delete confirmation (FR-006);
- the error page for a missing file (FR-007);
- the role change in the document list (FR-008);
- the progress of a summary being generated (F004, unchanged).

## Look

| Token | Value | Use |
|-------|-------|-----|
| Accent | `#0E6E80` (petrol) | Main buttons, links, citations, selection outline, progress bar, logo |
| Accent hover | `#0A5562` | Link and button hover |
| Accent tint | `#E5F1F2` | Selected project or document |
| Info tint | `#EEF6F6` | Information notes |
| Page | `#F6F6F3` | App background |
| Surface | `#FFFFFF` | Panels, cards, dialogs |
| Sidebar | `#FBFBF9` | Project list |
| Border | `#E3E3DE`; controls `#D5D5CF` | Hairlines; button and field borders |
| Text | `#1F2328`; secondary `#3D4550`; muted `#5B6470` | Body; supporting text; metadata |
| Success | `#2E7D5B` | "Resumo pronto", "Conectado ao ChatGPT" |
| Warning and failure | `#A3540C` | A failed summary, flagged points |
| Danger | `#A12B2B` | "Excluir" |
| Generated label | background `#FFF6E5`, text `#5C3B00` | "Gerado para sua revisão. Não verificado." |

On the accent tint, accent text has a contrast ratio of 5.1:1. White text on the accent is 5.9:1.

Type:
- Inter for the interface.
- Source Serif 4 for the summary's points and her instruction.
- Citations are italic, underlined links in the accent color.
