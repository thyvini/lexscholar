# Manual summary quality check ($167)

**Status: procedure only; not run.** No real-model result or owner acceptance is recorded here. This is a small, owner-reviewed observation, not a benchmark, legal advice, or proof that summaries or citations are generally reliable. It does not close $167's evidence-or-risk-acceptance criterion by itself.

## Public sample

Hafiz Gaffar and Saleh Albarashdi, “Copyright Protection for AI-Generated Works: Exploring Originality and Ownership in a Digital Landscape,” *Asian Journal of International Law* 15(1) (2025), 23–46, [doi:10.1017/S2044251323000735](https://doi.org/10.1017/S2044251323000735). This legal scholarship discusses authorship and originality across jurisdictions, giving the reviewer substantive, qualified claims to check.

- [Publisher article page and license statement](https://www.cambridge.org/core/journals/asian-journal-of-international-law/article/copyright-protection-for-aigenerated-works-exploring-originality-and-ownership-in-a-digital-landscape/12B8B8D836AC9DDFFF4082F7859603E3): Cambridge states CC BY 4.0 and requires proper citation. [License terms](https://creativecommons.org/licenses/by/4.0/).
- [Publisher PDF](https://www.cambridge.org/core/services/aop-cambridge-core/content/view/12B8B8D836AC9DDFFF4082F7859603E3/S2044251323000735a.pdf/copyright-protection-for-ai-generated-works-exploring-originality-and-ownership-in-a-digital-landscape.pdf). Download from the publisher; do not commit the PDF. Record the downloaded file's SHA-256 and retrieval date because the hosted file could change. If unavailable or not readable, stop and record that limitation rather than silently substituting another version.
- The article contains legal interpretations and references to third-party material. Support by this paper means the paper says it, not that the law or cited external source is correct. Attribute the article and check rights for any third-party material before reproducing it.

## Owner procedure

1. Download the PDF, calculate `sha256sum <downloaded-pdf>` and record the hash, date and page count. Visually inspect the file and confirm text is selectable on several pages, including the beginning, middle and end. Text selection is only a rough proxy for the application's extraction; note illegibility or missing text.
2. Use a separate disposable LexScholar workspace/project containing only this public PDF, not private research. Record the app version/commit, operating system and browser. Import and associate the PDF with the project; open it. Sign in to the intended ChatGPT plan, review the app's send confirmation and consent only if comfortable sending the public text to that service. Do not record credentials, tokens or account identifiers.
3. Request a first summary with the optional instruction **blank**, and wait for completion. Record the displayed service, exact model, generation time and any other visible context. If unavailable, write “not exposed”; do not guess. Preserve the complete generated points and citations in a separate evidence record, clearly labelled model output. If generation fails or extraction is rejected, record the observable failure and stop: there is insufficient evidence to judge semantic quality.
4. Review **every generated point**, splitting compound points into individually assessable claims. For each claim open each cited page in the actual PDF, read the relevant passage and surrounding context, and record both the app's PDF page number and the printed journal page number where visible. Check that the link opens the intended PDF/page, and record any navigation mismatch separately. Search other pages if needed, but do not count support found only elsewhere as support on the cited page.
5. For each claim, record a short passage or precise location and a rationale. Mark **supported** only when the cited page(s), read in context, support the whole claim and its qualifications; **unsupported** when they do not support it or contradict it; **uncertain** when interpretation, missing text or other limitations prevent a confident judgment. A cited passage supporting only part of a compound claim is not full support. Note where the article attributes a position to someone else instead of endorsing it. Assess uncited claims as unsupported by a citation; document any supporting location separately.
6. Compare the PDF's visible text with what can be observed of extraction if available. A valid page link/page number only establishes navigation, not correct extraction or semantic support; a generated claim can be wrong even when extraction and citation navigation are correct. If the app does not expose extracted text, mark extraction fidelity **not observable**, not passed or failed. Note likely extraction problems separately from questionable generated claims without asserting a cause that was not observed.
7. Record the review scope, counts by verdict, any material omissions noticed by reading the paper, and limitations. If useful, repeat as a distinct run only after preserving the first output; record changed conditions and compare observations, not identical wording. Regeneration may replace the saved summary. The owner decides whether the observation suffices or the remaining evidence risk is accepted; record that decision separately and explicitly.

The built-in prompt and any map/combine steps are implementation-dependent; blank optional instruction does not mean there is no prompt. Record the commit so the implementation can be inspected, but do not claim the exact provider-side prompt, parameters or hidden model version were observed if they were not. Even the same displayed model and input may produce different outputs or change over time; one run cannot establish repeatability or broad quality.

## Evidence template — copy for each run

Leave unknown fields as “not exposed,” “not checked,” or “uncertain”; never fill them by inference.

```text
Reviewer / date / decision maker:
App version and commit; OS; browser:
Source citation and publisher URL:
PDF retrieved on; SHA-256; total PDF pages; visual/selectable-text observations:
Service/destination; displayed model; generated time; other displayed context:
Optional instruction: blank (or exact public instruction if changed)
Prompt/parameters actually observable; unobservable details:
Run outcome (completed / failed / rejected); visible failure if any:
Complete generated output and citations, labelled as generated:

Claim ID and exact generated claim:
App-cited PDF page(s); printed page(s):
Link navigation observation:
Source passage/location and context on cited page(s):
Verdict (supported / unsupported / uncertain); rationale:
Support found elsewhere, if any (PDF and printed pages; not citation support):
Extraction fidelity observation or not observable:
Notes (qualification, attribution, contradiction, missing text):
[Repeat for every atomic claim]

Claims reviewed / supported / unsupported / uncertain:
Scope and any observed omissions:
Extraction, navigation, prompt, model variability and other limitations:
Repeat run reference and changed conditions, if any:
Owner conclusion / explicit remaining-risk decision / date:
```

If there is no completed, reviewable output, report **insufficient evidence**, not a zero-failure result. Avoid posting private material, credentials, or provider response bodies; only share public-source excerpts and generated output the owner is comfortable publishing. The evidence template is not itself a result.
