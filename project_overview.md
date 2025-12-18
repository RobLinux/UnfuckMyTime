# UnfuckMyTime - Project Overview

## 1. Description
**UnfuckMyTime** is a Windows-based AI Focus Coach designed to help users regain control of their time. It combines:
*   **AI Goal Setting**: Users state their intent in natural language, and the AI converts it into a structured plan (allowed apps, domains, duration).
*   **Activity Monitoring**: A low-level system hook tracks the active window and, for browsers, the URL, to detect distractions.
*   **Intervention System**: A tiered response system (Notifications -> Overlay -> Lock) that nudges the user back to work when they drift.

## 2. Project Structure

The solution consists of three main projects following a clean architecture pattern:

### **`src/UnfuckMyTime.Core`**
*   **Role**: Contains the "Domain" logic and interfaces. It has NO dependencies on UI or specific platform APIs (mostly).
*   **Key Components**:
    *   **Models**: `SessionPlan` (the goal), `ActivitySnapshot` (what you're doing), `ExceptionRule` (allowed distractions).
    *   **Interfaces**: `IAIGoalService` (AI abstraction).
    *   **Services**: 
        *   `RulesEngine`: Logic to decide if `ActivitySnapshot` is allowed by `SessionPlan`.
        *   `SessionManager`: (Planned) State machine for the user's session (Idle, Running, Paused).

### **`src/UnfuckMyTime.Infrastructure`**
*   **Role**: Implements interfaces defined in Core using platform-specific APIs.
*   **Key Components**:
    *   **`WindowsActivityCollector`**: Uses `SetWinEventHook` (User32.dll) to detect window changes and UI Automation to read browser URLs.
    *   **`OpenAIGoalService`**: Implements `IAIGoalService` using the OpenAI NuGet package to parse prompts.

### **`src/UnfuckMyTime.UI`**
*   **Role**: The WPF Application (Presentation Layer). Orchestrates the services and shows the UI.
*   **Key Components**:
    *   **`App.xaml.cs`**: Entry point. Sets up the Tray Icon and Dependency Injection (Global services).
    *   **`MainWindow.xaml`**:
        *   **Setup View**: Input goal and API key.
        *   **Dashboard View**: Shows timer, logs, and current status.
    *   **`appsettings.json`**: Stores configuration (API Keys).

## 3. Key Data Flow
1.  **Start**: User types goal -> `OpenAIGoalService` -> Returns `GeneratedPlan`.
2.  **Run**: `SessionManager` starts -> `WindowsActivityCollector` begins listening.
3.  **Loop**: 
    *   User switches window.
    *   `Collector` fires `ActivityChanged` event.
    *   `RulesEngine` checks `ActivitySnapshot` vs. `GeneratedPlan`.
    *   If distraction -> Trigger Intervention (Flash/Toast/Overlay).

---

## 4. Remaining Steps (Roadmap)

Derived from the original Implementation Plan.

### **Phase 1: Persistence & Rules (Immediate Next Step)**
*   **`ExceptionStore`**: We need a way to save "allowed distractions" permanently (e.g., "Spotify is always allowed").
*   **`RulesEngine` Integration**: Fully hook up the real-time activity feed to the Rules Engine to allow/deny apps in real-time.

### **Phase 2: Interventions (The "Coach")**
*   **Level 1 (Gentle)**: Send Windows Toast Notification ("Are you still working on X?").
*   **Level 2 (Invasive)**: Show a semi-transparent Fullscreen Overlay ("Get back to work").
*   **Level 3 (Strict)**: Minimize the distracting app immediately or Lock the screen.

### **Phase 3: AI Intelligence**
*   **Smart Exceptions**: If you open a new site, ask AI "Is this relevant to [Current Goal]?" and allow/deny dynamically.
*   **Context Awareness**: Use screen content (OCR) for deeper understanding (Optional/Future).

### **Phase 4: Polish**
*   **Dashboard Stats**: Show a graph of "Focus vs. Drift" time.
*   **User Walkthrough**: Verify the end-to-end flow is smooth.
