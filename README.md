# NeSud

Судоку для Windows (WPF) та браузера (ASP.NET Core). Спільні правила гри, SQLite, офлайн-режим Desktop, серверні щоденні ігри, профілі, рейтинги, турніри, друзі, винагороди та SignalR через WebSocket + MessagePack.

## Запуск

Потрібні .NET 10 SDK та Node.js (рекомендовано 22 або новіший).

```powershell
npm --prefix src/NeSud.Server install --ignore-scripts --no-audit --no-fund
npm --prefix src/NeSud.Server run prepare-web
dotnet build NeSud.slnx
dotnet run --project src/NeSud.Server -- --urls http://localhost:5080
```

Web: http://localhost:5080. Desktop в іншому терміналі:

```powershell
dotnet run --project src/NeSud
```

Для SMTP скопіюйте .env.example у .env та заповніть власні параметри. Не комітьте секрети. Детальна конфігурація та Docker описані в документації.

## Документація

[Зміст .docs](.docs/README.md)

[Правила та запуск](.docs/PROJECT.md)

[Архітектура](.docs/ARCHITECTURE.md)

[UI/UX](.docs/UI-UX.md)

[Приймання](.docs/VERIFICATION.md)
