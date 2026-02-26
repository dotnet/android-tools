// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
	public partial class SdkManager
	{
		async Task<(int ExitCode, string Stdout, string Stderr)> RunSdkManagerAsync (
			string sdkManagerPath, string[] arguments, bool acceptLicenses = false, CancellationToken cancellationToken = default)
		{
			var argumentsStr = string.Join (" ", arguments);

			// Check if the SDK path requires elevated permissions for write operations
			bool needsElevation = arguments.Length > 0
				&& !arguments[0].StartsWith ("--list", StringComparison.Ordinal)
				&& RequiresElevation ();

			if (needsElevation && OS.IsWindows) {
				logger (TraceLevel.Info, $"SDK path requires elevated permissions, running sdkmanager elevated...");
				return await RunSdkManagerElevatedAsync (sdkManagerPath, arguments, acceptLicenses, cancellationToken).ConfigureAwait (false);
			}

			var psi = ProcessUtils.CreateProcessStartInfo (sdkManagerPath, arguments);
			psi.RedirectStandardInput = acceptLicenses;

			var envVars = GetEnvironmentVariables ();

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

			logger (TraceLevel.Verbose, $"Running: {sdkManagerPath} {argumentsStr}");
			int exitCode;
			try {
				exitCode = await ProcessUtils.StartProcess (psi, stdout, stderr, cancellationToken, envVars, onStarted).ConfigureAwait (false);
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
		/// for write operations. Reuses <see cref="FileUtil.IsTargetPathWritable"/> and
		/// <see cref="ProcessUtils.IsElevated"/> to avoid duplicating path-check logic.
		/// </summary>
		bool RequiresElevation ()
		{
			if (ProcessUtils.IsElevated ())
				return false; // already elevated

			var sdkPath = AndroidSdkPath;
			if (string.IsNullOrEmpty (sdkPath))
				return false;

			return !FileUtil.IsTargetPathWritable (sdkPath!, logger);
		}

		/// <summary>
		/// Runs sdkmanager with elevated (Administrator) privileges on Windows using a wrapper
		/// script that captures stdout/stderr to temp files. UAC prompt will be shown to the user.
		/// </summary>
		/// <remarks>
		/// Uses <c>ProcessUtils.StartShellExecuteProcessAsync</c> because elevation requires
		/// support <c>UseShellExecute = true</c> with <c>Verb = "runas"</c> needed for UAC elevation.
		/// </remarks>
		async Task<(int ExitCode, string Stdout, string Stderr)> RunSdkManagerElevatedAsync (
			string sdkManagerPath, string[] arguments, bool acceptLicenses, CancellationToken cancellationToken)
		{
			var uniqueId = Guid.NewGuid ().ToString ("N");
			var stdoutFile = Path.Combine (Path.GetTempPath (), $"sdkmanager-stdout-{uniqueId}.txt");
			var stderrFile = Path.Combine (Path.GetTempPath (), $"sdkmanager-stderr-{uniqueId}.txt");
			var exitCodeFile = Path.Combine (Path.GetTempPath (), $"sdkmanager-exit-{uniqueId}.txt");
			var scriptFile = Path.Combine (Path.GetTempPath (), $"sdkmanager-elevated-{uniqueId}.cmd");
			var tempFiles = new[] { scriptFile, stdoutFile, stderrFile, exitCodeFile };

			try {
				var envVars = GetEnvironmentVariables ();
				var envBlock = new StringBuilder ();
				foreach (var kvp in envVars) {
					// Sanitize env values: escape % and ! to prevent cmd.exe expansion
					var safeKey = SanitizeCmdArgument (kvp.Key);
					var safeValue = SanitizeCmdArgument (kvp.Value);
					envBlock.AppendLine ($"set \"{safeKey}={safeValue}\"");
				}

				// Validate and escape each argument for cmd.exe safety.
				// SDK package IDs should only contain alphanumeric, dots, dashes, semicolons, underscores.
				foreach (var arg in arguments) {
					if (arg.IndexOfAny (new[] { '&', '|', '>', '<', '^', '(' , ')' }) >= 0)
						throw new ArgumentException ($"Unsafe character in argument: {arg}");
				}

				var escapedArgs = string.Join (" ", arguments.Select (a => $"\"{SanitizeCmdArgument (a)}\""));
				var licenseInput = acceptLicenses ? "echo y| " : "";
				var script = $"""
					@echo off
					{envBlock}
					{licenseInput}"{sdkManagerPath}" {escapedArgs} > "{stdoutFile}" 2> "{stderrFile}"
					echo %ERRORLEVEL% > "{exitCodeFile}"
					""";

				File.WriteAllText (scriptFile, script);
				logger (TraceLevel.Verbose, $"Running elevated: {sdkManagerPath} {string.Join (" ", arguments)}");

				var psi = ProcessUtils.CreateProcessStartInfo ("cmd.exe", "/c", $"\"{scriptFile}\"");
				psi.UseShellExecute = true;
				psi.Verb = "runas";
				psi.RedirectStandardOutput = false;
				psi.RedirectStandardError = false;
				psi.WindowStyle = ProcessWindowStyle.Hidden;

				await ProcessUtils.StartShellExecuteProcessAsync (psi, SdkManagerTimeout, cancellationToken)
					.ConfigureAwait (false);

				var stdoutStr = File.Exists (stdoutFile) ? File.ReadAllText (stdoutFile) : "";
				var stderrStr = File.Exists (stderrFile) ? File.ReadAllText (stderrFile) : "";

				int exitCode = 0;
				if (File.Exists (exitCodeFile) && int.TryParse (File.ReadAllText (exitCodeFile).Trim (), out var parsed))
					exitCode = parsed;

				if (exitCode != 0) {
					logger (TraceLevel.Warning, $"Elevated sdkmanager exited with code {exitCode}");
					logger (TraceLevel.Verbose, $"stdout: {stdoutStr}");
					logger (TraceLevel.Verbose, $"stderr: {stderrStr}");
				}

				return (exitCode, stdoutStr, stderrStr);
			}
			finally {
				FileUtil.TryDeleteFiles (tempFiles, logger);
			}
		}

		async Task DownloadFileAsync (string url, string destinationPath, long expectedSize, IProgress<SdkBootstrapProgress>? progress, CancellationToken cancellationToken)
		{
			logger (TraceLevel.Info, $"Downloading {url}...");

			// Adapt SdkBootstrapProgress to the generic progress type used by DownloadUtils
			IProgress<(double percent, string message)>? downloadProgress = null;
			if (progress is not null) {
				downloadProgress = new Progress<(double percent, string message)> (p => {
					progress.Report (new SdkBootstrapProgress {
						Phase = SdkBootstrapPhase.Downloading,
						PercentComplete = (int) p.percent,
						Message = p.message,
					});
				});
			}

			await DownloadUtils.DownloadFileAsync (httpClient, url, destinationPath, expectedSize, downloadProgress, cancellationToken).ConfigureAwait (false);
			logger (TraceLevel.Info, $"Download complete: {destinationPath}");
		}

		/// <summary>
		/// Returns environment variables needed by Android SDK tools.
		/// </summary>
		Dictionary<string, string> GetEnvironmentVariables ()
		{
			var env = new Dictionary<string, string> ();

			if (!string.IsNullOrEmpty (AndroidSdkPath))
				env[EnvironmentVariableNames.AndroidHome] = AndroidSdkPath!;

			if (!string.IsNullOrEmpty (JavaSdkPath))
				env[EnvironmentVariableNames.JavaHome] = JavaSdkPath!;

			// Ensure ANDROID_USER_HOME is set so sdkmanager can write install properties
			// to a user-writable location instead of the (possibly read-only) SDK path.
			env["ANDROID_USER_HOME"] = Path.Combine (
				Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), ".android");

			return env;
		}

		/// <summary>
		/// Escapes a string for safe interpolation inside a cmd.exe script.
		/// Handles <c>%</c>, <c>!</c>, and <c>"</c> which have special meaning in batch files.
		/// </summary>
		static string SanitizeCmdArgument (string value)
		{
			return value
				.Replace ("\"", "\\\"")
				.Replace ("%", "%%")
				.Replace ("!", "^!");
		}
	}
}
