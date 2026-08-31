---
name: init
description: Onboard a project into Comuki - repository, compute, models, and knowledge seed.
---

When the user invokes /init, run the onboarding wizard:

1. Ask for the project name and the repository URL (or confirm the detected
   one).
2. Offer compute setup: Docker (dev) or Kubernetes (prod); record the choice
   as a project setting.
3. Ask which model endpoints the brain and workers should use; store them as
   project settings, secrets stay in env.
4. Offer an initial knowledge seed from the repository README.
5. Summarize the created project and suggest the first run.
