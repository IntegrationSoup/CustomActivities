using System;
using System.Threading;

namespace Popokey.ExtensionRunners
{
    // Only compiled into runners. Source-linked adapters use the same bespoke
    // serialization and business handler locally, without starting another child.
    internal static class SourceAdapterDispatch
    {
        [ThreadStatic] internal static PersistentRunnerRequestHandler Handler;
        [ThreadStatic] internal static CancellationToken Cancellation;
        internal static string Invoke(string operation, string payload)
        {
            if (Handler == null) throw new InvalidOperationException("No local runner operation is active.");
            return Handler(operation, payload);
        }
    }
}
