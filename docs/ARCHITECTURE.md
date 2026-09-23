# Architecture

## Server PC
The server hosts:
1. Windows SMB shares for office files.
2. ASP.NET Core server on TCP port 5077.
3. SignalR hub for real-time office chat and presence.
4. Configuration and audit data (persistence will be added in the next phase).

## Client PCs
Windows WPF clients connect to the server using the server PC's LAN address. Ethernet and Wi-Fi clients are treated identically as long as the router permits communication between clients.

## Default folders
- Working Files
- Submitted Files
- Final Files

## Default access model
| Role | Working | Submitted | Final |
| --- | --- | --- | --- |
| Director | Full | Full | Full |
| Editor | Read/Write | Read/Write | Read |
| News Sourcing | Read/Write | Read/Write | Read |

The production permission service will apply both SMB share permissions and NTFS ACLs.

## Chat
SignalR provides:
- online presence
- everyone/broadcast messages
- role/group messages
- private messages
- automatic reconnection

Message persistence, unread counts, attachments and Windows notifications are scheduled for the next phase.
