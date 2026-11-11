# HanaMedia local background hosting

Run `Install-HanaMediaStartup.ps1 -RunAsUser "COMPUTER\User"` from an elevated Windows PowerShell after building Release. The installer refuses to overwrite an existing `HanaMedia-Web` task.

- Uses the existing Release executable and the specified Windows user's local configuration/user secrets. No passwords are copied into scripts.
- Hosts HTTP on port 5028 over both IPv4 and IPv6 (`--urls http://*:5028`). Stop any Visual Studio/debug instance using port 5028 before starting the background web. Allow TCP 5028 from the LAN in the host firewall. DNS A and AAAA records must point to this machine's reachable IPv4 and stable IPv6 addresses; use `http://hanamedia.internal:5028/Login`.
- Startup trigger: 30 seconds after Windows starts. A logon trigger provides another start opportunity. Duplicate task instances are ignored.
- Runs without a visible terminal. Uses S4U with limited privileges; network credentials are not available. Intended for this machine's local SQL Server, not remote Windows-authenticated resources.
- Restarts after process exit; three consecutive failed local `/Login` probes trigger a restart. Probes run approximately every 30 seconds, with a 10-second timeout. This checks the web process, not remote LAN reachability or authenticated database operations.
- Logs: `App_Data/hosting/` (Git-ignored). Periodically archive these logs; automatic retention is not configured.
- On AC power: closing the lid does nothing; idle sleep and hibernation are disabled. Battery settings are unchanged. Keep the laptop plugged in and ventilated.

## Maintenance

For restart without a UAC prompt each time, run `Install-HanaMediaRestart.ps1` once as Administrator. It registers the on-demand `HanaMedia-Web-Restart` task with the same limited S4U identity as the web. It does not disable UAC or store a password. Then run `schtasks /run /tn HanaMedia-Web-Restart` from a normal terminal. Verify a new web PID and HTTP response; inspect `schtasks /query /tn HanaMedia-Web-Restart /v /fo LIST` for completion. Remove this optional task separately when uninstalling hosting.

Use Task Scheduler to stop `HanaMedia-Web` before building/updating the Release executable; start the task again afterwards. Avoid running `dotnet run` simultaneously on port 5028. Stopping/disabling the task is also how to intentionally stop the watchdog; stopping only the web process causes a restart.

To remove autostart, stop and delete only `HanaMedia-Web` in Task Scheduler. Restore AC lid behavior to Sleep in Windows Power Options if required. Uninstalling the task does not delete the database or uploaded documents.

This does not reserve the DHCP address, change router isolation, open firewall rules, or provide database backups. A real reboot and a second LAN device are required to validate unattended startup and remote access respectively.

## Existing backup helper

`Backup-HanaMedia.ps1` is copied from the running local server. It targets `C:\HanaMediaBackups`, verifies the SQL backup and uploaded-file archive, and retains one completed set. It is not automatically installed by this source package. The transfer backup supplied separately was additionally restored into a verification database and compared across all table counts. Configure paths, permissions and retention deliberately before using this local helper on another server.
