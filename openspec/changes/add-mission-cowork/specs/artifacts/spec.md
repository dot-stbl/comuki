## ADDED Requirements

### Requirement: Mission-mediated artifact visibility
An artifact associated with a Task attached to a private Mission SHALL require the common Mission access decision on every route, signed URL issue, search, citation, and retrieval operation. Existing project/run permissions SHALL not widen access. Attachment applies to prior artifacts immediately.

#### Scenario: Old signed URL request after promotion
- **WHEN** a non-participant requests a fresh signed URL for an artifact from a promoted Task
- **THEN** the request is denied as not found even if the caller has project run-read permission

### Requirement: Context evidence handles
Large artifacts used by Brain SHALL be represented by immutable content-hashed handles with content type, size, summary, visibility, available ranges, and provenance. Context Packs contain handles and selective previews rather than unbounded artifact bodies.

#### Scenario: Brain inspects a large log
- **WHEN** a relevant log exceeds the Context Pack allocation
- **THEN** Brain receives a handle and selected ranges with citations instead of the entire file
