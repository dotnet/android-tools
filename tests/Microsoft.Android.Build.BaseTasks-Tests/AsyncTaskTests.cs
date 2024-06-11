using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Android.Build.BaseTasks.Tests.Utilities;
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;
using NUnit.Framework;

namespace Microsoft.Android.Build.BaseTasks.Tests
{
	[TestFixture]
	public class AsyncTaskTests
	{
		List<BuildErrorEventArgs> errors;
		List<BuildWarningEventArgs> warnings;
		List<BuildMessageEventArgs> messages;
		MockBuildEngine engine;

		[SetUp]
		public void TestSetup()
		{
			errors = new List<BuildErrorEventArgs> ();
			warnings = new List<BuildWarningEventArgs> ();
			messages = new List<BuildMessageEventArgs> ();
			engine = new MockBuildEngine (TestContext.Out, errors, warnings, messages);
		}

		public class AsyncMessage : AsyncTask
		{
			public string Text { get; set; }

			public override bool Execute()
			{
				Task.Run(async () =>
				{
					await Task.Delay(5000);
					LogMessage(Text);
					Complete();
				});

				LogTelemetry("Test", new System.Collections.Generic.Dictionary<string, string> () {{"Property", "Value"}});

				return base.Execute();
			}
		}

		[Test]
		public void RunAsyncMessage ()
		{
			var message = "Hello Async World!";
			var task = new AsyncMessage () {
				BuildEngine = engine,
				Text = message
			};
			var taskSucceeded = task.Execute ();
			Assert.IsTrue (messages.Any (e => e.Message.Contains (message)),
				$"Task did not contain expected message text: '{message}'.");
		}

	}
}