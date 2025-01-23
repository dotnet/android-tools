using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools;

class AsyncProcessRunner : ProcessRunner
{
	public AsyncProcessRunner (Action<TraceLevel, string>? logger = null)
		: base (logger)
	{}

	public async Task<ProcessExitState> RunAsync (string executablePath, params string[] arguments)
	{
		return await DoRun (executablePath, arguments);
	}

	public async Task<ProcessExitState> RunAsync (CancellationToken cancellationToken, string executablePath, params string[] arguments)
	{
		return await DoRun (executablePath, arguments, cancellationToken: cancellationToken);
	}

	public async Task<ProcessExitState> RunAsync (string executablePath, ICollection<string> arguments)
	{
		return await DoRun (executablePath, arguments);
	}

	public async Task<ProcessExitState> RunAsync (CancellationToken cancellationToken, string executablePath, ICollection<string> arguments)
	{
		return await DoRun (executablePath, arguments, cancellationToken: cancellationToken);
	}

	public async Task<ProcessExitState> RunAsync (ProcessRunOptions? runOptions, string executablePath, params string[] arguments)
	{
		return await DoRun (executablePath, arguments, runOptions);
	}

	public async Task<ProcessExitState> RunAsync (CancellationToken cancellationToken, ProcessRunOptions? runOptions, string executablePath, params string[] arguments)
	{
		return await DoRun (executablePath, arguments, runOptions, cancellationToken);
	}

	public async Task<ProcessExitState> RunAsync (ProcessRunOptions? runOptions, string executablePath, ICollection<string> arguments)
	{
		return await DoRun (executablePath, arguments, runOptions);
	}

	public async Task<ProcessExitState> RunAsync (CancellationToken cancellationToken, ProcessRunOptions? runOptions, string executablePath, ICollection<string> arguments)
	{
		return await DoRun (executablePath, arguments, runOptions, cancellationToken);
	}

	async Task<ProcessExitState> DoRun (string executablePath, ICollection<string>? arguments, ProcessRunOptions? runOptions = null, CancellationToken cancellationToken = default)
	{
		var state = new RunState (runOptions ?? CreateDefaultRunOptions ()) {
			ThreadSafe = true,
		};

		if (cancellationToken == default) {
			return await DoRunInner (state, executablePath, arguments, cancellationToken);
		}

		ProcessExitState exitState;

		// If the token is cancelled while we're running, kill the process.
		// Otherwise once we finish the Task.WhenAll we can remove this registration
		// as there is no longer any need to Kill the process.
		using (cancellationToken.Register (() => state.KillProcess ())) {
			exitState = await DoRunInner (state, executablePath, arguments, cancellationToken);
		}

		// If we invoke 'KillProcess' our output, error and exit tasks will all complete normally.
		// To protected against passing the user incomplete data we have to call
		// `cancellationToken.ThrowIfCancellationRequested ()` here.
		cancellationToken.ThrowIfCancellationRequested ();
		return exitState;
	}

	Task<ProcessExitState> DoRunInner (RunState state, string executablePath, ICollection<string>? arguments, CancellationToken cancellationToken = default)
	{
		return Task.Run<ProcessExitState> (
			() => RunInner (state, executablePath, arguments),
			cancellationToken
		);
	}
}
