# ApplyWise

AI job-application assistant built with .NET 10 and Azure. (Full description coming soon.)

## Run locally

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) – running, with at least 6 GB RAM
  (Settings → Resources, or `memory=8GB` under `[wsl2]` in `%UserProfile%\.wslconfig` on WSL 2)

### Start everything
```bash
git clone https://github.com/udayagamage/ApplyWise.git
cd ApplyWise
dotnet run --project ApplyWise/ApplyWise.AppHost
```

Open the **Aspire dashboard** link printed in the console. Every resource should show as *Running/Healthy*.

### What starts
| Resource | Purpose |
|---|---|
| SQL Server | App database (`applywise`) |
| Azurite | Blob storage emulator for CV uploads |
| Service Bus emulator | `cv-processing`, `job-analysis` queues and `application-events` topic |
| Api | Minimal API |
| Web | Blazor front end |
| Worker | Background message processing |

> The first run is slow while Docker downloads the images.
> Azure OpenAI and AI Search have no local emulator; you'll need real Azure resources for those (setup to follow).

### Troubleshooting
- **"Docker is not running"**: start Docker Desktop, then run the command again.
- **Service Bus emulator unhealthy**: give Docker more RAM and wait 1–2 minutes on the first start.
- **Port already in use**: stop the other app, or restart the AppHost.
