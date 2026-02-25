// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
#if NET5_0_OR_GREATER
using System.Buffers;
#endif
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
	public partial class SdkManager
	{
		async Task<(int ExitCode, string Stdout, string Stderr)> RunSdkManagerAsync (
			string sdkManagerPath, string arguments, bool acceptLicenses = false, CancellationToken cancellationToken = default)
		{
			// Check if the SDK path requires elevated permissions for write operations
			bool needsElevation = !string.IsNullOrEmpty (arguments)
				&& !arguments.TrimStart ().StartsWith ("--list", StringComparison.Ordinal)
				&& RequiresElevation ();

			if (needsElevation && OS.IsWindows) {
				logger (TraceLevel.Info, $"SDK path requires elevated permissions, running sdkmanager elevated...");
				return await RunSdkManagerElevatedAsync (sdkManagerPath, arguments, acceptLicenses, cancellationToken).ConfigureAwait (false);
			}

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

		/// <summary>
		/// Determines whether the current SDK path requires elevated (Administrator) permissions
		/// for write operations. This is typically the case when the SDK is installed in
		/// system-protected locations like <c>C:\Program Files</c>.
		/// </summary>
		bool RequiresElevation ()
		{
			if (ProcessUtils.IsElevated ())
				return false; // already elevated

			var sdkPath = AndroidSdkPath;
			if (string.IsNullOrEmpty (sdkPath))
				return false;

			// Check if path is in a system-protected location
			if (OS.IsWindows) {
				var programFiles = Environment.GetFolderPath (Environment.SpecialFolder.ProgramFiles);
				var programFilesX86 = Environment.GetFolderPath (Environment.SpecialFolder.ProgramFilesX86);
				if ((!string.IsNullOrEmpty (programFiles) && sdkPath.StartsWith (programFiles, StringComparison.OrdinalIgnoreCase)) ||
				    (!string.IsNullOrEmpty (programFilesX86) && sdkPath.StartsWith (programFilesX86, StringComparison.OrdinalIgnoreCase)))
					return true;
			}

			// Try a write test
			try {
				if (Directory.Exists (sdkPath)) {
					var testFile = Path.Combine (sdkPath, $".write-test-{Guid.NewGuid ()}");
					using (File.Create (testFile, 1, FileOptions.DeleteOnClose)) { }
					return false;
				}
			}
			catch {
				return true;
			}

			return false;
		}

		/// <summary>
		/// Runs sdkmanager with elevated (Administrator) privileges on Windows using a wrapper
		/// script that captures stdout/stderr to temp files. UAC prompt will be shown to the user.
		/// </summary>
		async Task<(int ExitCode, string Stdout, string Stderr)> RunSdkManagerElevatedAsync (
			string sdkManagerPath, string arguments, bool acceptLicenses, CancellationToken cancellationToken)
		{
			var stdoutFile = Path.Combine (Path.GetTempPath (), $"sdkmanager-stdout-{Guid.NewGuid ()}.txt");
			var stderrFile = Path.Combine (Path.GetTempPath (), $"sdkmanager-stderr-{Guid.NewGuid ()}.txt");
			var scriptFile = Path.Combine (Path.GetTempPath (), $"sdkmanager-elevated-{Guid.NewGuid ()}.cmd");

			try {
				// Build environment variable block for the elevated process
				var envBlock = new StringBuilder ();
				if (!string.IsNullOrEmpty (AndroidSdkPath))
					envBlock.AppendLine ($"set \"{EnvironmentVariableNames.AndroidHome}={AndroidSdkPath}\"");
				if (!string.IsNullOrEmpty (JavaSdkPath))
					envBlock.AppendLine ($"set \"{EnvironmentVariableNames.JavaHome}={JavaSdkPath}\"");
				var userHome = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), ".android");
				envBlock.AppendLine ($"set \"ANDROID_USER_HOME={userHome}\"");

				// Build the wrapper script
				var licenseInput = acceptLicenses ? "echo y| " : "";
				var script = $"""
					@echo off
					{envBlock}
					{licenseInput}"{sdkManagerPath}" {arguments} > "{stdoutFile}" 2> "{stderrFile}"
					echo %ERRORLEVEL% > "{stdoutFile}.exit"
					""";

				File.WriteAllText (scriptFile, script);
				logger (TraceLevel.Verbose, $"Running elevated: {sdkManagerPath} {arguments}");

				var psi = new ProcessStartInfo {
					FileName = "cmd.exe",
					Arguments = $"/c \"{scriptFile}\"",
					UseShellExecute = true,
					Verb = "runas",
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden,
				};

				using var process = Process.Start (psi);
				if (process is null)
					throw new InvalidOperationException ("Failed to start elevated sdkmanager process.");

				// Wait for the elevated process to complete
				await Task.Run (() => {
					if (!process.WaitForExit ((int) TimeSpan.FromMinutes (10).TotalMilliseconds)) {
						process.Kill ();
						throw new TimeoutException ("Elevated sdkmanager process timed out after 10 minutes.");
					}
				}, cancellationToken).ConfigureAwait (false);

				// Read captured output
				var stdoutStr = File.Exists (stdoutFile) ? File.ReadAllText (stdoutFile) : "";
				var stderrStr = File.Exists (stderrFile) ? File.ReadAllText (stderrFile) : "";

				// Read exit code from the marker file
				var exitCodeFile = $"{stdoutFile}.exit";
				int exitCode = process.ExitCode;
				if (File.Exists (exitCodeFile)) {
					var exitCodeStr = File.ReadAllText (exitCodeFile).Trim ();
					int.TryParse (exitCodeStr, out exitCode);
				}

				if (exitCode != 0) {
					logger (TraceLevel.Warning, $"Elevated sdkmanager exited with code {exitCode}");
					logger (TraceLevel.Verbose, $"stdout: {stdoutStr}");
					logger (TraceLevel.Verbose, $"stderr: {stderrStr}");
				}

				return (exitCode, stdoutStr, stderrStr);
			}
			finally {
				// Cleanup temp files
				try { if (File.Exists (scriptFile)) File.Delete (scriptFile); } catch { }
				try { if (File.Exists (stdoutFile)) File.Delete (stdoutFile); } catch { }
				try { if (File.Exists (stderrFile)) File.Delete (stderrFile); } catch { }
				try { if (File.Exists ($"{stdoutFile}.exit")) File.Delete ($"{stdoutFile}.exit"); } catch { }
			}
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

			// Ensure ANDROID_USER_HOME is set so sdkmanager can write install properties and
			// preferences to a user-writable location. Without this, sdkmanager fails with
			// "Failed to read or create install properties file" when the SDK is installed in
			// a system-protected path (e.g., C:\Program Files).
			if (!psi.EnvironmentVariables.ContainsKey ("ANDROID_USER_HOME") || string.IsNullOrEmpty (psi.EnvironmentVariables["ANDROID_USER_HOME"])) {
				var userHome = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), ".android");
				psi.EnvironmentVariables["ANDROID_USER_HOME"] = userHome;
			}
		}
	}
}
