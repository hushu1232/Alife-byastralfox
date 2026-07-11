# Dual-Account Character Instance Installation Design

**Date:** 2026-07-11

## Goal

Install the two existing local character instances into the isolated .NET dual-account runtime without creating a new persona, sharing mutable state, or committing private runtime data.

## Instance mapping

- `account-a` uses the existing `Storage/Character/真央` instance. The directory name remains `真央`; QChat's identity mapping presents this account as 咪绪.
- `account-b` uses the existing `Storage/Character/夏羽` instance.
- The mapping follows the installed OneBot account slots already assigned to loopback ports 3001 and 3002. Account identifiers and credentials are not copied into tracked files.

## Installation model

`CharacterSystem` discovers characters from the storage root selected by `ALIFE_STORAGE_PATH`, under `Character/<instance-name>/index.json`. Installation therefore copies each complete local instance into only its assigned account storage root:

```text
account-a storage root
└── Character/真央

account-b storage root
└── Character/夏羽
```

Each copy preserves the instance's `index.json` and existing `Configuration`, `Memory`, `State`, `Storage`, and `LifeEvents` content when present. Missing optional subdirectories are not synthesized.

The installer must use staging plus an atomic directory replacement where practical. It must reject a source without a parseable `index.json`, reject a name mismatch, and refuse any source or destination outside the expected Alife storage roots. Existing destination instances must be backed up locally before replacement.

## Isolation and privacy

- Each account receives exactly one assigned character instance.
- Memory, state, databases, life events, and configuration remain account-local and are never shared by junction, symlink, or common writable directory.
- The installer and validation output report only safe role labels and boolean/count results. They do not print QQ account numbers, Tokens, private messages, prompt bodies, SQL, login state, or absolute evidence paths.
- Character instances, backups, and account storage roots remain local ignored runtime data. They are not staged or committed.
- No NapCat, QQ, Alife, or capability process is started, stopped, or restarted during installation and static validation.

## Validation

Before changing local data, a dry run validates both source instances and both destination roots. After installation, static checks verify:

1. account A contains a parseable `Character/真央/index.json` whose character name is `真央`;
2. account B contains a parseable `Character/夏羽/index.json` whose character name is `夏羽`;
3. account A does not contain the installed 夏羽 instance and account B does not contain the installed 真央 instance;
4. the two resolved instance directories are distinct and are below their assigned storage roots;
5. no local character, backup, account configuration, Token, or runtime artifact is staged by Git.

Runtime activation is a separate owner-approved step. This installation pass ends after static validation and does not claim either account is online or production-ready.
