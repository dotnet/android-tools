// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
	public partial class SdkManager
	{
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
			ThrowIfDisposed ();
			var sdkManagerPath = FindSdkManagerPath () ?? throw new InvalidOperationException ("sdkmanager not found. Run BootstrapAsync first to install command-line tools.");
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
			ThrowIfDisposed ();
			if (packages is null || !packages.Any ())
				throw new ArgumentException ("At least one package must be specified.", nameof (packages));

			var sdkManagerPath = FindSdkManagerPath () ?? throw new InvalidOperationException ("sdkmanager not found. Run BootstrapAsync first.");
			
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
			ThrowIfDisposed ();
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
			ThrowIfDisposed ();
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
	}
}
