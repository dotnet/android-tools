// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
#if NET5_0_OR_GREATER
using System.Buffers;
#endif
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Provides Android SDK bootstrap and management capabilities using the <c>sdkmanager</c> CLI.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Downloads the Android command-line tools from the Xamarin Android manifest feed,
	/// extracts them to <c>cmdline-tools/&lt;version&gt;/</c>, then uses the included <c>sdkmanager</c>
	/// to install, uninstall, list, and update SDK packages.
	/// </para>
	/// <para>
	/// The manifest feed URL defaults to <c>https://aka.ms/AndroidManifestFeed/d18-0</c>
	/// but can be configured via the <see cref="ManifestFeedUrl"/> property.
	/// </para>
	/// </remarks>
	public class SdkManager : IDisposable
	{
		/// <summary>Default manifest feed URL (Xamarin/Microsoft).</summary>
		public const string DefaultManifestFeedUrl = "https://aka.ms/AndroidManifestFeed/d18-0";

		/// <summary>Google's official Android SDK repository manifest URL.</summary>
		public const string GoogleManifestFeedUrl = "https://dl.google.com/android/repository/repository2-3.xml";

		/// <summary>Buffer size for download operations (80 KB).</summary>
		const int DownloadBufferSize = 81920;

		readonly HttpClient httpClient = new HttpClient ();
		readonly Action<TraceLevel, string> logger;
		bool disposed;

		/// <summary>
		/// Gets or sets the manifest feed URL used to discover command-line tools.
		/// Defaults to <see cref="DefaultManifestFeedUrl"/>.
		/// </summary>
		public string ManifestFeedUrl { get; set; } = DefaultManifestFeedUrl;

		/// <summary>
		/// Gets or sets the manifest source. Changing this property updates <see cref="ManifestFeedUrl"/>.
		/// </summary>
		public SdkManifestSource ManifestSource
		{
			get => ManifestFeedUrl == GoogleManifestFeedUrl ? SdkManifestSource.Google : SdkManifestSource.Xamarin;
			set => ManifestFeedUrl = value == SdkManifestSource.Google ? GoogleManifestFeedUrl : DefaultManifestFeedUrl;
		}

		/// <summary>
		/// Gets or sets the Android SDK root path. Used to locate and invoke <c>sdkmanager</c>.
		/// </summary>
		public string? AndroidSdkPath { get; set; }

		/// <summary>
		/// Gets or sets the Java SDK (JDK) home path. Set as <c>JAVA_HOME</c> when invoking <c>sdkmanager</c>.
		/// </summary>
		public string? JavaSdkPath { get; set; }

		/// <summary>
		/// Creates a new <see cref="SdkManager"/> instance.
		/// </summary>
		/// <param name="logger">Optional logger callback. Defaults to <see cref="AndroidSdkInfo.DefaultConsoleLogger"/>.</param>
		public SdkManager (Action<TraceLevel, string>? logger = null)
		{
			this.logger = logger ?? AndroidSdkInfo.DefaultConsoleLogger;
		}

		/// <summary>
		/// Disposes the <see cref="SdkManager"/> and its owned <see cref="HttpClient"/>.
		/// </summary>
		public void Dispose ()
		{
			if (disposed)
				return;
			disposed = true;
			httpClient.Dispose ();
		}

		void ThrowIfDisposed ()
		{
			if (disposed)
				throw new ObjectDisposedException (nameof (SdkManager));
		}

		// --- Manifest Parsing ---

		/// <summary>
		/// Downloads and parses the Android manifest feed to discover available components.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A list of manifest components available for the current platform.</returns>
		public async Task<IReadOnlyList<SdkManifestComponent>> GetManifestComponentsAsync (CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed ();
			logger (TraceLevel.Info, $"Downloading manifest from {ManifestFeedUrl}...");
			// netstandard2.0 GetStringAsync has no CancellationToken overload; use GetAsync instead
			using var response = await httpClient.GetAsync (ManifestFeedUrl, cancellationToken).ConfigureAwait (false);
			response.EnsureSuccessStatusCode ();
			var xml = await response.Content.ReadAsStringAsync ().ConfigureAwait (false);
			return ParseManifest (xml);
		}

		/// <summary>
		/// Parses the Android manifest XML and returns components for the current platform.
		/// Uses XmlReader for better performance than XDocument/XElement.
		/// </summary>
		internal IReadOnlyList<SdkManifestComponent> ParseManifest (string xml)
		{
			var hostOs = GetManifestHostOs ();
			var hostArch = GetManifestHostArch ();
			var components = new List<SdkManifestComponent> ();

			using var stringReader = new StringReader (xml);
			using var reader = XmlReader.Create (stringReader, new XmlReaderSettings { IgnoreWhitespace = true });

			while (reader.Read ()) {
				if (reader.NodeType != XmlNodeType.Element)
					continue;

				// Skip root element
				if (reader.Depth == 0)
					continue;

				var elementName = reader.LocalName;
				var revision = reader.GetAttribute ("revision");
				if (string.IsNullOrEmpty (revision))
					continue;

				var component = new SdkManifestComponent {
					ElementName = elementName,
					Revision = revision!,
					Path = reader.GetAttribute ("path"),
					FilesystemPath = reader.GetAttribute ("filesystem-path"),
					Description = reader.GetAttribute ("description"),
					IsObsolete = string.Equals (reader.GetAttribute ("obsolete"), "True", StringComparison.OrdinalIgnoreCase),
				};

				// Read child elements to find matching URL
				if (!reader.IsEmptyElement) {
					var componentDepth = reader.Depth;
					while (reader.Read () && reader.Depth > componentDepth) {
						if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "urls") {
							var urlsDepth = reader.Depth;
							while (reader.Read () && reader.Depth > urlsDepth) {
								if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "url") {
									var urlHostOs = reader.GetAttribute ("host-os");
									var urlHostArch = reader.GetAttribute ("host-arch");

									if (!MatchesPlatform (urlHostOs, hostOs))
										continue;

									if (!string.IsNullOrEmpty (urlHostArch) && !string.Equals (urlHostArch, hostArch, StringComparison.OrdinalIgnoreCase))
										continue;

									component.ChecksumType = reader.GetAttribute ("checksum-type");
									component.Checksum = reader.GetAttribute ("checksum");

									var sizeStr = reader.GetAttribute ("size");
									if (long.TryParse (sizeStr, out var size))
										component.Size = size;

									// Read the URL text content
									component.DownloadUrl = reader.ReadElementContentAsString ()?.Trim ();
									break;
								}
							}
						}
					}
				}

				if (!string.IsNullOrEmpty (component.DownloadUrl))
					components.Add (component);
			}

			logger (TraceLevel.Verbose, $"Parsed {components.Count} components from manifest.");
			return components.AsReadOnly ();
		}

		static bool MatchesPlatform (string? urlHostOs, string hostOs)
		{
			if (string.IsNullOrEmpty (urlHostOs))
				return true; // No filter means any platform
			return string.Equals (urlHostOs, hostOs, StringComparison.OrdinalIgnoreCase);
		}

		static string GetManifestHostOs ()
		{
			if (OS.IsWindows) return "windows";
			if (OS.IsMac) return "macosx";
			if (OS.IsLinux) return "linux";
			throw new PlatformNotSupportedException ($"Unsupported operating system for Android SDK manifest.");
		}

		static string GetManifestHostArch ()
		{
			var arch = RuntimeInformation.OSArchitecture;
			switch (arch) {
				case Architecture.Arm64:
					return "aarch64";
				case Architecture.X64:
					return "x64";
				case Architecture.X86:
					return "x86";
				default:
					throw new PlatformNotSupportedException ($"Unsupported architecture '{arch}' for Android SDK manifest.");
			}
		}

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

					// Move to latest, with rollback on failure and cross-device fallback
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
							// Cross-device fallback: copy recursively then delete source
							CopyDirectoryRecursive (extractedDir, versionDir);
							Directory.Delete (extractedDir, recursive: true);
						}
						logger (TraceLevel.Info, $"Extracted cmdline-tools to '{versionDir}'.");
					}
					catch (Exception ex) {
						logger (TraceLevel.Error, $"Failed to install cmdline-tools to '{versionDir}': {ex.Message}");
						// Attempt to restore previous installation from backup
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
						if (!string.IsNullOrEmpty (backupPath) && Directory.Exists (backupPath)) {
							try { Directory.Delete (backupPath, recursive: true); }
							catch (Exception ex) {
								logger (TraceLevel.Warning, $"Could not clean up old cmdline-tools at '{backupPath}': {ex.Message}");
							}
						}
					}
				}
				finally {
					if (Directory.Exists (tempExtractDir)) {
						try { Directory.Delete (tempExtractDir, recursive: true); }
						catch (Exception ex) { logger (TraceLevel.Verbose, $"Failed to clean up temp extract dir: {ex.Message}"); }
					}
				}

				// Set executable permissions on Unix
				if (!OS.IsWindows) {
					SetExecutablePermissions (versionDir, logger);
				}

				// Update AndroidSdkPath for subsequent sdkmanager calls
				AndroidSdkPath = targetPath;

				progress?.Report (new SdkBootstrapProgress { Phase = SdkBootstrapPhase.Complete, PercentComplete = 100, Message = "Bootstrap complete." });
				logger (TraceLevel.Info, "Android SDK bootstrap complete.");
			}
			finally {
				if (File.Exists (tempArchivePath)) {
					try { File.Delete (tempArchivePath); }
					catch (Exception ex) { logger (TraceLevel.Verbose, $"Failed to clean up temp archive: {ex.Message}"); }
				}
			}
		}

		/// <summary>
		/// Resolves the path to the <c>sdkmanager</c> executable within the Android SDK.
		/// </summary>
		/// <returns>The full path to <c>sdkmanager</c>, or <c>null</c> if not found.</returns>
		public string? FindSdkManagerPath ()
		{
			if (string.IsNullOrEmpty (AndroidSdkPath))
				return null;

			var ext = OS.IsWindows ? ".bat" : string.Empty;
			var cmdlineToolsDir = Path.Combine (AndroidSdkPath, "cmdline-tools");

			if (Directory.Exists (cmdlineToolsDir)) {
				// Search versioned directories first (sorted descending), then "latest" for backward compatibility
				var searchDirs = new List<string> ();

				try {
					var versionedDirs = Directory.GetDirectories (cmdlineToolsDir)
						.Select (d => Path.GetFileName (d))
						.Where (n => n != "latest" && !string.IsNullOrEmpty (n))
						.OrderByDescending (n => {
							if (Version.TryParse (n, out var v))
								return v;
							return new Version (0, 0);
						})
						.ToList ();
					searchDirs.AddRange (versionedDirs);
				}
				catch (Exception ex) {
					logger (TraceLevel.Verbose, $"Error enumerating cmdline-tools directories: {ex.Message}");
				}

				// Add "latest" at the end for backward compatibility with existing installations
				searchDirs.Add ("latest");

				foreach (var dir in searchDirs) {
					var toolPath = Path.Combine (cmdlineToolsDir, dir, "bin", "sdkmanager" + ext);
					if (File.Exists (toolPath))
						return toolPath;
				}
			}

			// Legacy fallback: tools/bin/sdkmanager
			var legacyPath = Path.Combine (AndroidSdkPath, "tools", "bin", "sdkmanager" + ext);
			if (File.Exists (legacyPath))
				return legacyPath;

			return null;
		}

		/// <summary>
		/// Lists installed and available SDK packages using <c>sdkmanager --list</c>.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A tuple of (installed packages, available packages).</returns>
		public async Task<(IReadOnlyList<SdkPackage> Installed, IReadOnlyList<SdkPackage> Available)> ListAsync (CancellationToken cancellationToken = default)
		{
			var sdkManagerPath = FindSdkManagerPath ();
			if (sdkManagerPath is null)
				throw new InvalidOperationException ("sdkmanager not found. Run BootstrapAsync first to install command-line tools.");

			logger (TraceLevel.Info, "Running sdkmanager --list...");
			var (exitCode, stdout, stderr) = await RunSdkManagerAsync (sdkManagerPath, "--list", cancellationToken: cancellationToken).ConfigureAwait (false);

			if (exitCode != 0) {
				logger (TraceLevel.Error, $"sdkmanager --list failed (exit code {exitCode}): {stderr}");
				throw new InvalidOperationException ($"sdkmanager --list failed: {stderr}");
			}

			return ParseSdkManagerList (stdout);
		}

		/// <summary>
		/// Installs SDK packages using <c>sdkmanager</c>.
		/// </summary>
		/// <param name="packages">Package paths to install (e.g. "platform-tools", "platforms;android-35").</param>
		/// <param name="acceptLicenses">If <c>true</c>, automatically accepts licenses during installation.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task InstallAsync (IEnumerable<string> packages, bool acceptLicenses = true, CancellationToken cancellationToken = default)
		{
			if (packages is null || !packages.Any ())
				throw new ArgumentException ("At least one package must be specified.", nameof (packages));

			var sdkManagerPath = FindSdkManagerPath ();
			if (sdkManagerPath is null)
				throw new InvalidOperationException ("sdkmanager not found. Run BootstrapAsync first.");

			var packageList = string.Join (" ", packages.Select (p => $"\"{p}\""));
			logger (TraceLevel.Info, $"Installing packages: {packageList}");

			var (exitCode, stdout, stderr) = await RunSdkManagerAsync (
				sdkManagerPath, packageList, acceptLicenses, cancellationToken).ConfigureAwait (false);

			if (exitCode != 0) {
				logger (TraceLevel.Error, $"Package installation failed (exit code {exitCode}): {stderr}");
				throw new InvalidOperationException ($"Failed to install packages: {stderr}");
			}

			logger (TraceLevel.Info, "Packages installed successfully.");
		}

		/// <summary>
		/// Uninstalls SDK packages using <c>sdkmanager --uninstall</c>.
		/// </summary>
		/// <param name="packages">Package paths to uninstall.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task UninstallAsync (IEnumerable<string> packages, CancellationToken cancellationToken = default)
		{
			if (packages is null || !packages.Any ())
				throw new ArgumentException ("At least one package must be specified.", nameof (packages));

			var sdkManagerPath = FindSdkManagerPath ();
			if (sdkManagerPath is null)
				throw new InvalidOperationException ("sdkmanager not found. Run BootstrapAsync first.");

			var packageList = string.Join (" ", packages.Select (p => $"\"{p}\""));
			logger (TraceLevel.Info, $"Uninstalling packages: {packageList}");

			var (exitCode, stdout, stderr) = await RunSdkManagerAsync (
				sdkManagerPath, $"--uninstall {packageList}", cancellationToken: cancellationToken).ConfigureAwait (false);

			if (exitCode != 0) {
				logger (TraceLevel.Error, $"Package uninstall failed (exit code {exitCode}): {stderr}");
				throw new InvalidOperationException ($"Failed to uninstall packages: {stderr}");
			}

			logger (TraceLevel.Info, "Packages uninstalled successfully.");
		}

		/// <summary>
		/// Updates all installed SDK packages using <c>sdkmanager --update</c>.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task UpdateAsync (CancellationToken cancellationToken = default)
		{
			var sdkManagerPath = FindSdkManagerPath ();
			if (sdkManagerPath is null)
				throw new InvalidOperationException ("sdkmanager not found. Run BootstrapAsync first.");

			logger (TraceLevel.Info, "Updating all installed packages...");
			var (exitCode, stdout, stderr) = await RunSdkManagerAsync (
				sdkManagerPath, "--update", acceptLicenses: true, cancellationToken: cancellationToken).ConfigureAwait (false);

			if (exitCode != 0) {
				logger (TraceLevel.Error, $"Package update failed (exit code {exitCode}): {stderr}");
				throw new InvalidOperationException ($"Failed to update packages: {stderr}");
			}

			logger (TraceLevel.Info, "All packages updated successfully.");
		}

		/// <summary>
		/// Accepts all SDK licenses using <c>sdkmanager --licenses</c>.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task AcceptLicensesAsync (CancellationToken cancellationToken = default)
		{
			var sdkManagerPath = FindSdkManagerPath ();
			if (sdkManagerPath is null)
				throw new InvalidOperationException ("sdkmanager not found. Run BootstrapAsync first.");

			logger (TraceLevel.Info, "Accepting SDK licenses...");
			var (exitCode, stdout, stderr) = await RunSdkManagerAsync (
				sdkManagerPath, "--licenses", acceptLicenses: true, cancellationToken: cancellationToken).ConfigureAwait (false);

			// License acceptance may return non-zero when licenses are already accepted
			logger (TraceLevel.Info, "License acceptance complete.");
		}

		/// <summary>
		/// Gets pending licenses that need to be accepted, along with their full text.
		/// This allows IDEs and CLI tools to present licenses to the user before accepting.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A list of pending licenses with their ID and full text content.</returns>
		public async Task<IReadOnlyList<SdkLicense>> GetPendingLicensesAsync (CancellationToken cancellationToken = default)
		{
			var sdkManagerPath = FindSdkManagerPath ();
			if (sdkManagerPath is null)
				throw new InvalidOperationException ("sdkmanager not found. Run BootstrapAsync first.");

			logger (TraceLevel.Verbose, "Checking for pending licenses...");

			// Run --licenses without auto-accept to get the license text
			var psi = new ProcessStartInfo {
				FileName = sdkManagerPath,
				Arguments = "--licenses",
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = true,
			};

			ConfigureEnvironment (psi);

			using var stdout = new StringWriter ();
			using var stderr = new StringWriter ();

			// Send 'n' to decline all licenses so we just get the text
			Action<Process> onStarted = process => {
				Task.Run (async () => {
					try {
						await Task.Delay (500, cancellationToken).ConfigureAwait (false);
						while (!process.HasExited && !cancellationToken.IsCancellationRequested) {
							process.StandardInput.WriteLine ("n");
							await Task.Delay (200, cancellationToken).ConfigureAwait (false);
						}
					}
					catch (Exception ex) {
						// Process may have exited - expected behavior when process completes
						logger (TraceLevel.Verbose, $"License check loop ended: {ex.GetType ().Name}");
					}
				}, cancellationToken);
			};

			try {
				await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken, onStarted).ConfigureAwait (false);
			}
			catch (OperationCanceledException) {
				throw;
			}
			catch {
				// sdkmanager may exit with non-zero when declining licenses - that's expected
			}

			return ParseLicenseOutput (stdout.ToString ());
		}

		/// <summary>
		/// Accepts specific licenses by ID.
		/// </summary>
		/// <param name="licenseIds">The license IDs to accept (e.g., "android-sdk-license").</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task AcceptLicensesAsync (IEnumerable<string> licenseIds, CancellationToken cancellationToken = default)
		{
			if (licenseIds is null || !licenseIds.Any ())
				return;

			var sdkManagerPath = FindSdkManagerPath ();
			if (sdkManagerPath is null)
				throw new InvalidOperationException ("sdkmanager not found. Run BootstrapAsync first.");

			// Accept licenses by writing the hash to the licenses directory
			var licensesDir = Path.Combine (AndroidSdkPath!, "licenses");
			Directory.CreateDirectory (licensesDir);

			// Get pending licenses to find their hashes
			var pendingLicenses = await GetPendingLicensesAsync (cancellationToken).ConfigureAwait (false);
			var licenseIdSet = new HashSet<string> (licenseIds, StringComparer.OrdinalIgnoreCase);

			foreach (var license in pendingLicenses) {
				if (licenseIdSet.Contains (license.Id)) {
					var licensePath = Path.Combine (licensesDir, license.Id);
					// Compute hash of license text and write it
					var hash = ComputeLicenseHash (license.Text);
					File.WriteAllText (licensePath, $"\n{hash}");
					logger (TraceLevel.Info, $"Accepted license: {license.Id}");
				}
			}
		}

		/// <summary>
		/// Parses the output of <c>sdkmanager --licenses</c> to extract license information.
		/// </summary>
		internal static IReadOnlyList<SdkLicense> ParseLicenseOutput (string output)
		{
			var licenses = new List<SdkLicense> ();
			var lines = output.Split (new[] { '\n' }, StringSplitOptions.None);

			string? currentLicenseId = null;
			var currentLicenseText = new StringBuilder ();
			bool inLicenseText = false;

			foreach (var rawLine in lines) {
				var line = rawLine.TrimEnd ('\r');

				// License header: "License android-sdk-license:"
				if (line.StartsWith ("License ", StringComparison.OrdinalIgnoreCase) && line.TrimEnd ().EndsWith (":")) {
					// Save previous license if any
					if (currentLicenseId is not null && currentLicenseText.Length > 0) {
						licenses.Add (new SdkLicense {
							Id = currentLicenseId,
							Text = currentLicenseText.ToString ().Trim ()
						});
					}

					var trimmedLine = line.TrimEnd ();
					currentLicenseId = trimmedLine.Substring (8, trimmedLine.Length - 9).Trim ();
					currentLicenseText.Clear ();
					inLicenseText = true;
					continue;
				}

				// End of license text when we see the accept prompt
				if (line.Contains ("Accept?") || line.Contains ("(y/N)")) {
					if (currentLicenseId is not null && currentLicenseText.Length > 0) {
						licenses.Add (new SdkLicense {
							Id = currentLicenseId,
							Text = currentLicenseText.ToString ().Trim ()
						});
					}
					currentLicenseId = null;
					currentLicenseText.Clear ();
					inLicenseText = false;
					continue;
				}

				// Accumulate license text
				if (inLicenseText && currentLicenseId is not null) {
					// Skip separator lines
					if (!line.TrimStart ().StartsWith ("-------", StringComparison.Ordinal)) {
						currentLicenseText.AppendLine (line);
					}
				}
			}

			// Add last license if not yet added
			if (currentLicenseId is not null && currentLicenseText.Length > 0) {
				licenses.Add (new SdkLicense {
					Id = currentLicenseId,
					Text = currentLicenseText.ToString ().Trim ()
				});
			}

			return licenses.AsReadOnly ();
		}

		static string ComputeLicenseHash (string licenseText)
		{
			// Android SDK uses SHA-1 hash of the license text
			using var sha1 = SHA1.Create ();
			var bytes = Encoding.UTF8.GetBytes (licenseText.Replace ("\r\n", "\n").Trim ());
			var hash = sha1.ComputeHash (bytes);
			return BitConverter.ToString (hash).Replace ("-", "").ToLowerInvariant ();
		}

		/// <summary>
		/// Checks whether SDK licenses have been accepted by checking the licenses directory.
		/// </summary>
		/// <returns><c>true</c> if at least one license file exists; otherwise <c>false</c>.</returns>
		public bool AreLicensesAccepted ()
		{
			if (string.IsNullOrEmpty (AndroidSdkPath))
				return false;

			var licensesPath = Path.Combine (AndroidSdkPath, "licenses");
			if (!Directory.Exists (licensesPath))
				return false;

			return Directory.GetFiles (licensesPath).Length > 0;
		}

		/// <summary>
		/// Parses <c>sdkmanager --list</c> output into installed and available packages.
		/// </summary>
		internal static (IReadOnlyList<SdkPackage> Installed, IReadOnlyList<SdkPackage> Available) ParseSdkManagerList (string output)
		{
			var installed = new List<SdkPackage> ();
			var available = new List<SdkPackage> ();
			string? currentSection = null;

			var lines = output.Split (new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

			foreach (var line in lines) {
				var trimmed = line.Trim ();

				if (trimmed.IndexOf ("Installed packages:", StringComparison.Ordinal) >= 0) {
					currentSection = "installed";
					continue;
				}
				if (trimmed.IndexOf ("Available Packages:", StringComparison.Ordinal) >= 0) {
					currentSection = "available";
					continue;
				}
				if (trimmed.IndexOf ("Available Updates:", StringComparison.Ordinal) >= 0) {
					currentSection = null;
					continue;
				}

				if (currentSection is null || string.IsNullOrWhiteSpace (trimmed))
					continue;

				// Skip header and separator lines
				if (trimmed.StartsWith ("Path", StringComparison.Ordinal) || trimmed.StartsWith ("---", StringComparison.Ordinal))
					continue;

				var parts = trimmed.Split (new[] { '|' });
				if (parts.Length < 2)
					continue;

				var pkg = new SdkPackage {
					Path = parts[0].Trim (),
					Version = parts.Length > 1 ? parts[1].Trim () : null,
					Description = parts.Length > 2 ? parts[2].Trim () : null,
					IsInstalled = currentSection == "installed"
				};

				if (string.IsNullOrEmpty (pkg.Path))
					continue;

				if (currentSection == "installed")
					installed.Add (pkg);
				else
					available.Add (pkg);
			}

			return (installed.AsReadOnly (), available.AsReadOnly ());
		}

		async Task<(int ExitCode, string Stdout, string Stderr)> RunSdkManagerAsync (
			string sdkManagerPath, string arguments, bool acceptLicenses = false, CancellationToken cancellationToken = default)
		{
			var psi = new ProcessStartInfo {
				FileName = sdkManagerPath,
				Arguments = arguments,
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = acceptLicenses,
			};

			ConfigureEnvironment (psi);

			using var stdout = new StringWriter ();
			using var stderr = new StringWriter ();

			Action<Process>? onStarted = null;
			if (acceptLicenses) {
				onStarted = process => {
					// Feed "y\n" continuously for license prompts
					Task.Run (async () => {
						try {
							while (!process.HasExited && !cancellationToken.IsCancellationRequested) {
								process.StandardInput.WriteLine ("y");
								await Task.Delay (500, cancellationToken).ConfigureAwait (false);
							}
						}
						catch (Exception ex) {
							// Process may have exited or cancellation requested - expected behavior
							logger (TraceLevel.Verbose, $"Auto-accept loop ended: {ex.GetType ().Name}");
						}
					}, cancellationToken);
				};
			}

			logger (TraceLevel.Verbose, $"Running: {sdkManagerPath} {arguments}");
			int exitCode;
			try {
				exitCode = await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken, onStarted).ConfigureAwait (false);
			}
			catch (OperationCanceledException) {
				throw;
			}
			catch (Exception ex) {
				logger (TraceLevel.Error, $"Failed to run sdkmanager: {ex.Message}");
				logger (TraceLevel.Verbose, ex.ToString ());
				throw;
			}

			var stdoutStr = stdout.ToString ();
			var stderrStr = stderr.ToString ();

			if (exitCode != 0) {
				logger (TraceLevel.Warning, $"sdkmanager exited with code {exitCode}");
				logger (TraceLevel.Verbose, $"stdout: {stdoutStr}");
				logger (TraceLevel.Verbose, $"stderr: {stderrStr}");
			}

			return (exitCode, stdoutStr, stderrStr);
		}

		async Task DownloadFileAsync (string url, string destinationPath, long expectedSize, IProgress<SdkBootstrapProgress>? progress, CancellationToken cancellationToken)
		{
			logger (TraceLevel.Info, $"Downloading {url}...");

			using var response = await httpClient.GetAsync (url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait (false);
			response.EnsureSuccessStatusCode ();

			var totalSize = response.Content.Headers.ContentLength ?? expectedSize;

			// In netstandard2.0, ReadAsStreamAsync() has no CancellationToken overload.
			// Register to dispose the response on cancellation so the stream read will abort.
			cancellationToken.ThrowIfCancellationRequested ();
			using var registration = cancellationToken.Register (() => response.Dispose ());
			using var stream = await response.Content.ReadAsStreamAsync ().ConfigureAwait (false);
			using var fileStream = File.Create (destinationPath);

#if NET5_0_OR_GREATER
			// Use ArrayPool for buffer to reduce allocations (requires System.Buffers)
			var buffer = ArrayPool<byte>.Shared.Rent (DownloadBufferSize);
			try {
#else
			var buffer = new byte[DownloadBufferSize];
			{
#endif
				long totalRead = 0;
				int bytesRead;

				while ((bytesRead = await stream.ReadAsync (buffer, 0, buffer.Length, cancellationToken).ConfigureAwait (false)) > 0) {
					await fileStream.WriteAsync (buffer, 0, bytesRead, cancellationToken).ConfigureAwait (false);
					totalRead += bytesRead;

					if (totalSize > 0 && progress is not null) {
						var percent = (int) ((totalRead * 100) / totalSize);
						progress.Report (new SdkBootstrapProgress {
							Phase = SdkBootstrapPhase.Downloading,
							PercentComplete = percent,
							Message = $"Downloading... {percent}% ({totalRead / (1024 * 1024)}MB / {totalSize / (1024 * 1024)}MB)"
						});
					}
				}

				logger (TraceLevel.Info, $"Downloaded {totalRead} bytes to {destinationPath}.");
			}
#if NET5_0_OR_GREATER
			finally {
				ArrayPool<byte>.Shared.Return (buffer);
			}
#endif
		}

		bool VerifyChecksum (string filePath, string expectedChecksum, string? checksumType)
		{
			// Validate checksumType - only SHA1 is currently supported
			var type = checksumType ?? "sha1";
			if (!string.Equals (type, "sha1", StringComparison.OrdinalIgnoreCase)) {
				throw new NotSupportedException ($"Unsupported checksum type: '{checksumType}'. Only 'sha1' is currently supported.");
			}

			logger (TraceLevel.Verbose, $"Verifying {type} checksum for {filePath}...");

			using var stream = File.OpenRead (filePath);
			using var hasher = SHA1.Create ();
			var hashBytes = hasher.ComputeHash (stream);
			var actualChecksum = BitConverter.ToString (hashBytes).Replace ("-", "");

			var match = string.Equals (actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);
			if (!match) {
				logger (TraceLevel.Error, $"Checksum mismatch: expected={expectedChecksum}, actual={actualChecksum}");
			}
			return match;
		}

		static void SetExecutablePermissions (string directory, Action<TraceLevel, string> logger)
		{
			// Make sdkmanager and other binaries executable on Unix
			var binDir = Path.Combine (directory, "bin");
			if (Directory.Exists (binDir)) {
				foreach (var file in Directory.GetFiles (binDir)) {
					// Use p/invoke chmod for efficiency (avoid spawning processes)
					if (!Chmod (file, 0x1ED)) { // 0755 octal = 0x1ED
						// Fallback to chmod process if p/invoke fails
						try {
							var psi = new ProcessStartInfo ("chmod", $"+x \"{file}\"") {
								CreateNoWindow = true,
								UseShellExecute = false,
							};
							var process = Process.Start (psi);
							process?.WaitForExit ();
							if (process is null || process.ExitCode != 0) {
								throw new InvalidOperationException ($"chmod failed for '{file}' with exit code {process?.ExitCode ?? -1}");
							}
						}
						catch (Exception ex) {
							// Let the exception propagate - sdkmanager won't work without executable permissions
							logger (TraceLevel.Error, $"Failed to set executable permission on '{file}': {ex.Message}");
							throw new InvalidOperationException ($"Failed to set executable permissions on '{file}'. The sdkmanager will not be usable.", ex);
						}
					}
				}
			}
		}

		/// <summary>
		/// Sets file permissions on Unix using libc chmod.
		/// </summary>
		/// <param name="path">File path.</param>
		/// <param name="mode">Permission mode (e.g., 0x1ED for 0755 octal).</param>
		/// <returns>True if successful, false otherwise.</returns>
		static bool Chmod (string path, int mode)
		{
			try {
				return chmod (path, mode) == 0;
			}
			catch {
				// p/invoke failed (e.g., not on Unix) - caller will use fallback
				return false;
			}
		}

		[DllImport ("libc", SetLastError = true)]
		static extern int chmod (string pathname, int mode);

		/// <summary>
		/// Configures environment variables on a ProcessStartInfo for Android SDK tools.
		/// </summary>
		void ConfigureEnvironment (ProcessStartInfo psi)
		{
			if (!string.IsNullOrEmpty (AndroidSdkPath)) {
				psi.EnvironmentVariables[EnvironmentVariableNames.AndroidHome] = AndroidSdkPath;
				// Note: ANDROID_SDK_ROOT is deprecated per https://developer.android.com/tools/variables#envar
				// Only set ANDROID_HOME
			}
			if (!string.IsNullOrEmpty (JavaSdkPath)) {
				psi.EnvironmentVariables[EnvironmentVariableNames.JavaHome] = JavaSdkPath;
			}
		}

		static void CopyDirectoryRecursive (string sourceDir, string destinationDir)
		{
			if (!Directory.Exists (destinationDir))
				Directory.CreateDirectory (destinationDir);

			foreach (var file in Directory.GetFiles (sourceDir)) {
				var destFile = Path.Combine (destinationDir, Path.GetFileName (file));
				File.Copy (file, destFile, overwrite: true);
			}

			foreach (var subDir in Directory.GetDirectories (sourceDir)) {
				var destSubDir = Path.Combine (destinationDir, Path.GetFileName (subDir));
				CopyDirectoryRecursive (subDir, destSubDir);
			}
		}

}
}

