using System.Runtime.CompilerServices;
using Bunit;

namespace Chess.Web.Tests.Support;

/// <summary>
/// bUnit waits 1 s by default for a render that a test waits on. A page driven by a server-sent event stream renders
/// after the stream is read on another thread, and on a loaded CI runner (the API tests run beside these) that took longer:
/// <c>PlayPageTests.AnIllegalReplyIsShownInlineWithItsReasonWhileTheModelIsStillThinking</c> failed with one check in 2 s.
/// The timeout is a limit, not a delay: a passing wait returns at the first render that satisfies it.
/// </summary>
internal static class WaitTimeout
{
    [ModuleInitializer]
    internal static void Raise() => BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(10);
}
