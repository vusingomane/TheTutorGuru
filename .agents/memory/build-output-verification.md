---
name: Build output verification
description: Handling empty shell-tool results during .NET validation.
---

Do not treat an empty successful shell-tool result as proof that a .NET build passed.

**Why:** Several direct .NET build calls returned no compiler output; capturing the process with Python subprocess produced an explicit exit code and the compiler's build result.

**How to apply:** When build output is unexpectedly absent, capture the process exit status and stdout/stderr before reporting validation success. Avoid repeatedly issuing the same silent command.
