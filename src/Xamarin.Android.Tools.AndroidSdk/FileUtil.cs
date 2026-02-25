using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Xamarin.Android.Tools
{
	class FileUtil
	{
		public static string GetTempFilenameForWrite (string fileName)
		{
			return Path.GetDirectoryName (fileName) + Path.DirectorySeparatorChar + ".#" + Path.GetFileName (fileName);
		}

		//From MonoDevelop.Core.FileService
		public static void SystemRename (string sourceFile, string destFile)
		{
			//FIXME: use the atomic System.IO.File.Replace on NTFS
			if (OS.IsWindows) {
				string? wtmp = null;
				if (File.Exists (destFile)) {
					do {
						wtmp = Path.Combine (Path.GetTempPath (), Guid.NewGuid ().ToString ());
					} while (File.Exists (wtmp));

					File.Move (destFile, wtmp);
				}
				try {
					File.Move (sourceFile, destFile);
				}
				catch {
					try {
						if (wtmp != null)
							File.Move (wtmp, destFile);
					}
					catch {
						wtmp = null;
					}
					throw;
				}
				finally {
					if (wtmp != null) {
						try {
							File.Delete (wtmp);
						}
						catch { }
					}
				}
			}
			else {
				rename (sourceFile, destFile);
			}
		}

		/// <summary>Deletes a file if it exists, logging any failure instead of throwing.</summary>
		public static void TryDeleteFile (string path, Action<TraceLevel, string> logger)
		{
			if (!File.Exists (path))
				return;
			try { File.Delete (path); }
			catch (Exception ex) { logger (TraceLevel.Warning, $"Could not delete '{path}': {ex.Message}"); }
		}

		/// <summary>Recursively deletes a directory if it exists, logging any failure instead of throwing.</summary>
		public static void TryDeleteDirectory (string path, string label, Action<TraceLevel, string> logger)
		{
			if (!Directory.Exists (path))
				return;
			try { Directory.Delete (path, recursive: true); }
			catch (Exception ex) { logger (TraceLevel.Warning, $"Could not clean up {label} at '{path}': {ex.Message}"); }
		}

		/// <summary>Moves a directory to the target path, backing up any existing directory and restoring on failure.</summary>
		public static void MoveWithRollback (string sourcePath, string targetPath, Action<TraceLevel, string> logger)
		{
			string? backupPath = null;
			if (Directory.Exists (targetPath)) {
				backupPath = targetPath + $".old-{Guid.NewGuid ():N}";
				Directory.Move (targetPath, backupPath);
			}

			var parentDir = Path.GetDirectoryName (targetPath);
			if (!string.IsNullOrEmpty (parentDir))
				Directory.CreateDirectory (parentDir);

			try {
				Directory.Move (sourcePath, targetPath);

				// Only delete backup after successful move
				if (backupPath is not null)
					TryDeleteDirectory (backupPath, "old backup", logger);
			}
			catch (Exception ex) {
				logger (TraceLevel.Error, $"Failed to move to '{targetPath}': {ex.Message}");
				if (backupPath is not null && Directory.Exists (backupPath)) {
					try {
						if (Directory.Exists (targetPath))
							Directory.Delete (targetPath, recursive: true);
						Directory.Move (backupPath, targetPath);
						logger (TraceLevel.Warning, $"Restored previous directory from backup '{backupPath}'.");
					}
					catch (Exception restoreEx) {
						logger (TraceLevel.Error, $"Failed to restore from backup: {restoreEx.Message}");
					}
				}
				throw;
			}
		}

		/// <summary>Checks if the target path is writable by testing write access on the nearest existing ancestor.</summary>
		public static bool IsTargetPathWritable (string targetPath, Action<TraceLevel, string> logger)
		{
			if (string.IsNullOrEmpty (targetPath))
				return false;

			string normalizedPath;
			try {
				normalizedPath = Path.GetFullPath (targetPath);
			}
			catch {
				normalizedPath = targetPath;
			}

			if (OS.IsWindows) {
				var programFiles = Environment.GetFolderPath (Environment.SpecialFolder.ProgramFiles);
				var programFilesX86 = Environment.GetFolderPath (Environment.SpecialFolder.ProgramFilesX86);
				if (IsUnderDirectory (normalizedPath, programFiles) || IsUnderDirectory (normalizedPath, programFilesX86)) {
					logger (TraceLevel.Warning, $"Target path '{targetPath}' is in Program Files which typically requires elevation.");
					return false;
				}
			}

			try {
				var testDir = normalizedPath;
				while (!string.IsNullOrEmpty (testDir) && !Directory.Exists (testDir))
					testDir = Path.GetDirectoryName (testDir);

				if (string.IsNullOrEmpty (testDir))
					return false;

				var testFile = Path.Combine (testDir, $".write-test-{Guid.NewGuid ()}");
				using (File.Create (testFile, 1, FileOptions.DeleteOnClose)) { }
				return true;
			}
			catch (Exception ex) {
				logger (TraceLevel.Warning, $"Target path '{targetPath}' is not writable: {ex.Message}");
				return false;
			}
		}

		/// <summary>Checks if a path is under a given directory.</summary>
		public static bool IsUnderDirectory (string path, string directory)
		{
			if (string.IsNullOrEmpty (directory) || string.IsNullOrEmpty (path))
				return false;
			if (path.Equals (directory, StringComparison.OrdinalIgnoreCase))
				return true;
			return path.StartsWith (directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		}

		// Returns .msi (Windows), .pkg (macOS), or null (Linux)
		public static string? GetInstallerExtension ()
		{
			if (OS.IsWindows) return ".msi";
			if (OS.IsMac) return ".pkg";
			return null;
		}

		public static string GetArchiveExtension ()
		{
			return OS.IsWindows ? ".zip" : ".tar.gz";
		}

		[DllImport ("libc", SetLastError=true)]
		static extern int rename (string old, string @new);
	}
}

