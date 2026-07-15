# Reproduction Guidelines

- Put each reproduction in its own `<project>/<issue-slug>/` directory and add it to the root README.
- Keep it standalone and free of private names, paths, links, schemas, or context.
- Reduce it to the smallest, most direct failing sequence; remove unrelated application code, frameworks, schemas, and concurrency unless required.
- Keep prerequisites minimal and native to the repro’s stack; never require unrelated runtimes. Prefer self-restoring dependencies such as NuGet packages and Testcontainers when they simplify setup.
- Make `BUG_REPORT.md` copy-pasteable: link to the public repro and include complete clone, `cd`, and run commands; never rely on attachments or “this directory.”
