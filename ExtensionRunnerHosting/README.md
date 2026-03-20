# Persistent Extension Runners

`ExtensionRunnerHosting` contains the shared infrastructure for the persistent runner model used by the isolated Integration Soup extension libraries.

## Design
- One runner process is kept alive per extension type, per host process.
- The host-side caller connects over a fresh named-pipe connection for each request.
- Requests are serialized with a single in-process queue per extension type.
- The caller does not return until:
  - any queued requests ahead of it have completed
  - its own request has been sent
  - its own response has been fully read

## Failure Handling
- If the runner is missing before dispatch starts, the client can respawn it and retry once.
- If the runner dies after dispatch has started, the request fails and is not replayed automatically.
- The runner exits when its parent host process exits.

## Protocol
- Length-prefixed UTF-8 JSON over named pipes.
- Shared envelope fields:
  - `ProtocolVersion`
  - `RequestId`
  - `Operation`
  - `PayloadJson`
- If the protocol shape changes, update both sides together and review `PersistentRunnerProtocol.cs`.

## Modes
- Persistent server mode:
  - `--server --pipe-name <name> --parent-pid <pid>`
- One-shot command-line mode:
  - retained per runner executable for direct/manual execution only

## State
- Runner state is in-memory only.
- Examples:
  - HTML-to-PDF remembers the last successful browser/headless combination.
  - RTF-to-PDF remembers the resolved LibreOffice path.
  - Azure and AWS runners cache client objects.

## Documentation Reminder
- User-facing website/tutorial updates belong in:
  - `C:\Users\jason\source\repos\HL7SoupWebsite\HL7SoupWebsite`
- Product/code documentation updates belong in:
  - `C:\Users\jason\source\repos\Documentation`
