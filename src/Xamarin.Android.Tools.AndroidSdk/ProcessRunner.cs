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
	None,
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
	/// If `null`, then <see cref="ProcessRunner.CreateDefaultProcessStartInfo()"/> will be called.  Note that the <see cref="System.Diagnostics.ProcessStartInfo"/>
	/// instance in this field **will** be modified by <see cref="ProcessRunner.Run"/>
	/// </summary>
	public ProcessStartInfo? StartInfo                      { get; set; }

	/// <summary>
	/// Process timeout.  If not set, <see cref="ProcessRunner.TimeOut"/> is used.
	/// </summary>
	public TimeSpan? TimeOut                                { get; set; }

	/// <summary>
	/// Timeout to use when standard streams are redirected and we need to wait for them to finish reading the produced data.
	/// If not set, <see cref="ProcessRunner.StandardStreamsTimeOut"/> is used.
	/// </summary>
	public TimeSpan? StandardStreamsTimeOut                 { get; set; }

	/// <summary>
	/// If `true`, enable simple capture of the process's standard output.  On exit, <see cref="ProcessExitState.CapturedStdout"/> contains
	/// the process standard output.
	/// </summary>
	public bool CaptureStdout                               { get; set; }

	/// <summary>
	/// If `true`, enable simple capture of the process's standard error.  On exit, <see cref="ProcessExitState.CapturedStderr"/> contains
	/// the process standard error.
	/// </summary>
	public bool CaptureStderr                               { get; set; }

	/// <summary>
	/// If specified, the `TextWriter` will receive all the lines from the launched process's standard output stream.  If this field is
	/// assigned a non-null value, the <see cref="CaptureStdout"/> field is ignored.
	/// </summary>
	public TextWriter? StdoutSink                           { get; set; }

	/// <summary>
	/// If specified, the `TextWriter` will receive all the lines from the launched process's standard error stream.  If this field is
	/// assigned a non-null value, the <see cref="CaptureStderr"/> field is ignored.
	/// </summary>
	public TextWriter? StderrSink                           { get; set; }

	/// <summary>
	/// All the key-value pairs are copied from this dictionary, if set, to the process environment.
	/// </summary>
	public Dictionary<string, string>? EnvironmentVariables { get; set; }

	/// <summary>
	/// Specifies the process starting directory.  If not set, then <see cref="ProcessRunner.WorkingDirectory"/> will be used instead.
	/// </summary>
	public string? WorkingDirectory                         { get; set; }
}

/// <summary>
/// Fully describes process state after it exits.  This includes contents of process standard output and standard error streams, if
/// captured (see also <seealso cref="ProcessRunOptions.StdoutSink"/>, <seealso cref="ProcessRunOptions.StderrSink"/>)
/// </summary>
class ProcessExitState
{
	public ProcessExitReason ExitReason { get; set; } = ProcessExitReason.None;
	public int ExitCode                 { get; set; }
	public Stream? CapturedStdout       { get; set; }
	public Stream? CapturedStderr       { get; set; }

	/// <summary>
	/// Contains exception, if any, thrown by <see cref="System.Diagnostics.Process"/> methods, and **only** the exceptions
	/// documented for that class's methods that used by <see cref="ProcessRunner.Run"/>.  Any other exceptions will be
	/// thrown without capturing them in this field.
	/// </summary>
	public Exception? Exception         { get; set; }

	Encoding? stdoutEncoding;
	Encoding? stderrEncoding;

	/// <summary>
	/// A helper property which converts <see cref="CapturedStderr"/> stream, if any, to string.  Stream is rewound to
	/// beginning after it is converted.  If standard error stream is `null`, an empty string is returned.
	/// String encoding is the same as specified when running the process, see <see cref="ProcessRunner.StandardErrorEncoding"/>
	/// </summary>
	public string StdErr => StreamToString (CapturedStderr, stderrEncoding);

	/// <summary>
	/// A helper property which converts <see cref="CapturedStdout"/> stream, if any, to string.  Stream is rewound to
	/// beginning after it is converted.  If standard output stream is `null`, an empty string is returned.
	/// String encoding is the same as specified when running the process, see <see cref="ProcessRunner.StandardOutputEncoding"/>
	/// </summary>
	public string StdOut => StreamToString (CapturedStdout, stdoutEncoding);

	public ProcessExitState (Encoding? stdoutEncoding, Encoding? stderrEncoding)
	{
		this.stdoutEncoding = stdoutEncoding;
		this.stderrEncoding = stderrEncoding;
	}

	string StreamToString (Stream? stream, Encoding? encoding)
	{
		if (stream == null) {
			return String.Empty;
		}

		using StreamReader sr = new StreamReader (
			stream,
			encoding ?? Encoding.Default,
			detectEncodingFromByteOrderMarks: true,
			bufferSize: -1,
			leaveOpen: true
		);
		string ret = sr.ReadToEnd ();
		stream.Seek (0, SeekOrigin.Begin);

		return ret;
	}
}

class ProcessRunner
{
	protected sealed class RunState
	{
		public Process? Process;
		public bool ThreadSafe;
		public Stream? StdoutStream;
		public Stream? StderrStream;
		public ProcessRunOptions RunOptions { get; }

		public RunState (ProcessRunOptions runOptions)
		{
			RunOptions = runOptions;
		}

		public void KillProcess ()
		{
			try {
				Process?.Kill ();
			} catch (InvalidOperationException) {
				// If the process has already exited this could happen
			}
		}
	}

	Action<TraceLevel, string> logger;

	/// <summary>
	/// Default timeout value for all processes ran with this instance of the class.  The value can be overridden
	/// for a particular process by setting <see cref="ProcessRunOptions.TimeOut"/>. Defaults to 5 minutes.
	/// </summary>
	public TimeSpan TimeOut                                 { get; set; } = TimeSpan.FromMinutes (5);

	/// <summary>
	/// Default timeout value to wait for standard streams to complete writing after the process has exited.
	/// This value can be overriden for a particular process by setting <see cref="ProcessRunOptions.StandardStreamsTimeOut"/>.
	/// Defaults to 10 seconds.
	/// </summary>
	public TimeSpan StandardStreamsTimeOut                  { get; set; } = TimeSpan.FromSeconds (10);

	/// <summary>
	/// If set, specify the default working directory for all processes ran using this instance of the class. Useful
	/// when a number of processes will be ran from the same directory.  The value can be overridden for a particular
	/// process by setting <see cref="ProcessRunOptions.WorkingDirectory"/>.
	/// </summary>
	public string? WorkingDirectory                         { get; set; }

	/// <summary>
	/// Set the default encoding for stderr stream of all processes ran using this instance of the class.
	/// If <see cref="ProcessRunOptions.StartInfo.StandardErrorEncoding"/> is set, then the value
	/// of this property is not used.
	/// </summary>
	public Encoding? StandardErrorEncoding                  { get; set; }

	/// <summary>
	/// Set the default encoding for stdout stream of all processes ran using this instance of the class.
	/// If <see cref="ProcessRunOptions.StartInfo.StandardOutputEncoding"/> is set, then the value
	/// of this property is not used.
	/// </summary>
	public Encoding? StandardOutputEncoding                 { get; set; }

	/// <summary>
	/// Set the default environment variables for all processes ran with this instance of the class.  The precedence of
	/// environment variable values is as follows: <see cref="ProcessRunOptions.EnvironmentVariables"/>
	/// <see cref="ProcessRunOptions.StartInfo.EnvironmentVariables"/>, this property.
	/// </summary>
	public Dictionary<string, string>? EnvironmentVariables { get; set; }

	public ProcessRunner (Action<TraceLevel, string>? logger = null)
	{
		this.logger = logger ?? /*AndroidSdkInfo.*/DefaultConsoleLogger;
	}

	static void DefaultConsoleLogger (TraceLevel level, string message)
	{
		switch (level) {
			case TraceLevel.Error:
				Console.Error.WriteLine (message);
				break;
			default:
				Console.WriteLine ($"[{level}] {message}");
				break;
		}
	}

	/// <summary>
	/// Run a process synchronously, waiting for it to exit.  It calls the <see cref="Run(ProcessRunOptions, string, string[])"/> overload,
	/// passing it default <see cref="ProcessRunOptions"/> created by <see cref="CreateDefaultRunOptions"/>.  Arguments are assumed to
	/// be properly quoted, see <seealso cref="QuoteArgument(string)"/>.
	/// On return, the underlying process instance is disposed and another process can be run.
	/// </summary>
	public ProcessExitState Run (string executablePath, params string[] arguments)
	{
		return RunInner (CreateDefaultRunOptions (), executablePath, arguments);
	}

	public ProcessExitState Run (string executablePath, ICollection<string> arguments)
	{
		return RunInner (CreateDefaultRunOptions (), executablePath, arguments);
	}

	/// <summary>
	/// Run a process synchronously, waiting for it to exit. The <paramref name="runOptions"/> parameter specifies the `ProcessRunner`
	/// behavior while executing the process.  Arguments are assumed to be properly quoted, see <seealso cref="QuoteArgument(string)"/>.
	/// </summary>
	public ProcessExitState Run (ProcessRunOptions runOptions, string executablePath, params string[] arguments)
	{
		return RunInner (runOptions, executablePath, arguments);
	}

	public ProcessExitState Run (ProcessRunOptions runOptions, string executablePath, ICollection<string>? arguments)
	{
		return RunInner (runOptions, executablePath, arguments);
	}

	ProcessExitState RunInner (ProcessRunOptions runOptions, string executablePath, ICollection<string>? arguments, bool threadSafe = false)
	{
		return RunInner (new RunState (runOptions) { ThreadSafe = threadSafe }, executablePath, arguments);
	}

	protected ProcessExitState RunInner (RunState state, string executablePath, ICollection<string>? arguments)
	{
		Exception? exception = null;
		try {
			return DoRun (state, executablePath, arguments);
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
		} finally {
			state.Process?.Dispose ();
			state.Process = null;
		}

		return new ProcessExitState (null, null) {
			ExitReason = ProcessExitReason.Exception,
			Exception  = exception,
			ExitCode   = -1,
			CapturedStdout = state.StdoutStream,
			CapturedStderr = state.StderrStream,
		};
	}

	ProcessExitState DoRun (RunState state, string executablePath, ICollection<string>? arguments)
	{
		ProcessStartInfo psi = state.RunOptions.StartInfo ?? CreateDefaultProcessStartInfo ();
		psi.FileName = executablePath;
		psi.RedirectStandardOutput = state.RunOptions.CaptureStdout || state.RunOptions.StdoutSink != null;
		psi.RedirectStandardError = state.RunOptions.CaptureStderr || state.RunOptions.StderrSink != null;

		if (psi.UseShellExecute && (psi.RedirectStandardOutput || psi.RedirectStandardError)) {
			// As per https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.start?view=net-7.0#system-diagnostics-process-start
			TurnOffShellExecute (psi, "standard stream redirection was requested.");
		}

		if (arguments != null && arguments.Count > 0) {
			psi.Arguments = String.Join (" ", arguments);
		}

		if (!String.IsNullOrEmpty (psi.WorkingDirectory)) {
			psi.WorkingDirectory = state.RunOptions.WorkingDirectory ?? WorkingDirectory;
		}

		if (psi.RedirectStandardOutput && psi.StandardOutputEncoding == null) {
			psi.StandardOutputEncoding = StandardOutputEncoding;
		}

		if (psi.RedirectStandardError && psi.StandardErrorEncoding == null) {
			psi.StandardErrorEncoding = StandardErrorEncoding;
		}

		SetEnvironmentVariables (state.RunOptions, psi);

		ManualResetEventSlim? stdoutDone = null;
		ManualResetEventSlim? stderrDone = null;
		TextWriter? stdoutWriter = null;
		TextWriter? stderrWriter = null;
		state.StdoutStream = null;
		state.StderrStream = null;

		if (state.RunOptions.StdoutSink != null) {
			stdoutWriter = state.RunOptions.StdoutSink;
		} else if (state.RunOptions.CaptureStdout) {
			(state.StdoutStream, stdoutWriter) = MakeStreamAndWriter (psi.StandardOutputEncoding);
		}

		if (state.RunOptions.StderrSink != null) {
			stderrWriter = state.RunOptions.StderrSink;
		} else if (state.RunOptions.CaptureStderr) {
			(state.StderrStream, stderrWriter) = MakeStreamAndWriter (psi.StandardErrorEncoding);
		}

		if (stdoutWriter != null) {
			stdoutDone = new ManualResetEventSlim ();
		}

		if (stderrWriter != null) {
			stderrDone = new ManualResetEventSlim ();
		}

		ProcessExitState exitState;
		try {
			exitState = DoRunInner (state, psi, stdoutWriter, stderrWriter, stdoutDone, stderrDone);
		} finally {
			stdoutDone?.Dispose ();
			stderrDone?.Dispose ();
			stdoutWriter?.Dispose ();
			stderrWriter?.Dispose ();
		}

		if (state.StdoutStream != null) {
			state.StdoutStream.Seek (0, SeekOrigin.Begin);
			exitState.CapturedStdout = state.StdoutStream;
		}

		if (state.StderrStream != null) {
			state.StderrStream.Seek (0, SeekOrigin.Begin);
			exitState.CapturedStderr = state.StderrStream;
		}

		return exitState;

		(Stream, TextWriter) MakeStreamAndWriter (Encoding? encoding)
		{
			var stream = new MemoryStream ();
			var writer = new StreamWriter (stream, encoding ?? Encoding.Default, bufferSize: -1, leaveOpen: true);
			return (stream, state.ThreadSafe ? TextWriter.Synchronized (writer) : writer);
		}
	}

	ProcessExitState DoRunInner (RunState state, ProcessStartInfo psi, TextWriter? stdoutWriter, TextWriter? stderrWriter, ManualResetEventSlim? stdoutDone, ManualResetEventSlim? stderrDone)
	{
		state.Process = new Process {
			StartInfo = psi,
		};
		OnProcessCreated (state.Process);

		logger (TraceLevel.Verbose, $"Starting process: {GetFullCommandLine (psi)}");
		try {
			state.Process.Start ();
		} catch (Exception) {
			logger (TraceLevel.Error, $"Failed to start process, exception was thrown: {GetFullCommandLine (psi)}");
			throw;
		}

		bool redirecting = false;
		if (psi.RedirectStandardOutput) {
			redirecting = true;
			EnsureValidParameters (stdoutWriter, stdoutDone, "output", nameof (stdoutWriter), nameof (stdoutDone));

			state.Process.OutputDataReceived += (object sender, DataReceivedEventArgs e) => {
				if (e.Data != null) {
					stdoutWriter!.WriteLine (e.Data);
				} else {
					stdoutDone!.Set ();
				}
			};
			state.Process.BeginOutputReadLine ();
		}

		if (psi.RedirectStandardError) {
			redirecting = true;
			EnsureValidParameters (stderrWriter, stderrDone, "error", nameof (stderrWriter), nameof (stderrDone));

			state.Process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) => {
				if (e.Data != null) {
					stderrWriter!.WriteLine (e.Data);
				} else {
					stderrDone!.Set ();
				}
			};
			state.Process.BeginErrorReadLine ();
		}

		var exitState = new ProcessExitState (psi.StandardOutputEncoding ?? StandardOutputEncoding, psi.StandardErrorEncoding ?? StandardErrorEncoding);
		TimeSpan timeoutSpan = state.RunOptions.TimeOut ?? TimeOut;
		int timeout = timeoutSpan == Timeout.InfiniteTimeSpan ? -1 : (int)timeoutSpan.TotalMilliseconds;
		bool exited = state.Process.WaitForExit (timeout);

		if (!exited) {
			logger (TraceLevel.Error, $"Process timed out: {GetFullCommandLine (psi)}");
			exitState.ExitReason = ProcessExitReason.TimedOut;
			exitState.ExitCode = Int32.MinValue; // hardly the best value to use, but it can't be 0 in this case
		} else {
			exitState.ExitReason = ProcessExitReason.Finished;
			exitState.ExitCode = state.Process.ExitCode;
		}

		// See the Remarks section in: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=net-7.0#system-diagnostics-process-waitforexit(system-int32)
		if (exited && (psi.RedirectStandardError || psi.RedirectStandardOutput)) {
			state.Process.WaitForExit ();
		}

		if (redirecting) {
			timeoutSpan = state.RunOptions.StandardStreamsTimeOut ?? StandardStreamsTimeOut;
			stdoutDone?.Wait (timeoutSpan);
			stderrDone?.Wait (timeoutSpan);
		}

		return exitState;

		void EnsureValidParameters (TextWriter? writer, ManualResetEventSlim? semaphore, string streamName, string writerName, string semaphoreName)
		{
			if (semaphore == null) {
				throw new InvalidOperationException ($"Internal error: when redirecting standard {streamName}, {semaphoreName} must not be null");
			}

			if (writer == null) {
				throw new InvalidOperationException ($"Internal error: when redirecting standard {streamName}, {writerName} must not be null");
			}
		}
	}

	protected virtual void OnProcessCreated (Process newProcess)
	{
		// No-op
	}

	string GetFullCommandLine (ProcessStartInfo psi) => $"{psi.FileName} {psi.Arguments}";

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

	public static string QuoteArgument (string argument)
	{
		if (String.IsNullOrEmpty (argument)) {
			return argument;
		}

		var sb = new StringBuilder (argument);
		sb.Replace ("\"", "\\\"");
		sb.Insert (0, '"');
		sb.Append ('"');
		return sb.ToString ();
	}

	public virtual ProcessRunOptions CreateDefaultRunOptions ()
	{
		return new ProcessRunOptions {
			StartInfo = CreateDefaultProcessStartInfo (),
			CaptureStderr = true,
			CaptureStdout = true,
		};
	}

	public virtual ProcessStartInfo CreateDefaultProcessStartInfo ()
	{
		var psi = new ProcessStartInfo {
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			UseShellExecute = false,
			WindowStyle = ProcessWindowStyle.Hidden,
		};

		return psi;
	}
}
