# Choice Flame Communications Network

A Windows office-network application for Choice Flame Communications, designed for a small private LAN with both Ethernet and Wi-Fi computers.

## Features
- Red, blue and gold Choice Flame dashboard
- Working Files → Submitted Files → Final Files workflow
- Director, Editor and News Sourcing roles
- SignalR office chat with presence, broadcast, role and private-message server support
- Automatic chat reconnection
- Configurable server root
- Windows network diagnostics
- Windows Firewall/File Sharing setup service
- SMB share setup foundation
- Windows build and self-contained publish workflow

## Office workflow
1. Staff prepare material in **Working Files**.
2. A selected item is moved to **Submitted Files** for review.
3. The Director approves it to **Final Files**.
4. Final Files are intended to be read-only for ordinary staff when production ACLs are applied.

## Network
The server hosts the file folders and the chat service on TCP 5077. Wired and wireless PCs can communicate when they are on the same trusted LAN and the router does not isolate Wi-Fi clients.

For reliable operation, give the server PC a stable LAN IP using a DHCP reservation in the office router.

## Build
GitHub Actions builds the Windows client and server on every push to `main`. Open the repository's **Actions** tab, select the latest **Windows Build**, and download the artifact after a successful run.

## Important setup note
Server configuration and SMB/NTFS changes require Windows Administrator privileges. Do not expose port 5077 or SMB shares directly to the public internet.

## Remaining production hardening
Before organization-wide deployment, app-level authentication/password storage, persistent message storage, detailed NTFS group provisioning, and signed installer packaging should be completed and tested on the actual office Windows environment.
