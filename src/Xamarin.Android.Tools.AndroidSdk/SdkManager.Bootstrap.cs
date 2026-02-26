// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
	public partial class SdkManager
	{
		/// <summary>
		/// Downloads command-line tools from the manifest feed and extracts them to
		/// <c>&lt;targetPath&gt;/cmdline-tools/&lt;version&gt;/</c>.
		/// </summary>
		/// <param name="targetPath">The Android SDK root directory to bootstrap.</param>
		/// <param name="progress">Optional progress reporter.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task BootstrapAsync (string targetPath, IProgress<SdkBootstrapProgress>? progress = null, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed ();
			if (string.IsNullOrEmpty (targetPath))
				throw new ArgumentNullException (nameof (targetPath));

			// Step 1: Read manifest
			progress?.Report (new SdkBootstrapProgress { Phase = SdkBootstrapPhase.ReadingManifest, Message = "Reading manifest feed..." });
			logger (TraceLevel.Info, $"Reading manifest from {ManifestFeedUrl}...");

			var components = await GetManifestComponentsAsync (cancellationToken).ConfigureAwait (false);
			var cmdlineTools = components
				.Where (c => string.Equals (c.ElementName, "cmdline-tools", StringComparison.OrdinalIgnoreCase) && !c.IsObsolete)
				.OrderByDescending (c => {
					if (Version.TryParse (c.Revision, out var v))
						return v;
					return new Version (0, 0);
				})
				.FirstOrDefault ();

			if (cmdlineTools is null || string.IsNullOrEmpty (cmdlineTools.DownloadUrl)) {
				throw new InvalidOperationException ("Could not find command-line tools in the Android manifest feed.");
			}

			logger (TraceLevel.Info, $"Found cmdline-tools {cmdlineTools.Revision}: {cmdlineTools.DownloadUrl}");

			// Step 2: Download
			var tempArchivePath = Path.Combine (Path.GetTempPath (), $"cmdline-tools-{Guid.NewGuid ()}.zip");
			try {
				progress?.Report (new SdkBootstrapProgress { Phase = SdkBootstrapPhase.Downloading, Message = $"Downloading cmdline-tools {cmdlineTools.Revision}..." });
				await DownloadFileAsync (cmdlineTools.DownloadUrl!, tempArchivePath, cmdlineTools.Size, progress, cancellationToken).ConfigureAwait (false);

				// Step 3: Verify checksum
				if (!string.IsNullOrEmpty (cmdlineTools.Checksum)) {
					progress?.Report (new SdkBootstrapProgress { Phase = SdkBootstrapPhase.Verifying, Message = "Verifying checksum..." });
					var checksumValid = VerifyChecksum (tempArchivePath, cmdlineTools.Checksum!, cmdlineTools.ChecksumType);
					if (!checksumValid) {
						throw new InvalidOperationException ($"Checksum verification failed for cmdline-tools archive. Expected: {cmdlineTools.Checksum}");
					}
					logger (TraceLevel.Info, "Checksum verification passed.");
				}
				else {
					logger (TraceLevel.Warning, "No checksum available for cmdline-tools; skipping verification.");
				}

				// Step 4: Extract to cmdline-tools/<version>/ (use version number, not "latest" symlink)
				progress?.Report (new SdkBootstrapProgress { Phase = SdkBootstrapPhase.Extracting, Message = "Extracting cmdline-tools..." });
				var cmdlineToolsDir = Path.Combine (targetPath, "cmdline-tools");
				var versionDir = Path.Combine (cmdlineToolsDir, cmdlineTools.Revision);

				Directory.CreateDirectory (cmdlineToolsDir);

				// Extract to temp dir first
				var tempExtractDir = Path.Combine (Path.GetTempPath (), $"cmdline-tools-extract-{Guid.NewGuid ()}");
				try {
					Directory.CreateDirectory (tempExtractDir);

					// Safe extraction to prevent zip slip (path traversal) attacks
					var fullExtractRoot = Path.GetFullPath (tempExtractDir);
					using (var archive = ZipFile.OpenRead (tempArchivePath)) {
						foreach (var entry in archive.Entries) {
							if (string.IsNullOrEmpty (entry.FullName))
								continue;

							var destinationPath = Path.GetFullPath (
								Path.Combine (fullExtractRoot, entry.FullName));

							if (!destinationPath.StartsWith (fullExtractRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
							    !string.Equals (destinationPath, fullExtractRoot, StringComparison.OrdinalIgnoreCase)) {
								throw new InvalidOperationException ($"Archive entry '{entry.FullName}' would extract outside target directory.");
							}

							if (entry.FullName.EndsWith ("/", StringComparison.Ordinal) || entry.FullName.EndsWith ("\\", StringComparison.Ordinal)) {
								Directory.CreateDirectory (destinationPath);
								continue;
							}

							var destDir = Path.GetDirectoryName (destinationPath);
							if (!string.IsNullOrEmpty (destDir))
								Directory.CreateDirectory (destDir);

							entry.ExtractToFile (destinationPath, overwrite: true);
						}
					}

					// The zip contains a top-level "cmdline-tools" directory
					var extractedDir = Path.Combine (tempExtractDir, "cmdline-tools");
					if (!Directory.Exists (extractedDir)) {
						// Try to find the single top-level directory
						var dirs = Directory.GetDirectories (tempExtractDir);
						if (dirs.Length == 1)
							extractedDir = dirs[0];
						else
							extractedDir = tempExtractDir;
					}

					// Move extracted files into versioned directory with rollback on failure and cross-device fallback
					string? backupPath = null;
					if (Directory.Exists (versionDir)) {
						backupPath = versionDir + $".old-{Guid.NewGuid ():N}";
						Directory.Move (versionDir, backupPath);
					}

					try {
						try {
							Directory.Move (extractedDir, versionDir);
						}
						catch (IOException) {
							FileUtil.CopyDirectoryRecursive (extractedDir, versionDir);
							Directory.Delete (extractedDir, recursive: true);
						}
						logger (TraceLevel.Info, $"Extracted cmdline-tools to '{versionDir}'.");
					}
					catch (Exception ex) {
						logger (TraceLevel.Error, $"Failed to install cmdline-tools to '{versionDir}': {ex.Message}");
						if (!string.IsNullOrEmpty (backupPath) && Directory.Exists (backupPath)) {
							try {
								if (Directory.Exists (versionDir))
									Directory.Delete (versionDir, recursive: true);
								Directory.Move (backupPath, versionDir);
								logger (TraceLevel.Warning, "Restored previous cmdline-tools from backup.");
							}
							catch (Exception restoreEx) {
								logger (TraceLevel.Error, $"Failed to restore backup: {restoreEx.Message}");
							}
						}
						throw;
					}
					finally {
						FileUtil.TryDeleteDirectory (backupPath!, "old cmdline-tools backup", logger);
					}
				}
				finally {
					FileUtil.TryDeleteDirectory (tempExtractDir, "temp extract dir", logger);
				}

				// Set executable permissions on Unix
				if (!OS.IsWindows) {
					FileUtil.SetExecutablePermissions (versionDir, logger);
				}

				// Update AndroidSdkPath for subsequent sdkmanager calls
				AndroidSdkPath = targetPath;

				progress?.Report (new SdkBootstrapProgress { Phase = SdkBootstrapPhase.Complete, PercentComplete = 100, Message = "Bootstrap complete." });
				logger (TraceLevel.Info, "Android SDK bootstrap complete.");
			}
			finally {
				FileUtil.TryDeleteFile (tempArchivePath, logger);
			}
		}
	}
}
