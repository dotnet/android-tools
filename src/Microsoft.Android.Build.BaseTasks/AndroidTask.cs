using System;
using Microsoft.Build.Utilities;

namespace Microsoft.Android.Build.Tasks
{
	// We use this task to ensure that no unhandled exceptions
	// escape our tasks which would cause an MSB4018
	public abstract class AndroidTask : Task
	{
		public abstract string TaskPrefix { get; }

		public override bool Execute ()
		{
			try {
				return RunTask ();
			} catch (Exception ex) {
				Log.LogUnhandledException (TaskPrefix, ex);
				return false;
			}
		}

		public abstract bool RunTask ();
	}
}
