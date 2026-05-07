# SMB Enterprise Transfer Engine - WinForms Explorer

Ung dung WinForms duoc cap nhat theo huong Explorer + Transfer Manager de test van hanh SMB engine o quy mo lon.

## Nang cap chinh

- Kien truc DI + service layer thay vi dat logic trong Form.
- Global exception handling cho UI thread, background task va unhandled exception.
- Serilog rolling file logs + UI log viewer (tab Logs).
- Theme manager ho tro Light/Dark toggle.
- Explorer layout gom:
  - Navigation tree lazy loading
  - Address bar UNC
  - Virtualized file list (phu hop thu muc lon)
- Transfer Manager theo queue:
  - Queue nhieu transfer
  - Hien thi Progress/Speed/ETA/State
  - Pause/Resume/Retry/Cancel

## Cau truc quan trong

- Program.cs: bootstrap DI, logging, exception handling.
- MainForm.cs: UI shell, khong chua SMB business logic.
- TransferViewModel.cs: connect/list/transfer/checksum async.
- Transfer/TransferQueueController.cs: queue + worker concurrency + state machine.
- Services/UiLogStore.cs: log stream de hien thi tren UI.
- Themes/AppThemeManager.cs: theming toan app.

## Chay ung dung

```bash
dotnet run --project samples/SmbEnterprise.WinFormsApp/SmbEnterprise.WinFormsApp.csproj
```

## Logging

- Rolling logs tai:
  - `%LOCALAPPDATA%/SmbEnterprise/logs/winforms-YYYYMMDD.log`
- Log realtime duoc render trong tab `Logs`.

## Kich ban test khuyen nghi

- Connect/disconnect lien tuc de test reconnect.
- Browse thu muc 10k-100k files.
- Queue nhieu transfer song song va theo doi speed/ETA.
- Pause/Resume/Retry/Cancel tren transfer dang chay.
- Kiem tra log khi co network fault.

## Nen tang

- .NET 8
- WinForms
- x64
