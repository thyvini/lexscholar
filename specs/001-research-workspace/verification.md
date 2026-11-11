# F001 verification

This page records verification of F001's success criteria that needs more than the default automated suite. Automated results and usability evaluations are recorded separately: automated checks are not participant evidence.

## SC-002 import corpus

**Result: 100 of 100 imported** (SC-002 requires at least 99). Run on 2026-11-11 (T044; see [evidence](evidence/t044-sc002-corpus.md)).

### Environment

- CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, PdfPig 0.1.16, Microsoft.EntityFrameworkCore.Sqlite 10.0.14, default import limits (100,000,000 bytes, 2,000 pages).
- Selection tools: qpdf 12.4.2, poppler `pdfinfo` 26.10.0, curl 8.23.0.

### How to reproduce

The PDFs are not committed. Download them with the fetch script, which verifies every SHA-256 against the manifest, then run the corpus test project. The project is not part of `LexScholar.sln`, so the default suite does not include it.

```sh
scripts/fetch-sc002-corpus.sh specs/001-research-workspace/sc002-corpus.csv corpus/sc002
dotnet restore tests/LexScholar.Corpus.Tests/LexScholar.Corpus.Tests.fsproj -warnaserror
dotnet build tests/LexScholar.Corpus.Tests/LexScholar.Corpus.Tests.fsproj -c Release --no-restore -warnaserror
LEXSCHOLAR_SC002_CORPUS=$PWD/corpus/sc002 dotnet test --project tests/LexScholar.Corpus.Tests/LexScholar.Corpus.Tests.fsproj -c Release --no-build
```

`corpus/` is git-ignored.

The test does three things:
1. It checks that every manifest file is present, with the manifest's size and SHA-256.
2. It imports each file through the real `ImportDocument` workflow (PdfPig validator, document store, SQLite repository) into a temporary workspace.
3. It writes `corpus/sc002/results.csv` with identifiers, sizes, hashes, page counts and outcomes only, never file contents. It fails unless at least 99 files were imported and every imported document's page count equals the manifest's `pdfinfo` count.

If `LEXSCHOLAR_SC002_CORPUS` is unset, the test fails; it never skips.

### Selection

1. **Source:** arXiv OAI-PMH `arXivRaw` metadata (`https://oaipmh.arxiv.org/oai?verb=ListRecords&metadataPrefix=arXivRaw&set=<set>&from=2024-03-04&until=2024-03-04`) for the sets `cs`, `math`, `physics`, `q-bio` and `stat`. The date is the OAI record datestamp, so papers from several years appear.
2. **License:** only records whose `<license>` is exactly `http://creativecommons.org/licenses/by/4.0/` (CC BY 4.0) or `http://creativecommons.org/publicdomain/zero/1.0/` (CC0 1.0). Each paper is pinned to its latest version. Withdrawn versions (size 0) and cross-listed duplicates were skipped. Per-set quotas gave 118 candidates.
3. **Download:** `scripts/fetch-sc002-corpus.sh`, one request every 3 seconds, at most 100,000,000 bytes, with a generic user agent.
4. **Membership,** checked with tools independent of LexScholar's validator. A candidate is eligible only if all of these hold:
   - at most 100,000,000 bytes;
   - `qpdf --check` exits 0;
   - `pdfinfo` reports `Encrypted: no` and at most 2,000 pages;
   - its SHA-256 is distinct.
5. **Selection:** 100 eligible candidates taken round-robin across the sets, in metadata order within each set. All of this happened before LexScholar processed any file.

Corpus: cs 29, math 28, physics 28, stat 13, q-bio 2; CC-BY-4.0 96, CC0-1.0 4. Sizes 78,784 to 19,275,774 bytes (242,452,586 in total); 4 to 173 pages.

Candidates not in the corpus:

| Candidate | Set | Reason |
|---|---|---|
| [2403.00005v1](https://arxiv.org/abs/2403.00005v1) | q-bio | excluded: qpdf --check reported warnings (exit 3) |
| [2306.04060v2](https://arxiv.org/abs/2306.04060v2) | physics | excluded: qpdf --check reported warnings (exit 3) |
| [2304.02780v2](https://arxiv.org/abs/2304.02780v2) | cs | excluded: qpdf --check reported warnings (exit 3) |
| [2403.00026v1](https://arxiv.org/abs/2403.00026v1) | math | excluded: qpdf --check reported warnings (exit 3) |
| [2403.00279v1](https://arxiv.org/abs/2403.00279v1) | math | not selected: spare after 100 eligible |
| [2309.15002v2](https://arxiv.org/abs/2309.15002v2) | physics | not selected: spare after 100 eligible |
| [2308.11971v2](https://arxiv.org/abs/2308.11971v2) | cs | not selected: spare after 100 eligible |
| [2310.10392v2](https://arxiv.org/abs/2310.10392v2) | cs | not selected: spare after 100 eligible |
| [2310.11046v2](https://arxiv.org/abs/2310.11046v2) | cs | not selected: spare after 100 eligible |
| [2310.16298v2](https://arxiv.org/abs/2310.16298v2) | cs | not selected: spare after 100 eligible |
| [2310.19103v2](https://arxiv.org/abs/2310.19103v2) | cs | not selected: spare after 100 eligible |
| [2312.00398v2](https://arxiv.org/abs/2312.00398v2) | cs | not selected: spare after 100 eligible |
| [2312.14388v3](https://arxiv.org/abs/2312.14388v3) | cs | not selected: spare after 100 eligible |
| [2401.00037v2](https://arxiv.org/abs/2401.00037v2) | cs | not selected: spare after 100 eligible |
| [2401.10910v2](https://arxiv.org/abs/2401.10910v2) | cs | not selected: spare after 100 eligible |
| [2401.13662v2](https://arxiv.org/abs/2401.13662v2) | cs | not selected: spare after 100 eligible |
| [2402.01719v3](https://arxiv.org/abs/2402.01719v3) | cs | not selected: spare after 100 eligible |
| [2402.03776v4](https://arxiv.org/abs/2402.03776v4) | cs | not selected: spare after 100 eligible |

The four exclusions were qpdf warnings about structure, not encryption or limits: misplaced object offsets, a bad indirect reference treated as null, damaged linearization data, and an empty name-tree node. They were excluded under the selection rule, before any LexScholar processing.

### Corpus and results

`Pages` is the page count from `pdfinfo`. For every file, the page count LexScholar recorded on import was equal to it; the corpus test asserts this.

| # | arXiv | Set | License | Bytes | Pages | SHA-256 | Result |
|---|---|---|---|---|---|---|---|
| 1 | [2107.01815v1](https://arxiv.org/abs/2107.01815v1) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 567,016 | 16 | `6c4d6658d1257adfe353c9d8eed76011ad86d0aa78a0605de21bc5cf8d79bf8f` | Imported |
| 2 | [2110.11655v2](https://arxiv.org/abs/2110.11655v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 283,952 | 20 | `e662edb15ac319019fc813782b79012a671f8e885ed2d25afecb9afce784c3ae` | Imported |
| 3 | [0711.3992v2](https://arxiv.org/abs/0711.3992v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 120,483 | 6 | `00c8bb25a0f3cbc5a858fa939ab927fd9e84dfca6aff3a65d2e8e28353a324e9` | Imported |
| 4 | [2212.13294v3](https://arxiv.org/abs/2212.13294v3) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 787,204 | 46 | `35a64013407a1694761cb45e9031fa20f76ac32e87712d275ba9302fb53ebf8b` | Imported |
| 5 | [2112.04539v3](https://arxiv.org/abs/2112.04539v3) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 776,796 | 14 | `9daa54bc8a79a95784e9000576658ae50505bbaf636025c2b47235c9d0c7807c` | Imported |
| 6 | [2202.11758v6](https://arxiv.org/abs/2202.11758v6) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,209,291 | 53 | `9bce57315371bb98f26b0a8500c3899a493f3f79ea9d3fa5cbc058aad3a00120` | Imported |
| 7 | [2208.01659v2](https://arxiv.org/abs/2208.01659v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,957,361 | 60 | `27eb57d306903dca3273a7eb141a63f57de7120c07cc2398217477e81ed22120` | Imported |
| 8 | [2403.00022v1](https://arxiv.org/abs/2403.00022v1) | q-bio | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 2,158,694 | 15 | `a1263380dd338482088442754c9125ffff8b87872be5b345eff1408fbc403a42` | Imported |
| 9 | [2308.15773v3](https://arxiv.org/abs/2308.15773v3) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 13,006,453 | 85 | `3f82f4f43390503d92e1672f534675ba41227135fbf9fa9d42549eef61dd18be` | Imported |
| 10 | [2205.11029v2](https://arxiv.org/abs/2205.11029v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 8,402,950 | 15 | `257a5311f740e9cf519ebaff22d85bb462b687785b7e67eb686503bd8aa926a8` | Imported |
| 11 | [2208.00762v3](https://arxiv.org/abs/2208.00762v3) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 114,035 | 9 | `7ba85f47810a5e6ab106ec6b6ff81dd1458aaa2a3ebd5931d7305dd079a1d217` | Imported |
| 12 | [2210.00023v3](https://arxiv.org/abs/2210.00023v3) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 883,244 | 7 | `be00da0b84ed803beaa84abe0ed4dd2c4815f05d34b4dc60a2268b7f768572c6` | Imported |
| 13 | [2403.00716v1](https://arxiv.org/abs/2403.00716v1) | q-bio | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 2,658,148 | 22 | `d3a1ad0004afaa72022b99ed42d6610a478f6be92e51c543913a700650f0278f` | Imported |
| 14 | [2402.17915v2](https://arxiv.org/abs/2402.17915v2) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 938,740 | 38 | `1928628e2d72e93c3a1021e67f577e56f9bde66e90afd8ccdeef41338343fb67` | Imported |
| 15 | [2207.00288v2](https://arxiv.org/abs/2207.00288v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,332,226 | 26 | `d50272dadffc3ea807840cd1d15dfc975a7a3214a94bf47e161453e6080c6f0e` | Imported |
| 16 | [2208.03253v3](https://arxiv.org/abs/2208.03253v3) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 622,653 | 45 | `b1666932cd1f0d817b9fb4b383ad9e81bfdec38a6bcd12713f561feb8b9293c1` | Imported |
| 17 | [2211.02644v3](https://arxiv.org/abs/2211.02644v3) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,861,761 | 11 | `a28809c362e6cf511d570b1ad9a29c4c2b02c11a045c2ac73002be928b140a04` | Imported |
| 18 | [2403.00019v1](https://arxiv.org/abs/2403.00019v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 484,955 | 12 | `ae6752814d6962894532eb787a0da6945fbf9ecf07c6c4332a2ef303e688864a` | Imported |
| 19 | [2208.05424v9](https://arxiv.org/abs/2208.05424v9) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 7,540,503 | 40 | `88103de5c212f333d9aa9c80f7ce4743eff90e582471ee63e3e60442eb0a85e0` | Imported |
| 20 | [2208.10666v3](https://arxiv.org/abs/2208.10666v3) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 265,417 | 17 | `2024eae3a9603dc73f4a5b4da8bf3740a7f533496fcfa1620f470c58f6a592b7` | Imported |
| 21 | [2211.15190v4](https://arxiv.org/abs/2211.15190v4) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 2,285,420 | 17 | `f464bdafe0516bda810b1e99867fa5747453461308f8cb9de3ab18d6166725a9` | Imported |
| 22 | [2403.00161v1](https://arxiv.org/abs/2403.00161v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,064,434 | 5 | `bff6698e8f4d362b9ce4c28a72741bcc647f27b0eb020df5686ad39b82e06abf` | Imported |
| 23 | [2209.03602v1](https://arxiv.org/abs/2209.03602v1) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,160,293 | 40 | `700b905336515a17c22282a6438b68e58b8e410772fa323f383add700d2d8003` | Imported |
| 24 | [2210.00225v2](https://arxiv.org/abs/2210.00225v2) | math | [CC0-1.0](http://creativecommons.org/publicdomain/zero/1.0/) | 342,571 | 20 | `1b7993deeb9367b3c9417074f53bd84defe530dfcdc7bd4ffe3a9cdf3aa890df` | Imported |
| 25 | [2212.07033v2](https://arxiv.org/abs/2212.07033v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,341,742 | 11 | `a11ff2b78ca6ba9590975408e806b15aad34dffaa1382018a4ed831d78044fa9` | Imported |
| 26 | [2403.00184v1](https://arxiv.org/abs/2403.00184v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,074,534 | 10 | `06deaace5845b195ded5a4facd892a0f0500bd0b0cb31ad6b2ecc518d9e45995` | Imported |
| 27 | [2212.09171v2](https://arxiv.org/abs/2212.09171v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,340,190 | 27 | `943e568bda8b4c346ce4c1c3dd37ecda8da22b60507eac4686a1f1602f24f297` | Imported |
| 28 | [2210.08534v2](https://arxiv.org/abs/2210.08534v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 458,699 | 9 | `20e872f860825694f3ba2b155ebb618f3487643ffc2e4d611a7ab9633776e551` | Imported |
| 29 | [2301.02675v2](https://arxiv.org/abs/2301.02675v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 4,519,702 | 21 | `b79c1d3827dcffbf10fe028a1f1742aa19e5c693bb52287ad79b909139016ccd` | Imported |
| 30 | [2403.00224v1](https://arxiv.org/abs/2403.00224v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 754,023 | 35 | `b5d1f3071a8a07f89e92a2f2030eac08a13754208ab1394dfd6874fd2f610128` | Imported |
| 31 | [2301.11009v1](https://arxiv.org/abs/2301.11009v1) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 898,207 | 18 | `796d3ec302cbd0fabe0eabfc737c582124e1b1d5fb85baa40721e1116c5c2af4` | Imported |
| 32 | [2212.12225v3](https://arxiv.org/abs/2212.12225v3) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,287,784 | 99 | `e7a676769f4f710c121e0fbfc0ffe86d19aac64c917c6e9b3b6652e37b232a1a` | Imported |
| 33 | [2301.09019v4](https://arxiv.org/abs/2301.09019v4) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 219,043 | 14 | `b2a37016b983b4b9b34bcbf76bc5151ea99dafbce72b08fbee9672113ed647e3` | Imported |
| 34 | [2403.00258v1](https://arxiv.org/abs/2403.00258v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 561,788 | 32 | `52d62747a4530c12618360b9efe918697046ba760a400268480616cf5bcf2f15` | Imported |
| 35 | [2301.12357v3](https://arxiv.org/abs/2301.12357v3) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,498,673 | 42 | `9a96b7e2ea7c781bfe69cbf32f2174320875927f87644602adb2dea1ecf87cc6` | Imported |
| 36 | [2302.14178v4](https://arxiv.org/abs/2302.14178v4) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 584,274 | 46 | `eb308e08b813a36ebb45c03c2763559bf134b03d87ea61eec0fda93102345e7e` | Imported |
| 37 | [2301.10175v2](https://arxiv.org/abs/2301.10175v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 145,833 | 11 | `f650296610423b1254c363523be1bc3cfed8bf9721d50b70c2add971e851d24a` | Imported |
| 38 | [2403.00319v1](https://arxiv.org/abs/2403.00319v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 3,054,536 | 39 | `edbfdcab1cdb35c8513b5334409785b5374355a033d28a932f6eef64be30b02f` | Imported |
| 39 | [2302.00389v2](https://arxiv.org/abs/2302.00389v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,465,166 | 32 | `8a7cd3fb0e98dde2c959075cc796f89e7d0f644d5c096639229045b9cf2df44a` | Imported |
| 40 | [2305.13148v2](https://arxiv.org/abs/2305.13148v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 387,776 | 30 | `eb513855d6b27716000b34d7e2f5c38000b102e19025d5d527fdf4af273f75ad` | Imported |
| 41 | [2303.03615v3](https://arxiv.org/abs/2303.03615v3) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 398,316 | 10 | `ed2307cdee4606ca90fd1ee1cda000071df008179e390a75600ef0de3aa82062` | Imported |
| 42 | [2403.00472v1](https://arxiv.org/abs/2403.00472v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,559,381 | 39 | `b7586dfe13e95a059467d1c0c956adc9b2476e75bf17a46f9bc218dc54a4fdf4` | Imported |
| 43 | [2302.11351v4](https://arxiv.org/abs/2302.11351v4) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 2,596,320 | 22 | `61884aba8896598b6e1cd780668d758ab525a3b09b2220967d1b95f68ee364a0` | Imported |
| 44 | [2307.01125v2](https://arxiv.org/abs/2307.01125v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 2,800,898 | 52 | `372ab16c0c1b6e99f6d1abb4b0860e193f511328a5a3500ea5707f797d6bed8d` | Imported |
| 45 | [2303.07036v3](https://arxiv.org/abs/2303.07036v3) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 521,978 | 9 | `eecdde4917567b0db490062c6465ad232499751c10b995d216696f5c725f3d8b` | Imported |
| 46 | [2403.00551v1](https://arxiv.org/abs/2403.00551v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 4,582,982 | 25 | `e40de23bc5188f7f58ceadf72766e74d6d189ccbdabd4d251043e66f3ec0afc1` | Imported |
| 47 | [2302.11953v2](https://arxiv.org/abs/2302.11953v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 3,455,398 | 12 | `7ec9109a4b73607be63594f29ca9122a022218c6d8f862b48cf8afbbe9168481` | Imported |
| 48 | [2307.15819v2](https://arxiv.org/abs/2307.15819v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 221,531 | 15 | `bf52bebaca55455bb91c3dadd05d4834dfac6f54d794c13f86de8122e11fb586` | Imported |
| 49 | [2305.19102v1](https://arxiv.org/abs/2305.19102v1) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 4,041,726 | 25 | `45653639e603a2dea60c89d35d697e39772fef2daadd459a7d513fa32544412c` | Imported |
| 50 | [2403.00617v1](https://arxiv.org/abs/2403.00617v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 763,556 | 18 | `d596d8f7d00f06c1b2b0299c78ecaf2832f092e74dcda9cbf103d83c9f4af960` | Imported |
| 51 | [2302.14334v2](https://arxiv.org/abs/2302.14334v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 19,275,774 | 20 | `1605c3c3d323d42036989332d829f6b11741866a0cf902c1cd14b15f9e2adde8` | Imported |
| 52 | [2309.16860v2](https://arxiv.org/abs/2309.16860v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 863,425 | 19 | `1773939aa4c7d5d89f2f9bd588cfa0d32fabfc3ee64fe1e68addcac19441b097` | Imported |
| 53 | [2305.19387v1](https://arxiv.org/abs/2305.19387v1) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 13,146,682 | 33 | `616e06607d0e2b06647721a540b3ea02517922f0233d3ec6aec5f8d347ed15b2` | Imported |
| 54 | [2403.00701v1](https://arxiv.org/abs/2403.00701v1) | stat | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 726,289 | 41 | `912c5a123b48923a6e8a2f457e48b7de0c2f1facf98bcb6585880c43d9f66818` | Imported |
| 55 | [2303.05707v2](https://arxiv.org/abs/2303.05707v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,418,246 | 9 | `52f31ba9c2805f538e716f0783b8df49a33c427a518a0fa7848bd653faca8b02` | Imported |
| 56 | [2310.02715v2](https://arxiv.org/abs/2310.02715v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 308,776 | 26 | `a213991dad147c43ce62907f8060cbcf28a787488226bcdaa41a265f9b6e5b1c` | Imported |
| 57 | [2303.07539v2](https://arxiv.org/abs/2303.07539v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 823,976 | 6 | `ba16291439d6429e03c716a8325ff8c05ea953df93ac467dfc5715b63adc8849` | Imported |
| 58 | [2311.02318v2](https://arxiv.org/abs/2311.02318v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 893,024 | 54 | `fa2aaccc8a3d54df10c93622604ce402f99c719aa6ff194a40b597cad45c59a3` | Imported |
| 59 | [2306.06984v1](https://arxiv.org/abs/2306.06984v1) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 4,579,440 | 24 | `f18636102d726d33b9d1502736d13d41d2eb39f265003e82d6ba8cae7ef63ba3` | Imported |
| 60 | [2303.16871v1](https://arxiv.org/abs/2303.16871v1) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 469,488 | 10 | `10f6a95ff979be1c8fa38fb5aa063a3ebce4805c7a320ed5033383bb760b89ff` | Imported |
| 61 | [2401.03973v4](https://arxiv.org/abs/2401.03973v4) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 234,551 | 16 | `005e6e28a23fdb38b331c8a9ea7024a41c12219fcbaff77411383a87bb750e21` | Imported |
| 62 | [2306.08610v7](https://arxiv.org/abs/2306.08610v7) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,088,442 | 15 | `50616f68f38cfbbf680151888e2f93fe3084857a853415d9ed7e371d48462748` | Imported |
| 63 | [2304.00119v1](https://arxiv.org/abs/2304.00119v1) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 704,739 | 14 | `783484c82c3c6fbd824ab92b85b29fd35d204512a02aa62c6c99056a92c90687` | Imported |
| 64 | [2401.10066v2](https://arxiv.org/abs/2401.10066v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 468,114 | 27 | `3912be8c024991386ec917f7d52628408f0c43f670f5d2d4c4ee5c0670667655` | Imported |
| 65 | [2306.11998v2](https://arxiv.org/abs/2306.11998v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,041,674 | 10 | `c185f85ed5d3e56f3dc00bd3f2ac7dd0257893b258df9c0cc6988d72c53b271d` | Imported |
| 66 | [2402.01214v2](https://arxiv.org/abs/2402.01214v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 736,979 | 24 | `a1e3df4e6013f3e7349e701d5f3fe99736cdd1372cf7176f22b532d245279ebf` | Imported |
| 67 | [2306.15042v2](https://arxiv.org/abs/2306.15042v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 2,004,681 | 12 | `f11600bb12c3b19f218e45404e8fd32133046e77997e0f3b6fddf407894a4edb` | Imported |
| 68 | [2304.13120v1](https://arxiv.org/abs/2304.13120v1) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 6,257,217 | 8 | `8ba24e66c2428eb5be970ce8daea1c5575feb39fc391a9cdb345afc3384b46c8` | Imported |
| 69 | [2402.15597v2](https://arxiv.org/abs/2402.15597v2) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 202,530 | 20 | `d2a64cf7e3de0de5c27281ed2d06af5805cde6954eb7b6cf07762c6fb93cdc27` | Imported |
| 70 | [2307.07913v2](https://arxiv.org/abs/2307.07913v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 11,614,555 | 14 | `41527155afbba9e1566b9908a6dd1b6aaf7bb7911bc0ca01c657208e109f8372` | Imported |
| 71 | [2305.00188v4](https://arxiv.org/abs/2305.00188v4) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 603,889 | 35 | `fe45665433c8b4180c428a7ec291ac85532b5c5723b67ece0d288a8a7d1cdafd` | Imported |
| 72 | [2402.18384v2](https://arxiv.org/abs/2402.18384v2) | math | [CC0-1.0](http://creativecommons.org/publicdomain/zero/1.0/) | 78,784 | 4 | `741a035f4926097b9fa2b9316f8669ad24dc5c0987dfcaa68420e4ef5b8d901f` | Imported |
| 73 | [2307.12069v3](https://arxiv.org/abs/2307.12069v3) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 945,462 | 11 | `e2860615c54de4b08342715749423aa30af6d1ef80456c00d8f7b9a6ee7ae32b` | Imported |
| 74 | [2305.14779v3](https://arxiv.org/abs/2305.14779v3) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 2,603,053 | 14 | `f395d49a8020876dd70174014c7f2cf0f896b0e4e9b703b630662a15d02997ad` | Imported |
| 75 | [2403.00008v1](https://arxiv.org/abs/2403.00008v1) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 6,356,815 | 44 | `f481022dbe004536d5c02eaf0480017f5e6a4858693ae7cbb8843d1e937c23a9` | Imported |
| 76 | [2307.14645v1](https://arxiv.org/abs/2307.14645v1) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,264,551 | 15 | `710a10ed6a5023b1d4a2471260bc9fc8980ac43b2bfbd7e16731e59bb95c7bbe` | Imported |
| 77 | [2305.15022v3](https://arxiv.org/abs/2305.15022v3) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 751,614 | 28 | `7619c2b6cb28e1029d9615a51ae60cb48cea9398d8e1a55b4224d4d9d48dcd58` | Imported |
| 78 | [2308.00647v2](https://arxiv.org/abs/2308.00647v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,387,589 | 18 | `b7be0544e5a7f08f4b8b85b50dc9700815ac3904f9ef2406f3cc4d81f2d376cf` | Imported |
| 79 | [2305.18502v2](https://arxiv.org/abs/2305.18502v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 8,138,195 | 31 | `99a1630143455094d105844fc68a3272964f099909c1fbfc0563595ab69ebd0c` | Imported |
| 80 | [2403.00125v1](https://arxiv.org/abs/2403.00125v1) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 9,159,071 | 38 | `a66e14d5e1b9af8c160881b26d41d52ee797e8e8842405781461decf1d2c329e` | Imported |
| 81 | [2308.04507v1](https://arxiv.org/abs/2308.04507v1) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 824,070 | 15 | `25a6f1e8d9257536047b458758e5ccc7b1361e476a5a59f9fcd2106bafbab640` | Imported |
| 82 | [2306.09547v2](https://arxiv.org/abs/2306.09547v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 13,754,036 | 23 | `24a1a98f6f329bf566a7d1fb8a5bbfcadedb658e1eadc4f3c741796b164f0c41` | Imported |
| 83 | [2403.00150v1](https://arxiv.org/abs/2403.00150v1) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 907,899 | 36 | `3987368b3e33dcb66464c78e5d37baade9ff8da336480580d913d783ecae3351` | Imported |
| 84 | [2308.05173v2](https://arxiv.org/abs/2308.05173v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 381,961 | 6 | `750954647bb69f540384f0ba0e7c7abc1359497c548c2782577e9e55893b2146` | Imported |
| 85 | [2306.09731v2](https://arxiv.org/abs/2306.09731v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 3,790,216 | 22 | `3e6bbb00953bc6138ff49e7e220f3c623f0a21d5ed81ae63ec40f216241fdc96` | Imported |
| 86 | [2403.00159v1](https://arxiv.org/abs/2403.00159v1) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 412,924 | 27 | `18c55e7181e5611ed2a0cea22c8da0276250427aacf063db08857dff319a9d77` | Imported |
| 87 | [2309.05432v2](https://arxiv.org/abs/2309.05432v2) | physics | [CC0-1.0](http://creativecommons.org/publicdomain/zero/1.0/) | 1,373,800 | 9 | `2a74b798eac0f6b2e183020452c328a611218698f6c0cc892d3bfdecd7226ebf` | Imported |
| 88 | [2307.03742v2](https://arxiv.org/abs/2307.03742v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 141,984 | 9 | `b28ce0e9a5b70e6b47c6218a069cccb35479e8eb07828afa8a343992e25eb1f2` | Imported |
| 89 | [2403.00162v1](https://arxiv.org/abs/2403.00162v1) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 1,454,982 | 173 | `80b183f248ff84108eff170cb641bc0a7a65712eb159bfed3c0e692cd4853c9b` | Imported |
| 90 | [2309.05713v2](https://arxiv.org/abs/2309.05713v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 3,549,574 | 25 | `03ad94cd3c972691a34774e47120904da17d2363e98dea656f763c31e428ab17` | Imported |
| 91 | [2307.04652v2](https://arxiv.org/abs/2307.04652v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 267,438 | 17 | `ee73c4fcc18592fd78e7349cca949cf68a625afc83ed15e27cb1ab5a86bd20c7` | Imported |
| 92 | [2403.00173v1](https://arxiv.org/abs/2403.00173v1) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 2,893,468 | 28 | `93be32077db81f2fa1739306bdf92fa3ab9ef41566ffdb3dc8db79ca3a2fbe4e` | Imported |
| 93 | [2309.08862v2](https://arxiv.org/abs/2309.08862v2) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 8,796,858 | 28 | `2ab377b11a94a15abc943e35979933f3b2b2d62a3da0603441353618fbdf8b60` | Imported |
| 94 | [2307.07884v2](https://arxiv.org/abs/2307.07884v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 628,097 | 26 | `98e7000065274c3c2b06b0be95b3f155d3220027ec7cbe3d86f78b25bf19dc9b` | Imported |
| 95 | [2403.00202v1](https://arxiv.org/abs/2403.00202v1) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 741,525 | 36 | `2553547ced07b2105d0ccf0c47c4538c9912439f53653fa2cf6f93135595eb14` | Imported |
| 96 | [2309.10888v3](https://arxiv.org/abs/2309.10888v3) | physics | [CC0-1.0](http://creativecommons.org/publicdomain/zero/1.0/) | 1,999,921 | 7 | `985a89de83a2fcc568682c4318905cee497501f0ee1dcfca9c3ce84a54e8d96f` | Imported |
| 97 | [2308.01109v1](https://arxiv.org/abs/2308.01109v1) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 917,926 | 21 | `b45fc6c6a553e45b8a8896aeaef7fb68c2472315523f31477566191ff8284913` | Imported |
| 98 | [2403.00203v1](https://arxiv.org/abs/2403.00203v1) | math | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 584,529 | 53 | `cce0be84080806c219184cf5eaa71fe2a6f75da3a7abf650d2aad20067a01fb8` | Imported |
| 99 | [2309.14434v1](https://arxiv.org/abs/2309.14434v1) | physics | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 8,541,344 | 20 | `5b6b6a22d3a6f80d69484a9ddc92cbbe54f1d5d810889ddb880c9659114d1e69` | Imported |
| 100 | [2308.02747v2](https://arxiv.org/abs/2308.02747v2) | cs | [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/) | 983,753 | 17 | `739a7e20c7cfbfd7b52bff463fa1eca3809f67f66f5adab3e1005bf054214d63` | Imported |

Successful imports: **100 / 100**. No import was rejected or failed. Because no import failed, no error message needed an SC-007 assessment for this corpus. SC-007 error-message quality is assessed separately.

### Limitations

- **Homogeneous source.** arXiv PDFs are mostly produced by the same TeX toolchain (pdfTeX and similar), so the corpus covers fewer PDF producers, scanned documents and unusual structures than a mixed collection would.
- **Size range.** The largest file is about 19 MB and the longest has 173 pages, far below the limits. Boundary sizes and page counts are covered by the automated validation and limit tests (T020, T021), not by this corpus.
- **Selection rule.** Excluding the four candidates with qpdf warnings narrows "valid, readable" to PDFs qpdf checks cleanly. Whether LexScholar imports PDFs that tools only warn about is not measured here.

## Automated results (SC-003, SC-004, SC-005, SC-007, SC-008)

_Not yet recorded._ These criteria are covered by the automated suite. Their results are recorded here with T041 and T042 at the end of F001.

## Usability evaluations (SC-001, SC-006)

_Not yet recorded._ T046 is waiting for the user to run unassisted researcher evaluations. Automated browser checks are not participant evidence and are not recorded here.
