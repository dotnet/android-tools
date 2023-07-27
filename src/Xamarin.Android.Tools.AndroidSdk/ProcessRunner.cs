using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Xamarin.Android.Tools;

enum ProcessExitReason
{
	Finished,
	TimedOut,
	Exception,
}

/// <summary>
/// A set of options that can be passed to the <see cref="ProcessRunner.Run"/> method in order to modify the
/// default behavior when running a process.
/// </summary>
class ProcessRunOptions
{
	/// <summary>
	/// If `null`, then <see cref="ProcessRunner.MakeDefaultProcessStartInfo()"/> will be called.  Note that the <see cref="System.Diagnostics.ProcessStartInfo"/>
	/// instance in this field **will** be modified by <see cref="ProcessRunner.Run"/>
	/// </summary>
	public ProcessStartInfo? StartInfo;

	/// <summary>
	/// Process timeout.  Defaults to <see cref="TimeSpan.MaxValue"/>
	/// </summary>
	public TimeSpan Timeout = TimeSpan.MaxValue;

	/// <summary>
	/// If `true`, enable simple capture of the process's standard output.  On exit, <see cref="ProcessExitState.CapturedStdout"/> contains
	/// the process standard output.
	/// </summary>
	public bool CaptureStdout;

	/// <summary>
	/// If `true`, enable simple capture of the process's standard error.  On exit, <see cref="ProcessExitState.CapturedStderr"/> contains
	/// the process standard error.
	/// </summary>
	public bool CaptureStderr;

	/// <summary>
	/// If specified, the `TextWriter` will receive all the lines from the launched process's standard output stream.  If this field is
	/// assigned a non-null value, the <see cref="CaptureStdout"/> field is ignored.
	/// </summary>
	public TextWriter? StdoutSink;

	/// <summary>
	/// If specified, the `TextWriter` will receive all the lines from the launched process's standard error stream.  If this field is
	/// assigned a non-null value, the <see cref="CaptureStderr"/> field is ignored.
	/// </summary>
	public TextWriter? StderrSink;

	/// <summary>
	/// All the key-value pairs are copied from this dictionary, if set, to the process environment.
	/// </summary>
	public Dictionary<string, string>? EnvironmentVariables;

	/// <summary>
	/// Specifies the process starting directory.  If not set, then <see cref="ProcessRunner.WorkingDirectory"/> will be used instead.
	/// </summary>
	public string? WorkingDirectory;
}

/// <summary>
/// Fully describes process state after it exits.  This includes contents of process standard output and standard error streams, if
/// captured (see also <seealso cref="ProcessRunOptions.StdoutSink"/>, <seealso cref="ProcessRunOptions.StderrSink"/>)
/// </summary>
class ProcessExitState
{
	public ProcessExitReason ExitReason;
	public int ExitCode;
	public Stream? CapturedStdout;
	public Stream? CapturedStderr;

	/// <summary>
	/// Contains exception, if any, thrown by <see cref="System.Diagnostics.Process"/> methods, and **only** the exceptions
	/// documented for that class's methods that used by <see cref="ProcessRunner.Run"/>.  Any other exceptions will be
	/// thrown without capturing them in this field.
	/// </summary>
	public Exception? Exception;
}

class ProcessRunner : IDisposable
{
	bool disposed;
	Process? process;
	Action<TraceLevel, string> logger;

	public Process? Process => process;

	/// <summary>
	/// If set, specify the default working directory for all processes ran using this instance of the class. Useful
	/// when a number of processes will be ran from the same directory.  The value can be overridden by setting
	/// <see cref="ProcessRunOptions.WorkingDirectory"/> for any process ran using this instance of the class.
	/// </summary>
	public string? WorkingDirectory { get; set; }

	/// <summary>
	/// Set the default encoding for stderr stream of all processes ran using this instance of the class.
	/// If <see cref="ProcessRunOptions.StartInfo.StandardErrorEncoding"/> is set, then the value
	/// of this property is not used.
	/// </summary>
	public Encoding? StandardErrorEncoding { get; set; }

	/// <summary>
	/// Set the default encoding for stdout stream of all processes ran using this instance of the class.
	/// If <see cref="ProcessRunOptions.StartInfo.StandardOutputEncoding"/> is set, then the value
	/// of this property is not used.
	/// </summary>
	public Encoding? StandardOutputEncoding { get; set; }

	/// <summary>
	/// Set the default environment variables for all processes ran with this instance of the class.  The precedence of
	/// environment variable values is as follows: <see cref="ProcessRunOptions.EnvironmentVariables"/>
	/// <see cref="ProcessRunOptions.StartInfo.EnvironmentVariables"/>, this property.
	/// </summary>
	public Dictionary<string, string>? EnvironmentVariables { get; set; }

	public ProcessRunner (Action<TraceLevel, string>? logger = null)
	{
		this.logger = logger ?? AndroidSdkInfo.DefaultConsoleLogger;
	}

	~ProcessRunner ()
	{
	    Dispose (disposing: false);
	}

	/// <summary>
	/// Run a process synchronously, waiting for it to exit.  It calls the <see cref="Run(ProcessRunOptions, string, string[])"/> overload,
	/// passing it default <see cref="ProcessRunOptions"/> created by <see cref="MakeDefaultRunOptions"/>.  Arguments are assumed to
	/// be properly quoted, see <seealso cref="QuoteArgument(string)"/>
	/// </summary>
	public ProcessExitState Run (string executablePath, params string[] arguments)
	{
		return Run (MakeDefaultRunOptions (), executablePath, arguments);
	}

	/// <summary>
	/// Run a process synchronously, waiting for it to exit. The <paramref name="runOptions"/> parameter specifies the `ProcessRunner`
	/// behavior while executing the process.  Arguments are assumed to be properly quoted, see <seealso cref="QuoteArgument(string)"/>
	/// </summary>
	public ProcessExitState Run (ProcessRunOptions runOptions, string executablePath, params string[] arguments)
	{
		Exception? exception = null;
		try {
			return DoRun (runOptions, executablePath, arguments);
		} catch (ObjectDisposedException ex) {
			exception = ex;
		} catch (InvalidOperationException ex) {
			exception = ex;
		} catch (Win32Exception ex) {
			exception = ex;
		} catch (PlatformNotSupportedException ex) {
			exception = ex;
		} catch (SystemException ex) {
			exception = ex;
		}

		process?.Dispose ();
		process = null;

		return new ProcessExitState {
			ExitReason = ProcessExitReason.Exception,
			Exception  = exception,
			ExitCode   = -1,
		};
	}

	protected virtual ProcessExitState DoRun (ProcessRunOptions runOptions, string executablePath, params string[] arguments)
	{
		if (process != null) {
			throw new InvalidOperationException ("A process is already running in this instance");
		}

		ProcessStartInfo psi = runOptions.StartInfo ?? MakeDefaultProcessStartInfo ();
		psi.FileName = executablePath;
		psi.RedirectStandardOutput = runOptions.CaptureStdout || runOptions.StdoutSink != null;
		psi.RedirectStandardError = runOptions.CaptureStderr || runOptions.StderrSink != null;

		if (psi.UseShellExecute && (psi.RedirectStandardOutput || psi.RedirectStandardError)) {
			// As per https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.start?view=net-7.0#system-diagnostics-process-start
			TurnOffShellExecute (psi, "standard stream redirection was requested.");
		}

		if (arguments != null && arguments.Length > 0) {
			psi.Arguments = String.Join (" ", arguments);
		}

		if (!String.IsNullOrEmpty (psi.WorkingDirectory)) {
			psi.WorkingDirectory = runOptions.WorkingDirectory ?? WorkingDirectory;
		}

		if (psi.RedirectStandardOutput && psi.StandardOutputEncoding == null) {
			psi.StandardOutputEncoding = StandardOutputEncoding;
		}

		if (psi.RedirectStandardError && psi.StandardErrorEncoding == null) {
			psi.StandardErrorEncoding = StandardErrorEncoding;
		}

		SetEnvironmentVariables (runOptions, psi);

		ManualResetEventSlim? stdoutDone = null;
		ManualResetEventSlim? stderrDone = null;
		Stream? stdoutStream = null;
		Stream? stderrStream = null;
		TextWriter? stdoutWriter = null;
		TextWriter? stderrWriter = null;

		if (runOptions.StdoutSink != null) {
			stdoutWriter = runOptions.StdoutSink;
		} else if (runOptions.CaptureStdout) {
			(stdoutStream, stdoutWriter) = MakeStreamAndWriter (psi.StandardOutputEncoding);
		}

		if (runOptions.StderrSink != null) {
			stderrWriter = runOptions.StderrSink;
		} else if (runOptions.CaptureStderr) {
			(stderrStream, stderrWriter) = MakeStreamAndWriter (psi.StandardErrorEncoding);
		}

		if (stdoutWriter != null) {
			stdoutDone = new ManualResetEventSlim ();
		}

		if (stderrWriter != null) {
			stderrDone = new ManualResetEventSlim ();
		}

		ProcessExitState exitState;
		try {
			exitState = DoRunInner (psi, stdoutWriter, stderrWriter, stdoutDone, stderrDone);
		} catch (Exception) {
			stdoutStream?.Dispose ();
			stderrStream?.Dispose ();
			throw;
		} finally {
			stdoutDone?.Dispose ();
			stderrDone?.Dispose ();
			stdoutWriter?.Dispose ();
			stderrWriter?.Dispose ();
		}

		if (stdoutStream != null) {
			exitState.CapturedStdout = stdoutStream;
		}

		if (stderrStream != null) {
			exitState.CapturedStderr = stderrStream;
		}

		return exitState;

		(Stream, StreamWriter) MakeStreamAndWriter (Encoding? encoding)
		{
			var stream = new MemoryStream ();
			return (stream, new StreamWriter (stream, encoding ?? Encoding.Default));
		}
	}

	ProcessExitState DoRunInner (ProcessStartInfo psi, TextWriter? stdoutWriter, TextWriter? stderrWriter, ManualResetEventSlim? stdoutDone, ManualResetEventSlim? stderrDone)
	{
		process = new Process {
			StartInfo = psi,
		};

		logger (TraceLevel.Verbose, $"Starting process: {psi.FileName} {psi.Arguments}");

		try {
			process.Start ();
		} catch (Exception) {
			logger (TraceLevel.Error, $"Failed to start process: {psi.FileName} {psi.Arguments}");
			throw;
		}

		throw new NotImplementedException ();
	}

	void TurnOffShellExecute (ProcessStartInfo psi, string why)
	{
		psi.UseShellExecute = false;
		logger (TraceLevel.Warning, $"Turning off the UseShellExecute option for process '{psi.FileName}' because: {why}");
	}

	void SetEnvironmentVariables (ProcessRunOptions runOptions, ProcessStartInfo psi)
	{
		if ((runOptions.EnvironmentVariables == null || runOptions.EnvironmentVariables.Count == 0) &&
		    (EnvironmentVariables == null || EnvironmentVariables.Count == 0)) {
			return;
		}

		if (psi.UseShellExecute) {
			// As per https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.environment?view=net-7.0#remarks
			TurnOffShellExecute (psi, "one or more environment variables were set.");
		}

		CopyVariables (runOptions.EnvironmentVariables, onlyNew: false);
		CopyVariables (EnvironmentVariables, onlyNew: true);

		void CopyVariables (Dictionary<string, string>? dict, bool onlyNew)
		{
			if (dict == null || dict.Count == 0) {
				return;
			}

			foreach (var kvp in dict) {
				if (psi.Environment.ContainsKey (kvp.Key) && onlyNew) {
					continue;
				}

				psi.Environment[kvp.Key] = kvp.Value;
			}
		}
	}

	public string QuoteArgument (string argument)
	{
		if (String.IsNullOrEmpty (argument)) {
			return argument;
		}

		var sb = new StringBuilder (argument);
		if (argument.IndexOf ('"') >= 0) {
			sb.Replace ("\"", "\\\"");
		}

		sb.Insert (0, '"');
		sb.Append ('"');
		return sb.ToString ();
	}

	public virtual ProcessRunOptions MakeDefaultRunOptions ()
	{
		return new ProcessRunOptions {
			StartInfo = MakeDefaultProcessStartInfo (),
			CaptureStderr = true,
			CaptureStdout = true,
		};
	}

	public virtual ProcessStartInfo MakeDefaultProcessStartInfo ()
	{
		var psi = new ProcessStartInfo {
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
		};

		return psi;
	}

	protected virtual void Dispose (bool disposing)
	{
		if (!disposed) {
			if (disposing) {
				// TODO: dispose managed state (managed objects)
			}

			// TODO: free unmanaged resources (unmanaged objects) and override finalizer
			process?.Dispose ();
			process = null;

			// TODO: set large fields to null
			disposed = true;
		}
	}

	public void Dispose ()
	{
		Dispose (disposing: true);
		GC.SuppressFinalize (this);
	}
}
