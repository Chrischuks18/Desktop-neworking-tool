# Choice Flame Communications Network

A Windows office-network application for Choice Flame Communications, designed for a small private LAN with both Ethernet and Wi-Fi computers.

## Features
- Red, blue and gold Choice Flame dashboard
- Working Files → Submitted Files → Final Files workflow
- Director, Editor and News Sourcing roles
- SignalR office chat with presence, broadcast, role and private-message server support
- News Production Tracker with stages, assignments, deadlines, search and updates
- Meeting Minutes with decisions, action items, search and updates
- Equipment Register with unique asset tags, custody, condition, search and updates
- Director/Admin record management with staff viewing access
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

## Backup, recovery and account security
The server creates daily full office archives with online SQLite backups and integrity manifests. The Director can create and download archives from Settings. Offline restore validates the archive, preserves rollback copies and refuses to run alongside the server. See [the backup and restore guide](docs/BACKUP-RESTORE.md) for the command and verification steps.

Accounts use PBKDF2 password records, revocable sessions, login throttling and server-enforced roles. Account edits disconnect old sessions. Existing account credentials and database records are preserved during upgrades.

## Remaining deployment checks
Test the installer on the actual server and one staff Windows PC before wider deployment. Encrypted LAN transport, complete SMB/NTFS group provisioning, persistent chat history and installer signing remain separate production work.
