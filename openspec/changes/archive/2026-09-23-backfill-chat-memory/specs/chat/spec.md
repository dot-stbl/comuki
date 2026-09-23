## Purpose

Defines the operator chat surface: subject-owned sessions, Voluta graph turns
with plan-approve interrupts, transcript paging, and the merged slash-command
catalog. Chat is the human loop into Brain/Orchestration — not a free-form
LLM playground.

## ADDED Requirements

### Requirement: Subject-owned chat sessions
The platform SHALL persist chat sessions in `chat_sessions` with a client-side
UUIDv7 id, owning subject id, optional project id, title, status, and
timestamps. Creating a session SHALL bind it to the authenticated subject.
Listing SHALL return only that subject's recent active sessions. Reading or
mutating another subject's session SHALL answer 404 (missing), never 403.

#### Scenario: Foreign session is missing
- **WHEN** subject A requests subject B's session by id
- **THEN** the answer is 404 with a stable not-found problem code

#### Scenario: Create returns owned session
- **WHEN** an authenticated subject with `chat:use` posts a new session
- **THEN** the response is 201 with the session view and `Location` under
  `/api/v1/chat/sessions/{id}`

### Requirement: Chat REST surface
Chat sessions SHALL be served under `/api/v1/chat/sessions` and demand
`chat:use`: `POST /` create, `GET /` list recent, `GET /{sessionId}`,
`POST /{sessionId}/messages` (one turn), `GET /{sessionId}/messages` (paged
transcript, oldest first), `POST /{sessionId}/approve` (resolve pending plan
interrupt). The merged slash catalog SHALL be `GET /api/v1/chat/slash`.
Validation failures answer 400; a turn while an approve interrupt is pending
answers 409.

#### Scenario: Approve resolves interrupt
- **WHEN** a session has a pending plan-approve interrupt and the subject
  posts approve/reject
- **THEN** the graph resumes and the turn result reflects the decision

#### Scenario: Message while pending approve
- **WHEN** a subject posts a message while an approve interrupt is open
- **THEN** the answer is 409 Conflict

### Requirement: Voluta graph checkpoints
Each session SHALL keep a Voluta graph checkpoint in `chat_checkpoints`
(jsonb state + pending interrupt). The transcript SHALL be append-only
`chat_messages` with role and body. Checkpoints SHALL make sessions resumable
across host restarts.

#### Scenario: Resume after restart
- **WHEN** the host restarts mid-session
- **THEN** the next turn loads the stored checkpoint and continues from the
  pending node rather than resetting the graph

### Requirement: Slash catalog merge
`GET /api/v1/chat/slash` SHALL merge built-in slash commands with the
control-plane chat-commands pack. Built-ins include the `/init` wizard
skeleton and memory-oriented commands declared by the host; unknown slash
tokens fall through to the graph as ordinary text after expansion fails.

#### Scenario: Catalog lists both sources
- **WHEN** the slash catalog is requested
- **THEN** the response includes built-ins and control-plane pack entries
  without exposing prompt bodies
