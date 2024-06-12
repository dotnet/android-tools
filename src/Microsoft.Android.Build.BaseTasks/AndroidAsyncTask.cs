// https://github.com/xamarin/xamarin-android/blob/9fca138604c53989e1cff7fc0c2e939583b4da28/src/Xamarin.Android.Build.Tasks/Tasks/AndroidTask.cs#L27

using System;

namespace Microsoft.Android.Build.Tasks
{
	/// <summary>
	/// AndroidAsyncTask is a thin wrapper around AsyncTask that includes enhanced logging of unhandled exceptions,
	/// as well as a project directory specific task object key that can be used with the MSBuild RegisterTaskObject API.
	/// </summary>
	public abstract class AndroidAsyncTask : AsyncTask
	{
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

		protected object ProjectSpecificTaskObjectKey (object key) => (key, WorkingDirectory);
	}
}
