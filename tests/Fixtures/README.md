# PDF test fixtures

These synthetic fixtures were created for LexScholar; they contain no research
materials or third-party documents. They may be reused and redistributed under
[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/).

The valid PDFs were generated as minimal PDF 1.4 documents with two pages,
the standard Helvetica font and a short literal label. `valid-renamed.pdf` is
an exact copy of `valid.pdf`; `distinct.pdf` uses a different label. The
encrypted fixture was produced from `valid.pdf` with qpdf AES-256 encryption;
its user password is `fixture-password` and owner password is
`fixture-owner-password`. `corrupt.pdf` is deliberately truncated and
`not-a-pdf.txt` is plain text. Passwords are public test data.

| File | SHA-256 |
|---|---|
| `valid.pdf` | `0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93b` |
| `valid-renamed.pdf` | `0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93b` |
| `distinct.pdf` | `a0ba5d689dd633805ff9d5e6a77712204ca9b6d7b0e6b728ea58d8b66ce96694` |
| `encrypted.pdf` | `13734bb11fa4f533bc28695f30b395bced34fc15b6a5d5282c7e3aad40c7f369` |
| `corrupt.pdf` | `24605897b990617d564009e8a5621d60f2e85bfcd08768907a6c368c3bd56951` |
| `not-a-pdf.txt` | `0d28a0d3b65144f83417ae4ddb8cd9e4e5dd3510c2fc8c4397edbda12793de6f` |
