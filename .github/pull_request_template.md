## What changed

<!-- One or two sentences. Say what the user sees, not only which files moved. -->

## Checklist

- [ ] `dotnet build Pad8Lab.slnx -c Release` passes.
- [ ] `ParityCheck` still reports `PARITY OK` if the firmware builder changed.
- [ ] No controller backup, full stock image or unit-specific data is included.
- [ ] Documentation is updated when behavior or paths change.
- [ ] The change keeps the write path in `plan -> check ranges -> erase -> write -> read back -> restart`.
