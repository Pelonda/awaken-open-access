# System Architecture

The system follows a **Server–Client architecture** with a shared data layer.

## Components

### Admin Server (WPF)
- Session creation and management
- Computer monitoring
- Reporting and analytics
- Configuration and customization

### Client Application (WPF)
- Full-screen lock enforcement
- PIN-based session login
- Countdown and reminders
- Online/offline heartbeat reporting

### Shared Data Layer
- SQL Server backend
- Entity Framework Core ORM
- Secure admin authentication
- Centralized configuration


## Communication Flow

1. Admin creates a session
2. System generates a PIN
3. Client validates PIN
4. Session timer begins
5. Client sends heartbeat
6. Session expires → client locks

## Design Goals

- Security
- Reliability
- Scalability
- Low administrative overhead
