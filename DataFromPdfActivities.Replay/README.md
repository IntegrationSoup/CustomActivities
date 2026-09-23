# Data from PDF recovery replay

This harness links the exact `UnlabeledColumnRecovery.cs` source used by the production
runner. It reads existing JSON and replays only the new additive recovery pass.
It does **not** parse PDFs, rerun legacy field extraction, or reconstruct word boxes
or horizontal segments. Requires the .NET 10 SDK; the production runner remains .NET 4.8.

```powershell
dotnet build DataFromPdfActivities.Runner/DataFromPdfActivities.Runner.csproj -c Release
dotnet run --project DataFromPdfActivities.Replay/DataFromPdfActivities.Replay.csproj -c Release -- <Sample1.txt> <sample2.txt> <saved.hl7Workflow> <output-directory>
```

The saved workflow is read only. The harness selects the `Convert to JSON` activity's
historical `ResponseMessageTemplate` in `PDF Referral to HL7`. It never executes the
workflow or changes mappings. Inputs and workflow hashes are checked before/after replay.

## Results from the supplied datasets

| Input | Original fields | Added fields | Changed/removed |
| --- | ---: | ---: | ---: |
| Sample1 | 19 | 5 | 0 |
| sample2 | 23 | 5 | 0 |
| Historical successful template | 30 | 0 | 0 |

Sample1 gains `unlabeledAfterVisitNo`, `unlabeledAfterVisitNoPage`, and
`unlabeledAfterVisitNoLine1` through `Line3`. Sample2 gains `unlabeledAfterAcc`,
`unlabeledAfterAccPage`, and `unlabeledAfterAccLine1` through `Line3`. These retain
the combined block, page number, and individual name/address rows. The anchor comes
from the actual preceding labelled row, resolved against existing field keys; no
particular label or patient is hardcoded in production. Sample2's populated `acc`
value is unchanged. Existing `unlabeledAfterAccLine1/2/3` and `unlabeledBlock1*`
fields in the successful template remain unchanged. Mapping is separate work.

These anchored keys replace the earlier `recoveredUnlabeledBlock...` naming in the
new recovery pass. A collision reserves the entire existing field family, even if
only a blank line or page key exists. Recovery then uses a separate anchored family,
for example `unlabeledAfterCodeBlock2`, `unlabeledAfterCodeBlock2Page`, and
`unlabeledAfterCodeBlock2Line1` (and further lines); it increments the block number
until free. It never fills blanks or mixes old and new lines in the occupied family.
Already represented blocks are skipped, including when recovery is repeated. No
collision suffix is needed for the three supplied documents.

Full replay JSON, local field comparisons (including values), and a verification
report are under `artifacts/pdf-replay-private/`, excluded from Git because the
supplied data can still contain personal information. Source files are not rewritten.
Only newly added `data.fields` properties differ semantically in output; all other
nodes, original fields, and original strings remain intact. JSON whitespace/escaping
can differ because the output is serialized anew.

## Representation and limits

Recovery reads `pages[].lines[].text`, `x`, and `y`, together with existing field keys
and values. It does not substitute `text.full`, `text.pages`, or `pages[].text` for
line text. Anonymization differs across these representations; therefore recovered
values can retain residual text present in the line representation. Those values
are confined to the private artifacts, not repeated in this report.

The fallback requires a nearby labelled row starting more than 45 units to the
right, followed by at least two rows aligned within 24 units with vertical gaps
no larger than 22 units. Known labels delimit prefixes in mixed rows. Unknown
colons, empty prefixes, changes of alignment, and large gaps end recovery. It
does not classify names or addresses and cannot recover every possible layout.
In Sample1 the last recovered row retains its entire supplied line, including a
trailing numeric token: without segment geometry its column provenance cannot be
determined. No guessed word coordinates or semantic cleanup are applied.

Checks cover original value preservation (including blank values), all other JSON
nodes, exact requested name/address rows, unchanged historical success, repeat-run
idempotence, complete/partial/case-insensitive key collisions, populated anchors, mixed-row labels, legacy
duplicate suppression, and conservative negative boundaries. Synthetic checks use
invented rows and are explicitly separate from the three supplied JSON replays.

Compilation verifies the actual .NET 4.8 runner. JSON replay cannot validate PdfPig
ingestion, word grouping, segment splitting, or legacy extraction on the original
PDFs. No PDFs were used. The pre-existing `sample2` clinicalIndication continuation
truncation remains unchanged and needs separate investigation.

Reviewable website and Documentation repository patches are staged alongside this
README to keep documentation edits reviewable. The approved repair was subsequently
consolidated with the released bridge branch and committed on
`codex/pdf-anchored-recovery`. The signed Data from PDF 5.0.5 MSI was verified and
copied to local website downloads staging with a backup of 5.0.4. See
`Setup.DataFromPdfActivities/RELEASE-5.0.5.md` for build and verification details.
No installation, workflow execution, mapping change, public upload, or Git push occurred.
