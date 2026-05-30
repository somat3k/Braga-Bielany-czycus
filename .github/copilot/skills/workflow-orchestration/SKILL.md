---
name: Workflow Orchestration and State Logging
about: Compose CI workflows with Docker/Redis logging, checkpoints, and artifact snapshots.
---

## Goal
Run repeatable workflow automation with complete run logs and checkpointed states.

## Use this skill when
- You need scheduled and event-driven workflow execution.
- You need Redis resource logs, run logs, and uploaded snapshots.

## Operational checklist
1. Bring up Redis service for each workflow run.
2. Execute build/check steps and capture runtime details.
3. Store run metadata and checkpoint JSON files.
4. Upload artifacts to preserve logs and snapshots.
