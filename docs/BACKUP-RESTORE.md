# Choice Flame full backup and restore

## Daily backups

While the office server is running, it checks once an hour after an enabled Director has been set up. If no completed full archive is less than 24 hours old, it creates one containing:

- the SQLite database (accounts, assignments, attendance, calendar, filing, audit and durable notifications);
- all files beneath the configured office storage folder, including Working, Submitted, Final, Shared, assignment attachments, review notes and recovery history;
- a manifest with SHA-256 checksums to detect accidental corruption.

Archives are in `%ProgramData%\Choice Flame Communications Network\Archives`. An incomplete `.partial` archive is never offered for download. A failed daily backup is logged and retried at the next hourly check. No existing backup is automatically deleted. Monitor free disk space.

Small daily database-only backups continue independently, so a locked office document does not prevent the database backup. SQLite is backed up online. Office files are copied afterwards, so this is not a transaction spanning the database, all files and external SMB edits. For a coordinated recovery point, ask staff to save and close office files and pause submissions before creating a manual backup. Files locked for writing or changing while copied cause the backup to fail rather than silently omit them. Linked folders and files are rejected.

## Create and save a manual backup

1. Sign in as the Director and open **Settings → Full Office Backup**.
2. Select **Create Full Backup** and wait for completion.
3. Select the archive and choose **Save Selected Backup**. Save a copy on a different drive, in a location restricted to the Director/server administrator. The archive contains the account database, including protected password records.

The existing authenticated archive API remains available to Director and Admin accounts. Ordinary staff cannot create, list or download full archives. Windows database/archive folder permissions are restricted at server startup to the server operator, local Administrators and SYSTEM. Run the server under the same Windows account used for server setup, or use an administrator when changing the server operator.

## Restore a full backup

Restore replaces the office database and office files with their state at the selected backup date. Accounts and passwords become those from that backup. Recent files and account changes are retained in rollback copies, not merged into the restored snapshot.

1. Ask staff to sign out, save and close office files. Exit Choice Flame clients, including the server PC's tray app, so they do not restart the server during restore.
2. Stop **OfficeNetwork.Server.exe** on the server PC using Task Manager. Close any external program with the database or office files open.
3. Open PowerShell as the Windows server operator or administrator and change to the installed server folder:

   ```powershell
   Set-Location "$env:ProgramFiles\Choice Flame Communications Network\Server"
   ```

4. Run the following with the archive path you saved. `--confirm-restore` explicitly accepts restoring the selected snapshot:

   ```powershell
   .\OfficeNetwork.Server.exe --restore-archive "E:\Office Backups\office-YYYYMMDD-HHMMSS-ID.zip" --confirm-restore
   ```

   The command uses the server's currently configured office folder, preserving the location and existing matching Windows file/folder permissions. For a replacement server, explicitly choose a dedicated office folder:

   ```powershell
   .\OfficeNetwork.Server.exe --restore-archive "E:\Office Backups\office-YYYYMMDD-HHMMSS-ID.zip" --confirm-restore --restore-root "D:\Choice Flame Communications"
   ```

5. Wait for **Restore completed**. The command prints the locations of the previous office files and database. Keep these until you have verified the restored data. The command exits without starting the server.
6. Start the server normally, sign in using an account from the backup, and check accounts, file counts, assignments, filing, attendance and calendar entries. Test one staff computer before reconnecting the whole office. If you changed the office folder or server PC, apply the normal Windows sharing/firewall setup and test permissions first.

The command validates paths, SQLite integrity, an enabled Director account and manifest checksums before replacing current data. Legacy archives without manifests are accepted after path and database checks. Archives over 100 GB uncompressed or 100,000 entries are refused. The archive's recorded original root does not select the restore destination. Only the current configured root or your explicit `--restore-root` does.

The same exclusive lock protects the server and restore process; an updated server that is running prevents restore. Ordinary application errors during installation of the snapshot trigger rollback. A power failure or forced termination may interrupt directory moves. In that case server startup is deliberately blocked until the recovery journal is resolved.

## Interrupted restore recovery

With the server and clients stopped, find the `prepared` journal at:

`%ProgramData%\Choice Flame Communications Network\RestoreRollback\<restore-id>\restore-journal.json`

The journal identifies the office folder, its `before-restore` sibling and the rollback database folder. To return to the pre-restore state:

1. Preserve copies of both the current office folder and the `before-restore` folder. If the `before-restore` folder exists, move the partially restored office folder aside, then move `before-restore` back to the original office-folder path. If it does not exist, the original office-folder move had not completed; keep the existing original folder.
2. For each original database file actually present in the rollback folder (`choiceflame.db`, `choiceflame.db-wal`, `choiceflame.db-shm`, `choiceflame.db-journal`), keep any file at the live location aside and move the saved file back into `%ProgramData%\Choice Flame Communications Network`. Preserve the database and its available sidecars together. Files not present in the rollback folder may not yet have been moved from the live folder; do not delete those originals.
3. Restore the saved `server-storage.txt` when present. If you are uncertain which files were moved, keep everything and request recovery assistance before starting the server.
4. After confirming the original database and office folder are back, change the journal's `state` to `rolled-back`, then start the server. Never mark a journal completed simply to bypass the startup check.

Keep rollback folders outside the office SMB share and restrict access to the server operator/administrators. The application does not delete rollback copies automatically.

## Security behaviour in this update

- Account edits, password changes, disabling and deletion revoke active tokens and disconnect tracked SignalR connections.
- Concurrent logins cannot bypass the existing one-computer-per-account rule. Concurrent initial setup cannot create multiple first Directors.
- Sessions expire after three minutes without activity and after twelve hours even with activity. Expired clients return to sign-in.
- New/reset passwords require at least eight characters. Existing password records are not rehashed or reset; existing short passwords continue to authenticate.
- Invalid role values are rejected. Login requests receive an additional per-computer rate limit, alongside the existing failed-account attempt limit.
- Existing LAN-only checks remain. Connections still use HTTP on the trusted office LAN; encrypted transport and comprehensive SMB/NTFS provisioning remain separate deployment work.
