# Security policy

## Supported versions

Only the latest release and the current `main` branch receive fixes.

## Reporting a vulnerability

Report it privately through
[GitHub Security Advisories](https://github.com/JehuAlv/Pad8Lab/security/advisories/new). Do not open a
public issue for a security problem.

Include the Pad8Lab version, the controller model and firmware, and the steps to reproduce. Do not
attach a controller backup or a full firmware image; the advisory has a private attachment area if a
file is really needed.

## What is in scope

- A check that can be bypassed to write outside the app area, into the bootloader, config, MAC sector
  or unit data.
- A restore that could write one unit's data to another unit.
- A path or permission problem in the data folder (`%LOCALAPPDATA%\Pad8Lab`).
- A supply-chain problem in the build or the published executable.

Firmware behavior on a modified controller is covered by [DISCLAIMER.md](DISCLAIMER.md) and
[SAFEGUARDS.md](SAFEGUARDS.md), not by this policy.
