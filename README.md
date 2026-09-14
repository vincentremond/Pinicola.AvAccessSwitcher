# Pinicola.AvAccessSwitcher

A lightweight, background Windows System Tray application written in **pure functional F# (.NET 10)** that automatically manages your display topology when using an **AV Access KVM Switch / Docking Station**.

---

## 🎯 The Problem It Solves

KVM docks such as the **AV Access iDock C20** use hardware **EDID Emulation** so that Windows never sees the physical monitor disconnect when you switch to your secondary computer. This prevents Windows from resetting screen resolutions or scrambling your open application windows.

However, because Windows believes the monitor is always connected, it continues extending your desktop onto the "ghost" monitor while the KVM is switched to your other PC.

**`Pinicola.AvAccessSwitcher`** solves this by monitoring the KVM's USB controller status:

1. **When KVM is switched away (PC 2):** Automatically switches Windows display topology from **"Extend these displays"** to **"Show only on 1"** (Laptop screen only) and shows a Windows Toast Notification.
2. **When KVM is switched back (PC 1):** Automatically restores display topology to **"Extend these displays"** and alerts you via Toast Notification.

---

## 🖥️ Target Hardware

* **Primary Target:** [AV Access iDock C20](https://www.avaccess.com/) (KVM Switch Docking Station for Dual Laptops/PCs)
* **Compatibility:** Compatible with other **AV Access iDock** models (e.g., iDock C10, iDock B30, etc.) and similar KVM switches using USB PnP switching (`VID_1D6B&PID_B022` / `AV Access iDock`).

---

## ✨ Features

* **Pure Functional F#:** Built targeting **.NET 10.0** (`net10.0-windows`) using the `MailboxProcessor` actor model with **zero `mutable` variables**.
* **Headless System Tray Icon:** Runs in the background without any visible console or GUI window.
  * 🟢 **Green Status Dot:** KVM Active on this PC (Extended Displays).
  * 🟠 **Orange Status Dot:** KVM Switched Away (Show Only on 1 / Laptop Screen).
* **Tray Context Menu Controls:**
  * **Status Display:** Shows current real-time KVM state.
  * **Auto-Extend on Switch Back:** `[✓]` Option to automatically restore Extended mode upon reconnecting.
  * **Force: Show Only on 1:** Instant manual switch to Laptop primary screen.
  * **Force: Extend Displays:** Instant manual switch to Extended desktop.
  * **Check Status Now:** Manual status refresh trigger.
* **Native Windows Toast Notifications:** Alerts you instantly whenever the display topology is changed, displaying the application icon.
* **Serilog File Logging:** Logs state transitions, Win32 P/Invoke display topology events, and diagnostics to daily rolling log files.
* **Native Win32 Display API:** Uses P/Invoke calls to `SetDisplayConfig` (`SDC_TOPOLOGY_INTERNAL` & `SDC_TOPOLOGY_EXTEND`) with a `DisplaySwitch.exe` fallback mechanism.

---

## 📋 Logging & Diagnostics

Logging is powered by **Serilog** (`Serilog.Sinks.File`). Logs are automatically written to daily rolling files at:

```
%LOCALAPPDATA%\Pinicola.AvAccessSwitcher\logs\switcher-YYYYMMDD.log
```

---

## 🛠️ Building & Running

### Prerequisites
* [.NET 10 SDK](https://dotnet.microsoft.com/)

### Build
```powershell
# Build Release binary using Paket and MSBuild
dotnet build -c Release
```

### Run
Launch the compiled executable:

```powershell
& ".\Pinicola.AvAccessSwitcher\bin\Release\net10.0-windows\Pinicola.AvAccessSwitcher.exe"
```

---

## 📁 Project Structure

```
Pinicola.AvAccessSwitcher/
├── Pinicola.AvAccessSwitcher.slnx              # Solution file (.slnx format)
├── README.md                                   # Documentation
├── paket.dependencies                          # Paket dependency declaration
├── paket.lock                                  # Paket lock file
└── Pinicola.AvAccessSwitcher/                  # F# Source code folder
    ├── Pinicola.AvAccessSwitcher.fsproj        # Project file (.NET 10.0-windows)
    ├── paket.references                         # Project dependencies
    ├── icon.ico / icon.png                     # Application icon assets
    ├── NativeDisplay.fs                        # SetDisplayConfig P/Invoke & fallback
    ├── KvmDetector.fs                          # Functional AV Access PnP detector
    ├── TrayApplication.fs                      # MailboxProcessor state actor & NotifyIcon UI
    └── Program.fs                              # WinExe entry point & Serilog logger setup
```

---

## 📄 License

MIT License.
