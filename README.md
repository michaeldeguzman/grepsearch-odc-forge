# OdcAgentFileInspector

OdcAgentFileInspector is an ODC External Library that gives an AI agent three read-only tools over one ODC `File` value: `GrepFile`, `ReadFileLines` and `GetFileOutline`. It lets an agent find the exact lines it needs in a long log, Markdown or CSV file without loading the whole file into the model.

It handles UTF-8 text, one file per call. It does not extract text from PDF or DOCX, it does not rank documents, and it does not index anything. Use semantic search or BM25 to find the right document, then use these tools to find the right lines inside it.

## The three actions

| Action | What it returns |
|---|---|
| `GrepFile` | 1-based matching lines for a literal or regular expression, with optional context lines |
| `ReadFileLines` | A 1-based inclusive line range, at most 200 lines, with line numbers |
| `GetFileOutline` | Line count, byte size and Markdown headings (H1 to H4) with line numbers |

All three take an `IOSFile`. None of them writes anything. A bad input or an unreadable file comes back in `errorMessage`. An action never throws to the caller.

### GrepFile

- `pattern` is literal text when `isRegex` is false. When `isRegex` is true it is a .NET regular expression compiled with `RegexOptions.NonBacktracking` and a 2 second per-line timeout. There is no fallback to a backtracking engine. Lookarounds and backreferences are rejected, and the error message says so.
- `contextLines` is clamped to 0 to 5. `maxMatches` is clamped to 1 to 50, and 0 means 20.
- Output lines start with `> ` for a match and two spaces for context. A `--` line separates groups that are not adjacent.
- The header reads `Showing N of M matches for "pattern"`. The scan keeps counting after `maxMatches` is reached, up to 10,000 matches. If the count passes 10,000, the header reads `Showing N of 10,000+ matches for "pattern" (count stopped at limit; use a more specific pattern)`. A file with exactly 10,000 matches gets the plain header.
- `totalMatchesFound` never exceeds 10,000.

### ReadFileLines

- `startLine` below 1 becomes 1. `endLine` below `startLine` becomes `startLine`. The range is capped at `startLine + 199`.
- A line longer than 500 characters is clipped. A `startLine` past the end of the file returns an error that names the real line count.
- `totalLines` is exact because the scan always runs to the end of the file. `hasMoreBelow` is true when lines remain after the returned range.

### GetFileOutline

- It lists headings written as `#` to `####`, with up to three leading spaces. It skips lines inside fenced code blocks. A fence closes only on the same character and at least as many of them.
- The first line is a summary, for example `Lines: 7,084 | Bytes: 194,474 | Headings: 841 (showing 100)`.
- `maxHeadings` is clamped to 1 to 200, and 0 means 100. A file with more headings than that shows the first ones in file order and says how many it left out.

### Limits that apply to every action

| Limit | Value |
|---|---|
| Output size | 64 KB of UTF-8, then `[output truncated at 64 KB]` |
| Clipped line length | 300 characters in `GrepFile`, 500 in `ReadFileLines` |
| Lines scanned by `GrepFile` | 1,000,000 |
| Scan deadline | 60 seconds, then `[scan stopped at line N: time limit]` |
| Binary check | A `0x00` byte in the first 1,024 bytes, unless the file starts with a UTF-16 byte order mark |

If `GrepFile` stops at the 1,000,000 line limit, `isTruncated` is true, but the header does not say so. I did not add a header marker for that case.

The library streams the file. In a test app, `GrepFile` on a 20 MB log came back in 541 ms to 1,031 ms measured at the browser, one run each. The first call after an idle period took 1,759 ms. Those are single observations, not a benchmark.

## Choosing a tool

| You need to | Use |
|---|---|
| Find which document answers a question | Semantic search or BM25 |
| Find an exact ID, error code, name or heading inside one document | `GrepFile` |
| Read the text around a match, or a section named in the outline | `ReadFileLines` |
| Learn how an unfamiliar Markdown document is organised | `GetFileOutline` |
| Handle a PDF or DOCX | Convert it to text or Markdown first. The binary error message suggests OmniDoc2MD. |

## Getting started

1. Run `./build.sh`. It runs the tests, publishes the library and writes `AgentFileInspector.zip`.
2. Upload the zip in ODC Portal under External Logic.
3. Release the library as a version before you reference it. A library that is only published cannot be referenced by an app.
4. Reference that version from the app that calls the actions.

## Wiring the actions to an agent

I tested this with an agentic app, a web app that owns the documents (a test app), and the library. The shape that worked is below.

**The tool actions live inside the agentic app.** A server action in a web app cannot be Public in ODC. The editor rejects it with "Public Property of a Server Action within an App, which is not supported on this version". Entities can be Public. So the web app makes its `Document` entity Public, and the agentic app references that entity and the library.

**Each tool is a wrapper server action** with these inputs and outputs:

| Wrapper | Inputs | Outputs |
|---|---|---|
| `Agent_GrepFile` | `DocumentId`, `Pattern`, `IgnoreCase` | `FormattedMatches`, `TotalMatchesFound`, `IsTruncated`, `ErrorMessage` |
| `Agent_ReadFileLines` | `DocumentId`, `StartLine`, `EndLine` | `Content`, `TotalLines`, `HasMoreBelow`, `ErrorMessage` |
| `Agent_GetFileOutline` | `DocumentId` | `Outline`, `TotalLines`, `ErrorMessage` |

Each wrapper does the same four things. It gets the `Document` by `DocumentId`. It returns `Document not found.` when there is none. It passes `Document.FileContent` to the library action. It catches exceptions into `ErrorMessage`. The `GrepFile` wrapper passes `isRegex` false, `contextLines` 2 and `maxMatches` 20. The outline wrapper passes `maxHeadings` 0.

**The DocumentId reaches the agent in the user message.** The consuming app calls the agent's service action, `Call<AgentName>`, with a `SessionId` and a `UserInput`. I format `UserInput` as `DocumentId: <id>`, a newline, then `Question: <question>`. The model fills the tools' `DocumentId` parameter from that text. Mark every tool parameter as generated by the AI, and say in each description where the value comes from.

**Describe the parameters.** Generated wrappers tend to set every input description to the bare parameter name. Replace them with texts like these:

| Parameter | Description |
|---|---|
| `Agent_GrepFile.DocumentId` | Id of the document to search, taken from the user message. |
| `Agent_GrepFile.Pattern` | Exact text to find. Prefer specific tokens such as error codes, IDs or heading words. |
| `Agent_GrepFile.IgnoreCase` | True for case-insensitive search. |
| `Agent_ReadFileLines.DocumentId` | Id of the document to read, taken from the user message. |
| `Agent_ReadFileLines.StartLine` | 1-based first line to read. |
| `Agent_ReadFileLines.EndLine` | 1-based last line to read, at most 200 lines after StartLine. |
| `Agent_GetFileOutline.DocumentId` | Id of the document to outline, taken from the user message. |

**Action descriptions** for the three tools:

- `GrepFile`: Searches a text, log, CSV or Markdown document for literal text or a regular expression and returns matching line numbers with surrounding context. Use this first to find identifiers, error codes, clause numbers, headings or keywords without reading the whole document. Then use ReadFileLines to read around a match.
- `ReadFileLines`: Reads a range of lines (1-based, inclusive, max 200). Read only what you need: when reading a section, stop at the next heading of the same or higher level from the outline.
- `GetFileOutline`: Returns a document's line count, size and Markdown headings with line numbers. Call this first on unfamiliar Markdown documents to decide where to read.

**System prompt** I used:

```text
You answer questions about one document using tools. You cannot see the document directly. The DocumentId to use is provided in the user message.
1. If the document is Markdown or you don't know its structure, call Agent_GetFileOutline first.
2. Use Agent_GrepFile to find exact terms: IDs, error codes, names, headings, keywords. Prefer specific patterns. If results say "count stopped at limit", narrow the pattern.
3. Use Agent_ReadFileLines to read around a match or a section from the outline. Read only what you need (usually 20 to 80 lines).
4. Answer only from what the tools returned. Cite line numbers. If you can't find it, say so and say what you searched for.
Never guess line contents you haven't read.
To find where a section ends, grep for the next heading at the same level (for example ### Section 12.4) before reading.
```

The last line was added after a first evaluation showed the model reading far more of a section than it needed. Its effect is in the last table row and the fourth point below.

**Call condition and timeout.** I capped the loop with `LoopCount >= 15 or TotalCallsCount >= 25 or TokenUsage >= 30000`. The ODC documentation names `LoopCount`, `TotalCallsCount` and `TokenUsage` as the variables available in that expression. A tripped condition stops the agent and raises an error. I raised the request timeout for agent calls to 60 seconds.

**Check the model binding in Studio.** The agent's model binding is set on the Call Agent element, separately from the model connections in your tenant. Check it in Studio after any generated build.

## What I observed

I asked five questions against a synthetic 7,084 line Markdown document and a synthetic 20 MB log, using Sonnet 5.5 only, on an Anthropic model connection. Each row is one run, so treat the numbers as observations, not a benchmark.

| Question | Result |
|---|---|
| Which unit describes rule VR-01232 | Correct, 1 call, 2,942 ms |
| Outline and line count | Correct, 1 call, 3,880 ms |
| A rule that does not exist | Honest not-found, 1 call, 2,649 ms |
| How often ERROR appears in the 20 MB log | "at least 10,000", 2 calls, 6,637 ms |
| What Section 12.3 says, without the last prompt line | Correct, 3 calls, 8,388 ms, read 177 lines |
| What Section 12.3 says, with the last prompt line | Correct, 4 calls, 12,673 ms, read 35 lines |

- The model answered the 10,000 match question with a lower bound, as the system prompt asked.
- Without the last prompt line the model read far more than the section needed. Section 12.3 is 34 lines and it read 177 (lines 2024 to 2200). Changing the `ReadFileLines` description to say to stop at the next heading of the same or higher level did not change that: the same three calls, the same read, a correct answer.
- The cause was missing information, not a weak description. `GetFileOutline` shows the first 100 of the document's 841 headings, so the model could not see where Section 12.3 ends. A description cannot supply a fact the model was never given.
- The last prompt line fixed the over-read. The model ran four calls: the outline, a grep for `### Section 12.3`, a grep for `### Section 12.4` (found at line 2059), then a read of lines 2024 to 2058. That is 35 lines instead of 177, 80% fewer, for one extra grep call. Time went up to 12,673 ms and the answer was correct. This is one run on one model.

## Known limits

- UTF-8 text only. A binary file returns an error before any scan. Only the first 1,024 bytes are checked, so a NUL byte later in a file is not detected.
- One file per call.
- The `File` type is not supported in ODC Self-hosted, so neither is this library.
- Regular expressions use the NonBacktracking subset. Lookarounds and backreferences are not available.
- Output is capped at 64 KB per call. A match count is a lower bound once it reaches 10,000.
- `GetFileOutline` lists at most 200 headings, from the start of the file. In a document with more headings, the later ones are not visible to the model through that action. To find where a section ends, grep for the next heading, for example `### Section 12.4`.
- The library does not check who may read a document. That check belongs in your wrapper.

## Security

File content is untrusted input to the model. A document can contain text written to steer the agent. Keep the agent's tools read-only, as these three are. Validate in each wrapper that the current user may read the `DocumentId` before you pass the file on.

If your wrappers log tool calls, remember that the output is file content. If your documents are sensitive, do not log it, or log only metadata.

## Build and test

```bash
./build.sh                 # tests (85), publish, AgentFileInspector.zip
./tools/make-fixtures.sh   # 1 MB and 20 MB synthetic logs and a Markdown spec in spike-files/ (gitignored)
dotnet test --filter "Category=Perf"   # 50 MB scan, excluded from build.sh
```

The fixtures contain synthetic data only.

## What comes next

Planned for v1.1, not built: an optional `startLine` on `GetFileOutline` that returns headings from that line onwards, so an agent can see where a section ends without grepping for a guessed heading name.
