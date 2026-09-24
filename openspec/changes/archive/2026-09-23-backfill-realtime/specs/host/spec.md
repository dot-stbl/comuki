## ADDED Requirements

### Requirement: Realtime composition
The host composition SHALL register SignalR, the `IRunEventsBroadcaster`
implementation, and map `/hubs/runs` (see realtime). Detailed errors MAY be
enabled in development; production still requires authenticated hub access.

#### Scenario: Hub is mapped
- **WHEN** the host boots
- **THEN** `/hubs/runs` accepts SignalR negotiate for authenticated clients
