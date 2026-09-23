# Office Network Manager

Windows desktop software for configuring and managing a small-office file server and LAN chat.

## Planned roles
- Director — full access and administration
- Editor — working/submitted files, read-only final files
- News Sourcing — working files and submission access, read-only final files

## Architecture
- **OfficeNetwork.Server** — ASP.NET Core .NET 10 server, SignalR chat hub, API and server services.
- **OfficeNetwork.Client** — .NET 10 WPF Windows desktop client.
- **OfficeNetwork.Shared** — shared models/contracts.
- **OfficeNetwork.Windows** — Windows file sharing, SMB and NTFS configuration services.

The Server PC is the central hub. Client PCs can connect through Ethernet or Wi-Fi as long as they are on the same trusted office LAN.

## Security
This application is intended for a trusted private office network. File access will be enforced with Windows/NTFS and SMB permissions rather than UI hiding alone.

## Status
Initial project foundation under active development.
