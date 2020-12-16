using System;
using Xamarin.Build;
using static System.Threading.Tasks.TaskExtensions;

namespace Microsoft.Android.Build.Tasks
{
	public abstract class AndroidAsyncTask : AsyncTask
	{
		/// <summary>
		/// A helper for non-async overrides of RunTaskAsync, etc.
		/// </summary>
		public static readonly System.Threading.Tasks.Task Done =
			System.Threading.Tasks.Task.CompletedTask;

		public abstract string TaskPrefix { get; }

		public override bool Execute ()
		{
			try {
				return RunTask ();
			} catch (Exception ex) {
				this.LogUnhandledException (TaskPrefix, ex);
				return false;
			}
		}

		public virtual bool RunTask ()
		{
			Yield ();
			try {
				this.RunTask (() => RunTaskAsync ())
					.Unwrap ()
					.ContinueWith (Complete);

				// This blocks on AsyncTask.Execute, until Complete is called
				return base.Execute ();
			} finally {
				Reacquire ();
			}
		}

		/// <summary>
		/// Override this method for simplicity of AsyncTask usage:
		/// * Yield / Reacquire is handled for you
		/// * RunTaskAsync is already on a background thread
		/// </summary>
		public virtual System.Threading.Tasks.Task RunTaskAsync () => Done;
	}
}
