---
status: accepted
---

# Keep QQ and desktop runtimes independent while sharing the character core

The QQ side and desktop side run as independent Alife Channel Runtimes with separate transports, hot conversation windows, state machines, tool queues, and failure domains. They read the same Character Core so 夏羽 keeps one persona and approved body of knowledge, while AIRI remains the desktop runtime's Embodiment Adapter rather than a second AI or a child of QChat. Cross-channel sharing is limited to protected persona facts and deliberately promoted durable facts; raw chat history, permissions, diagnostics, and transient state remain channel-scoped to prevent context contamination and coupled outages.