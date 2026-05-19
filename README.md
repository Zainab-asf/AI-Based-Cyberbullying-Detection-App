# 🛡️ KidSafe — AI Cyberbullying Detection App

KidSafe is an AI-powered child-safe chat platform that detects and moderates cyberbullying in real time using machine learning.

## Features
- 🔐 JWT Authentication
- 💬 Real-time chat with SignalR
- 🤖 AI toxicity detection
- 🚫 Message blocking & masking
- 👨‍👩‍👧 Parent & teacher alerts
- 🔔 Firebase push notifications
- 🏆 Reward & badge system
- 📊 Safety analytics dashboard

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor WebAssembly |
| Backend | ASP.NET Core 8 |
| AI Service | FastAPI + Python |
| Realtime | SignalR |
| Database | SQLite |
| Notifications | Firebase Cloud Messaging |

---

## Architecture

```text
Frontend (Blazor WASM)
        │
 ASP.NET Core Backend
        │
 FastAPI AI Service
        │
 ML Toxicity Classifier
